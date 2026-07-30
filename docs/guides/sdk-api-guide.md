# LLRPCSharp SDK API 指南

`LlrpReader` 是一个读写器连接的应用入口。SDK 支持 LLRP 1.0.1 和 1.1；本文描述当前的高层 API 边界，而不是所有底层 LLRP 报文。

## 三个控制层

| 层 | 入口 | 责任与边界 |
|---|---|---|
| 高层意图 | `ReaderSettings`、`QuerySettingsAsync`、`ApplySettingsAsync`、`StartInventoryAsync` | SDK 独占 ROSpec / AccessSpec 资源域；使用保留 ROSpec `14150` 与 AttachedData AccessSpec `14151`。 |
| 专家资源 | `EnterManualResourceModeAsync`、`RoSpecs`、`AccessSpecs` | 专家自行管理标准 LLRP 资源；写操作只允许在手动模式。 |
| 原始协议 | `reader.Protocol.TransactAsync<TResponse>` | 直接发送生成的 LLRP 报文，例如 `GET_READER_CONFIG` / `SET_READER_CONFIG`；成功调用后必须 `SynchronizeStateAsync()`。 |

连接、事件订阅、`TagsReported`、`ReadTagReportsAsync` 和能力读取不会改变资源模式。`GetAllAsync()` 在任意已连接模式都可用于检查资源。

## 高层设置

`ReaderSettings` 是唯一的高层设置读写模型：

```csharp
ReaderSettingsSnapshot snapshot = await reader.QuerySettingsAsync();
ReaderSettings settings = snapshot.Settings;

await reader.ApplySettingsAsync(settings with
{
    Inventory = settings.Inventory with
    {
        Filters =
        [
            new InventorySelectFilter
            {
                MemoryBank = 1,
                BitPointer = 32,
                Mask = Convert.FromHexString("3008"),
                BitLength = 16
            }
        ]
    }
});
```

`QuerySettingsAsync()` 在一个操作锁中读取 `GET_READER_CONFIG`、`GET_ROSPECS` 和 `GET_ACCESSSPECS`。它只解释 SDK 保留的 `14150`，并返回其实际的 `Disabled`、`Enabled` 或 `Running` 状态；手动资源不会被伪装为高层盘点。

`ApplySettingsAsync(settings)` 的行为：

- `Inventory == null`：仅写入 `ReaderConfiguration` 与已识别的高层厂商配置，不接管资源。
- `Inventory != null`：删除全部 AccessSpec、删除全部 ROSpec、写入配置、重建唯一高层 ROSpec 和 AttachedData AccessSpec，保持它们为 Disabled；随后由 `StartAsync()` 或 `StartInventoryAsync()` 启动。

清场或应用失败会停止后续写入、尽力清理，并将资源模式置为 `StateUnknown`。调用 `SynchronizeStateAsync()` 只读取事实并回到 `Idle`，不会恢复旧意图。

`ReaderConfiguration` 仍是 `ReaderSettings.Configuration` 的版本无关子模型。需要逐字段、原始或设备特有的配置控制时，请直接使用 `reader.Protocol`，而不是期待 SDK 把手动状态合并回高层 Settings。

SDK 会将已应用的高层盘点意图保留为 Reader 上的 `14150` ROSpec（及需要时的 `14151` AccessSpec）。`StopAsync()` 只停止并禁用它，因此 `QuerySettingsAsync()` 仍返回真实的 `Inventory`，而无参 `StartAsync()` / `StartInventoryAsync()` 可再次启动。应用仍应保存未应用的 `ReaderSettings` 草稿；要释放高层资源域则调用 `ClearManagedSettingsAsync()`。

## 盘点与报告

```csharp
await using InventorySession inventory = await reader.StartInventoryAsync(new InventorySettings
{
    AntennaIds = [1, 2],
    Session = 2,
    AttachedData = new AttachedDataOptions
    {
        Enabled = true,
        MemoryBank = 2,
        WordCount = 2
    }
});

await foreach (TagReport report in inventory.ReadReportsAsync())
{
    Console.WriteLine(Convert.ToHexString(report.ElectronicProductCode.Span));
}
```

`InventorySession` 只输出属于该盘点 ROSpec 与 AttachedData AccessSpec 的报告。连接级 `TagsReported` 和 `ReadTagReportsAsync()` 仍然是观察全部报告的流。读写器自行结束 ROSpec 时，session 报告流完成并更新状态。

`StartAsync(InventorySettings)` 和 `StopAsync()` 是兼容的持续盘点控制方法；它们使用同一高层资源生命周期，但不提供 session 专属报告流。

盘点运行期间的 Tag Access 会复用 `14150`，只临时创建、启用并清理自己的 AccessSpec；不会删除或重建正在运行的 ROSpec。空闲或手动模式下的 Tag Access 会先接管资源域。

## 手动资源模式

```csharp
await reader.EnterManualResourceModeAsync();
try
{
    await reader.RoSpecs.AddAsync(myRoSpec);
    await reader.RoSpecs.EnableAsync(myRoSpecId);
    await reader.RoSpecs.StartAsync(myRoSpecId);
}
finally
{
    await reader.ExitManualResourceModeAsync(); // DELETE_ACCESSSPEC(0), DELETE_ROSPEC(0)
}
```

手动模式不能使用保留 ID `14150` 或 `14151`。存在已停止或运行中的高层配置时，先调用 `ClearManagedSettingsAsync()`，然后才能进入手动模式。调用带 `InventorySettings` 的高层启动操作会清场并自动接管资源域。

## Impinj 扩展

调用 `.UseImpinj()` 后，生成的 XML 类型仍只用于 wire 编解码；高层模型由扩展包手写：

- `ImpinjReaderConfiguration` 位于 `ReaderSettings.Configuration.Extensions["impinj.configuration"]`，包含 Search Mode、频率、低占空比、GPI 防抖、Link Monitor、Report Buffer、AccessSpec 和 Advanced GPO。
- `ImpinjReaderFacts` 位于 `ReaderSettings.Configuration.Extensions["impinj.facts"]`，是区域、温度等只读事实。
- `ImpinjInventoryReportOptions` 位于 `InventorySettings.Extensions["impinj.inventoryReport"]`。

扩展按照已识别的型号/固件能力目录拒绝未经验证的字段。当前 R420 6.4.1 Profile 已验证 Serialized TID、RF Phase 和 Peak RSSI 报告选择；其他字段不会静默下发。

`ReaderSettingsSerializer` 可接收已启用扩展提供的 `IReaderSettingsSerializationContributor`，把这些强类型值写成版本化 Settings JSON；没有对应 contributor 的扩展字段会明确失败，避免导出后丢失。Live CLI 会自动使用连接读写器的扩展集合。`impinj.facts` 是只读事实，可随快照导出但不会由 `ApplySettingsAsync` 写回设备。

## CLI 与同步

Live CLI 使用：

```text
settings get
settings export settings.json
settings validate settings.json
settings apply settings.json --yes
inventory start
inventory stop
resources manual enter
resources manual exit
resources clear
```

`settings apply` 始终必须显式 `--yes`：仅含 Configuration 的文件不会接管资源；含 Inventory 的文件会执行独占清场和重建。`config` 命令已移除；专家使用 `raw transact` 或 SDK 的 `reader.Protocol`。

Raw 成功调用、网络中断、清场失败都会使资源状态未知。执行 `sync` / `SynchronizeStateAsync()` 后再进行下一次高层操作。
