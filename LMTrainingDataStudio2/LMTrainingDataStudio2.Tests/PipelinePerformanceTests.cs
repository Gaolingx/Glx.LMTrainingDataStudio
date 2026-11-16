using System.Diagnostics;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

/// <summary>
/// End-to-end pipeline acceptance check. Skipped unless LMTDS_PERF_TESTS=1.
/// Set LMTDS_PERF_PIPELINE_ROWS to override the default 1,000,000 rows.
/// </summary>
[TestFixture]
public sealed class PipelinePerformanceTests
{
    private const int DefaultRows = 1_000_000;
    private const long MemoryLimitBytes = 300L * 1024 * 1024;

    [Test]
    public async Task OneMillionRowPipeline_SeedExpressionSftExport_StaysWithinMemoryBudget()
    {
        if (Environment.GetEnvironmentVariable("LMTDS_PERF_TESTS") != "1")
        {
            Assert.Ignore("Set LMTDS_PERF_TESTS=1 to run the 1M-row pipeline baseline.");
            return;
        }

        var rows = int.TryParse(
            Environment.GetEnvironmentVariable("LMTDS_PERF_PIPELINE_ROWS"), out var configured)
            ? Math.Clamp(configured, 1_000, 10_000_000)
            : DefaultRows;

        var directory = Path.Combine(Path.GetTempPath(), "lmtds-perf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "pipeline.jsonl");
            var outputPath = Path.Combine(directory, "pipeline.out.jsonl");

            var generate = Stopwatch.StartNew();
            await GenerateAsync(sourcePath, rows);
            generate.Stop();

            var settings = new AppSettings
            {
                CacheDirectory = Path.Combine(directory, "cache"),
                MaxCacheSizeMb = 512
            };

            var recipe = CreateRecipe(sourcePath, outputPath);
            var runner = new RecipeRunner(
                new RecipeValidationService(),
                new IBlockExecutor[]
                {
                    new SeedBlockExecutor(settings),
                    new ExpressionBlockExecutor(),
                    new SftExportBlockExecutor()
                },
                options: new RecipeRunOptions(
                    MaxParallelism: 1,
                    OutputDirectory: Path.Combine(directory, "runs"),
                    SpillThresholdRows: 5_000,
                    SpillDirectory: Path.Combine(directory, "spill")));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

            var run = await runner.RunAsync(recipe).WaitAsync(TimeSpan.FromMinutes(30));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
            var memoryDelta = memoryAfter - memoryBefore;
            TestContext.Progress.WriteLine(
                $"Pipeline baseline: {rows} rows, generate {generate.Elapsed.TotalSeconds:F1}s, " +
                $"run {(run.FinishedAt - run.StartedAt)?.TotalSeconds:F1}s, " +
                $"retained memory {memoryDelta / 1024.0 / 1024.0:F0} MB.");

            Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', run.Logs));
            Assert.That(run.Nodes.Single(node => node.BlockName == "seed").OutputRows, Is.EqualTo(rows));
            Assert.That(run.Nodes.Single(node => node.BlockName == "export").OutputRows, Is.EqualTo(rows));
            Assert.That(await CountLinesAsync(outputPath), Is.EqualTo(rows));
            Assert.That(memoryDelta, Is.LessThan(MemoryLimitBytes),
                $"Retained managed memory: {memoryDelta / 1024.0 / 1024.0:F0} MB " +
                $"(generate {generate.Elapsed.TotalSeconds:F1}s, run {run.FinishedAt - run.StartedAt}).");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
    }

    private static Recipe CreateRecipe(string sourcePath, string outputPath)
    {
        var recipe = new Recipe { Name = "pipeline-perf" };

        var seed = new RecipeBlock
        {
            Id = "seed",
            Type = BlockType.Seed,
            Name = "seed",
            DisplayName = "seed",
            Config = new SeedBlockConfig(
                sourcePath, DataFileType.Jsonl, Streaming: true, UseIndexCache: false)
        };
        seed.OutputPorts.Add(Port("seed_out", PortDirection.Output));

        var expression = new RecipeBlock
        {
            Id = "expr",
            Type = BlockType.Expression,
            Name = "expr",
            DisplayName = "expr",
            Config = new ExpressionBlockConfig(
                """{"data":{{ messages | json }}}""",
                new Dictionary<string, string>())
        };
        expression.InputPorts.Add(Port("expr_in", PortDirection.Input));
        expression.OutputPorts.Add(Port("expr_out", PortDirection.Output));

        var export = new RecipeBlock
        {
            Id = "export",
            Type = BlockType.SftExport,
            Name = "export",
            DisplayName = "export",
            Config = new SftExportBlockConfig(outputPath)
        };
        export.InputPorts.Add(Port("export_in", PortDirection.Input));

        recipe.Blocks.AddRange(new[] { seed, expression, export });
        recipe.Edges.Add(Edge("seed_out", "expr_in", seed, expression));
        recipe.Edges.Add(Edge("expr_out", "export_in", expression, export));
        return recipe;
    }

    private static BlockPort Port(string id, PortDirection direction) => new()
    {
        Id = id,
        Name = id,
        DataType = BlockPortSchema.RowsDataType,
        Direction = direction
    };

    private static RecipeEdge Edge(
        string sourcePort, string targetPort, RecipeBlock source, RecipeBlock target) => new()
    {
        SourceBlockId = source.Id,
        SourcePortId = sourcePort,
        TargetBlockId = target.Id,
        TargetPortId = targetPort
    };

    private static async Task GenerateAsync(string path, int rows)
    {
        var padding = new string('x', 160);
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        for (var i = 0; i < rows; i++)
        {
            await writer.WriteLineAsync(
                $"{{\"messages\":[{{\"role\":\"user\",\"content\":\"row {i} {padding}\"}}]}}");
        }
    }

    private static async Task<long> CountLinesAsync(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var count = 0L;
        while (await reader.ReadLineAsync() is not null)
        {
            count++;
        }

        return count;
    }
}
