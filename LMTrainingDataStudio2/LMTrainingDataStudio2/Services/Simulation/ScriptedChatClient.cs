using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Services.Simulation;

/// <summary>
/// Deterministic simulated API. Plays back a configured script per trajectory;
/// when no script is configured it calls every exposed tool once, then finishes.
/// </summary>
public sealed class ScriptedChatClient : IChatCompletionClient
{
    private readonly SimulatedTurn[] _script;
    private readonly string _finalContent;
    private int _turn;

    public ScriptedChatClient(IReadOnlyList<SimulatedTurn>? script = null, string? finalContent = null)
    {
        _script = script?.ToArray() ?? Array.Empty<SimulatedTurn>();
        _finalContent = string.IsNullOrWhiteSpace(finalContent) ? "Task completed." : finalContent;
    }

    public Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_script.Length > 0)
        {
            if (_turn < _script.Length)
            {
                var turn = _script[_turn];
                return Task.FromResult(new ChatCompletionResponse(BuildMessage(turn, _turn++)));
            }

            return Task.FromResult(new ChatCompletionResponse(new ChatMessage
            {
                Role = "assistant",
                Content = _finalContent
            }));
        }

        if (_turn == 0 && request.Tools.Count > 0)
        {
            _turn++;
            var calls = request.Tools
                .Select((tool, index) => new ToolCall
                {
                    Id = $"call_0_{index}",
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = tool.Function.Name,
                        Arguments = "{}"
                    }
                })
                .ToList();

            return Task.FromResult(new ChatCompletionResponse(new ChatMessage
            {
                Role = "assistant",
                Content = string.Empty,
                ToolCalls = calls
            }));
        }

        return Task.FromResult(new ChatCompletionResponse(new ChatMessage
        {
            Role = "assistant",
            Content = _finalContent
        }));
    }

    private ChatMessage BuildMessage(SimulatedTurn turn, int index)
    {
        var message = new ChatMessage
        {
            Role = "assistant",
            Content = turn.Content ?? string.Empty
        };

        if (turn.ToolCalls is { Length: > 0 })
        {
            message.ToolCalls = turn.ToolCalls
                .Select((spec, callIndex) => new ToolCall
                {
                    Id = $"call_{index}_{callIndex}",
                    Type = "function",
                    Function = new ToolCallFunction
                    {
                        Name = spec.Name,
                        Arguments = spec.ArgumentsJson
                    }
                })
                .ToList();
        }

        return message;
    }
}
