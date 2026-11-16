using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LMTrainingDataStudio2.Commands;
using LMTrainingDataStudio2.Controls;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.ViewModels;
using Path = Avalonia.Controls.Shapes.Path;

namespace LMTrainingDataStudio2.Views;

public partial class RecipeCanvasView : UserControl
{
    private const string DragBlockFormat = "application/x-lmtds-block-type";
    private const double MiniMapWidth = 160;
    private const double MiniMapHeight = 100;
    private const double MiniMapPadding = 8;

    private Canvas? _canvas;
    private Canvas? _miniMapCanvas;
    private GridBackground? _gridBackground;
    private Border? _marquee;
    private Border? _nodeMenu;
    private ContextMenu? _contextMenu;
    private Path? _previewEdge;

    private bool _isDraggingNode;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _isConnecting;
    private Point _dragStartPoint;
    private Point _panStartPoint;
    private Point _selectionStartPoint;
    private readonly CanvasViewport _viewport = new();
    private BlockNodeViewModel? _draggedBlock;
    private Border? _draggedBorder;
    private BlockNodeViewModel? _connectionSourceBlock;
    private PortViewModel? _connectionSourcePort;
    private Point _lastCanvasMenuPosition = new(300, 200);
    private readonly List<BlockNodeViewModel> _clipboard = new();
    private readonly List<string> _clipboardOriginalIds = new();
    private readonly List<ClipboardEdge> _clipboardEdges = new();
    private readonly Dictionary<string, (BlockNodeViewModel Block, double X, double Y)> _dragInitialPositions = new();
    private readonly Dictionary<string, Border> _blockBorders = new();
    private readonly Dictionary<(string BlockId, string PortId), Point> _portOffsets = new();
    private readonly Dictionary<string, Path> _edgeVisuals = new();
    private readonly Dictionary<string, TextBlock> _edgeLabels = new();
    private MainWindowViewModel? _subscribedViewModel;
    private EdgeViewModel? _hoveredEdge;
    private TextBox? _renameBox;
    private BlockNodeViewModel? _renamingBlock;
    private string? _renameOriginalName;
    private bool _portRefreshScheduled;
    private int _portRefreshAttempts;
    private MiniMapState? _miniMapState;

    private sealed record ClipboardEdge(
        string SourceBlockId,
        string SourcePortId,
        string TargetBlockId,
        string TargetPortId,
        string? Label);

    private sealed record MiniMapState(double MinX, double MinY, double Scale);

    public RecipeCanvasView()
    {
        InitializeComponent();
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _canvas = this.FindControl<Canvas>("CanvasArea");
        _miniMapCanvas = this.FindControl<Canvas>("MiniMapCanvas");
        _gridBackground = this.FindControl<GridBackground>("GridBackground");

        if (_canvas != null)
        {
            DragDrop.SetAllowDrop(_canvas, true);
            _canvas.PointerPressed += OnCanvasPointerPressed;
            _canvas.PointerMoved += OnCanvasPointerMoved;
            _canvas.PointerReleased += OnCanvasPointerReleased;
            _canvas.PointerWheelChanged += OnCanvasPointerWheelChanged;
            _canvas.DoubleTapped += OnCanvasDoubleTapped;
            _canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
            _canvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop);
            _canvas.SizeChanged += (_, _) => UpdateGridAndMiniMap();
        }

        if (_miniMapCanvas != null)
        {
            _miniMapCanvas.PointerPressed += OnMiniMapPointerPressed;
        }

        KeyDown += OnKeyDown;
        UpdateGrid();
        RenderBlocks();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.CanvasRenderRequested -= OnCanvasRenderRequested;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _subscribedViewModel = vm;
            vm.CanvasRenderRequested += OnCanvasRenderRequested;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.Blocks.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            vm.Edges.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            vm.CommandHistory.HistoryChanged += (_, _) => Dispatcher.UIThread.Post(RenderBlocks);
            UpdateCanvasZoomText(vm);
            UpdateGrid();
            RenderBlocks();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.GridSnapSize)
            or nameof(MainWindowViewModel.EnableGridSnap))
        {
            UpdateGrid();
        }
    }

    private void OnCanvasRenderRequested() => Dispatcher.UIThread.Post(RenderBlocks);

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        if (e.Source is Visual source && source.GetVisualAncestors()
                .Any(ancestor => ancestor is Border { Tag: "node-menu" }))
        {
            return;
        }

        Focus();
        HideNodeMenu();
        CloseContextMenu();

        var point = e.GetPosition(_canvas);
        var props = e.GetCurrentPoint(_canvas).Properties;

        if (props.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panStartPoint = point;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed)
        {
            _lastCanvasMenuPosition = point;
            var (block, _) = HitTestBlock(point);
            if (block != null) ShowNodeContextMenu(block);
            else if (HitTestEdge(point) is { } hitEdge) ShowEdgeContextMenu(hitEdge);
            else ShowCanvasContextMenu(point);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed) return;

        if (HitTestOutputPort(point) is { } outputHit)
        {
            ClearEdgeSelection(vm);
            _isConnecting = true;
            _connectionSourceBlock = outputHit.Block;
            _connectionSourcePort = outputHit.Port;
            _previewEdge = CreateBezierEdge(outputHit.Center.X, outputHit.Center.Y, point.X, point.Y, true, null);
            _previewEdge.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 6, 3 };
            _canvas.Children.Insert(0, _previewEdge);
            e.Pointer.Capture(_canvas);
            e.Handled = true;
            return;
        }

        if (HitTestEdge(point) is { } edge)
        {
            foreach (var b in vm.Blocks) b.IsSelected = false;
            vm.SelectedBlock = null;
            foreach (var e2 in vm.Edges)
            {
                e2.IsSelected = false;
                e2.IsHighlighted = false;
            }
            edge.IsSelected = true;
            edge.IsHighlighted = true;
            RenderBlocks();
            e.Handled = true;
            return;
        }

        var (hitBlock, hitBorder) = HitTestBlock(point);
        if (hitBlock != null && hitBorder != null)
        {
            ClearEdgeSelection(vm);
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                foreach (var b in vm.Blocks) b.IsSelected = false;
            }
            hitBlock.IsSelected = true;
            vm.SelectedBlock = hitBlock;

            _isDraggingNode = true;
            _draggedBlock = hitBlock;
            _draggedBorder = hitBorder;
            _dragStartPoint = point;
            _dragInitialPositions.Clear();
            foreach (var block in GetSelectedBlocks())
            {
                _dragInitialPositions[block.Id] = (block, block.X, block.Y);
            }
            e.Pointer.Capture(_canvas);
            RenderBlocks();
            e.Handled = true;
            return;
        }

        ClearEdgeSelection(vm);
        foreach (var b in vm.Blocks) b.IsSelected = false;
        vm.SelectedBlock = null;
        UpdateBlockSelectionVisuals();
        RenderMiniMap();
        _isSelecting = true;
        _selectionStartPoint = point;
        ShowMarquee(point, point);
        e.Pointer.Capture(_canvas);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null) return;
        var point = e.GetPosition(_canvas);

        if (_isConnecting && _previewEdge != null && _connectionSourceBlock != null)
        {
            var start = GetOutputPortCenter(_connectionSourceBlock, _connectionSourcePort);
            _previewEdge.Data = CreateBezierGeometry(start.X, start.Y, point.X, point.Y);
            e.Handled = true;
            return;
        }

        if (_isDraggingNode && _draggedBlock != null && _draggedBorder != null)
        {
            var dx = point.X - _dragStartPoint.X;
            var dy = point.Y - _dragStartPoint.Y;
            var vm = DataContext as MainWindowViewModel;
            var snap = vm is { EnableGridSnap: true } &&
                       !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            var gridSize = vm?.GridSnapSize ?? 0;

            foreach (var (block, startX, startY) in _dragInitialPositions.Values)
            {
                var targetX = startX + dx;
                var targetY = startY + dy;
                if (snap)
                {
                    targetX = CanvasSnap.Snap(targetX, gridSize);
                    targetY = CanvasSnap.Snap(targetY, gridSize);
                }

                block.X = targetX;
                block.Y = targetY;
            }

            UpdateBlockVisualPositions();
            UpdateEdgesOnly();
            e.Handled = true;
            return;
        }

        if (_isSelecting)
        {
            ShowMarquee(_selectionStartPoint, point);
            SelectBlocksInRectangle(_selectionStartPoint, point);
            UpdateBlockSelectionVisuals();
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            var dx = point.X - _panStartPoint.X;
            var dy = point.Y - _panStartPoint.Y;
            _viewport.PanBy(dx, dy);
            _panStartPoint = point;
            ApplyCanvasTransform();
            e.Handled = true;
            return;
        }

        UpdateEdgeHover(point);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var point = e.GetPosition(_canvas);

        if (_isConnecting)
        {
            var target = HitTestInputPort(point);
            if (_connectionSourceBlock != null && _connectionSourcePort != null && target != null)
            {
                vm.TryConnect(
                    _connectionSourceBlock,
                    _connectionSourcePort,
                    target.Value.Block,
                    target.Value.Port);
            }
            _isConnecting = false;
            _connectionSourceBlock = null;
            _connectionSourcePort = null;
            _previewEdge = null;
            RenderBlocks();
        }

        if (_isSelecting)
        {
            HideMarquee();
            _isSelecting = false;
        }

        if (_isDraggingNode)
        {
            CommitMoveCommand(vm);
            RenderMiniMap();
        }

        _isDraggingNode = false;
        _draggedBlock = null;
        _draggedBorder = null;
        _isPanning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var point = e.GetPosition(_canvas);
        var oldScale = _viewport.Scale;
        var delta = e.Delta.Y > 0 ? CanvasViewport.ScaleStep : -CanvasViewport.ScaleStep;
        _viewport.ZoomAt(point.X, point.Y, delta);
        if (Math.Abs(oldScale - _viewport.Scale) < 0.001) return;

        ApplyCanvasTransform();
        UpdateCanvasZoomText(vm);
        RenderMiniMap();
        e.Handled = true;
    }

    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DragBlockFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var text = e.Data.Get(DragBlockFormat) as string;
        if (Enum.TryParse<BlockType>(text, out var type))
        {
            AddBlockAtPosition(vm, type, e.GetPosition(_canvas));
            RenderBlocks();
        }
        e.Handled = true;
    }

    private void OnCanvasDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var point = e.GetPosition(_canvas);
        var (block, _) = HitTestBlock(point);
        if (block is not null)
        {
            BeginInlineRename(block);
            e.Handled = true;
            return;
        }

        if (HitTestOutputPort(point) is null && HitTestInputPort(point) is null &&
            HitTestEdge(point) is null)
        {
            _lastCanvasMenuPosition = point;
            ShowSearchableNodeMenu(point);
            e.Handled = true;
        }
    }

    private void OnMiniMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_miniMapCanvas == null || _canvas == null ||
            DataContext is not MainWindowViewModel vm ||
            !e.GetCurrentPoint(_miniMapCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_miniMapState is not { } state) return;
        var point = e.GetPosition(_miniMapCanvas);
        var worldX = state.MinX + (point.X - MiniMapPadding) / state.Scale;
        var worldY = state.MinY + (point.Y - MiniMapPadding) / state.Scale;
        _viewport.CenterOn(worldX, worldY, _canvas.Bounds.Width, _canvas.Bounds.Height);
        ApplyCanvasTransform();
        UpdateCanvasZoomText(vm);
        RenderMiniMap();
        e.Handled = true;
    }

    private void BeginInlineRename(BlockNodeViewModel block)
    {
        if (_canvas == null) return;
        HideNodeMenu();
        CloseContextMenu();
        CancelInlineRename();

        _renamingBlock = block;
        _renameOriginalName = block.Name;
        var box = new TextBox
        {
            Text = block.Name,
            Width = Math.Max(120, block.Width - 36),
            Height = 26,
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#4A90D9")),
            BorderThickness = new Thickness(1),
            Tag = "inline-rename"
        };
        Canvas.SetLeft(box, block.X + 18);
        Canvas.SetTop(box, block.Y + 2);

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                CommitInlineRename();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                CancelInlineRename();
                args.Handled = true;
            }
        };
        box.LostFocus += (_, _) => CommitInlineRename();

        _renameBox = box;
        _canvas.Children.Add(box);
        box.Focus();
        box.SelectAll();
    }

    private void CommitInlineRename()
    {
        if (_renameBox is null || _renamingBlock is null) return;

        var name = _renameBox.Text?.Trim();
        var block = _renamingBlock;
        var original = _renameOriginalName;
        RemoveRenameBox();

        if (string.IsNullOrWhiteSpace(name) || name == original) return;
        if (DataContext is not MainWindowViewModel vm) return;

        vm.CommandHistory.Execute(new RenameBlockCommand(block, name));
        vm.SelectedBlock = block;
        RenderBlocks();
    }

    private void CancelInlineRename()
    {
        RemoveRenameBox();
    }

    private void RemoveRenameBox()
    {
        if (_renameBox is not null && _canvas is not null)
        {
            _canvas.Children.Remove(_renameBox);
        }

        _renameBox = null;
        _renamingBlock = null;
        _renameOriginalName = null;
    }

    /// <summary>
    /// Fits all blocks into the current viewport.
    /// </summary>
    public void FitToView()
    {
        if (_canvas is null || DataContext is not MainWindowViewModel vm) return;
        var bounds = vm.Blocks.Select(block => new CanvasBounds(
            block.X, block.Y, block.Width, Math.Max(block.Height, 80)));
        _viewport.Fit(bounds, _canvas.Bounds.Width, _canvas.Bounds.Height);
        ApplyCanvasTransform();
        UpdateCanvasZoomText(vm);
        RenderMiniMap();
    }

    private void UpdateGrid()
    {
        if (_gridBackground is null) return;
        _gridBackground.GridSize = (DataContext as MainWindowViewModel)?.GridSnapSize ?? 16;
        _gridBackground.Scale = _viewport.Scale;
        _gridBackground.OffsetX = _viewport.OffsetX;
        _gridBackground.OffsetY = _viewport.OffsetY;
    }

    private void UpdateGridAndMiniMap()
    {
        UpdateGrid();
        RenderMiniMap();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (_renameBox != null || e.Source is TextBox) return;

        if (e.Key == Key.Oem3)
        {
            ShowSearchableNodeMenu(_lastCanvasMenuPosition);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelection(vm);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
        {
            vm.CommandHistory.Undo();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
        {
            vm.CommandHistory.Redo();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            var selected = GetSelectedBlocks().ToList();
            _clipboard.Clear();
            _clipboardOriginalIds.Clear();
            _clipboardEdges.Clear();
            _clipboard.AddRange(selected.Select(CloneBlock));
            _clipboardOriginalIds.AddRange(selected.Select(block => block.Id));

            var selectedIds = selected.Select(block => block.Id).ToHashSet();
            _clipboardEdges.AddRange(vm.Edges
                .Where(edge => selectedIds.Contains(edge.SourceBlockId) &&
                               selectedIds.Contains(edge.TargetBlockId))
                .Select(edge => new ClipboardEdge(
                    edge.SourceBlockId,
                    edge.SourcePortId,
                    edge.TargetBlockId,
                    edge.TargetPortId,
                    edge.Label)));
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.V)
        {
            PasteClipboard(vm);
            e.Handled = true;
        }
    }

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestOutputPort(Point point) => HitTestPort(point, true);

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestInputPort(Point point) => HitTestPort(point, false);

    private (BlockNodeViewModel Block, PortViewModel Port, Point Center)? HitTestPort(Point point, bool output)
    {
        if (DataContext is not MainWindowViewModel vm) return null;
        foreach (var block in vm.Blocks.AsEnumerable().Reverse())
        {
            var ports = output ? block.OutputPorts : block.InputPorts;
            foreach (var port in ports)
            {
                var center = output ? GetOutputPortCenter(block, port) : GetInputPortCenter(block, port);
                if (Distance(point, center) <= 10) return (block, port, center);
            }
        }
        return null;
    }

    private (BlockNodeViewModel? block, Border? border) HitTestBlock(Point point)
    {
        if (_canvas == null) return (null, null);
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is Border { Tag: BlockNodeViewModel block } border)
            {
                var left = Canvas.GetLeft(border);
                var top = Canvas.GetTop(border);
                var height = Math.Max(border.Bounds.Height, block.Height);
                if (point.X >= left && point.X <= left + block.Width && point.Y >= top && point.Y <= top + height)
                    return (block, border);
            }
        }
        return (null, null);
    }

    private EdgeViewModel? HitTestEdge(Point point)
    {
        if (DataContext is not MainWindowViewModel vm) return null;
        foreach (var edge in vm.Edges.AsEnumerable().Reverse())
        {
            var source = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
            var target = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
            if (source == null || target == null) continue;
            var start = GetOutputPortCenter(source, source.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId));
            var end = GetInputPortCenter(target, target.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId));
            if (DistanceToSegment(point, start, end) <= 8) return edge;
        }
        return null;
    }

    private void ShowNodeContextMenu(BlockNodeViewModel block)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        CloseContextMenu();
        foreach (var b in vm.Blocks) b.IsSelected = false;
        block.IsSelected = true;
        vm.SelectedBlock = block;
        RenderBlocks();

        var menu = new ContextMenu();
        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => PasteBlocks(
            vm,
            new[] { CloneBlock(block) },
            new[] { block.Id },
            Array.Empty<ClipboardEdge>(),
            new Point(block.X + 40, block.Y + 40));
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteSelection(vm);
        var renameItem = new MenuItem { Header = "Rename" };
        renameItem.Click += (_, _) => BeginInlineRename(block);
        menu.Items.Add(duplicateItem);
        menu.Items.Add(renameItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        _contextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu)) _contextMenu = null;
        };
        menu.Open(this);
    }

    private void ShowEdgeContextMenu(EdgeViewModel edge)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        CloseContextMenu();
        foreach (var b in vm.Blocks) b.IsSelected = false;
        vm.SelectedBlock = null;
        foreach (var e in vm.Edges)
        {
            e.IsSelected = false;
            e.IsHighlighted = false;
        }
        edge.IsSelected = true;
        edge.IsHighlighted = true;
        RenderBlocks();

        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => DeleteEdge(vm, edge);
        menu.Items.Add(deleteItem);
        _contextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu)) _contextMenu = null;
        };
        menu.Open(this);
    }

    private void ShowCanvasContextMenu(Point position)
    {
        CloseContextMenu();
        var menu = new ContextMenu();
        var addItem = new MenuItem { Header = "Add item" };
        addItem.Click += (_, _) => ShowSearchableNodeMenu(position);
        menu.Items.Add(addItem);
        _contextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu)) _contextMenu = null;
        };
        menu.Open(this);
    }

    private void ShowSearchableNodeMenu(Point position)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        HideNodeMenu();

        var search = new TextBox
        {
            Watermark = "Search block...",
            Margin = new Thickness(8, 8, 8, 4),
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Foreground = Brushes.White
        };
        var list = new ListBox
        {
            Height = 190,
            Margin = new Thickness(8, 0, 8, 8),
            Background = new SolidColorBrush(Color.Parse("#202033")),
            Foreground = Brushes.White
        };

        void Refresh(string? query = null)
        {
            list.Items.Clear();
            foreach (var template in vm.BlockTemplates.Where(t => string.IsNullOrWhiteSpace(query) || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                list.Items.Add(new ListBoxItem { Content = $"{template.IconText}  {template.Name}", Tag = template.Type });
            }
        }

        search.TextChanged += (_, _) => Refresh(search.Text);
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: BlockType type })
            {
                AddBlockAtPosition(vm, type, position);
                HideNodeMenu();
                RenderBlocks();
            }
        };

        _nodeMenu = new Border
        {
            Width = 240,
            Background = new SolidColorBrush(Color.Parse("#28283A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A90D9")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = "node-menu",
            Child = new StackPanel { Children = { search, list } }
        };
        Canvas.SetLeft(_nodeMenu, position.X);
        Canvas.SetTop(_nodeMenu, position.Y);
        _canvas.Children.Add(_nodeMenu);
        Refresh();
        search.Focus();
    }

    private void HideNodeMenu()
    {
        if (_canvas != null && _nodeMenu != null) _canvas.Children.Remove(_nodeMenu);
        _nodeMenu = null;
    }

    private void CloseContextMenu()
    {
        if (_contextMenu != null)
        {
            _contextMenu.Close();
            _contextMenu = null;
        }
    }

    private void ShowMarquee(Point start, Point end)
    {
        if (_canvas == null) return;
        _marquee ??= new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(35, 74, 144, 217)),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A90D9")),
            BorderThickness = new Thickness(1),
            Tag = "marquee"
        };
        if (!_canvas.Children.Contains(_marquee)) _canvas.Children.Add(_marquee);
        Canvas.SetLeft(_marquee, Math.Min(start.X, end.X));
        Canvas.SetTop(_marquee, Math.Min(start.Y, end.Y));
        _marquee.Width = Math.Abs(end.X - start.X);
        _marquee.Height = Math.Abs(end.Y - start.Y);
    }

    private void HideMarquee()
    {
        if (_canvas != null && _marquee != null) _canvas.Children.Remove(_marquee);
    }

    private void SelectBlocksInRectangle(Point start, Point end)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        foreach (var block in vm.Blocks)
        {
            block.IsSelected = block.X < right && block.X + block.Width > left && block.Y < bottom && block.Y + GetBlockHeight(block) > top;
        }
        vm.SelectedBlock = vm.Blocks.LastOrDefault(b => b.IsSelected);
    }

    private void DeleteSelection(MainWindowViewModel vm)
    {
        var selectedEdges = vm.Edges.Where(e => e.IsSelected || e.IsHighlighted).ToList();
        var selectedBlocks = vm.Blocks.Where(b => b.IsSelected).ToList();
        foreach (var block in selectedBlocks)
        {
            selectedEdges.AddRange(vm.Edges.Where(e => e.SourceBlockId == block.Id || e.TargetBlockId == block.Id));
        }

        selectedEdges = selectedEdges.DistinctBy(e => e.Id).ToList();
        if (selectedBlocks.Count == 0 && selectedEdges.Count == 0) return;

        vm.CommandHistory.Execute(new DeleteSelectionCommand(vm, selectedBlocks, selectedEdges));
        vm.SelectedBlock = null;
        RenderBlocks();
    }

    private void CommitMoveCommand(MainWindowViewModel vm)
    {
        var moves = _dragInitialPositions.Values
            .Where(start => Math.Abs(start.Block.X - start.X) > 0.1 || Math.Abs(start.Block.Y - start.Y) > 0.1)
            .Select(start => (start.Block, start.X, start.Y, start.Block.X, start.Block.Y))
            .ToList();

        if (moves.Count > 0)
        {
            vm.CommandHistory.Execute(new MoveBlocksCommand(moves));
        }

        _dragInitialPositions.Clear();
    }

    private void DeleteEdge(MainWindowViewModel vm, EdgeViewModel edge)
    {
        vm.CommandHistory.Execute(new DeleteEdgeCommand(vm, edge));
        RenderBlocks();
    }

    private void ApplyCanvasTransform()
    {
        if (_canvas == null) return;
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(_viewport.Scale, _viewport.Scale));
        transform.Children.Add(new TranslateTransform(_viewport.OffsetX, _viewport.OffsetY));
        _canvas.RenderTransform = transform;
        UpdateGrid();
        RenderMiniMap();
    }

    private void UpdateCanvasZoomText(MainWindowViewModel vm)
    {
        vm.SetCanvasZoomScale(_viewport.Scale);
    }

    private IEnumerable<BlockNodeViewModel> GetSelectedBlocks()
        => DataContext is MainWindowViewModel vm ? vm.Blocks.Where(b => b.IsSelected) : Enumerable.Empty<BlockNodeViewModel>();

    private void PasteClipboard(MainWindowViewModel vm)
    {
        if (_clipboard.Count == 0) return;
        PasteBlocks(
            vm,
            _clipboard,
            _clipboardOriginalIds,
            _clipboardEdges,
            new Point(_lastCanvasMenuPosition.X + 24, _lastCanvasMenuPosition.Y + 24));
        RenderBlocks();
    }

    private static void PasteBlocks(
        MainWindowViewModel vm,
        IReadOnlyList<BlockNodeViewModel> sourceBlocks,
        IReadOnlyList<string> originalIds,
        IReadOnlyList<ClipboardEdge> edges,
        Point position)
    {
        var blocks = sourceBlocks.Select(CloneBlock).ToList();
        if (blocks.Count == 0) return;
        var minX = blocks.Min(b => b.X);
        var minY = blocks.Min(b => b.Y);
        foreach (var block in vm.Blocks)
        {
            block.IsSelected = false;
        }

        var idMap = new Dictionary<string, BlockNodeViewModel>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            block.Id = Guid.NewGuid().ToString("N");
            block.Name = $"{block.Name}_copy";
            block.X = position.X + (block.X - minX);
            block.Y = position.Y + (block.Y - minY);
            block.IsSelected = true;
            if (i < originalIds.Count)
            {
                idMap[originalIds[i]] = block;
            }
        }

        var commands = new List<IUndoableCommand>(
            blocks.Select(block => new AddBlockCommand(vm, block)));

        foreach (var edge in edges)
        {
            if (!idMap.TryGetValue(edge.SourceBlockId, out var source) ||
                !idMap.TryGetValue(edge.TargetBlockId, out var target))
            {
                continue;
            }

            var originalSource = vm.Blocks.FirstOrDefault(block => block.Id == edge.SourceBlockId);
            var originalTarget = vm.Blocks.FirstOrDefault(block => block.Id == edge.TargetBlockId);
            var sourcePort = FindPortByIndex(originalSource?.OutputPorts, edge.SourcePortId, source.OutputPorts);
            var targetPort = FindPortByIndex(originalTarget?.InputPorts, edge.TargetPortId, target.InputPorts);
            if (sourcePort is null || targetPort is null) continue;

            commands.Add(new AddEdgeCommand(vm, new EdgeViewModel
            {
                SourceBlockId = source.Id,
                SourcePortId = sourcePort.Id,
                TargetBlockId = target.Id,
                TargetPortId = targetPort.Id,
                Label = edge.Label
            }));
        }

        vm.CommandHistory.Execute(new CompositeCommand("Paste blocks", commands));
        vm.SelectedBlock = blocks.LastOrDefault();
    }

    private static PortViewModel? FindPortByIndex(
        IReadOnlyList<PortViewModel>? originalPorts,
        string originalPortId,
        IReadOnlyList<PortViewModel> clonedPorts)
    {
        if (originalPorts is null) return null;
        var index = -1;
        for (var i = 0; i < originalPorts.Count; i++)
        {
            if (originalPorts[i].Id == originalPortId)
            {
                index = i;
                break;
            }
        }

        return index >= 0 && index < clonedPorts.Count ? clonedPorts[index] : null;
    }

    private static BlockNodeViewModel CloneBlock(BlockNodeViewModel block)
    {
        var clone = new BlockNodeViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = block.Type,
            Name = block.Name,
            DisplayName = block.DisplayName,
            X = block.X,
            Y = block.Y,
            Width = block.Width,
            Height = block.Height,
            Config = block.Config.DeepClone()
        };
        foreach (var p in block.InputPorts) clone.InputPorts.Add(new PortViewModel { Name = p.Name, DataType = p.DataType });
        foreach (var p in block.OutputPorts) clone.OutputPorts.Add(new PortViewModel { Name = p.Name, DataType = p.DataType });
        return clone;
    }

    private static void AddBlockAtPosition(MainWindowViewModel vm, BlockType type, Point position)
    {
        foreach (var b in vm.Blocks) b.IsSelected = false;
        var block = new BlockNodeViewModel
        {
            Type = type,
            Name = $"block_{vm.Blocks.Count + 1}",
            DisplayName = type switch
            {
                BlockType.Seed => "Training Data",
                BlockType.Llm => "LLM / Model",
                BlockType.Expression => "Expression",
                BlockType.Validator => "Validator",
                BlockType.Sampler => "Sampler",
                BlockType.ToolProfile => "Tool Profile",
                BlockType.QuerySource => "Query Source",
                BlockType.Simulation => "Simulation / Harness",
                BlockType.Cleaner => "Cleaner",
                BlockType.SftExport => "SFT Export",
                _ => $"New {type}"
            },
            X = position.X,
            Y = position.Y,
            IsSelected = true,
            Config = BlockConfig.CreateDefault(type)
        };
        block.ApplyDefaultPorts();
        vm.CommandHistory.Execute(new AddBlockCommand(vm, block));
        vm.SelectedBlock = block;
    }

    private void ClearEdgeSelection(MainWindowViewModel vm)
    {
        foreach (var edge in vm.Edges)
        {
            edge.IsSelected = false;
            edge.IsHighlighted = false;
        }

        _hoveredEdge = null;
        Cursor = Cursor.Default;
    }

    private void RenderBlocks()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        _canvas.Children.Clear();
        _blockBorders.Clear();
        _portOffsets.Clear();
        _edgeVisuals.Clear();
        _edgeLabels.Clear();

        foreach (var block in vm.Blocks)
        {
            var node = CreateBlockNode(block);
            Canvas.SetLeft(node, block.X);
            Canvas.SetTop(node, block.Y);
            _blockBorders[block.Id] = node;
            _canvas.Children.Add(node);
        }

        foreach (var edge in vm.Edges) RenderEdge(edge);
        RenderMiniMap();
        _portRefreshAttempts = 0;
        SchedulePortRefresh();
    }

    private void UpdateBlockVisualPositions()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        foreach (var block in vm.Blocks)
        {
            if (_blockBorders.TryGetValue(block.Id, out var border))
            {
                Canvas.SetLeft(border, block.X);
                Canvas.SetTop(border, block.Y);
            }
        }
    }

    private void SchedulePortRefresh()
    {
        if (_portRefreshScheduled) return;
        _portRefreshScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _portRefreshScheduled = false;
            RefreshPortOffsets();
        }, DispatcherPriority.Loaded);
    }

    private void RefreshPortOffsets()
    {
        if (DataContext is not MainWindowViewModel vm || _blockBorders.Count == 0) return;

        var resolved = 0;
        foreach (var (blockId, border) in _blockBorders)
        {
            var block = vm.Blocks.FirstOrDefault(candidate => candidate.Id == blockId);
            if (block is null) continue;

            foreach (var ellipse in border.GetVisualDescendants().OfType<Ellipse>())
            {
                if (ellipse.Tag is not PortViewModel port) continue;
                if (ellipse.Bounds.Width <= 0 || border.Bounds.Width <= 0) continue;
                var center = ellipse.TranslatePoint(
                    new Point(ellipse.Bounds.Width / 2, ellipse.Bounds.Height / 2), border);
                if (center is null) continue;

                _portOffsets[(block.Id, port.Id)] = center.Value;
                resolved++;
            }
        }

        if (resolved == 0 && _portRefreshAttempts < 5)
        {
            _portRefreshAttempts++;
            SchedulePortRefresh();
            return;
        }

        UpdateEdgesOnly();
        RenderMiniMap();
    }

    private void UpdateBlockSelectionVisuals()
    {
        if (_canvas == null) return;
        foreach (var child in _canvas.Children)
        {
            if (child is Border { Tag: BlockNodeViewModel block } border)
            {
                border.BorderBrush = new SolidColorBrush(Color.Parse(block.IsSelected ? "#4A90D9" : "#3A3A4E"));
                border.BorderThickness = new Thickness(block.IsSelected ? 2 : 1);
            }
        }
    }

    private void RenderMiniMap()
    {
        if (_miniMapCanvas == null || DataContext is not MainWindowViewModel vm) return;

        _miniMapCanvas.Children.Clear();
        _miniMapState = null;

        if (vm.Blocks.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Mini-map",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#666666"))
            };
            Canvas.SetLeft(hint, 58);
            Canvas.SetTop(hint, 42);
            _miniMapCanvas.Children.Add(hint);
            return;
        }

        var minX = vm.Blocks.Min(b => b.X);
        var minY = vm.Blocks.Min(b => b.Y);
        var maxX = vm.Blocks.Max(b => b.X + b.Width);
        var maxY = vm.Blocks.Max(b => b.Y + GetBlockHeight(b));
        var spanX = Math.Max(1, maxX - minX);
        var spanY = Math.Max(1, maxY - minY);
        var scale = Math.Min(
            (MiniMapWidth - MiniMapPadding * 2) / spanX,
            (MiniMapHeight - MiniMapPadding * 2) / spanY);
        _miniMapState = new MiniMapState(minX, minY, scale);

        foreach (var edge in vm.Edges)
        {
            var sourceBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
            var targetBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
            if (sourceBlock == null || targetBlock == null) continue;

            var line = new Line
            {
                StartPoint = ToMiniMapPoint(GetOutputPortCenter(sourceBlock, sourceBlock.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId)), minX, minY, scale),
                EndPoint = ToMiniMapPoint(GetInputPortCenter(targetBlock, targetBlock.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId)), minX, minY, scale),
                Stroke = new SolidColorBrush(Color.Parse(edge.IsSelected || edge.IsHighlighted ? "#4A90D9" : "#55556A")),
                StrokeThickness = edge.IsSelected || edge.IsHighlighted ? 1.5 : 1
            };
            _miniMapCanvas.Children.Add(line);
        }

        foreach (var block in vm.Blocks)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(5, block.Width * scale),
                Height = Math.Max(4, GetBlockHeight(block) * scale),
                Fill = new SolidColorBrush(Color.Parse(GetBlockHeaderColor(block.Type))),
                Stroke = new SolidColorBrush(Color.Parse(block.IsSelected ? "#FFFFFF" : "#3A3A4E")),
                StrokeThickness = block.IsSelected ? 1.5 : 1,
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, MiniMapPadding + (block.X - minX) * scale);
            Canvas.SetTop(rect, MiniMapPadding + (block.Y - minY) * scale);
            _miniMapCanvas.Children.Add(rect);
        }

        if (_canvas is not null && _canvas.Bounds.Width > 0 && _canvas.Bounds.Height > 0)
        {
            var (worldLeft, worldTop) = _viewport.ScreenToWorld(0, 0);
            var (worldRight, worldBottom) = _viewport.ScreenToWorld(
                _canvas.Bounds.Width, _canvas.Bounds.Height);
            var viewport = new Rectangle
            {
                Width = Math.Max(4, (worldRight - worldLeft) * scale),
                Height = Math.Max(4, (worldBottom - worldTop) * scale),
                Fill = new SolidColorBrush(Color.FromArgb(20, 74, 144, 217)),
                Stroke = new SolidColorBrush(Color.Parse("#FFFFFF")),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 }
            };
            Canvas.SetLeft(viewport, MiniMapPadding + (worldLeft - minX) * scale);
            Canvas.SetTop(viewport, MiniMapPadding + (worldTop - minY) * scale);
            _miniMapCanvas.Children.Add(viewport);
        }
    }

    private static Point ToMiniMapPoint(Point point, double minX, double minY, double scale)
        => new(MiniMapPadding + (point.X - minX) * scale, MiniMapPadding + (point.Y - minY) * scale);

    private void RenderEdge(EdgeViewModel edge)
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;
        var sourceBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
        var targetBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
        if (sourceBlock == null || targetBlock == null) return;
        var start = GetOutputPortCenter(sourceBlock, sourceBlock.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId));
        var end = GetInputPortCenter(targetBlock, targetBlock.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId));
        var path = CreateBezierEdge(start.X, start.Y, end.X, end.Y, edge.IsHighlighted || edge.IsSelected, edge);
        _canvas.Children.Insert(0, path);
        _edgeVisuals[edge.Id] = path;

        if (!string.IsNullOrEmpty(edge.Label))
        {
            var label = new TextBlock
            {
                Text = edge.Label,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.Parse(edge.IsSelected ? "#FFFFFF" : "#888888")),
                Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
                Padding = new Thickness(3, 1),
                Tag = "edge-label"
            };
            Canvas.SetLeft(label, (start.X + end.X) / 2 - 20);
            Canvas.SetTop(label, (start.Y + end.Y) / 2 - 8);
            _canvas.Children.Add(label);
            _edgeLabels[edge.Id] = label;
        }
    }

    /// <summary>
    /// Updates existing edge visuals without recreating nodes or paths.
    /// </summary>
    private void UpdateEdgesOnly()
    {
        if (_canvas == null || DataContext is not MainWindowViewModel vm) return;

        var activeEdges = new HashSet<string>();
        foreach (var edge in vm.Edges)
        {
            var sourceBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.SourceBlockId);
            var targetBlock = vm.Blocks.FirstOrDefault(b => b.Id == edge.TargetBlockId);
            if (sourceBlock == null || targetBlock == null) continue;

            activeEdges.Add(edge.Id);
            var start = GetOutputPortCenter(
                sourceBlock, sourceBlock.OutputPorts.FirstOrDefault(p => p.Id == edge.SourcePortId));
            var end = GetInputPortCenter(
                targetBlock, targetBlock.InputPorts.FirstOrDefault(p => p.Id == edge.TargetPortId));

            if (!_edgeVisuals.TryGetValue(edge.Id, out var path))
            {
                RenderEdge(edge);
                path = _edgeVisuals[edge.Id];
            }

            path.Data = CreateBezierGeometry(start.X, start.Y, end.X, end.Y);
            var highlighted = edge.IsHighlighted || edge.IsSelected || ReferenceEquals(edge, _hoveredEdge);
            path.Stroke = new SolidColorBrush(Color.Parse(highlighted ? "#4A90D9" : "#AAAAAA"));
            path.StrokeThickness = highlighted ? 4 : 2;
            path.StrokeDashArray = highlighted
                ? null
                : new Avalonia.Collections.AvaloniaList<double> { 4, 2 };

            if (_edgeLabels.TryGetValue(edge.Id, out var label))
            {
                Canvas.SetLeft(label, (start.X + end.X) / 2 - 20);
                Canvas.SetTop(label, (start.Y + end.Y) / 2 - 8);
            }
        }

        foreach (var edgeId in _edgeVisuals.Keys.Where(id => !activeEdges.Contains(id)).ToList())
        {
            _canvas.Children.Remove(_edgeVisuals[edgeId]);
            _edgeVisuals.Remove(edgeId);
        }

        foreach (var edgeId in _edgeLabels.Keys.Where(id => !activeEdges.Contains(id)).ToList())
        {
            _canvas.Children.Remove(_edgeLabels[edgeId]);
            _edgeLabels.Remove(edgeId);
        }
    }

    private void UpdateEdgeHover(Point point)
    {
        var edge = HitTestEdge(point);
        if (ReferenceEquals(edge, _hoveredEdge)) return;

        if (_hoveredEdge is not null)
        {
            _hoveredEdge.IsHighlighted = false;
        }

        _hoveredEdge = edge;
        if (_hoveredEdge is not null)
        {
            _hoveredEdge.IsHighlighted = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            Cursor = Cursor.Default;
        }

        UpdateEdgesOnly();
    }

    private static Path CreateBezierEdge(double startX, double startY, double endX, double endY, bool isHighlighted, EdgeViewModel? edge)
        => new()
        {
            Data = CreateBezierGeometry(startX, startY, endX, endY),
            Stroke = new SolidColorBrush(Color.Parse(isHighlighted ? "#4A90D9" : "#AAAAAA")),
            StrokeThickness = isHighlighted ? 4 : 2,
            StrokeDashArray = isHighlighted ? null : new Avalonia.Collections.AvaloniaList<double> { 4, 2 },
            Tag = edge
        };

    private static Geometry CreateBezierGeometry(double startX, double startY, double endX, double endY)
    {
        var controlOffset = Math.Max(60, Math.Abs(endX - startX) * 0.4);
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = false };
        figure.Segments!.Add(new BezierSegment
        {
            Point1 = new Point(startX + controlOffset, startY),
            Point2 = new Point(endX - controlOffset, endY),
            Point3 = new Point(endX, endY)
        });
        geometry.Figures!.Add(figure);
        return geometry;
    }

    private Border CreateBlockNode(BlockNodeViewModel block)
    {
        var headerColor = GetBlockHeaderColor(block.Type);

        var body = new StackPanel
        {
            Margin = new Thickness(10, 6, 10, 8),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = $"Name: {{{{ {block.Name} }}}}", FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#AAAAAA")) },
                new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#3A3A4E")), Margin = new Thickness(0, 2) }
            }
        };

        foreach (var port in block.OutputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Children = { new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) }, CreatePortEllipse(port, isOutput: true) }
            });
        }

        foreach (var port in block.InputPorts)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Children = { CreatePortEllipse(port, isOutput: false), new TextBlock { Text = port.Name, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#888888")) } }
            });
        }

        var header = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = block.IconText, FontSize = 12 },
                new TextBlock { Text = block.DisplayName, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White }
            }
        };

        if (block.HasValidationError)
        {
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.Parse("#D0021B")),
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = "!",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(badge, block.ValidationError ?? "Validation error");
            header.Children.Add(badge);
        }
        else if (block.ValidationWarningCount > 0)
        {
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.Parse("#F5A623")),
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = block.ValidationWarningCount.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };
            ToolTip.SetTip(badge, $"{block.ValidationWarningCount} validation warning(s)");
            header.Children.Add(badge);
        }

        return new Border
        {
            Width = block.Width,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#2A2A3E")),
            BorderBrush = new SolidColorBrush(Color.Parse(block.HasValidationError ? "#D0021B" : block.IsSelected ? "#4A90D9" : "#3A3A4E")),
            BorderThickness = new Thickness(block.IsSelected || block.HasValidationError ? 2 : 1),
            Tag = block,
            Child = new StackPanel
            {
                Children =
                {
                    new Border
                    {
                        Height = 28,
                        CornerRadius = new CornerRadius(8, 8, 0, 0),
                        Background = new SolidColorBrush(Color.Parse(headerColor)),
                        Padding = new Thickness(10, 4),
                        Child = header
                    },
                    body
                }
            }
        };
    }

    private static Ellipse CreatePortEllipse(PortViewModel port, bool isOutput)
        => new()
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.Parse(isOutput ? "#4A90D9" : "#F5A623")),
            Stroke = new SolidColorBrush(Color.Parse(isOutput ? "#6AB0F9" : "#FFD080")),
            StrokeThickness = 1,
            Tag = port
        };

    private static string GetBlockHeaderColor(BlockType type)
        => type switch
        {
            BlockType.Seed => "#4A90D9",
            BlockType.Llm => "#7B61FF",
            BlockType.Expression => "#F5A623",
            BlockType.Validator => "#D0021B",
            BlockType.Sampler => "#417505",
            BlockType.ToolProfile => "#9B9B9B",
            BlockType.QuerySource => "#E67E22",
            BlockType.Simulation => "#00A3A3",
            BlockType.Cleaner => "#8E44AD",
            BlockType.SftExport => "#2E8B57",
            _ => "#4A90D9"
        };

    private Point GetOutputPortCenter(BlockNodeViewModel block, PortViewModel? port)
    {
        if (port is not null && _portOffsets.TryGetValue((block.Id, port.Id), out var offset))
        {
            return new Point(block.X + offset.X, block.Y + offset.Y);
        }

        var index = port == null ? 0 : Math.Max(0, block.OutputPorts.IndexOf(port));
        return new Point(block.X + block.Width - 15, block.Y + 69 + index * 16);
    }

    private Point GetInputPortCenter(BlockNodeViewModel block, PortViewModel? port)
    {
        if (port is not null && _portOffsets.TryGetValue((block.Id, port.Id), out var offset))
        {
            return new Point(block.X + offset.X, block.Y + offset.Y);
        }

        var index = port == null ? 0 : Math.Max(0, block.InputPorts.IndexOf(port));
        return new Point(block.X + 15, block.Y + 69 + block.OutputPorts.Count * 16 + index * 16);
    }

    private double GetBlockHeight(BlockNodeViewModel block) =>
        _blockBorders.TryGetValue(block.Id, out var border) && border.Bounds.Height > 0
            ? border.Bounds.Height
            : Math.Max(block.Height, 80);

    private static double Distance(Point a, Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (dx == 0 && dy == 0) return Distance(p, a);
        var t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy)));
        return Distance(p, new Point(a.X + t * dx, a.Y + t * dy));
    }
}
