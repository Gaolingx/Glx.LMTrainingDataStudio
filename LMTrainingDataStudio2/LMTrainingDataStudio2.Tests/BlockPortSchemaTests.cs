using LMTrainingDataStudio2.Models;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class BlockPortSchemaTests
{
    [TestCase(BlockType.Seed, false, true)]
    [TestCase(BlockType.QuerySource, false, true)]
    [TestCase(BlockType.ToolProfile, false, false)]
    [TestCase(BlockType.SftExport, true, false)]
    [TestCase(BlockType.Llm, true, true)]
    [TestCase(BlockType.Expression, true, true)]
    [TestCase(BlockType.Validator, true, true)]
    [TestCase(BlockType.Sampler, true, true)]
    [TestCase(BlockType.Simulation, true, true)]
    [TestCase(BlockType.Cleaner, true, true)]
    public void ExpectsInputAndOutput_MatchBlockRole(BlockType type, bool expectsInput, bool expectsOutput)
    {
        Assert.That(BlockPortSchema.ExpectsInput(type), Is.EqualTo(expectsInput));
        Assert.That(BlockPortSchema.ExpectsOutput(type), Is.EqualTo(expectsOutput));
    }

    [Test]
    public void CreateInputPorts_SourceAndSinkRoles_AreCorrect()
    {
        Assert.That(BlockPortSchema.CreateInputPorts(BlockType.Seed), Is.Empty);
        Assert.That(BlockPortSchema.CreateInputPorts(BlockType.QuerySource), Is.Empty);
        Assert.That(BlockPortSchema.CreateInputPorts(BlockType.ToolProfile), Is.Empty);

        var input = BlockPortSchema.CreateInputPorts(BlockType.SftExport);
        Assert.That(input, Has.Count.EqualTo(1));
        Assert.That(input[0].Name, Is.EqualTo("input"));
        Assert.That(input[0].DataType, Is.EqualTo(BlockPortSchema.RowsDataType));
        Assert.That(input[0].Direction, Is.EqualTo(PortDirection.Input));
    }

    [Test]
    public void CreateOutputPorts_SourceAndSinkRoles_AreCorrect()
    {
        Assert.That(BlockPortSchema.CreateOutputPorts(BlockType.SftExport), Is.Empty);
        Assert.That(BlockPortSchema.CreateOutputPorts(BlockType.ToolProfile), Is.Empty);

        var seed = BlockPortSchema.CreateOutputPorts(BlockType.Seed);
        Assert.That(seed, Has.Count.EqualTo(1));
        Assert.That(seed[0].Name, Is.EqualTo("rows"));
        Assert.That(seed[0].DataType, Is.EqualTo(BlockPortSchema.RowsDataType));
        Assert.That(seed[0].Direction, Is.EqualTo(PortDirection.Output));

        var queries = BlockPortSchema.CreateOutputPorts(BlockType.QuerySource);
        Assert.That(queries, Has.Count.EqualTo(1));
        Assert.That(queries[0].Name, Is.EqualTo("queries"));
    }

    [TestCase(BlockType.Llm)]
    [TestCase(BlockType.Expression)]
    [TestCase(BlockType.Sampler)]
    [TestCase(BlockType.Simulation)]
    [TestCase(BlockType.Cleaner)]
    public void CreatePorts_Transforms_HaveOneInputAndOneOutput(BlockType type)
    {
        var input = BlockPortSchema.CreateInputPorts(type);
        var output = BlockPortSchema.CreateOutputPorts(type);

        Assert.That(input, Has.Count.EqualTo(1));
        Assert.That(input[0].Name, Is.EqualTo("input"));
        Assert.That(input[0].Direction, Is.EqualTo(PortDirection.Input));
        Assert.That(output, Has.Count.EqualTo(1));
        Assert.That(output[0].Name, Is.EqualTo("output"));
        Assert.That(output[0].Direction, Is.EqualTo(PortDirection.Output));
        Assert.That(output[0].DataType, Is.EqualTo(BlockPortSchema.RowsDataType));
    }

    [Test]
    public void CreateOutputPorts_Validator_HasPassAndRejectPorts()
    {
        var outputs = BlockPortSchema.CreateOutputPorts(BlockType.Validator);

        Assert.That(outputs.Select(port => port.Name),
            Is.EqualTo(new[] { BlockPortSchema.ValidatorPassPort, BlockPortSchema.ValidatorRejectPort }));
        Assert.That(outputs.Select(port => port.Direction), Is.All.EqualTo(PortDirection.Output));
        Assert.That(outputs.Select(port => port.DataType), Is.All.EqualTo(BlockPortSchema.RowsDataType));
    }

    [Test]
    public void CreateOutputPorts_SamplerSplits_UseSplitNames()
    {
        var config = new SamplerBlockConfig(
            Count: null,
            Ratio: null,
            Seed: 42,
            Splits: new[] { "train:0.8", "valid:0.2" });

        var outputs = BlockPortSchema.CreateOutputPorts(BlockType.Sampler, config);

        Assert.That(outputs.Select(port => port.Name), Is.EqualTo(new[] { "train", "valid" }));
    }

    [Test]
    public void CreatePorts_AssignStableNamesButFreshIds()
    {
        var first = BlockPortSchema.CreateOutputPorts(BlockType.Seed)[0];
        var second = BlockPortSchema.CreateOutputPorts(BlockType.Seed)[0];

        Assert.That(first.Name, Is.EqualTo(second.Name));
        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }
}
