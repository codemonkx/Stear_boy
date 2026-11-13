using System.Globalization;

namespace Gyropad.Core.Linux;

/// <summary>
/// Best-effort Linux equivalent of the original Windows port-owner lookup:
/// finds the process holding a UDP port so the UI can say "port 5005 is used by
/// X" instead of a generic error.
/// </summary>
public static class PortOwner
{
    public static string? Describe(int port)
    {
        try
        {
            long inode = FindUdpInode(port);
            if (inode <= 0)
                return null;

            int pid = FindPidForSocket(inode);
            if (pid <= 0)
                return null;

            string name = ReadComm(pid);
            return string.IsNullOrEmpty(name)
                ? $"another program (process {pid})"
                : $"{name} (process {pid})";
        }
        catch
        {
            return null;
        }
    }

    private static long FindUdpInode(int port)
    {
        string hexPort = port.ToString("X4", CultureInfo.InvariantCulture);
        foreach (string file in new[] { "/proc/net/udp", "/proc/net/udp6" })
        {
            if (!File.Exists(file))
                continue;

            foreach (string line in File.ReadLines(file).Skip(1))
            {
                string[] cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 10)
                    continue;

                // local_address is "ADDR:PORT" in hex.
                int colon = cols[1].LastIndexOf(':');
                if (colon < 0)
                    continue;
                string localPort = cols[1][(colon + 1)..];
                if (!string.Equals(localPort, hexPort, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (long.TryParse(cols[9], out long inode) && inode > 0)
                    return inode;
            }
        }

        return 0;
    }

    private static int FindPidForSocket(long inode)
    {
        string needle = $"socket:[{inode}]";
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            string name = Path.GetFileName(dir);
            if (!int.TryParse(name, out int pid))
                continue;

            string fdDir = Path.Combine(dir, "fd");
            try
            {
                foreach (string fd in Directory.EnumerateFiles(fdDir))
                {
                    try
                    {
                        FileSystemInfo? target = new FileInfo(fd).ResolveLinkTarget(returnFinalTarget: false);
                        if (target != null && target.Name == needle)
                            return pid;
                    }
                    catch
                    {
                        // fd closed while scanning
                    }
                }
            }
            catch
            {
                // process exited or not readable
            }
        }

        return 0;
    }

    private static string ReadComm(int pid)
    {
        try
        {
            string comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
            return comm;
        }
        catch
        {
            return "";
        }
    }
}
