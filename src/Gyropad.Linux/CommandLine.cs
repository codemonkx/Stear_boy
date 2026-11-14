using System;
using System.Collections.Generic;

namespace Gyropad.Linux;

internal enum PairPolicy
{
    Ask,
    Auto,
    Deny
}

/// <summary>Parsed command line. See <see cref="Usage"/> for the text users see.</summary>
internal sealed class Options
{
    public bool Headless;
    public bool Status;
    public bool Unpair;
    public bool Verbose;
    public bool Doctor;
    public bool Report;
    public bool Version;
    public bool Help;
    public PairPolicy Pair = PairPolicy.Ask;

    public const string Usage = """
        Gyropad - Linux port

        usage: Gyropad [options]

        Desktop:
          (no options)        open the window
          --doctor            check uinput, permissions, ports and settings

        Headless:
          --headless          run without a desktop, for SSH and minimal installs
          --pair ask          approve pairing on the terminal (default)
          --pair auto         approve every pairing request
          --pair deny         refuse every pairing request
          --verbose           print input values once a second

        One-shot:
          --report            write gyropad-report-*.txt for a bug report
          --status            show the running instance (reads runtime.json)
          --unpair            forget the paired phone in settings.ini
          --version           print the version
          --help              this text

        Notes:
          * --pair ask needs a terminal; with redirected input it declines and
            tells you to use --pair auto.
          * --status exits 1 when no instance is running.
        """;

    public static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options();
        error = "";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--headless":
                    options.Headless = true;
                    break;
                case "--status":
                    options.Status = true;
                    break;
                case "--unpair":
                    options.Unpair = true;
                    break;
                case "--report":
                    options.Report = true;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--doctor":
                case "doctor":
                    options.Doctor = true;
                    break;
                case "--version":
                case "-v":
                    options.Version = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.Help = true;
                    break;
                case "--pair":
                    if (i + 1 >= args.Length)
                    {
                        error = "--pair needs a value: ask, auto or deny";
                        return false;
                    }

                    if (!TryPair(args[++i], out options.Pair, out error))
                        return false;
                    break;
                default:
                    if (arg.StartsWith("--pair=", StringComparison.Ordinal))
                    {
                        if (!TryPair(arg["--pair=".Length..], out options.Pair, out error))
                            return false;
                        break;
                    }

                    error = "unknown option: " + arg;
                    return false;
            }
        }

        var actions = new List<string>();
        if (options.Doctor)
            actions.Add("--doctor");
        if (options.Status)
            actions.Add("--status");
        if (options.Unpair)
            actions.Add("--unpair");
        if (options.Report)
            actions.Add("--report");
        if (options.Headless)
            actions.Add("--headless");

        if (actions.Count > 1)
        {
            error = "choose one of " + string.Join(", ", actions);
            return false;
        }

        return true;
    }

    private static bool TryPair(string value, out PairPolicy policy, out string error)
    {
        error = "";
        switch (value.ToLowerInvariant())
        {
            case "ask":
                policy = PairPolicy.Ask;
                return true;
            case "auto":
            case "allow":
                policy = PairPolicy.Auto;
                return true;
            case "deny":
            case "none":
                policy = PairPolicy.Deny;
                return true;
            default:
                policy = PairPolicy.Ask;
                error = "--pair must be ask, auto or deny";
                return false;
        }
    }
}
