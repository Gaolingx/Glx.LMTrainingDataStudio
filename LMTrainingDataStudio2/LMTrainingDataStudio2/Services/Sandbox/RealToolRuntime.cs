using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Simulation;

namespace LMTrainingDataStudio2.Services.Sandbox;

/// <summary>
/// Executes the built-in agent tools for real: python, powershell, bash and web_fetch.
/// Each call runs in a fresh temporary directory with a scrubbed environment and sandbox limits.
/// </summary>
public sealed class RealToolRuntime : IToolRuntime
{
    private static readonly HttpClient DefaultHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SandboxLimits _limits;
    private readonly ISandboxProcessRunner _runner;
    private readonly HttpClient _httpClient;

    public RealToolRuntime(
        SandboxLimits? limits,
        ISandboxProcessRunner runner,
        HttpClient? httpClient = null)
    {
        _limits = limits ?? new SandboxLimits();
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _httpClient = httpClient ?? DefaultHttpClient;
    }

    public async Task<string> ExecuteAsync(
        SimulatedToolRule rule,
        ToolCall call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(call);

        var arguments = ParseArguments(rule.Name, call);
        var tool = rule.Name.Trim().ToLowerInvariant();

        return tool switch
        {
            "python" => await RunPythonAsync(rule.Name, arguments, cancellationToken),
            "powershell" => await RunPowerShellAsync(rule.Name, arguments, cancellationToken),
            "bash" => await RunBashAsync(rule.Name, arguments, cancellationToken),
            "web_fetch" => await FetchAsync(rule.Name, arguments, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Tool '{rule.Name}' has no real sandbox executor. Supported tools: python, powershell, bash, web_fetch.")
        };
    }

    private async Task<string> RunPythonAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var code = RequireString(arguments, toolName, "code");
        var executable = string.IsNullOrWhiteSpace(_limits.PythonExecutable)
            ? "python"
            : _limits.PythonExecutable.Trim();

        return await RunScriptAsync(
            toolName,
            executable,
            new[] { "-I", "main.py" },
            "main.py",
            code,
            new Dictionary<string, string>
            {
                ["PYTHONUNBUFFERED"] = "1",
                ["PYTHONDONTWRITEBYTECODE"] = "1",
                ["PYTHONNOUSERSITE"] = "1"
            },
            cancellationToken);
    }

    private async Task<string> RunPowerShellAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var command = RequireString(arguments, toolName, "command");
        var executable = string.IsNullOrWhiteSpace(_limits.PowerShellExecutable)
            ? (OperatingSystem.IsWindows() ? "powershell" : "pwsh")
            : _limits.PowerShellExecutable.Trim();

        return await RunScriptAsync(
            toolName,
            executable,
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "script.ps1" },
            "script.ps1",
            command,
            new Dictionary<string, string> { ["POWERSHELL_TELEMETRY_OPTOUT"] = "1" },
            cancellationToken);
    }

    private async Task<string> RunBashAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var command = RequireString(arguments, toolName, "command");
        var executable = string.IsNullOrWhiteSpace(_limits.BashExecutable)
            ? "bash"
            : _limits.BashExecutable.Trim();

        return await RunScriptAsync(
            toolName,
            executable,
            new[] { "--noprofile", "--norc", "-c", command },
            scriptFileName: null,
            scriptContent: null,
            environment: null,
            cancellationToken);
    }

    private async Task<string> RunScriptAsync(
        string toolName,
        string executable,
        string[] arguments,
        string? scriptFileName,
        string? scriptContent,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var directory = CreateSandboxDirectory();
        try
        {
            if (scriptFileName is not null && scriptContent is not null)
            {
                var scriptPath = Path.Combine(directory, scriptFileName);
                await File.WriteAllTextAsync(scriptPath, scriptContent, Utf8NoBom, cancellationToken);
            }

            var result = await _runner.RunAsync(
                new SandboxProcessRequest(
                    executable,
                    arguments,
                    _limits,
                    directory,
                    StandardInput: null,
                    environment),
                cancellationToken);

            return FormatResult(toolName, result);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private async Task<string> FetchAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!_limits.AllowNetwork)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' is blocked: real web access is disabled. Enable AllowNetwork in the sandbox settings.");
        }

        var url = RequireString(arguments, toolName, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' requires an absolute http(s) URL; got '{url}'.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' does not allow credentials embedded in URLs.");
        }

        await EnsureHostAllowedAsync(uri, cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_limits.TimeoutMs > 0)
        {
            timeoutCts.CancelAfter(_limits.TimeoutMs);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("LMTrainingDataStudio2-Sandbox/1.0");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Tool '{toolName}' timed out after {_limits.TimeoutMs} ms.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' fetch failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            var maxBytes = _limits.MaxFetchBytes <= 0 ? int.MaxValue : _limits.MaxFetchBytes;
            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
            var chunk = new byte[8192];
            var truncated = false;

            while (true)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(), timeoutCts.Token);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > maxBytes)
                {
                    var take = (int)Math.Max(0, maxBytes - buffer.Length);
                    buffer.Write(chunk, 0, take);
                    truncated = true;
                    break;
                }

                buffer.Write(chunk, 0, read);
            }

            var text = Encoding.UTF8.GetString(buffer.ToArray());
            return truncated
                ? text + "\n[truncated at " + maxBytes + " bytes]"
                : text;
        }
    }

    private async Task EnsureHostAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (_limits.AllowPrivateNetwork)
        {
            return;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"Tool 'web_fetch' could not resolve host '{uri.Host}': {ex.Message}", ex);
            }
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new InvalidOperationException(
                $"Tool 'web_fetch' blocked host '{uri.Host}' because it resolves to a loopback or private address. " +
                "Enable AllowPrivateNetwork to override.");
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   (bytes[0] & 0xFE) == 0xFC ||
                   (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
        }

        return true;
    }

    private static JsonElement ParseArguments(string toolName, ToolCall call)
    {
        var raw = call.Function.Arguments switch
        {
            null => "{}",
            string text => string.IsNullOrWhiteSpace(text) ? "{}" : text,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(call.Function.Arguments)
        };

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Tool '{toolName}' arguments must be a JSON object; got {document.RootElement.ValueKind}.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' received invalid JSON arguments: {ex.Message}", ex);
        }
    }

    private static string RequireString(JsonElement arguments, string toolName, string property)
    {
        if (!arguments.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' requires a non-empty string argument '{property}'.");
        }

        return value.GetString()!;
    }

    private static string FormatResult(string toolName, SandboxProcessResult result)
    {
        if (result.TimedOut)
        {
            throw new TimeoutException(
                $"Tool '{toolName}' timed out after {result.Duration.TotalMilliseconds:F0} ms.");
        }

        var stdout = result.StandardOutput.Trim();
        var stderr = result.StandardError.Trim();

        if (result.ExitCode != 0)
        {
            var details = new StringBuilder();
            details.Append($"Tool '{toolName}' exited with code {result.ExitCode}.");
            if (stdout.Length > 0)
            {
                details.Append(" stdout: ").Append(Truncate(stdout, 1000));
            }

            if (stderr.Length > 0)
            {
                details.Append(" stderr: ").Append(Truncate(stderr, 1000));
            }

            throw new InvalidOperationException(details.ToString());
        }

        var output = stdout.Length > 0 ? stdout : stderr;
        if (result.OutputTruncated)
        {
            output += "\n[output truncated]";
        }

        return output.Length > 0 ? output : $"Tool '{toolName}' completed with no output.";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "LMTrainingDataStudio", "sandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
