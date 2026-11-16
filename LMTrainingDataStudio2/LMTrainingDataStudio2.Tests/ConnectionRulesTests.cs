using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class ConnectionRulesTests
{
    [Test]
    public void Validate_ValidConnection_ReturnsNull()
    {
        var seed = CreateBlock("seed", BlockType.Seed);
        var validator = CreateBlock("val", BlockType.Validator);

        var error = ConnectionRules.Validate(
            new[] { seed, validator },
            Array.Empty<RecipeEdge>(),
            seed.Id,
            seed.OutputPorts[0].Id,
            validator.Id,
            validator.InputPorts[0].Id);

        Assert.That(error, Is.Null);
    }

    [Test]
    public void Validate_SelfConnection_IsRejected()
    {
        var validator = CreateBlock("val", BlockType.Validator);

        var error = ConnectionRules.Validate(
            new[] { validator },
            Array.Empty<RecipeEdge>(),
            validator.Id,
            validator.OutputPorts[0].Id,
            validator.Id,
            validator.InputPorts[0].Id);

        Assert.That(error, Does.Contain("cannot connect to itself"));
    }

    [Test]
    public void Validate_DuplicateConnection_IsRejected()
    {
        var seed = CreateBlock("seed", BlockType.Seed);
        var validator = CreateBlock("val", BlockType.Validator);
        var edge = CreateEdge("e1", seed, seed.OutputPorts[0], validator, validator.InputPorts[0]);

        var error = ConnectionRules.Validate(
            new[] { seed, validator },
            new[] { edge },
            seed.Id,
            seed.OutputPorts[0].Id,
            validator.Id,
            validator.InputPorts[0].Id);

        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void Validate_SecondIncomingConnection_IsRejected()
    {
        var first = CreateBlock("seed1", BlockType.Seed);
        var second = CreateBlock("seed2", BlockType.Seed);
        var validator = CreateBlock("val", BlockType.Validator);
        var edge = CreateEdge("e1", first, first.OutputPorts[0], validator, validator.InputPorts[0]);

        var error = ConnectionRules.Validate(
            new[] { first, second, validator },
            new[] { edge },
            second.Id,
            second.OutputPorts[0].Id,
            validator.Id,
            validator.InputPorts[0].Id);

        Assert.That(error, Does.Contain("already has an incoming connection"));
    }

    [Test]
    public void Validate_IncompatiblePortTypes_AreRejected()
    {
        var seed = CreateBlock("seed", BlockType.Seed);
        seed.OutputPorts.Clear();
        seed.OutputPorts.Add(new BlockPort
        {
            Id = "seed_out",
            Name = "rows",
            DataType = "jsonl",
            Direction = PortDirection.Output
        });
        var validator = CreateBlock("val", BlockType.Validator);

        var error = ConnectionRules.Validate(
            new[] { seed, validator },
            Array.Empty<RecipeEdge>(),
            seed.Id,
            seed.OutputPorts[0].Id,
            validator.Id,
            validator.InputPorts[0].Id);

        Assert.That(error, Does.Contain("Port type mismatch"));
    }

    [Test]
    public void Validate_CycleCreatingConnection_IsRejected()
    {
        var first = CreateBlock("a", BlockType.Validator);
        var second = CreateBlock("b", BlockType.Validator);
        var existing = CreateEdge("e1", first, first.OutputPorts[0], second, second.InputPorts[0]);

        var error = ConnectionRules.Validate(
            new[] { first, second },
            new[] { existing },
            second.Id,
            second.OutputPorts[0].Id,
            first.Id,
            first.InputPorts[0].Id);

        Assert.That(error, Does.Contain("would create a cycle"));
    }

    private static RecipeBlock CreateBlock(string id, BlockType type)
    {
        var block = new RecipeBlock
        {
            Id = id,
            Type = type,
            Name = id,
            DisplayName = id,
            Config = BlockConfig.CreateDefault(type)
        };

        block.InputPorts.AddRange(BlockPortSchema.CreateInputPorts(type));
        block.OutputPorts.AddRange(BlockPortSchema.CreateOutputPorts(type));
        return block;
    }

    private static RecipeEdge CreateEdge(
        string id,
        RecipeBlock source,
        BlockPort sourcePort,
        RecipeBlock target,
        BlockPort targetPort) => new()
    {
        Id = id,
        SourceBlockId = source.Id,
        SourcePortId = sourcePort.Id,
        TargetBlockId = target.Id,
        TargetPortId = targetPort.Id
    };
}