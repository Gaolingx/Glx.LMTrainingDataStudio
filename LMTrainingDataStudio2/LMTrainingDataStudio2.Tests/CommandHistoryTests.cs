using LMTrainingDataStudio2.Commands;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class CommandHistoryTests
{
    [Test]
    public void Execute_Undo_Redo_TransitionsHistoryState()
    {
        var history = new CommandHistory();
        var command = new RecordingCommand();

        history.Execute(command);
        Assert.That(command.ExecuteCount, Is.EqualTo(1));
        Assert.That(history.CanUndo, Is.True);
        Assert.That(history.CanRedo, Is.False);

        history.Undo();
        Assert.That(command.UndoCount, Is.EqualTo(1));
        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.True);

        history.Redo();
        Assert.That(command.ExecuteCount, Is.EqualTo(2));
        Assert.That(history.CanUndo, Is.True);
        Assert.That(history.CanRedo, Is.False);
    }

    [Test]
    public void Execute_AfterUndo_ClearsRedoStack()
    {
        var history = new CommandHistory();
        history.Execute(new RecordingCommand());
        history.Undo();

        history.Execute(new RecordingCommand());

        Assert.That(history.CanRedo, Is.False);
    }

    [Test]
    public void History_IsCappedAtOneHundredEntries()
    {
        var history = new CommandHistory();
        for (var i = 0; i < 105; i++)
            history.Execute(new RecordingCommand());

        var undoCount = 0;
        while (history.CanUndo)
        {
            history.Undo();
            undoCount++;
        }

        Assert.That(undoCount, Is.EqualTo(100));
    }

    [Test]
    public void Clear_EmptiesBothStacks()
    {
        var history = new CommandHistory();
        history.Execute(new RecordingCommand());
        history.Undo();

        history.Clear();

        Assert.That(history.CanUndo, Is.False);
        Assert.That(history.CanRedo, Is.False);
    }

    private sealed class RecordingCommand : IUndoableCommand
    {
        public string Description => "record";
        public int ExecuteCount { get; private set; }
        public int UndoCount { get; private set; }

        public void Execute() => ExecuteCount++;
        public void Undo() => UndoCount++;
    }
}
