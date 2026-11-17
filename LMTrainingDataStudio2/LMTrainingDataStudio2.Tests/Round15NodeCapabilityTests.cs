using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;
using LMTrainingDataStudio2.Services.Executors;
using LMTrainingDataStudio2.Services.Simulation;
using LMTrainingDataStudio2.ViewModels;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class Round15NodeCapabilityTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lmtds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [Test]
    public async Task LlmApiClient_SendsToolsMessagesAndStructuredSchema_AndParsesUsage()
    {
        string? body = null;
        var client = CreateLlmClient((request, content) =>
        {
            body = content;
            return JsonResponse(
                """{"choices":[{"message":{"content":"{\"ok\":true}","tool_calls":[{"id":"call_1","function":{"name":"python","arguments":"{\"code\":\"1\"}"}}]}}],"usage":{"prompt_tokens":11,"completion_tokens":7}}""");
        });
        var tools = new[]
        {
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = "python",
                    Description = "run python",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>()
                    }
                }
            }
        };

        var result = await client.CompleteAsync(
            new LlmCompletionRequest(
                [new LlmChatTurn("system", "be brief"), new LlmChatTurn("user", "hello")],
                tools,
                """{"type":"object","required":["ok"]}"""),
            CreateLlmConfig());

        Assert.That(result.Content, Is.EqualTo("{\"ok\":true}"));
        Assert.That(result.Usage!.PromptTokens, Is.EqualTo(11));
        Assert.That(result.Usage.CompletionTokens, Is.EqualTo(7));
        Assert.That(result.ToolCalls![0].Name, Is.EqualTo("python"));

        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("system"));
        Assert.That(root.GetProperty("messages")[1].GetProperty("content").GetString(), Is.EqualTo("hello"));
        Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString(),
            Is.EqualTo("python"));
        Assert.That(root.GetProperty("response_format").GetProperty("json_schema")
            .GetProperty("schema").GetProperty("type").GetString(), Is.EqualTo("object"));
    }

    [Test]
    public async Task LlmExecutor_UsesRowMessagesAndCollectsFailureSamples()
    {
        var client = new CapturingLlmClient();
        var failures = Path.Combine(_directory, "llm-failures.jsonl");
        var block = CreateBlock(BlockType.Llm, new LlmBlockConfig(
            "openai",
            "gpt-4o",
            Temperature: 0.2,
            MaxTokens: 128,
            Prompt: null,
            UseRowMessages: true,
            MaxConcurrency: 2,
            MaxFailureSamples: 10,
            InputCostPerMillionTokens: 1,
            OutputCostPerMillionTokens: 2,
            ContinueOnError: true,
            FailureOutputPath: failures));
        var rows = new[]
        {
            new DatasetRow(0, """{"messages":[{"role":"user","content":"first"}]}"""),
            new DatasetRow(1, """{"messages":[{"role":"user","content":"fail"}]}"""),
            new DatasetRow(2, """{"messages":[{"role":"user","content":"third"}]}""")
        };
        var logs = new List<string>();

        var executor = new LlmBlockExecutor(client);
        var result = await executor.ExecuteAsync(CreateContext(block, rows, logs), CancellationToken.None);
        var output = await CollectAsync(result.Output);

        Assert.That(output, Has.Count.EqualTo(2));
        Assert.That(client.MaxConcurrency, Is.GreaterThanOrEqualTo(2));
        var first = JsonDocument.Parse(output[0].Json);
        Assert.That(first.RootElement.GetProperty("messages")[0].GetProperty("content").GetString(),
            Is.EqualTo("first"));
        Assert.That(first.RootElement.GetProperty("messages")[1].GetProperty("role").GetString(),
            Is.EqualTo("assistant"));
        Assert.That(File.Exists(failures), Is.True);
        Assert.That(await File.ReadAllTextAsync(failures), Does.Contain("Row 1"));
        Assert.That(logs.Any(log => log.Contains("Tokens: 2+2")), Is.True);
        Assert.That(logs.Any(log => log.Contains("skipped 1")), Is.True);
    }

    [Test]
    public async Task ValidatorExecutor_MultipleRulesAndPassRejectPorts_ConserveRows()
    {
        var config = new ValidatorBlockConfig(
            "expression",
            "messages.length > 0",
            RejectOnFailure: true,
            Rules:
            [
                new ValidatorRuleSpec("expression", "messages.length > 0"),
                new ValidatorRuleSpec("regex", "\"role\":\"user\"")
            ],
            MatchAllRules: true);
        var block = CreateBlock(BlockType.Validator, config);
        var rows = new[]
        {
            new DatasetRow(0, """{"messages":[{"role":"user"}]}"""),
            new DatasetRow(1, """{"messages":[]}"""),
            new DatasetRow(2, """{"messages":[{"role":"assistant"}]}""")
        };

        var executor = new ValidatorBlockExecutor();
        var result = await executor.ExecuteAsync(
            CreateContext(block, rows, connectedPorts: new HashSet<string> { "pass", "reject" }),
            CancellationToken.None);
        var passTask = CollectAsync(result.Output);
        var rejectTask = CollectAsync(result.AdditionalOutputs![BlockPortSchema.ValidatorRejectPort]);
        await Task.WhenAll(passTask, rejectTask);

        Assert.That(passTask.Result, Has.Count.EqualTo(1));
        Assert.That(rejectTask.Result, Has.Count.EqualTo(2));
        Assert.That(rejectTask.Result[0].Json, Is.EqualTo(rows[1].Json));
    }

    [Test]
    public async Task ValidatorExecutor_ScriptTimeout_RejectsRowWithoutHanging()
    {
        var config = new ValidatorBlockConfig(
            "script",
            "slow",
            RejectOnFailure: true,
            TimeoutMs: 20);
        var block = CreateBlock(BlockType.Validator, config);
        var executor = new ValidatorBlockExecutor(async (_, _, ct) =>
        {
            await Task.Delay(200, ct);
            return true;
        });

        var result = await executor.ExecuteAsync(
            CreateContext(block, new[] { new DatasetRow(0, """{"messages":[{"role":"user"}]}""") },
                connectedPorts: new HashSet<string> { "pass", "reject" }),
            CancellationToken.None);
        var pass = await CollectAsync(result.Output);
        var reject = await CollectAsync(result.AdditionalOutputs!["reject"]);

        Assert.That(pass, Is.Empty);
        Assert.That(reject, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SamplerExecutor_SplitsEmitSeparatePorts_AndConserveRows()
    {
        var config = new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 7,
            Splits: new[] { "train:0.5", "valid:0.5" },
            OutputDirectory: _directory);
        var block = CreateBlock(BlockType.Sampler, config);
        var rows = Enumerable.Range(0, 40)
            .Select(i => new DatasetRow(i, $"{{\"i\":{i}}}"))
            .ToArray();

        var executor = new SamplerBlockExecutor();
        var result = await executor.ExecuteAsync(
            CreateContext(block, rows, connectedPorts: new HashSet<string> { "train", "valid" }),
            CancellationToken.None);

        var primary = await CollectAsync(result.Output);
        var train = await CollectAsync(result.AdditionalOutputs!["train"]);
        var valid = await CollectAsync(result.AdditionalOutputs["valid"]);

        Assert.That(primary, Is.Empty);
        Assert.That(train.Count + valid.Count, Is.EqualTo(rows.Length));
        Assert.That(train.Select(row => row.Json).Intersect(valid.Select(row => row.Json)), Is.Empty);
        Assert.That(File.Exists(Path.Combine(_directory, "train.jsonl")), Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "valid.jsonl")), Is.True);
    }

    [Test]
    public async Task SamplerExecutor_StratifiedSplits_KeepPerGroupBalance()
    {
        var config = new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 11,
            Splits: new[] { "train:0.5", "valid:0.5" },
            OutputDirectory: _directory,
            StratifyField: "category");
        var block = CreateBlock(BlockType.Sampler, config);
        var rows = Enumerable.Range(0, 1000)
            .Select(i => new DatasetRow(i, $"{{\"category\":\"{(i % 2 == 0 ? "a" : "b")}\",\"i\":{i}}}"))
            .ToArray();

        var executor = new SamplerBlockExecutor();
        var result = await executor.ExecuteAsync(
            CreateContext(block, rows, connectedPorts: new HashSet<string> { "train", "valid" }),
            CancellationToken.None);
        var train = await CollectAsync(result.AdditionalOutputs!["train"]);
        var valid = await CollectAsync(result.AdditionalOutputs["valid"]);

        var trainA = train.Count(row => row.Json.Contains("\"category\":\"a\"", StringComparison.Ordinal));
        var trainB = train.Count(row => row.Json.Contains("\"category\":\"b\"", StringComparison.Ordinal));

        Assert.That(trainA, Is.InRange(200, 300));
        Assert.That(trainB, Is.InRange(200, 300));
        Assert.That(train.Count + valid.Count, Is.EqualTo(1000));
    }

    [Test]
    public void SamplerEditor_ExposesStratifyAndStatsPreview()
    {
        var applied = new List<SamplerBlockConfig>();
        var editor = new SamplerConfigEditorViewModel(
            new SamplerBlockConfig(null, null, 42, new[] { "train:0.8", "valid:0.2" }, _directory),
            applied.Add);

        editor.StratifyField = "category";
        editor.PreviewRowsText = "1000";

        Assert.That(editor.StatsPreview, Does.Contain("train"));
        Assert.That(editor.StatsPreview, Does.Contain("~800"));
        Assert.That(editor.StatsPreview, Does.Contain("category"));
        Assert.That(applied[^1].StratifyField, Is.EqualTo("category"));
    }

    [Test]
    public async Task QuerySourceExecutor_RendersTemplateVariablesAndExternalRows()
    {
        var literal = new QuerySourceBlockConfig(
            new[] { "Explain {{ topic }} #{{ index }}" },
            Repeat: 2,
            TemplateVariablesJson: """{"topic":"TCP"}""");
        var literalExecutor = new QuerySourceBlockExecutor(new AppSettings
        {
            CacheDirectory = Path.Combine(_directory, "cache")
        });
        var literalResult = await literalExecutor.ExecuteAsync(
            CreateContext(CreateBlock(BlockType.QuerySource, literal), Array.Empty<DatasetRow>()),
            CancellationToken.None);
        var literalRows = await CollectAsync(literalResult.Output);
        Assert.That(literalRows.Select(row => JsonDocument.Parse(row.Json).RootElement
            .GetProperty("query").GetString()),
            Is.EqualTo(new[] { "Explain TCP #0", "Explain TCP #1" }));

        var sourcePath = Path.Combine(_directory, "queries.jsonl");
        await File.WriteAllLinesAsync(sourcePath,
        [
            """{"query":"What is {{ topic }}?","system":"be brief"}""",
            """{"query":"Why {{ topic }}?"}"""
        ]);
        var external = new QuerySourceBlockConfig(
            Array.Empty<string>(),
            SourcePath: sourcePath,
            QueryField: "query",
            SystemField: "system",
            TemplateVariablesJson: """{"topic":"UDP"}""");
        var externalResult = await literalExecutor.ExecuteAsync(
            CreateContext(CreateBlock(BlockType.QuerySource, external), Array.Empty<DatasetRow>()),
            CancellationToken.None);
        var externalRows = await CollectAsync(externalResult.Output);

        var first = JsonDocument.Parse(externalRows[0].Json).RootElement;
        Assert.That(first.GetProperty("query").GetString(), Is.EqualTo("What is UDP?"));
        Assert.That(first.GetProperty("system").GetString(), Is.EqualTo("be brief"));
    }

    [Test]
    public void SimulationEditor_EditsToolsRulesScriptAndPricePresets()
    {
        var applied = new List<SimulationBlockConfig>();
        var editor = new SimulationConfigEditorViewModel(
            new SimulationBlockConfig(
                "model",
                Tools: new[] { SimulatedToolCatalog.Python() },
                Rules: new ToolHarnessRules { MaxRetries = 2 },
                Script: new[] { new SimulatedTurn("hi", null) }),
            applied.Add);

        Assert.That(editor.Tools, Has.Count.EqualTo(1));
        Assert.That(editor.ScriptTurns, Has.Count.EqualTo(1));

        editor.Tools[0].Name = "custom_python";
        editor.ScriptTurns[0].Content = "updated";
        editor.AllowedToolsText = "custom_python, web_fetch";
        editor.ToolMaxRetries = 4;
        editor.RequestsPerMinute = 30;
        editor.StreamDeltas = true;
        editor.SelectedPricePreset = editor.PricePresets.Single(preset => preset.Id == "gpt-4o-mini");

        var config = applied[^1];
        Assert.That(config.Tools![0].Name, Is.EqualTo("custom_python"));
        Assert.That(config.Script![0].Content, Is.EqualTo("updated"));
        Assert.That(config.Rules!.AllowedTools, Is.EqualTo(new[] { "custom_python", "web_fetch" }));
        Assert.That(config.Rules.MaxRetries, Is.EqualTo(4));
        Assert.That(config.RequestsPerMinute, Is.EqualTo(30));
        Assert.That(config.StreamDeltas, Is.True);
        Assert.That(config.InputCostPerMillionTokens, Is.EqualTo(0.15));
    }

    [Test]
    public async Task ProviderClient_StreamDeltas_AreAccumulated()
    {
        var sse = string.Join("\n",
        [
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}",
            "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"python\",\"arguments\":\"{\\\"code\\\":\"}}]}}]}",
            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"\\\"1\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":3}}",
            "data: [DONE]"
        ]);
        var http = new HttpClient(new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
            return response;
        }));
        var config = new SimulationBlockConfig(
            "gpt-4o",
            Provider: LlmProviderIds.OpenAiChat,
            ModelMode: SimulationModelMode.Provider,
            StreamDeltas: true);
        var client = new ProviderChatCompletionClient(http, new FakeCredentials("test-key"), config);

        var response = await client.CompleteAsync(
            new ChatCompletionRequest(
                "gpt-4o",
                [new ChatMessage { Role = "user", Content = "hi" }],
                Array.Empty<ToolDefinition>(),
                0.2,
                128,
                0),
            CancellationToken.None);

        Assert.That(response.Message.Content, Is.EqualTo("Hello"));
        Assert.That(response.Message.ToolCalls![0].Function.Name, Is.EqualTo("python"));
        Assert.That(response.Message.ToolCalls[0].Function.Arguments, Is.EqualTo("{\"code\":\"1\"}"));
        Assert.That(response.FinishReason, Is.EqualTo("tool_calls"));
        Assert.That(response.Usage!.PromptTokens, Is.EqualTo(5));
    }

    [Test]
    public async Task Runner_RoutesSamplerSplitPortsToDownstreamBlocks()
    {
        var dataPath = Path.Combine(_directory, "data.jsonl");
        await File.WriteAllLinesAsync(dataPath,
            Enumerable.Range(0, 10).Select(i => $"{{\"i\":{i}}}"));
        var trainPath = Path.Combine(_directory, "train-out.jsonl");
        var validPath = Path.Combine(_directory, "valid-out.jsonl");

        var recipe = new Recipe { Name = "split-routing" };
        var seed = CreateRecipeBlock("seed", BlockType.Seed, "seed_data",
            new SeedBlockConfig(dataPath, DataFileType.Jsonl, false, true));
        var sampler = CreateRecipeBlock("sampler", BlockType.Sampler, "split",
            new SamplerBlockConfig(null, null, 3, new[] { "train:5", "valid:5" }, _directory));
        var trainExport = CreateRecipeBlock("export-train", BlockType.SftExport, "export_train",
            new SftExportBlockConfig(trainPath));
        var validExport = CreateRecipeBlock("export-valid", BlockType.SftExport, "export_valid",
            new SftExportBlockConfig(validPath));

        recipe.Blocks.AddRange([seed, sampler, trainExport, validExport]);
        recipe.Edges.Add(new RecipeEdge
        {
            SourceBlockId = seed.Id,
            SourcePortId = seed.OutputPorts[0].Id,
            TargetBlockId = sampler.Id,
            TargetPortId = sampler.InputPorts[0].Id
        });
        recipe.Edges.Add(new RecipeEdge
        {
            SourceBlockId = sampler.Id,
            SourcePortId = sampler.OutputPorts[0].Id,
            TargetBlockId = trainExport.Id,
            TargetPortId = trainExport.InputPorts[0].Id
        });
        recipe.Edges.Add(new RecipeEdge
        {
            SourceBlockId = sampler.Id,
            SourcePortId = sampler.OutputPorts[1].Id,
            TargetBlockId = validExport.Id,
            TargetPortId = validExport.InputPorts[0].Id
        });

        var runner = new RecipeRunner(
            new RecipeValidationService(),
            new IBlockExecutor[]
            {
                new SeedBlockExecutor(new AppSettings
                {
                    CacheDirectory = Path.Combine(_directory, "cache")
                }),
                new SamplerBlockExecutor(),
                new SftExportBlockExecutor()
            });
        var run = await runner.RunAsync(recipe);

        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), string.Join('\n', run.Logs));
        Assert.That((await File.ReadAllLinesAsync(trainPath)), Has.Length.EqualTo(5));
        Assert.That((await File.ReadAllLinesAsync(validPath)), Has.Length.EqualTo(5));
        Assert.That(run.Nodes.Single(node => node.BlockId == sampler.Id).OutputRows, Is.EqualTo(10));
    }

    [Test]
    public void Round15ConfigFields_SurviveSerialization()
    {
        var configs = new BlockConfig[]
        {
            new LlmBlockConfig(
                "openai", "gpt-4o", 0.2, 128, "p",
                SystemPrompt: "sys",
                UseRowMessages: true,
                MaxConcurrency: 3,
                ResponseSchema: """{"type":"object"}""",
                FailureOutputPath: "out/fail.jsonl",
                MaxFailureSamples: 7,
                InputCostPerMillionTokens: 1.5,
                OutputCostPerMillionTokens: 2.5,
                ContinueOnError: true),
            new ValidatorBlockConfig(
                "expression", "messages.length > 0", true,
                Rules:
                [
                    new ValidatorRuleSpec("expression", "messages.length > 0"),
                    new ValidatorRuleSpec("regex", "user", Negate: true)
                ],
                MatchAllRules: false,
                TimeoutMs: 50),
            new SamplerBlockConfig(null, null, 5, new[] { "train:0.8", "valid:0.2" }, _directory, "category"),
            new SimulationBlockConfig(
                "gpt-4o", RequestsPerMinute: 60, StreamDeltas: true),
            new QuerySourceBlockConfig(
                new[] { "hi {{ topic }}" },
                SourcePath: "queries.jsonl",
                QueryField: "prompt",
                SystemField: "system",
                TemplateVariablesJson: """{"topic":"x"}""")
        };

        foreach (var config in configs)
        {
            var json = JsonSerializer.Serialize(config, RecipeStore.SerializerOptions);
            var roundtrip = JsonSerializer.Deserialize<BlockConfig>(json, RecipeStore.SerializerOptions);
            var roundtripJson = JsonSerializer.Serialize(roundtrip, RecipeStore.SerializerOptions);

            Assert.That(roundtripJson, Is.EqualTo(json), config.GetType().Name);
        }
    }

    [Test]
    public void Validation_FlagsNewNodeConfigurationProblems()
    {
        var recipe = new Recipe();
        recipe.Blocks.Add(CreateBlock(BlockType.Llm, new LlmBlockConfig(
            "openai", "gpt-4o", 0.2, 128, "p", MaxConcurrency: 0)));
        var secondLlm = CreateBlock(BlockType.Llm, new LlmBlockConfig(
            "openai", "gpt-4o", 0.2, 128, "p", MaxFailureSamples: -1));
        secondLlm.Id = "llm-2";
        secondLlm.Name = "llm-2";
        recipe.Blocks.Add(secondLlm);
        var regexValidator = CreateBlock(BlockType.Validator, new ValidatorBlockConfig(
            "regex", "(", RejectOnFailure: true));
        regexValidator.Id = "val-regex";
        regexValidator.Name = "val-regex";
        recipe.Blocks.Add(regexValidator);
        var timeoutValidator = CreateBlock(BlockType.Validator, new ValidatorBlockConfig(
            "expression", "messages.length > 0", RejectOnFailure: true, TimeoutMs: -1));
        timeoutValidator.Id = "val-timeout";
        timeoutValidator.Name = "val-timeout";
        recipe.Blocks.Add(timeoutValidator);
        recipe.Blocks.Add(CreateBlock(BlockType.QuerySource, new QuerySourceBlockConfig(
            new[] { "q" }, TemplateVariablesJson: "[1,2]")));
        var rateSimulation = CreateBlock(BlockType.Simulation, new SimulationBlockConfig(
            "gpt-4o", RequestsPerMinute: -1));
        rateSimulation.Id = "sim-rate";
        rateSimulation.Name = "sim-rate";
        recipe.Blocks.Add(rateSimulation);
        var streamSimulation = CreateBlock(BlockType.Simulation, new SimulationBlockConfig(
            "claude", StreamDeltas: true, Provider: LlmProviderIds.Anthropic));
        streamSimulation.Id = "sim-stream";
        streamSimulation.Name = "sim-stream";
        recipe.Blocks.Add(streamSimulation);

        var messages = new RecipeValidationService().Validate(recipe).Issues
            .Select(issue => issue.Message)
            .ToArray();

        Assert.That(messages.Any(message => message.Contains("max concurrency")), Is.True);
        Assert.That(messages.Any(message => message.Contains("max failure samples")), Is.True);
        Assert.That(messages.Any(message => message.Contains("regular expression")), Is.True);
        Assert.That(messages.Any(message => message.Contains("timeout must not be negative")), Is.True);
        Assert.That(messages.Any(message => message.Contains("template variables must be a JSON object")), Is.True);
        Assert.That(messages.Any(message => message.Contains("requests per minute")), Is.True);
        Assert.That(messages.Any(message => message.Contains("streams deltas only")), Is.True);
    }

    [Test]
    public async Task LlmExecutor_InvalidStructuredOutput_IsReportedAsFailure()
    {
        var client = new StaticLlmClient("not json");
        var failures = Path.Combine(_directory, "structured-failures.jsonl");
        var block = CreateBlock(BlockType.Llm, new LlmBlockConfig(
            "openai", "gpt-4o", 0.2, 128, "p",
            ResponseSchema: """{"type":"object"}""",
            FailureOutputPath: failures,
            ContinueOnError: true));
        var executor = new LlmBlockExecutor(client);

        var result = await executor.ExecuteAsync(
            CreateContext(block, new[] { new DatasetRow(0, """{"content":"x"}""") }),
            CancellationToken.None);
        var rows = await CollectAsync(result.Output);

        Assert.That(rows, Is.Empty);
        Assert.That(File.Exists(failures), Is.True);
        Assert.That(await File.ReadAllTextAsync(failures), Does.Contain("structured output"));
    }

    private LlmApiClient CreateLlmClient(Func<HttpRequestMessage, string, HttpResponseMessage> responder) =>
        new(new HttpClient(new StubHandler(responder)), new FakeCredentials("test-key"));

    private static LlmBlockConfig CreateLlmConfig() => new(
        LlmProviderIds.OpenAiChat,
        "gpt-4o",
        Temperature: 0.2,
        MaxTokens: 128,
        Prompt: "hello");

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static RecipeBlock CreateBlock(BlockType type, BlockConfig config) => new()
    {
        Id = type.ToString(),
        Type = type,
        Name = type.ToString(),
        DisplayName = type.ToString(),
        Config = config
    };

    private static RecipeBlock CreateRecipeBlock(
        string id, BlockType type, string name, BlockConfig config) => new()
    {
        Id = id,
        Type = type,
        Name = name,
        DisplayName = name,
        Config = config,
        InputPorts = BlockPortSchema.CreateInputPorts(type),
        OutputPorts = BlockPortSchema.CreateOutputPorts(type, config)
    };

    private static BlockExecutionContext CreateContext(
        RecipeBlock block,
        IEnumerable<DatasetRow> input,
        List<string>? logs = null,
        IReadOnlySet<string>? connectedPorts = null) => new()
    {
        Block = block,
        Input = ToAsync(input),
        Log = message => logs?.Add(message),
        ConnectedOutputPorts = connectedPorts
    };

    private static async IAsyncEnumerable<DatasetRow> ToAsync(IEnumerable<DatasetRow> rows)
    {
        await Task.Yield();
        foreach (var row in rows)
        {
            yield return row;
        }
    }

    private static async Task<List<DatasetRow>> CollectAsync(IAsyncEnumerable<DatasetRow> rows)
    {
        var result = new List<DatasetRow>();
        await foreach (var row in rows)
        {
            result.Add(row);
        }

        return result;
    }

    private sealed class StaticLlmClient(string content) : ILlmClient
    {
        public Task<string> CompleteAsync(
            string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default) =>
            Task.FromResult(content);

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            LlmBlockConfig config,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmCompletionResult(content, null, new LlmTokenUsage(1, 1)));
    }

    private sealed class CapturingLlmClient : ILlmClient
    {
        private int _running;

        public int MaxConcurrency { get; private set; }

        public Task<string> CompleteAsync(
            string prompt, LlmBlockConfig config, CancellationToken cancellationToken = default) =>
            CompleteAsync(new LlmCompletionRequest([new LlmChatTurn("user", prompt)]), config, cancellationToken)
                .ContinueWith(task => task.Result.Content, cancellationToken);

        public async Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request,
            LlmBlockConfig config,
            CancellationToken cancellationToken = default)
        {
            var running = Interlocked.Increment(ref _running);
            MaxConcurrency = Math.Max(MaxConcurrency, running);
            try
            {
                var prompt = request.Messages[^1].Content ?? string.Empty;
                await Task.Delay(25, cancellationToken);
                if (prompt.Contains("fail", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("synthetic failure");
                }

                return new LlmCompletionResult(
                    "reply:" + prompt,
                    Usage: new LlmTokenUsage(1, 1));
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private sealed class FakeCredentials(string? apiKey) : ILlmCredentials
    {
        public string? GetApiKey(string provider) => apiKey;

        public string? GetBaseUrl(string provider) => null;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, content);
        }
    }
}
