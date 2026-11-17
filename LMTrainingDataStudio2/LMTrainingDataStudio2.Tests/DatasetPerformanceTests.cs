using System.Diagnostics;
using System.Globalization;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Datasets;

namespace LMTrainingDataStudio2.Tests;

/// <summary>
/// Large-file acceptance checks. Skipped unless LMTDS_PERF_TESTS=1.
/// Set LMTDS_PERF_ROW_BYTES to control the average row size (default 1024, matching PLAN 2.6).
/// </summary>
[TestFixture]
public sealed class DatasetPerformanceTests
{
    private const int Rows = 1_000_000;
    private const long MemoryLimitBytes = 300L * 1024 * 1024;

    [Test]
    public async Task OneMillionJsonlRows_MeetsIndexPreviewAndConversionBaseline()
    {
        if (Environment.GetEnvironmentVariable("LMTDS_PERF_TESTS") != "1")
        {
            Assert.Ignore("Set LMTDS_PERF_TESTS=1 to run the 1M-row dataset baseline.");
            return;
        }

        var rowBytes = int.TryParse(
            Environment.GetEnvironmentVariable("LMTDS_PERF_ROW_BYTES"), out var configured)
            ? Math.Clamp(configured, 64, 4096)
            : 1024;

        var directory = Path.Combine(Path.GetTempPath(), "lmtds-perf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "million.jsonl");
            var targetPath = Path.Combine(directory, "million.out.jsonl");

            var generate = Stopwatch.StartNew();
            Generate(sourcePath, Rows, rowBytes);
            generate.Stop();

            var settings = new AppSettings
            {
                CacheDirectory = Path.Combine(directory, "cache"),
                MaxCacheSizeMb = 512
            };

            var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
            var open = Stopwatch.StartNew();
            await using var reader = new JsonlDatasetReader(settings);
            await reader.OpenAsync(sourcePath);
            open.Stop();
            var memoryAfterOpen = GC.GetTotalMemory(forceFullCollection: false);

            Assert.That(reader.TotalLines, Is.EqualTo(Rows));
            Assert.That(open.Elapsed, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(10)),
                $"Index build took {open.Elapsed.TotalSeconds:F1}s.");
            Assert.That(memoryAfterOpen - memoryBefore, Is.LessThan(MemoryLimitBytes),
                $"Memory after indexing: {(memoryAfterOpen - memoryBefore) / 1024.0 / 1024.0:F0} MB.");

            var maxPage = TimeSpan.Zero;
            foreach (var start in new long[] { 0, Rows / 2, Rows - 100 })
            {
                var pageRead = Stopwatch.StartNew();
                var page = await reader.ReadPageAsync(start, 100);
                pageRead.Stop();
                maxPage = pageRead.Elapsed > maxPage ? pageRead.Elapsed : maxPage;

                Assert.That(page, Has.Count.EqualTo(100));
                Assert.That(page[0].LineNumber, Is.EqualTo(start));
            }

            Assert.That(maxPage, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(100)),
                $"Slowest 100-row page read: {maxPage.TotalMilliseconds:F1}ms.");

            var conversion = Stopwatch.StartNew();
            var converter = new DatasetBatchConverter(settings);
            var result = await converter.ConvertAsync(new BatchConversionRequest(
                sourcePath, DataFileType.Jsonl, DatasetFormat.Auto,
                targetPath, DataFileType.Jsonl, DatasetFormat.OpenAI));
            conversion.Stop();

            var memoryAfterConversion = GC.GetTotalMemory(forceFullCollection: false);

            Assert.That(result.ConvertedRows, Is.EqualTo(Rows));
            Assert.That(result.FailedRows, Is.Zero);
            Assert.That(File.ReadLines(targetPath).Count(), Is.EqualTo(Rows));
            Assert.That(memoryAfterConversion - memoryBefore, Is.LessThan(MemoryLimitBytes),
                $"Memory after conversion: {(memoryAfterConversion - memoryBefore) / 1024.0 / 1024.0:F0} MB.");

            TestContext.Progress.WriteLine(
                $"1M rows @ {rowBytes}B: generate {generate.Elapsed.TotalSeconds:F1}s, " +
                $"index {open.Elapsed.TotalSeconds:F1}s, max page {maxPage.TotalMilliseconds:F1}ms, " +
                $"convert {conversion.Elapsed.TotalSeconds:F1}s, " +
                $"memory delta {(memoryAfterConversion - memoryBefore) / 1024.0 / 1024.0:F0} MB.");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task EditSave_LargeInput_StaysBoundedAndConservesRows()
    {
        if (Environment.GetEnvironmentVariable("LMTDS_PERF_TESTS") != "1")
        {
            Assert.Ignore("Set LMTDS_PERF_TESTS=1 to run the large edit/save baseline.");
            return;
        }

        var rows = int.TryParse(Environment.GetEnvironmentVariable("LMTDS_PERF_ROWS"), out var configured)
            ? Math.Clamp(configured, 100_000, 20_000_000)
            : 1_000_000;
        const int rowBytes = 256;

        var directory = Path.Combine(Path.GetTempPath(), "lmtds-perf-edit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var sourcePath = Path.Combine(directory, "edit.jsonl");
            var targetPath = Path.Combine(directory, "edit.out.jsonl");

            var generate = Stopwatch.StartNew();
            GenerateDuplicates(sourcePath, rows, rowBytes);
            generate.Stop();

            var settings = new AppSettings
            {
                CacheDirectory = Path.Combine(directory, "cache"),
                MaxCacheSizeMb = 512
            };

            const int replacements = 1000;
            const int deletions = 100;
            const int insertions = 100;
            var step = Math.Max(1, rows / replacements);
            var replacedRows = new Dictionary<long, string>();
            var deletedRows = new HashSet<long>();
            var insertedRows = new Dictionary<long, IReadOnlyList<DatasetInsertedRow>>();
            for (var i = 0; i < replacements; i++)
            {
                replacedRows[(long)i * step] = $$"""{"messages":[{"role":"user","content":"edited {{i}}"}],"id":{{i}}}""";
            }

            for (var i = 0; i < deletions; i++)
            {
                deletedRows.Add((long)i * step + 1);
            }

            for (var i = 0; i < insertions; i++)
            {
                var anchor = (long)i * step + 2;
                insertedRows[anchor] = new[]
                {
                    new DatasetInsertedRow(i + 1, anchor, $$"""{"messages":[{"role":"user","content":"inserted {{i}}"}],"id":-{{i + 1}}}""")
                };
            }

            var snapshot = new DatasetEditSnapshot(replacedRows, deletedRows, insertedRows, insertions + 1);
            var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

            var save = Stopwatch.StartNew();
            var service = new DatasetEditService(settings);
            var result = await service.SaveAsync(snapshot, sourcePath, DataFileType.Jsonl, targetPath, chunkRows: 4096);
            save.Stop();

            var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            var expectedRows = rows - deletions + insertions;

            Assert.That(result.WrittenRows, Is.EqualTo(expectedRows));
            Assert.That(File.ReadLines(targetPath).Count(), Is.EqualTo(expectedRows));
            Assert.That(memoryAfter - memoryBefore, Is.LessThan(MemoryLimitBytes),
                $"Managed memory delta after edit+save: {(memoryAfter - memoryBefore) / 1024.0 / 1024.0:F0} MB.");

            TestContext.Progress.WriteLine(
                $"{rows:N0} rows @ {rowBytes}B: generate {generate.Elapsed.TotalSeconds:F1}s, " +
                $"edit+save {save.Elapsed.TotalSeconds:F1}s ({replacements} replaced, {deletions} deleted, " +
                $"{insertions} inserted, {expectedRows:N0} written), " +
                $"managed memory delta {(memoryAfter - memoryBefore) / 1024.0 / 1024.0:F0} MB.");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
    }

    [Test]
public async Task QualityOperations_LargeInput_StayBoundedAndConserveRows()
{
    if (Environment.GetEnvironmentVariable("LMTDS_PERF_TESTS") != "1")
    {
        Assert.Ignore("Set LMTDS_PERF_TESTS=1 to run the quality-operation baseline.");
        return;
    }

    var rows = int.TryParse(Environment.GetEnvironmentVariable("LMTDS_PERF_ROWS"), out var configured)
        ? Math.Clamp(configured, 10_000, 20_000_000)
        : 1_000_000;
    const int rowBytes = 256;

    var directory = Path.Combine(Path.GetTempPath(), "lmtds-perf-quality", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    try
    {
        var sourcePath = Path.Combine(directory, "quality.jsonl");
        var splitDirectory = Path.Combine(directory, "splits");

        var generate = Stopwatch.StartNew();
        GenerateDuplicates(sourcePath, rows, rowBytes);
        generate.Stop();

        var settings = new AppSettings
        {
            CacheDirectory = Path.Combine(directory, "cache"),
            MaxCacheSizeMb = 512
        };

        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

        var transform = Stopwatch.StartNew();
        var transformService = new DatasetTransformService(settings);
        var transformResult = await transformService.TransformAsync(
            sourcePath, DataFileType.Jsonl,
            new DatasetTransformOptions(
                Deduplicate: true,
                Shuffle: true,
                Seed: 42,
                Splits: new[] { "train:0.8", "valid:0.2" },
                OutputDirectory: splitDirectory));
        transform.Stop();

        var memoryAfterTransform = GC.GetTotalMemory(forceFullCollection: false);

        var uniqueRows = rows - rows / 2;
        Assert.That(transformResult.TotalRows, Is.EqualTo(rows));
        Assert.That(transformResult.OutputRows, Is.EqualTo(uniqueRows));
        Assert.That(transformResult.DuplicateRows, Is.EqualTo(rows - uniqueRows));
        Assert.That(transformResult.OutputRows + transformResult.DuplicateRows,
            Is.EqualTo(transformResult.TotalRows));
        Assert.That(transformResult.Splits.Sum(split => split.Rows), Is.EqualTo(uniqueRows));
        Assert.That(memoryAfterTransform - memoryBefore, Is.LessThan(MemoryLimitBytes),
            $"Managed memory delta after transform: {(memoryAfterTransform - memoryBefore) / 1024.0 / 1024.0:F0} MB.");

        var statistics = Stopwatch.StartNew();
        var statisticsService = new DatasetStatisticsService(settings);
        var stats = await statisticsService.ComputeAsync(sourcePath, DataFileType.Jsonl);
        statistics.Stop();

        var memoryAfterStats = GC.GetTotalMemory(forceFullCollection: false);

        Assert.That(stats.TotalRows, Is.EqualTo(rows));
        Assert.That(stats.DuplicateRows, Is.EqualTo(rows - uniqueRows));
        Assert.That(stats.LengthBuckets.Sum(bucket => bucket.Count), Is.EqualTo(rows));
        Assert.That(memoryAfterStats - memoryBefore, Is.LessThan(MemoryLimitBytes),
            $"Managed memory delta after statistics: {(memoryAfterStats - memoryBefore) / 1024.0 / 1024.0:F0} MB.");

        TestContext.Progress.WriteLine(
            $"{rows:N0} quality rows @ {rowBytes}B: generate {generate.Elapsed.TotalSeconds:F1}s, " +
            $"dedup+shuffle+split {transform.Elapsed.TotalSeconds:F1}s " +
            $"({transformResult.OutputRows:N0} kept, {transformResult.DuplicateRows:N0} duplicates), " +
            $"statistics {statistics.Elapsed.TotalSeconds:F1}s, " +
            $"managed memory delta {(memoryAfterStats - memoryBefore) / 1024.0 / 1024.0:F0} MB.");
    }
    finally
    {
        try { Directory.Delete(directory, true); } catch { /* best effort */ }
    }
}

private static void GenerateDuplicates(string path, int rows, int rowBytes)
{
    var padding = new string('x', Math.Max(0, rowBytes - 80));
    var half = Math.Max(1, rows / 2);
    using var writer = new StreamWriter(
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        1 << 20);

    for (var i = 0; i < rows; i++)
    {
        var key = i % half;
        writer.Write("{\"messages\":[{\"role\":\"user\",\"content\":\"row ");
        writer.Write(key.ToString(CultureInfo.InvariantCulture));
        writer.Write("\"}],\"id\":");
        writer.Write(key.ToString(CultureInfo.InvariantCulture));
        writer.Write(",\"padding\":\"");
        writer.Write(padding);
        writer.WriteLine("\"}");
    }
}

private static void Generate(string path, int rows, int rowBytes)
    {
        var padding = new string('x', Math.Max(0, rowBytes - 60));
        using var writer = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            1 << 20);

        for (var i = 0; i < rows; i++)
        {
            writer.Write("{\"messages\":[{\"role\":\"user\",\"content\":\"row ");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
            writer.Write("\"}],\"id\":");
            writer.Write(i.ToString(CultureInfo.InvariantCulture));
            writer.Write(",\"padding\":\"");
            writer.Write(padding);
            writer.WriteLine("\"}");
        }
    }
}
