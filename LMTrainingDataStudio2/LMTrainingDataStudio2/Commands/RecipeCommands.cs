using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Commands;

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
