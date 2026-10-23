using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkProbe;

/// <summary>
/// Finds clumsy.exe and checks that it can run on this computer. clumsy's WinDivert driver has to match the
/// operating system, so only a build for this computer's processor is accepted (the same rule as
/// <c>Get-PeMachine</c> in <c>tmp/handtest_blackhole.ps1</c>).
/// </summary>
internal static class ClumsyLocator
{
    /// <summary>
    /// Finds the clumsy.exe to use: the one named on the command line, else the first build for this computer's
    /// processor found in the usual places (Downloads, Desktop, Documents, C:\tools, Program Files; depth 4).
    /// </summary>
    /// <param name="explicitPath">The <c>--clumsy</c> value, or null.</param>
    /// <returns>The full path of a usable clumsy.exe.</returns>
    /// <exception cref="ProbeRefusalException">No usable clumsy.exe: names what was found and what is needed.</exception>
    public static string Resolve(string? explicitPath)
    {
        var wanted = WantedMachine();
        if (explicitPath is not null)
        {
            if (!File.Exists(explicitPath))
                throw new ProbeRefusalException($"--clumsy '{explicitPath}' does not exist.");
            var machine = PeMachine(explicitPath);
            if (machine != wanted)
                throw new ProbeRefusalException(
                    $"'{explicitPath}' is a {machine} build; this computer needs a {wanted} build of clumsy.");
            return Path.GetFullPath(explicitPath);
        }

        var found = new List<string>();
        foreach (var root in SearchRoots().Where(Directory.Exists))
        {
            foreach (var path in EnumerateClumsy(root))
                if (!found.Contains(path, StringComparer.OrdinalIgnoreCase))
                    found.Add(path);
        }

        foreach (var path in found)
            if (PeMachine(path) == wanted)
                return path;

        var seen = found.Count == 0
            ? "No clumsy.exe was found in Downloads, Desktop, Documents, C:\\tools or Program Files."
            : "Found, but not usable:\n" + string.Join("\n", found.Select(p => $"  {p}  [{PeMachine(p)}]"));
        throw new ProbeRefusalException(
            $"No usable clumsy.exe: this computer needs a {wanted} build.\n{seen}\n" +
            "Fix: pass --clumsy <path to the x64 clumsy.exe>.");
    }

    /// <summary>
    /// Whether a clumsy is running right now. clumsy allows only one instance, so the probe refuses to start while
    /// one runs; it never stops it (it did not start it).
    /// </summary>
    /// <returns>The process ids of running clumsy processes (detection only).</returns>
    public static IReadOnlyList<int> RunningInstances()
    {
        var pids = new List<int>();
        foreach (var process in Process.GetProcessesByName("clumsy"))
        {
            pids.Add(process.Id);
            process.Dispose();
        }
        return pids;
    }

    /// <summary>
    /// The processor type this computer needs a clumsy build for.
    /// </summary>
    /// <returns><c>x64</c>, <c>x86</c> or <c>arm64</c>.</returns>
    public static string WantedMachine() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    /// <summary>
    /// Reads the processor type a Windows program was built for from its PE header.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><c>x64</c>, <c>x86</c>, <c>arm64</c>, <c>unknown</c>, <c>not a Windows program</c> or <c>unreadable</c>.</returns>
    public static string PeMachine(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[1024];
            var count = stream.Read(buffer, 0, buffer.Length);
            if (count < 0x40 || buffer[0] != 0x4D || buffer[1] != 0x5A)
                return "not a Windows program";

            var peOffset = BitConverter.ToInt32(buffer, 0x3C);
            if (peOffset < 0 || peOffset + 6 > count)
                return "not a Windows program";

            return BitConverter.ToUInt16(buffer, peOffset + 4) switch
            {
                0x8664 => "x64",
                0x014C => "x86",
                0xAA64 => "arm64",
                _ => "unknown"
            };
        }
        catch
        {
            return "unreadable";
        }
    }

    /// <summary>The folders searched, in the order the spec lists them.</summary>
    /// <returns>Folder paths (some may not exist).</returns>
    private static IEnumerable<string> SearchRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return @"C:\tools";
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    /// <summary>Finds clumsy.exe under a folder, at most four levels deep, ignoring folders that can't be read.</summary>
    /// <param name="root">The folder to search.</param>
    /// <returns>Full paths.</returns>
    private static IEnumerable<string> EnumerateClumsy(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 4,
            IgnoreInaccessible = true
        };
        try { return Directory.EnumerateFiles(root, "clumsy.exe", options).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
