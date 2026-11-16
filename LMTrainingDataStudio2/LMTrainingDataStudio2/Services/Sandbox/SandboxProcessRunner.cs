using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Sandbox;

/// <summary>
/// One sandboxed process invocation. Arguments are passed as an argv list; no shell is involved.
/// </summary>
public sealed record SandboxProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    SandboxLimits Limits,
    string? WorkingDirectory = null,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record SandboxProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool OutputTruncated,
    TimeSpan Duration);

public interface ISandboxProcessRunner
{
    Task<SandboxProcessResult> RunAsync(
        SandboxProcessRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs a child process with a minimal environment, capped output, wall-clock timeout
/// and process-tree termination. On Windows an additional Job Object enforces memory
/// and active-process limits; on other platforms those two limits are not available.
/// </summary>
public sealed class SandboxProcessRunner : ISandboxProcessRunner
{
    private static readonly string[] EnvironmentAllowList =
    {
        "PATH", "PATHEXT", "SystemRoot", "SYSTEMROOT", "windir", "WINDIR",
        "COMSPEC", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE",
        "USERNAME", "USER", "SHELL", "LANG", "LC_ALL", "NUMBER_OF_PROCESSORS",
        "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT"
    };

    public async Task<SandboxProcessResult> RunAsync(
        SandboxProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
        }

        startInfo.Environment.Clear();
        foreach (var name in EnvironmentAllowList)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                startInfo.Environment[name] = value;
            }
        }

        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to start sandbox process '{request.FileName}': {ex.Message}", ex);
        }

        using var jobLimits = WindowsJobLimits.TryApply(process, request.Limits);

        var outputTask = ReadCappedAsync(process.StandardOutput, request.Limits.MaxOutputBytes);
        var errorTask = ReadCappedAsync(process.StandardError, request.Limits.MaxOutputBytes);

        try
        {
            if (request.StandardInput is null)
            {
                process.StandardInput.Close();
            }
            else
            {
                await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process may exit before consuming stdin; the exit code carries the real outcome.
        }

        var timedOut = false;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Limits.TimeoutMs > 0)
        {
            timeoutCts.CancelAfter(request.Limits.TimeoutMs);
        }

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            KillTree(process, request.FileName);
        }
        catch (OperationCanceledException)
        {
            KillTree(process, request.FileName);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        stopwatch.Stop();

        return new SandboxProcessResult(
            timedOut ? -1 : process.ExitCode,
            output.Text,
            error.Text,
            timedOut,
            output.Truncated || error.Truncated,
            stopwatch.Elapsed);
    }

    private static void ValidateRequest(SandboxProcessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("Sandbox process file name must not be empty.", nameof(request));
        }

        var limits = request.Limits;
        if (limits.TimeoutMs < 0 || limits.MaxOutputBytes < 0 || limits.MaxMemoryMb < 0 ||
            limits.MaxProcesses < 0 || limits.MaxFetchBytes < 0)
        {
            throw new InvalidOperationException("Sandbox limits must not be negative.");
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(
        StreamReader reader,
        int maxBytes)
    {
        var limit = maxBytes <= 0 ? int.MaxValue : maxBytes;
        var builder = new StringBuilder(Math.Min(limit, 64 * 1024));
        var buffer = new char[8192];
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0)
            {
                break;
            }

            if (builder.Length < limit)
            {
                var take = Math.Min(read, limit - builder.Length);
                builder.Append(buffer, 0, take);
                truncated |= take < read;
            }
            else
            {
                truncated = true;
            }
        }

        return (builder.ToString(), truncated);
    }

    private static void KillTree(Process process, string fileName)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The process exited between the HasExited check and Kill.
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                $"Sandbox process '{fileName}' could not be terminated: {ex.Message}", ex);
        }

        try
        {
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
        }
    }
}

/// <summary>
/// Windows Job Object limits. Memory and active-process limits are only enforced on Windows;
/// the timeout and process-tree kill in <see cref="SandboxProcessRunner"/> apply everywhere.
/// </summary>
internal static class WindowsJobLimits
{
    private const int ExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    public static IDisposable? TryApply(Process process, SandboxLimits limits)
    {
        if (!OperatingSystem.IsWindows() || (limits.MaxMemoryMb <= 0 && limits.MaxProcesses <= 0))
        {
            return null;
        }

        try
        {
            return Apply(process, limits);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // Very short-lived processes can exit before the job assignment; there is nothing left to limit.
            return null;
        }
    }

    private static IDisposable Apply(Process process, SandboxLimits limits)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to create sandbox job object: {Marshal.GetLastWin32Error()}.");
        }

        var handle = new JobHandle(job);
        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                    ActiveProcessLimit = 0
                },
                ProcessMemoryLimit = UIntPtr.Zero
            };

            if (limits.MaxMemoryMb > 0)
            {
                information.BasicLimitInformation.LimitFlags |= JobObjectLimitProcessMemory;
                information.ProcessMemoryLimit = (UIntPtr)((ulong)limits.MaxMemoryMb * 1024 * 1024);
            }

            if (limits.MaxProcesses > 0)
            {
                information.BasicLimitInformation.LimitFlags |= JobObjectLimitActiveProcess;
                information.BasicLimitInformation.ActiveProcessLimit = (uint)limits.MaxProcesses;
            }

            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, pointer, fDeleteOld: false);
                if (!SetInformationJobObject(
                        job, ExtendedLimitInformationClass, pointer, (uint)size))
                {
                    throw new InvalidOperationException(
                        $"Failed to configure sandbox job object: {Marshal.GetLastWin32Error()}.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                throw new InvalidOperationException(
                    $"Failed to assign process to sandbox job object: {Marshal.GetLastWin32Error()}.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class JobHandle(IntPtr handle) : IDisposable
    {
        private IntPtr _handle = handle;

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
