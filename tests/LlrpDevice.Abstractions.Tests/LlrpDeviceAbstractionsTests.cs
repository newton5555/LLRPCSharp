using System.Reflection;
using LlrpDevice.Abstractions;

namespace LlrpDevice.Abstractions.Tests;

public sealed class LlrpDeviceAbstractionsTests
{
    [Fact]
    public void Abstractions_assembly_has_no_protocol_or_client_product_reference()
    {
        string[] references = typeof(ILlrpDevice).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, static name =>
            name.StartsWith("LlrpNet", StringComparison.Ordinal) ||
            name.StartsWith("LlrpSdk", StringComparison.Ordinal));
    }

    [Fact]
    public void Inventory_round_and_access_models_are_version_neutral()
    {
        var round = new LlrpInventoryRound(7, 3, [1, 2]);
        var request = new LlrpTagAccessRequest
        {
            AccessSpecId = 11,
            RoSpecId = round.RoSpecId,
            Selector = new LlrpTagSelector
            {
                MemoryBank = LlrpTagMemoryBank.ElectronicProductCode,
                BitPointer = 32,
                BitLength = 16,
                Mask = new byte[] { 0xFF, 0xFF },
                Data = new byte[] { 0xE2, 0x80 },
            },
            Operations =
            [
                new LlrpTagAccessOperation
                {
                    OperationId = 1,
                    Kind = LlrpTagAccessOperationKind.Read,
                    MemoryBank = LlrpTagMemoryBank.User,
                    WordCount = 2,
                },
            ],
        };

        Assert.Equal((uint)7, request.RoSpecId);
        Assert.Equal([1, 2], round.AntennaIds);
        Assert.Equal(LlrpTagAccessOperationKind.Read, Assert.Single(request.Operations).Kind);
    }
}
