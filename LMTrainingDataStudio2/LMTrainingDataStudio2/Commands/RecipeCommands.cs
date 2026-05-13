using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Commands;

/// <summary>
/// Command that groups several undoable commands into one history entry.
/// </summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> _commands;

    public string Description { get; }

    public CompositeCommand(string description, IEnumerable<IUndoableCommand> commands)
    {
        Description = description;
        _commands = commands.ToList();
    }

    public void Execute()
    {
        foreach (var command in _commands)
            command.Execute();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}

/// <summary>
/// Command for moving several block nodes as one history entry.
/// </summary>
public sealed class MoveBlocksCommand : IUndoableCommand
{
    private readonly List<(BlockNodeViewModel Block, double OldX, double OldY, double NewX, double NewY)> _moves;

    public string Description => "Move blocks";

    public MoveBlocksCommand(IEnumerable<(BlockNodeViewModel Block, double OldX, double OldY, double NewX, double NewY)> moves)
    {
        _moves = moves.ToList();
    }

    public void Execute()
    {
        foreach (var move in _moves)
        {
            move.Block.X = move.NewX;
            move.Block.Y = move.NewY;
        }
    }

    public void Undo()
    {
        foreach (var move in _moves)
        {
            move.Block.X = move.OldX;
            move.Block.Y = move.OldY;
        }
    }
}

/// <summary>
/// Command for deleting blocks and edges as one history entry.
/// </summary>
public sealed class DeleteSelectionCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly List<BlockNodeViewModel> _blocks;
    private readonly List<EdgeViewModel> _edges;

    public string Description => "Delete selection";

    public DeleteSelectionCommand(MainWindowViewModel vm, IEnumerable<BlockNodeViewModel> blocks, IEnumerable<EdgeViewModel> edges)
    {
        _vm = vm;
        _blocks = blocks.DistinctBy(b => b.Id).ToList();
        _edges = edges.DistinctBy(e => e.Id).ToList();
    }

    public void Execute()
    {
        foreach (var edge in _edges)
            _vm.Edges.Remove(edge);
        foreach (var block in _blocks)
            _vm.Blocks.Remove(block);
    }

    public void Undo()
    {
        foreach (var block in _blocks)
            if (!_vm.Blocks.Contains(block)) _vm.Blocks.Add(block);
        foreach (var edge in _edges)
            if (!_vm.Edges.Contains(edge)) _vm.Edges.Add(edge);
    }
}

/// <summary>
/// Command for moving a block node on the canvas.
/// </summary>
public sealed class MoveBlockCommand : IUndoableCommand
{
    private readonly BlockNodeViewModel _block;
    private readonly double _oldX, _oldY;
    private readonly double _newX, _newY;

    public string Description => $"Move '{_block.DisplayName}'";

    public MoveBlockCommand(BlockNodeViewModel block, double oldX, double oldY, double newX, double newY)
    {
        _block = block;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }

    public void Execute()
    {
        _block.X = _newX;
        _block.Y = _newY;
    }

    public void Undo()
    {
        _block.X = _oldX;
        _block.Y = _oldY;
    }
}

/// <summary>
/// Command for adding a block to the recipe.
/// </summary>
public sealed class AddBlockCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly BlockNodeViewModel _block;

    public string Description => $"Add '{_block.DisplayName}'";

    public AddBlockCommand(MainWindowViewModel vm, BlockNodeViewModel block)
    {
        _vm = vm;
        _block = block;
    }

    public void Execute()
    {
        _vm.Blocks.Add(_block);
    }

    public void Undo()
    {
        _vm.Blocks.Remove(_block);
    }
}

/// <summary>
/// Command for deleting a block from the recipe.
/// </summary>
public sealed class DeleteBlockCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly BlockNodeViewModel _block;
    private readonly List<EdgeViewModel> _connectedEdges;

    public string Description => $"Delete '{_block.DisplayName}'";

    public DeleteBlockCommand(MainWindowViewModel vm, BlockNodeViewModel block)
    {
        _vm = vm;
        _block = block;
        _connectedEdges = vm.Edges
            .Where(e => e.SourceBlockId == block.Id || e.TargetBlockId == block.Id)
            .ToList();
    }

    public void Execute()
    {
        foreach (var edge in _connectedEdges)
            _vm.Edges.Remove(edge);
        _vm.Blocks.Remove(_block);
    }

    public void Undo()
    {
        _vm.Blocks.Add(_block);
        foreach (var edge in _connectedEdges)
            _vm.Edges.Add(edge);
    }
}

/// <summary>
/// Command for adding an edge between two ports.
/// </summary>
public sealed class AddEdgeCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly EdgeViewModel _edge;

    public string Description => "Add connection";

    public AddEdgeCommand(MainWindowViewModel vm, EdgeViewModel edge)
    {
        _vm = vm;
        _edge = edge;
    }

    public void Execute()
    {
        _vm.Edges.Add(_edge);
    }

    public void Undo()
    {
        _vm.Edges.Remove(_edge);
    }
}

/// <summary>
/// Command for deleting an edge.
/// </summary>
public sealed class DeleteEdgeCommand : IUndoableCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly EdgeViewModel _edge;

    public string Description => "Delete connection";

    public DeleteEdgeCommand(MainWindowViewModel vm, EdgeViewModel edge)
    {
        _vm = vm;
        _edge = edge;
    }

    public void Execute()
    {
        _vm.Edges.Remove(_edge);
    }

    public void Undo()
    {
        _vm.Edges.Add(_edge);
    }
}
