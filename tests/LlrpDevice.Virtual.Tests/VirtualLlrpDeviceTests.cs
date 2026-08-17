using LlrpDevice.Abstractions;
using LlrpDevice.Virtual;

namespace LlrpDevice.Virtual.Tests;

public sealed class VirtualLlrpDeviceTests
{
    [Fact]
    public async Task Inventory_is_deterministic_for_same_seed_and_round()
    {
        await using var first = new VirtualLlrpDevice(new VirtualDeviceOptions
        {
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = VirtualRfScenario.Noisy,
                RandomSeed = 42,
                DetectionProbability = 0.75,
                RssiJitterDb = 4,
            },
        });
        await using var second = new VirtualLlrpDevice(new VirtualDeviceOptions
        {
            RfSimulation = new VirtualRfSimulationOptions
            {
                Scenario = VirtualRfScenario.Noisy,
                RandomSeed = 42,
                DetectionProbability = 0.75,
                RssiJitterDb = 4,
            },
        });

        InventoryObservationBatch firstBatch = await ObserveAsync(first, 7);
        InventoryObservationBatch secondBatch = await ObserveAsync(second, 7);

        Assert.Equal(
            firstBatch.Tags.Select(static tag => (Convert.ToHexString(tag.ElectronicProductCode.Span), tag.PeakRssi)),
            secondBatch.Tags.Select(static tag => (Convert.ToHexString(tag.ElectronicProductCode.Span), tag.PeakRssi)));
    }

    [Fact]
    public async Task Explicit_inventory_data_source_is_independent_from_device_options()
    {
        byte[] sourceEpc = Convert.FromHexString("300833B2DDD9014000000001");
        var source = new InMemoryVirtualInventoryDataSource(
            "test-source",
            [new VirtualTagDefinition { ElectronicProductCode = sourceEpc }]);

        await using var device = new VirtualLlrpDevice(new VirtualDeviceOptions(), source);

        InventoryObservationBatch batch = await ObserveAsync(device, 0);

        Assert.Equal("test-source", source.Id);
        Assert.Equal("300833B2DDD9014000000001", Convert.ToHexString(Assert.Single(batch.Tags).ElectronicProductCode.Span));
    }

    [Fact]
    public void Standard_capability_profile_contains_the_four_antenna_101_shape()
    {
        VirtualDeviceCapabilityProfile profile =
            VirtualDeviceCapabilityProfiles.Get(VirtualDeviceCapabilityProfiles.Standard101Id);

        Assert.Equal("1.0.1", profile.ProtocolVersion);
        Assert.Equal(0U, profile.Identity.ManufacturerId);
        Assert.Equal(0U, profile.Identity.ModelId);
        Assert.Equal("virtual-1.0.1", profile.Identity.FirmwareVersion);
        Assert.Equal((ushort)4, profile.Capabilities.MaxNumberOfAntennas);
        LlrpDeviceRegulatoryCapabilities regulatory = Assert.IsType<LlrpDeviceRegulatoryCapabilities>(
            profile.Capabilities.RegulatoryCapabilities);
        Assert.Equal(193, regulatory.TransmitPowerLevels.Count);
        Assert.Equal(41, regulatory.C1G2RfModes.Count);
    }

    [Fact]
    public async Task Tag_access_supports_read_write_lock_block_erase_and_kill()
    {
        byte[] epc = Convert.FromHexString("E28011710000020D056E9BEE");
        await using var device = new VirtualLlrpDevice(new VirtualDeviceOptions
        {
            Tags =
            [
                new VirtualTagDefinition
                {
                    ElectronicProductCode = epc,
                    UserMemory = [1, 2, 3, 4],
                    KillPassword = 0x12345678,
                },
            ],
        });

        LlrpTagAccessResult read = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 1,
                Kind = LlrpTagAccessOperationKind.Read,
                MemoryBank = LlrpTagMemoryBank.User,
                WordCount = 4,
            })));
        Assert.Equal([1, 2, 3, 4], Assert.Single(read.Operations).ReadData);

        LlrpTagAccessResult write = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 2,
                Kind = LlrpTagAccessOperationKind.Write,
                MemoryBank = LlrpTagMemoryBank.User,
                WriteData = [9, 8],
                WordCount = 2,
            })));
        Assert.Equal(LlrpTagAccessResultCode.Success, Assert.Single(write.Operations).Result);

        LlrpTagAccessResult erase = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 3,
                Kind = LlrpTagAccessOperationKind.BlockErase,
                MemoryBank = LlrpTagMemoryBank.User,
                WordCount = 1,
            })));
        Assert.Equal(LlrpTagAccessResultCode.Success, Assert.Single(erase.Operations).Result);

        LlrpTagAccessResult lockResult = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 4,
                Kind = LlrpTagAccessOperationKind.Lock,
                LockRequests = [new LlrpTagLockRequest(LlrpTagLockPrivilege.PermaLock, LlrpTagMemoryBank.User)],
            })));
        Assert.Equal(LlrpTagAccessResultCode.Success, Assert.Single(lockResult.Operations).Result);

        LlrpTagAccessResult lockedWrite = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 5,
                Kind = LlrpTagAccessOperationKind.Write,
                MemoryBank = LlrpTagMemoryBank.User,
                WriteData = [7],
                WordCount = 1,
            })));
        Assert.Equal(LlrpTagAccessResultCode.Locked, Assert.Single(lockedWrite.Operations).Result);

        LlrpTagAccessResult kill = Assert.Single(await device.ExecuteTagAccessAsync(Request(
            epc,
            new LlrpTagAccessOperation
            {
                OperationId = 6,
                Kind = LlrpTagAccessOperationKind.Kill,
                KillPassword = 0x12345678,
            })));
        Assert.Equal(LlrpTagAccessResultCode.Success, Assert.Single(kill.Operations).Result);
        Assert.Empty((await ObserveAsync(device, 0)).Tags);
    }

    [Fact]
    public async Task Tag_state_isolated_between_virtual_devices()
    {
        byte[] epc = Convert.FromHexString("E28011710000020D056E9BEE");
        await using var first = new VirtualLlrpDevice();
        await using var second = new VirtualLlrpDevice();

        await first.ExecuteTagAccessAsync(Request(epc, new LlrpTagAccessOperation
        {
            OperationId = 1,
            Kind = LlrpTagAccessOperationKind.Write,
            MemoryBank = LlrpTagMemoryBank.User,
            WriteData = [0xCAFE],
            WordCount = 1,
        }));

        LlrpTagAccessResult secondRead = Assert.Single(await second.ExecuteTagAccessAsync(Request(epc, new LlrpTagAccessOperation
        {
            OperationId = 2,
            Kind = LlrpTagAccessOperationKind.Read,
            MemoryBank = LlrpTagMemoryBank.User,
            WordCount = 4,
        })));
        Assert.Equal([0, 0, 0, 0], Assert.Single(secondRead.Operations).ReadData);
    }

    [Fact]
    public async Task Inventory_observation_preserves_tag_seen_timestamps_and_count()
    {
        await using var device = new VirtualLlrpDevice();
        DateTimeOffset firstRound = DateTimeOffset.UtcNow;
        await using IInventoryExecution firstExecution = await device.StartInventoryAsync(
            new LlrpInventoryPlan { RoSpecId = 1 });
        TagObservation first = Assert.Single((await firstExecution.ObserveAsync(
            new LlrpInventoryRound(1, 0, []) { StartedAtUtc = firstRound })).Tags);

        DateTimeOffset secondRound = firstRound.AddSeconds(1);
        await using IInventoryExecution secondExecution = await device.StartInventoryAsync(
            new LlrpInventoryPlan { RoSpecId = 1 });
        TagObservation second = Assert.Single((await secondExecution.ObserveAsync(
            new LlrpInventoryRound(1, 1, []) { StartedAtUtc = secondRound })).Tags);

        Assert.Equal(firstRound, first.FirstSeenUtc);
        Assert.Equal(firstRound, first.LastSeenUtc);
        Assert.Equal((uint)1, first.SeenCount);
        Assert.Equal(first.FirstSeenUtc, second.FirstSeenUtc);
        Assert.Equal(secondRound, second.LastSeenUtc);
        Assert.Equal((uint)2, second.SeenCount);
    }

    private static async Task<InventoryObservationBatch> ObserveAsync(VirtualLlrpDevice device, int sequence)
    {
        await using IInventoryExecution execution = await device.StartInventoryAsync(new LlrpInventoryPlan { RoSpecId = 1 });
        return await execution.ObserveAsync(new LlrpInventoryRound(1, sequence, []));
    }

    private static LlrpTagAccessRequest Request(byte[] epc, LlrpTagAccessOperation operation) => new()
    {
        AccessSpecId = 1,
        RoSpecId = 1,
        Selector = new LlrpTagSelector
        {
            MemoryBank = LlrpTagMemoryBank.ElectronicProductCode,
            BitPointer = 32,
            BitLength = 96,
            Mask = epc,
            Data = epc,
        },
        Operations = [operation],
    };
}
