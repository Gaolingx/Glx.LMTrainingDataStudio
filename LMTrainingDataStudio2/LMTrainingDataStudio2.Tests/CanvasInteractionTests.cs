using System.Diagnostics;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class CanvasInteractionTests
{
    [Test]
    public void Snap_RoundsToGridAndIgnoresNonPositiveGrid()
    {
        Assert.That(CanvasSnap.Snap(17, 16), Is.EqualTo(16));
        Assert.That(CanvasSnap.Snap(25, 16), Is.EqualTo(32));
        Assert.That(CanvasSnap.Snap(-17, 16), Is.EqualTo(-16));
        Assert.That(CanvasSnap.Snap(17, 0), Is.EqualTo(17));
    }

    [Test]
    public void ZoomAt_KeepsWorldPointUnderPointer()
    {
        var viewport = new CanvasViewport();
        viewport.Set(1, 40, 30);
        var before = viewport.ScreenToWorld(300, 200);

        viewport.ZoomAt(300, 200, 0.4);

        var after = viewport.ScreenToWorld(300, 200);
        Assert.That(after.X, Is.EqualTo(before.X).Within(0.001));
        Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.001));
        Assert.That(viewport.Scale, Is.EqualTo(1.4).Within(0.001));
    }

    [Test]
    public void ZoomAt_ClampsScaleAndKeepsAnchor()
    {
        var viewport = new CanvasViewport();
        viewport.Set(CanvasViewport.MaxScale, 0, 0);

        viewport.ZoomAt(10, 10, 1);

        Assert.That(viewport.Scale, Is.EqualTo(CanvasViewport.MaxScale));
        Assert.That(viewport.ScreenToWorld(10, 10).X, Is.EqualTo(10.0 / CanvasViewport.MaxScale).Within(0.001));
    }

    [Test]
    public void Fit_CentersBoundsWithPadding()
    {
        var viewport = new CanvasViewport();
        var bounds = new[]
        {
            new CanvasBounds(100, 100, 200, 80),
            new CanvasBounds(600, 300, 200, 80)
        };

        viewport.Fit(bounds, 1000, 600, 50);

        var left = viewport.WorldToScreen(100, 100);
        var right = viewport.WorldToScreen(800, 380);
        Assert.That(left.X, Is.GreaterThanOrEqualTo(49));
        Assert.That(left.Y, Is.GreaterThanOrEqualTo(49));
        Assert.That(right.X, Is.LessThanOrEqualTo(951));
        Assert.That(right.Y, Is.LessThanOrEqualTo(551));
        Assert.That((left.X + right.X) / 2, Is.EqualTo(500).Within(1));
        Assert.That((left.Y + right.Y) / 2, Is.EqualTo(300).Within(1));
    }

    [Test]
    public void Layout_BuildsTopologicalColumns()
    {
        var recipe = new Recipe { Name = "layout" };
        recipe.Blocks.AddRange(new[]
        {
            Block("a", "A"), Block("b", "B"), Block("c", "C")
        });
        recipe.Edges.Add(Edge("e1", "a", "b"));
        recipe.Edges.Add(Edge("e2", "b", "c"));

        var positions = RecipeLayoutService.Arrange(recipe);

        Assert.That(positions["a"].X, Is.EqualTo(80));
        Assert.That(positions["b"].X, Is.EqualTo(360));
        Assert.That(positions["c"].X, Is.EqualTo(640));
        Assert.That(positions["a"].Y, Is.EqualTo(80));
    }

    [Test]
    public void Layout_OrdersParallelBlocksByRow()
    {
        var recipe = new Recipe { Name = "parallel" };
        recipe.Blocks.AddRange(new[] { Block("b", "B"), Block("a", "A") });

        var positions = RecipeLayoutService.Arrange(recipe);

        Assert.That(positions["a"].Y, Is.EqualTo(80));
        Assert.That(positions["b"].Y, Is.EqualTo(220));
    }

    [Test]
    public void Layout_500Blocks_CompletesQuickly()
    {
        var recipe = new Recipe { Name = "large" };
        for (var i = 0; i < 500; i++)
        {
            recipe.Blocks.Add(Block($"n{i}", $"Node {i}"));
            if (i > 0)
            {
                recipe.Edges.Add(Edge($"e{i}", $"n{i - 1}", $"n{i}"));
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var positions = RecipeLayoutService.Arrange(recipe);
        stopwatch.Stop();

        Assert.That(positions, Has.Count.EqualTo(500));
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100),
            $"Layout took {stopwatch.ElapsedMilliseconds}ms.");
    }

    private static RecipeBlock Block(string id, string name) => new()
    {
        Id = id,
        Type = BlockType.Expression,
        Name = name,
        DisplayName = name,
        Config = new ExpressionBlockConfig(string.Empty, new Dictionary<string, string>())
    };

    private static RecipeEdge Edge(string id, string source, string target) => new()
    {
        Id = id,
        SourceBlockId = source,
        TargetBlockId = target
    };
}
