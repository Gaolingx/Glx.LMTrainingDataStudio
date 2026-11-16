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
