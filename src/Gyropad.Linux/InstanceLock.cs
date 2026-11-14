using System;
using System.IO;
using Gyropad.Core.Config;
using Gyropad.Core.State;

namespace Gyropad.Linux;

/// <summary>
/// Keeps a second instance (window or headless) from taking the ports and the
/// runtime status file. The lock is a file handle held for the process
/// lifetime; the kernel releases it if the process dies, so a crashed run does
/// not leave a stuck lock.
/// </summary>
internal sealed class InstanceLock : IDisposable
{
    private readonly FileStream? _stream;

    private InstanceLock(FileStream? stream)
    {
        _stream = stream;
    }

    public static InstanceLock? TryAcquire(out string error, out string warning)
    {
        error = "";
        warning = "";

        string directory = Store.ConfigDirectory;
        string path = Path.Combine(directory, "instance.lock");

        try
        {
            Directory.CreateDirectory(directory);
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            byte[] pid = System.Text.Encoding.ASCII.GetBytes(Environment.ProcessId.ToString());
            stream.SetLength(0);
            stream.Write(pid);
            stream.Flush();

            return new InstanceLock(stream);
        }
        catch (UnauthorizedAccessException)
        {
            // The config directory is not writable; the rest of the app degrades
            // the same way (settings are best effort), so keep running.
            warning = "warning: cannot create " + path + "; the single-instance guard is off";
            return new InstanceLock(null);
        }
        catch (IOException)
        {
            RuntimeStatus? status = RuntimeStatus.Read();
            if (status != null && RuntimeStatus.IsProcessAlive(status.Pid))
                error = "Gyropad is already running (pid " + status.Pid + ", " + status.Mode + ").";
            else
                error = "Gyropad is already running (another process holds " + path + ").";
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
    }
}
