using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Sandbox;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class SandboxTests
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string ShellFile => IsWindows ? "cmd.exe" : "/bin/sh";

    private static string[] ShellArguments(string script) =>
        IsWindows ? new[] { "/c", script } : new[] { "-c", script };

    [Test]
    public async Task RunAsync_Echo_ReturnsStdoutAndZeroExit()
    {
        var result = await RunAsync(ShellArguments("echo hello-sandbox"));

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.StandardOutput.Trim(), Is.EqualTo("hello-sandbox"));
        Assert.That(result.TimedOut, Is.False);
        Assert.That(result.OutputTruncated, Is.False);
    }

    [Test]
    public async Task RunAsync_NonZeroExit_ReturnsErrorAndCode()
    {
        var result = await RunAsync(ShellArguments(IsWindows ? "exit 3" : "echo boom >&2; exit 3"));

        Assert.That(result.ExitCode, Is.EqualTo(3));
        if (!IsWindows)
        {
            Assert.That(result.StandardError.Trim(), Is.EqualTo("boom"));
        }
    }

    [Test]
    public async Task RunAsync_Timeout_KillsProcessAndReportsTimeout()
    {
        var start = DateTimeOffset.UtcNow;
        var result = await RunAsync(
            ShellArguments(IsWindows ? "ping 127.0.0.1 -n 10 > nul" : "sleep 5"),
            new SandboxLimits { TimeoutMs = 300 });

        var elapsed = DateTimeOffset.UtcNow - start;
        Assert.That(result.TimedOut, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(-1));
        Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public async Task RunAsync_OutputExceedsLimit_IsTruncated()
    {
        var directory = CreateTempDirectory();
        try
        {
            var file = Path.Combine(directory, "big.txt");
            await File.WriteAllTextAsync(file, new string('a', 50_000));
            var result = await RunAsync(
                ShellArguments(IsWindows ? "type big.txt" : "cat big.txt"),
                new SandboxLimits { MaxOutputBytes = 1024 },
                workingDirectory: directory);

            Assert.That(result.ExitCode, Is.EqualTo(0), $"stderr: {result.StandardError}");
            Assert.That(result.OutputTruncated, Is.True);
            Assert.That(result.StandardOutput.Length, Is.LessThanOrEqualTo(1024));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_WorkingDirectory_IsApplied()
    {
        var directory = CreateTempDirectory();
        try
        {
            var runner = new SandboxProcessRunner();
            var result = await runner.RunAsync(
                new SandboxProcessRequest(
                    ShellFile,
                    ShellArguments(IsWindows ? "cd" : "pwd"),
                    new SandboxLimits(),
                    directory),
                CancellationToken.None);

            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(
                Path.GetFullPath(result.StandardOutput.Trim()),
                Is.EqualTo(Path.GetFullPath(directory)).IgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_Environment_IsScrubbedAndCustomValuesApply()
    {
        const string secretName = "LMTDS_SANDBOX_HOST_SECRET";
        Environment.SetEnvironmentVariable(secretName, "should-not-leak");
        try
        {
            var result = await RunAsync(
                ShellArguments(IsWindows
                    ? "echo %LMTDS_SANDBOX_CUSTOM% %LMTDS_SANDBOX_HOST_SECRET%"
                    : "echo \"$LMTDS_SANDBOX_CUSTOM\" \"$LMTDS_SANDBOX_HOST_SECRET\""),
                environment: new Dictionary<string, string>
                {
                    ["LMTDS_SANDBOX_CUSTOM"] = "custom-value"
                });

            Assert.That(result.StandardOutput, Does.Contain("custom-value"));
            Assert.That(result.StandardOutput, Does.Not.Contain("should-not-leak"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, null);
        }
    }

    [Test]
    public void RunAsync_Cancelled_ThrowsAndKillsProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var runner = new SandboxProcessRunner();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(
                new SandboxProcessRequest(
                    ShellFile,
                    ShellArguments(IsWindows ? "ping 127.0.0.1 -n 10 > nul" : "sleep 5"),
                    new SandboxLimits { TimeoutMs = 30_000 }),
                cancellation.Token));
    }

    [Test]
    public async Task RunAsync_MemoryLimit_IsEnforcedOnWindows()
    {
        if (!IsWindows)
        {
            Assert.Ignore("Job Object memory limits are Windows-only.");
        }

        if (!IsExecutableAvailable("python", "--version"))
        {
            Assert.Ignore("python is not installed.");
        }

        var runner = new SandboxProcessRunner();
        var result = await runner.RunAsync(
            new SandboxProcessRequest(
                "python",
                new[] { "-c", "x=bytearray(512*1024*1024);print(len(x))" },
                new SandboxLimits { TimeoutMs = 20_000, MaxMemoryMb = 128 }),
            CancellationToken.None);

        Assert.That(result.TimedOut, Is.False);
        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
    }

    [Test]
    public async Task RealToolRuntime_Python_ExecutesRealCode()
    {
        if (!IsExecutableAvailable("python", "--version"))
        {
            Assert.Ignore("python is not installed.");
        }

        var runtime = new RealToolRuntime(
            new SandboxLimits { TimeoutMs = 20_000 },
            new SandboxProcessRunner());

        var output = await runtime.ExecuteAsync(
            PythonRule(),
            Call("python", """{"code":"print(6*7)"}"""),
            CancellationToken.None);

        Assert.That(output.Trim(), Is.EqualTo("42"));
    }

    [Test]
    public async Task RealToolRuntime_PowerShell_ExecutesRealScript()
    {
        var executable = IsWindows ? "powershell" : "pwsh";
        if (!IsExecutableAvailable(executable, "-NoProfile -Command exit"))
        {
            Assert.Ignore($"{executable} is not installed.");
        }

        var runtime = new RealToolRuntime(
            new SandboxLimits
            {
                TimeoutMs = 20_000,
                PowerShellExecutable = executable
            },
            new SandboxProcessRunner());

        var output = await runtime.ExecuteAsync(
            Rule("powershell"),
            Call("powershell", """{"command":"Write-Output (6*7)"}"""),
            CancellationToken.None);

        Assert.That(output.Trim(), Is.EqualTo("42"));
    }

    [Test]
    public async Task RealToolRuntime_Bash_ExecutesRealCommand()
    {
        if (!IsExecutableAvailable("bash", "--version"))
        {
            Assert.Ignore("bash is not installed.");
        }

        var runtime = new RealToolRuntime(
            new SandboxLimits { TimeoutMs = 20_000 },
            new SandboxProcessRunner());

        var output = await runtime.ExecuteAsync(
            Rule("bash"),
            Call("bash", """{"command":"echo shell-ok"}"""),
            CancellationToken.None);

        Assert.That(output.Trim(), Is.EqualTo("shell-ok"));
    }

    [Test]
    public void RunAsync_NegativeLimits_Throws()
    {
        var runner = new SandboxProcessRunner();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.RunAsync(
                new SandboxProcessRequest(ShellFile, Array.Empty<string>(), new SandboxLimits { TimeoutMs = -1 }),
                CancellationToken.None));
    }

    [Test]
    public async Task RealToolRuntime_Python_WritesScriptAndUsesIsolatedInterpreter()
    {
        var runner = new CapturingRunner(_ => Task.FromResult(
            new SandboxProcessResult(0, "python-ok", string.Empty, false, false, TimeSpan.Zero)));
        var runtime = new RealToolRuntime(
            new SandboxLimits { PythonExecutable = "python-custom" },
            runner);

        var output = await runtime.ExecuteAsync(
            PythonRule(),
            Call("python", """{"code":"print('hi')"}"""),
            CancellationToken.None);

        Assert.That(output, Is.EqualTo("python-ok"));
        Assert.That(runner.Request!.FileName, Is.EqualTo("python-custom"));
        Assert.That(runner.Request.Arguments, Is.EqualTo(new[] { "-I", "main.py" }));
        Assert.That(runner.ScriptFileName, Is.EqualTo("main.py"));
        Assert.That(runner.ScriptContent, Is.EqualTo("print('hi')"));
        Assert.That(runner.Request.Environment!["PYTHONDONTWRITEBYTECODE"], Is.EqualTo("1"));
        Assert.That(Directory.Exists(runner.WorkingDirectoryPath), Is.False);
    }

    [Test]
    public async Task RealToolRuntime_PowerShell_UsesProfileFreeScriptFile()
    {
        var runner = new CapturingRunner(_ => Task.FromResult(
            new SandboxProcessResult(0, "ps-ok", string.Empty, false, false, TimeSpan.Zero)));
        var runtime = new RealToolRuntime(
            new SandboxLimits { PowerShellExecutable = "powershell-custom" },
            runner);

        await runtime.ExecuteAsync(
            Rule("powershell"),
            Call("powershell", """{"command":"Write-Output hi"}"""),
            CancellationToken.None);

        Assert.That(runner.Request!.FileName, Is.EqualTo("powershell-custom"));
        Assert.That(runner.Request.Arguments,
            Is.EqualTo(new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "script.ps1" }));
        Assert.That(runner.ScriptContent, Is.EqualTo("Write-Output hi"));
    }

    [Test]
    public async Task RealToolRuntime_Bash_PassesCommandAsSingleArgument()
    {
        var runner = new CapturingRunner(_ => Task.FromResult(
            new SandboxProcessResult(0, "bash-ok", string.Empty, false, false, TimeSpan.Zero)));
        var runtime = new RealToolRuntime(new SandboxLimits { BashExecutable = "bash-custom" }, runner);

        await runtime.ExecuteAsync(
            Rule("bash"),
            Call("bash", """{"command":"echo hi"}"""),
            CancellationToken.None);

        Assert.That(runner.Request!.FileName, Is.EqualTo("bash-custom"));
        Assert.That(runner.Request.Arguments, Is.EqualTo(new[] { "--noprofile", "--norc", "-c", "echo hi" }));
        Assert.That(runner.ScriptContent, Is.Null);
    }

    [Test]
    public void RealToolRuntime_WebFetch_RequiresNetworkOptIn()
    {
        var runtime = new RealToolRuntime(new SandboxLimits(), new CapturingRunner(_ =>
            throw new InvalidOperationException("should not run")));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(
                Rule("web_fetch"),
                Call("web_fetch", """{"url":"https://example.com"}"""),
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("AllowNetwork"));
    }

    [Test]
    public void RealToolRuntime_WebFetch_BlocksLoopbackAddress()
    {
        var runtime = new RealToolRuntime(
            new SandboxLimits { AllowNetwork = true, AllowPrivateNetwork = false },
            new CapturingRunner(_ => throw new InvalidOperationException("should not run")));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(
                Rule("web_fetch"),
                Call("web_fetch", """{"url":"http://127.0.0.1/admin"}"""),
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("private address"));
    }

    [Test]
    public async Task RealToolRuntime_WebFetch_ReturnsResponseBody()
    {
        var runtime = CreateFetchRuntime(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("page text", Encoding.UTF8, "text/plain")
            },
            maxFetchBytes: 1024);

        var output = await runtime.ExecuteAsync(
            Rule("web_fetch"),
            Call("web_fetch", """{"url":"https://1.1.1.1/page"}"""),
            CancellationToken.None);

        Assert.That(output, Is.EqualTo("page text"));
    }

    [Test]
    public async Task RealToolRuntime_WebFetch_TruncatesLargeResponses()
    {
        var runtime = CreateFetchRuntime(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('a', 100), Encoding.UTF8, "text/plain")
            },
            maxFetchBytes: 10);

        var output = await runtime.ExecuteAsync(
            Rule("web_fetch"),
            Call("web_fetch", """{"url":"https://1.1.1.1/page"}"""),
            CancellationToken.None);

        Assert.That(output, Does.StartWith("aaaaaaaaaa"));
        Assert.That(output, Does.Contain("[truncated at 10 bytes]"));
    }

    [Test]
    public void RealToolRuntime_WebFetch_DoesNotFollowRedirects()
    {
        var runtime = CreateFetchRuntime(
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("http://127.0.0.1/admin") }
            },
            maxFetchBytes: 1024);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(
                Rule("web_fetch"),
                Call("web_fetch", """{"url":"https://1.1.1.1/page"}"""),
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("302"));
    }

    [Test]
    public void RealToolRuntime_UnknownTool_ThrowsWithSupportedList()
    {
        var runtime = new RealToolRuntime(new SandboxLimits(), new CapturingRunner(_ =>
            throw new InvalidOperationException("should not run")));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(Rule("mystery"), Call("mystery", "{}"), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("no real sandbox executor"));
        Assert.That(exception.Message, Does.Contain("python"));
    }

    [Test]
    public void RealToolRuntime_InvalidArgumentsJson_ThrowsWithToolName()
    {
        var runtime = new RealToolRuntime(new SandboxLimits(), new CapturingRunner(_ =>
            throw new InvalidOperationException("should not run")));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(PythonRule(), Call("python", "{not-json"), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("python"));
        Assert.That(exception.Message, Does.Contain("invalid JSON"));
    }

    [Test]
    public void RealToolRuntime_NonZeroExit_IncludesExitCodeAndStderr()
    {
        var runner = new CapturingRunner(_ => Task.FromResult(
            new SandboxProcessResult(2, "partial", "traceback", false, false, TimeSpan.Zero)));
        var runtime = new RealToolRuntime(new SandboxLimits(), runner);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.ExecuteAsync(PythonRule(), Call("python", """{"code":"boom"}"""), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("exited with code 2"));
        Assert.That(exception.Message, Does.Contain("traceback"));
    }

    [Test]
    public void RealToolRuntime_TimeoutResult_ThrowsTimeoutException()
    {
        var runner = new CapturingRunner(_ => Task.FromResult(
            new SandboxProcessResult(-1, string.Empty, string.Empty, true, false, TimeSpan.FromSeconds(1))));
        var runtime = new RealToolRuntime(new SandboxLimits(), runner);

        var exception = Assert.CatchAsync<TimeoutException>(async () =>
            await runtime.ExecuteAsync(PythonRule(), Call("python", """{"code":"sleep"}"""), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("timed out"));
    }

    private static async Task<SandboxProcessResult> RunAsync(
        string[] shellArguments,
        SandboxLimits? limits = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        var runner = new SandboxProcessRunner();
        var request = new SandboxProcessRequest(
            ShellFile,
            shellArguments,
            limits ?? new SandboxLimits(),
            workingDirectory,
            Environment: environment);
        return await runner.RunAsync(request, CancellationToken.None);
    }

    private static SimulatedToolRule PythonRule() => new()
    {
        Name = "python",
        Description = "run python"
    };

    private static SimulatedToolRule Rule(string name) => new()
    {
        Name = name,
        Description = name
    };

    private static ToolCall Call(string name, string arguments) => new()
    {
        Id = "call_1",
        Type = "function",
        Function = new ToolCallFunction { Name = name, Arguments = arguments }
    };

    private static RealToolRuntime CreateFetchRuntime(
        HttpResponseMessage response,
        int maxFetchBytes) =>
        new(
            new SandboxLimits
            {
                AllowNetwork = true,
                TimeoutMs = 5000,
                MaxFetchBytes = maxFetchBytes
            },
            new CapturingRunner(_ => throw new InvalidOperationException("process runner should not be used")),
            new HttpClient(new FixedResponseHandler(response)));

    private static bool IsExecutableAvailable(string fileName, string arguments)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lmt-ds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FixedResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class CapturingRunner : ISandboxProcessRunner
    {
        private readonly Func<SandboxProcessRequest, Task<SandboxProcessResult>> _handler;

        public CapturingRunner(Func<SandboxProcessRequest, Task<SandboxProcessResult>> handler) =>
            _handler = handler;

        public SandboxProcessRequest? Request { get; private set; }

        public string? ScriptFileName { get; private set; }

        public string? ScriptContent { get; private set; }

        public string? WorkingDirectoryPath { get; private set; }

        public async Task<SandboxProcessResult> RunAsync(
            SandboxProcessRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            WorkingDirectoryPath = request.WorkingDirectory;

            if (request.WorkingDirectory is not null)
            {
                var file = Directory.EnumerateFiles(request.WorkingDirectory).FirstOrDefault();
                if (file is not null)
                {
                    ScriptFileName = Path.GetFileName(file);
                    ScriptContent = await File.ReadAllTextAsync(file, cancellationToken);
                }
            }

            return await _handler(request);
        }
    }
}
