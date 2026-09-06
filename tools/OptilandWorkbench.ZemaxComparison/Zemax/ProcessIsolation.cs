using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OptilandWorkbench.ZemaxComparison.Zemax;

public sealed record ProcessResult(int ExitCode, bool TimedOut, bool Cancelled, string StandardOutput, string StandardError);
public static class ProcessIsolation
{
    public static async Task<ProcessResult> Run(string executable, IEnumerable<string> arguments, string directory,
        int seconds, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) info.Environment[key] = value;
        using var process = new Process { StartInfo = info };
        process.Start();
        using var job = OperatingSystem.IsWindows() ? WindowsJob.Attach(process) : null;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            if (job is not null) job.Dispose();
            else if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new(4, timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested, cancellationToken.IsCancellationRequested,
                await stdout, await stderr);
        }
        // A child may inherit stdout/stderr after its host exits. Close the owned job before draining
        // pipes so an orphan cannot bypass the per-worker timeout by keeping those handles open.
        job?.Dispose();
        return new(process.ExitCode, false, false, await stdout, await stderr);
    }

    // Closing this job terminates only descendants of this worker. Existing OpticStudio instances are never assigned.
    private sealed class WindowsJob : IDisposable
    {
        private IntPtr _handle;
        public static WindowsJob Attach(Process process)
        {
            var job = new WindowsJob { _handle = CreateJobObject(IntPtr.Zero, null) };
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (job._handle == IntPtr.Zero || !SetInformationJobObject(job._handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())
                || !AssignProcessToJobObject(job._handle, process.Handle))
            {
                var error = Marshal.GetLastWin32Error();
                job.Dispose(); if (!process.HasExited) process.Kill(true);
                throw new System.ComponentModel.Win32Exception(error, "Cannot establish worker process isolation");
            }
            return job;
        }
        public void Dispose() { if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic; public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimits value, int length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
