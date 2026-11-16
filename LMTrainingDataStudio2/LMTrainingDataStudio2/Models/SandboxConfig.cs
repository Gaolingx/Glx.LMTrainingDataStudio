namespace LMTrainingDataStudio2.Models;

/// <summary>
/// Selects how simulation tools are executed.
/// </summary>
public enum ToolExecutionMode
{
    /// <summary>Deterministic in-process responses; no process or network access.</summary>
    Simulated,

    /// <summary>Real process execution inside a restricted sandbox directory.</summary>
    Real
}

/// <summary>
/// Resource and isolation limits applied to real sandboxed tool execution.
/// </summary>
public sealed record SandboxLimits
{
    /// <summary>Hard wall-clock limit for one process or HTTP request; 0 disables the limit.</summary>
    public int TimeoutMs { get; init; } = 30_000;

    /// <summary>Maximum captured stdout/stderr size in bytes; 0 means unlimited.</summary>
    public int MaxOutputBytes { get; init; } = 64 * 1024;

    /// <summary>Process memory limit in megabytes; 0 disables the limit. Windows Job Object only.</summary>
    public int MaxMemoryMb { get; init; } = 512;

    /// <summary>Maximum processes allowed in the sandbox job; 0 disables the limit.</summary>
    public int MaxProcesses { get; init; } = 8;

    /// <summary>Allows the web_fetch tool to make HTTP requests.</summary>
    public bool AllowNetwork { get; init; }

    /// <summary>Allows web_fetch to reach loopback and private network addresses.</summary>
    public bool AllowPrivateNetwork { get; init; }

    /// <summary>Maximum downloaded response size for web_fetch in bytes.</summary>
    public int MaxFetchBytes { get; init; } = 256 * 1024;

    /// <summary>Python executable used by the python tool; null resolves "python" from PATH.</summary>
    public string? PythonExecutable { get; init; }

    /// <summary>PowerShell executable used by the powershell tool; null resolves the platform default.</summary>
    public string? PowerShellExecutable { get; init; }

    /// <summary>Bash executable used by the bash tool; null resolves "bash" from PATH.</summary>
    public string? BashExecutable { get; init; }
}
