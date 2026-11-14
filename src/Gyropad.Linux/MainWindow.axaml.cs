using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Gyropad.Core.Config;
using Gyropad.Core.Input;
using Gyropad.Core.Linux;
using Gyropad.Core.State;
using Gyropad.Core.Telemetry;
using Gyropad.Core.Titles;
using Gyropad.Core.Transport;

namespace Gyropad.Linux;

public partial class MainWindow : Window
{
    private sealed record DeviceRow(string Pretty, string Host, int Port)
    {
        public string Detail => Host + ":" + Port;
    }

    private readonly Store _store = new();
    private readonly Wheel _wheel = new();
    private readonly Pad _pad = new();
    private readonly OutGauge _gauge = new();
    private readonly OutSim _sim;
    private readonly DriftAssist _assist = new();
    private readonly UinputBridge _output = new();
    private readonly Net _net;
    private readonly DispatcherTimer _timer;
    private readonly long _startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private long _lastStatusWrite;

    private readonly ObservableCollection<DeviceRow> _devices = [];
    private readonly List<string> _log = [];
    private readonly object _logGate = new();

    private TaskCompletionSource<bool>? _prompt;
    private string? _lastStatus;
    private bool _lastConnected;
    private string _lastDeviceStatus = "";
    private string _lastDeviceDetail = "";
    private string _lastDeviceKey = "";
    private string _lastDeviceListKey = "";

    public MainWindow()
    {
        InitializeComponent();

        _store.Load();
        _assist.Strength = _store.AssistStrength;
        _assist.DeadbandDeg = _store.AssistDeadbandDeg;
        _assist.FullAtDeg = _store.AssistFullAtDeg;
        _sim = new OutSim { Port = _store.OutSimPort, Invert = _store.AssistInvert };

        _net = new Net(_store, _wheel, _pad, _output, _gauge, _sim, _assist, Environment.MachineName)
        {
            OnApprovalRequest = AskIncomingPair,
            OnPairWanted = OnPairWanted,
            OnChanged = () => Dispatcher.UIThread.Post(Refresh)
        };

        DevicesList.ItemsSource = _devices;
        DevicesList.SelectionChanged += (_, _) =>
            PairButton.IsEnabled = DevicesList.SelectedItem is DeviceRow;
        PairButton.Click += (_, _) => PairSelected();
        UnpairButton.Click += (_, _) => Unpair();
        ApproveButton.Click += (_, _) => ResolvePrompt(true);
        DenyButton.Click += (_, _) => ResolvePrompt(false);
        DoctorButton.Click += (_, _) => ShowDoctor();

        VersionText.Text = "Linux port " + AppInfo.Version;
        ConfigText.Text = "settings: " + Store.SettingsPath;

        _output.Start();
        _gauge.Start();
        _sim.Start();
        Games.Bound = _store;
        Games.StartWatching();
        _net.Start();

        Log("Started. " + _net.StatusLine);
        PublishStatus();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _prompt?.TrySetResult(false);
        _timer.Stop();
        _net.Stop();
        _gauge.Stop();
        _sim.Stop();
        _output.Dispose();
        RuntimeStatus.Clear();
        base.OnClosing(e);
    }

    private void Refresh()
    {
        PillText.Text = !_net.Connected
            ? (_net.InputFault.Length > 0 ? "blocked" : "waiting")
            : _net.PadMode ? "gamepad" : "wheel";

        StatusText.Text = _net.InputFault.Length > 0 ? _net.InputFault : _net.Status;

        if (_net.StatusLine != _lastStatus)
        {
            _lastStatus = _net.StatusLine;
            Log(_lastStatus);
        }

        if (_net.Connected != _lastConnected)
        {
            _lastConnected = _net.Connected;
            Log(_net.Connected ? "Phone linked." : "Phone link closed.");
        }

        PhoneText.Text = _store.HasPeer ? Pretty(_store.PeerName) : "-";
        AddressText.Text = _store.PeerHost.Length > 0 ? _store.PeerHost : "-";

        string persona = StatusSnapshot.Persona(_net);
        PersonaText.Text = persona;

        string notes = string.Join("  ", new[] { _net.InputNote, _net.PairError }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        DetailText.Text = notes;
        DetailText.IsVisible = notes.Length > 0;

        string status = _output.Status;
        string detail = _output.Detail;
        string key = status + "\u0000" + detail;
        if (key != _lastDeviceKey)
        {
            _lastDeviceKey = key;
            DeviceStatusText.Text = WheelSummary(status);
            DeviceDetailText.Text = detail;
            DeviceDetailText.IsVisible = detail.Length > 0;
            if (status != _lastDeviceStatus || detail != _lastDeviceDetail)
            {
                _lastDeviceStatus = status;
                _lastDeviceDetail = detail;
                Log("Virtual device: " + WheelSummary(status) + (detail.Length > 0 ? " - " + detail : ""));
            }
        }

        RefreshDevices();
        PublishStatus();
    }

    /// <summary>
    /// Publishes runtime.json for <c>--status</c>. Once a second is often enough
    /// for a status view and keeps the config directory from churning.
    /// </summary>
    private void PublishStatus()
    {
        long now = Environment.TickCount64;
        if (now - _lastStatusWrite < 1000)
            return;

        _lastStatusWrite = now;
        StatusSnapshot.Capture("window", _store, _net, _output, _wheel, _pad, _startedAt).Write();
    }

    private string WheelSummary(string status) => status switch
    {
        "ready" => _output.PadPlugged
            ? "wheel and gamepad are ready"
            : "wheel is ready; the gamepad appears when a title needs it",
        "not started" => "not started",
        "uinput not available" => "uinput is not available",
        "could not create the gamepad" => "the gamepad could not be created",
        "the wheel device disappeared" => "the wheel device disappeared",
        "the gamepad disappeared" => "the gamepad disappeared",
        _ => status
    };

    private void RefreshDevices()
    {
        Discovered[] snapshot = _net.Snapshot()
            .Where(d => d.Name.Length > 0 && Environment.TickCount - d.SeenAt < 9000)
            .ToArray();

        string key = string.Join("|", snapshot.Select(d => d.Name + "@" + d.Host + ":" + d.PairPort));
        if (key == _lastDeviceListKey)
            return;
        _lastDeviceListKey = key;

        DeviceRow? selected = DevicesList.SelectedItem as DeviceRow;
        _devices.Clear();
        foreach (Discovered d in snapshot)
            _devices.Add(new DeviceRow(d.Pretty, d.Host, d.PairPort));

        if (selected != null)
        {
            DeviceRow? again = _devices.FirstOrDefault(r =>
                r.Pretty == selected.Pretty && r.Host == selected.Host && r.Port == selected.Port);
            if (again != null)
                DevicesList.SelectedItem = again;
        }

        PairButton.IsEnabled = DevicesList.SelectedItem is DeviceRow;
    }

    private static string Pretty(string name) => name.Replace('_', ' ');

    private void PairSelected()
    {
        if (DevicesList.SelectedItem is not DeviceRow row)
            return;

        var discovered = new Discovered { Name = row.Pretty.Replace(' ', '_'), Host = row.Host, PairPort = row.Port };
        PairButton.IsEnabled = false;
        Log("Calling " + row.Pretty + "...");

        _net.RequestPair(discovered, (ok, message) => Dispatcher.UIThread.Post(() =>
        {
            Log(message);
            _lastStatus = _net.StatusLine;
            Refresh();
        }));
    }

    private void Unpair()
    {
        _net.Unpair();
        Log("Forgot the paired phone.");
        Refresh();
    }

    private bool AskIncomingPair(string name)
    {
        return Prompt(
            Pretty(name) + " wants to pair",
            "Only continue if you just tapped Connect in Gyropad on this phone.",
            60);
    }

    private void OnPairWanted(Discovered d)
    {
        if (_net.Connected)
            return;

        // The discovery loop calls this; run the prompt on its own thread so a
        // pending question cannot delay announcements.
        var worker = new Thread(() =>
        {
            bool yes = Prompt(
                d.Pretty + " asked this PC to connect",
                "The phone could not reach this PC directly. Connect back to it instead?",
                30);

            if (!yes)
                return;

            Dispatcher.UIThread.Post(() => Log("Calling " + d.Pretty + "..."));
            _net.RequestPair(d, (ok, message) => Dispatcher.UIThread.Post(() =>
            {
                Log(message);
                Refresh();
            }));
        })
        {
            IsBackground = true,
            Name = "gyropad-pairwanted"
        };
        worker.Start();
    }

    /// <summary>Shows the approval overlay and blocks a network thread until answered.</summary>
    private bool Prompt(string title, string body, int seconds)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            _prompt?.TrySetResult(false);
            _prompt = tcs;
            ApprovalTitle.Text = title;
            ApprovalBody.Text = body;
            ApprovalOverlay.IsVisible = true;
        });

        bool answered = tcs.Task.Wait(TimeSpan.FromSeconds(seconds));

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_prompt, tcs))
            {
                _prompt = null;
                ApprovalOverlay.IsVisible = false;
            }
        });

        return answered && tcs.Task.Result;
    }

    private void ResolvePrompt(bool accepted)
    {
        ApprovalOverlay.IsVisible = false;
        _prompt?.TrySetResult(accepted);
        _prompt = null;
    }

    private void Log(string message)
    {
        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        lock (_logGate)
        {
            _log.Add(line);
            if (_log.Count > 120)
                _log.RemoveAt(0);
            LogText.Text = string.Join(Environment.NewLine, _log);
        }
    }

    private void ShowDoctor()
    {
        (bool ok, IReadOnlyList<string> lines) = Doctor.Collect(skipPorts: true);

        var text = new TextBox
        {
            Text = string.Join(Environment.NewLine, lines),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        var body = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        body.Children.Add(close);
        body.Children.Add(new ScrollViewer { Content = text });

        var window = new Window
        {
            Title = ok ? "Gyropad doctor - ready" : "Gyropad doctor - problems found",
            Width = 680,
            Height = 560,
            Background = new SolidColorBrush(Color.Parse("#14171c")),
            Content = body
        };
        close.Click += (_, _) => window.Close();
        window.ShowDialog(this);
    }
}
