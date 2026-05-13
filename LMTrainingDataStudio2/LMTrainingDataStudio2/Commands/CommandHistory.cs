namespace LMTrainingDataStudio2.Commands;

/// <summary>
/// Interface for undoable commands (Command Pattern).
/// </summary>
public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

/// <summary>
/// Manages undo/redo history for the recipe editor.
/// </summary>
public sealed class CommandHistory
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private const int MaxHistorySize = 100;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? HistoryChanged;

    /// <summary>
    /// Executes a command and pushes it onto the undo stack.
    /// </summary>
    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        // Trim history if too large
        if (_undoStack.Count > MaxHistorySize)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < MaxHistorySize; i++)
                _undoStack.Push(items[i]);
        }

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Undoes the last command.
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Redoes the last undone command.
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all history.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
