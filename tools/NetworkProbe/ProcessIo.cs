using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkProbe;

/// <summary>
/// This process's own I/O byte counters (Windows <c>GetProcessIoCounters</c>; .NET has no managed equivalent).
///
/// <para><b>Why the probe needs it:</b> to cut a long read part-way through, the probe must know the read is really
/// reading, not still opening the file. The first real run (2026-10-22) showed the difference matters: the hash case
/// was cut 1.5 s after the call started, and every time the stall landed in the OPEN (a 32 MB file just uploaded can
/// take seconds to open, for example while the server's antivirus scans it). The bytes this process has read since
/// the call started are direct evidence that the read loop is running.</para>
/// </summary>
internal static class ProcessIo
{
    /// <summary>The Windows <c>IO_COUNTERS</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        /// <summary>Number of read operations.</summary>
        public ulong ReadOperationCount;

        /// <summary>Number of write operations.</summary>
        public ulong WriteOperationCount;

        /// <summary>Number of other operations.</summary>
        public ulong OtherOperationCount;

        /// <summary>Bytes read (files, including files on a share).</summary>
        public ulong ReadTransferCount;

        /// <summary>Bytes written.</summary>
        public ulong WriteTransferCount;

        /// <summary>Bytes transferred by other operations.</summary>
        public ulong OtherTransferCount;
    }

    /// <summary>Windows: the I/O counters of a process.</summary>
    /// <param name="process">Handle of the process.</param>
    /// <param name="counters">Receives the counters.</param>
    /// <returns>True on success.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    /// <summary>
    /// The total number of bytes this process has read so far (files on the local disk and on shares alike).
    /// </summary>
    /// <returns>The byte count, or -1 when Windows would not say (the caller then falls back to time alone).</returns>
    public static long ReadBytes()
    {
        using var self = Process.GetCurrentProcess();
        return GetProcessIoCounters(self.Handle, out var counters) ? (long)counters.ReadTransferCount : -1;
    }
}
