using System.IO.Pipes;
using System.Text;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Ensures a single running instance and forwards recipe paths from later
/// launches to the primary instance over a named pipe.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\LMTrainingDataStudio2.SingleInstance";

    private static readonly string PipeName =
        "LMTrainingDataStudio2.Open." + Sanitize(Environment.UserName);

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly bool _ownsMutex;
    private Task? _serverTask;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        if (createdNew)
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero);
            IsFirstInstance = _ownsMutex;
            return;
        }

        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero);
            IsFirstInstance = _ownsMutex;
        }
        catch (AbandonedMutexException)
        {
            // The previous instance crashed; this process becomes the primary instance.
            _ownsMutex = true;
            IsFirstInstance = true;
        }
    }

    public bool IsFirstInstance { get; }

    /// <summary>
    /// Starts listening for recipe paths from later launches.
    /// </summary>
    public void StartServer(Action<string> onRecipeRequested)
    {
        if (!IsFirstInstance || _serverTask is not null) return;

        _serverTask = Task.Run(() => RunServerAsync(onRecipeRequested, _cancellation.Token));
    }

    /// <summary>
    /// Sends a recipe path to the primary instance. Returns false when it is not reachable.
    /// </summary>
    public static bool TrySendToPrimary(string recipePath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(recipePath);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task RunServerAsync(Action<string> onRecipeRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var path = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    onRecipeRequested(path.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client disconnected early; keep listening.
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        return builder.Length == 0 ? "user" : builder.ToString();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Shutting down; nothing else to do.
        }

        _cancellation.Dispose();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        }

        _mutex.Dispose();
    }
}
