# LLRPCSharp SDK API 指南

`LlrpReader` 是一个读写器连接的应用入口。SDK 支持 LLRP 1.0.1 和 1.1；本文描述托管 Reader API 的边界，而不是所有底层 LLRP 报文。

## 三个控制层

| 层 | 入口 | 责任与边界 |
|---|---|---|
| 托管 Reader API | `ReaderSettings`、`QuerySettingsAsync`、`ApplySettingsAsync`、`StartInventoryAsync` | 接受声明式的设置与盘点意图；SDK 独占 ROSpec / AccessSpec 资源域，并维护保留 ROSpec `14150` 与 AttachedData AccessSpec `14151`。 |
| 专家资源 | `EnterManualResourceModeAsync`、`RoSpecs`、`AccessSpecs` | 专家自行管理标准 LLRP 资源；写操作只允许在手动模式。 |
| 原始协议 | `reader.Protocol.TransactAsync<TResponse>` | 直接发送生成的 LLRP 报文，例如 `GET_READER_CONFIG` / `SET_READER_CONFIG`；成功调用后必须 `SynchronizeStateAsync()`。 |

连接、事件订阅、`TagsReported`、`ReadTagReportsAsync` 和能力读取不会改变资源模式。`GetAllAsync()` 在任意已连接模式都可用于检查资源。

托管 Reader API 负责默认值、资源所有权、清理与报告投影；专家资源 API 负责显式 ROSpec / AccessSpec 生命周期；原始协议 API 则直接操作 LLRP Message。后文不再将前者称为“高级 API”，以免与专家接口混淆。

## 托管设置

托管 Settings 有两种明确的初始化来源，不应混用：

```csharp
// 设备实况：读取设备当前配置以及 SDK 保留 ROSpec/AO。
ReaderSettings current = (await reader.QuerySettingsAsync()).Settings;

// SDK 推荐基线：根据已连接设备的身份、型号、固件和 Capabilities 生成；不读取或修改资源。
ReaderSettingsDefaults defaults = await reader.GetDefaultSettingsAsync();
ReaderSettings recommended = defaults.Settings;

// 离线、厂商无关的可移植基线。
ReaderSettings portable = ReaderSettingsDefaults.CreateGeneric().Settings;
```

`ReaderSettingsDefaults` 包含 `ProfileId`、`Source`（`Generic` 或 `ReaderProfile`）和决策说明。它的 `Settings` 可直接编辑并交给 `ApplySettingsAsync()`；`QuerySettingsAsync()` 返回的则是设备事实，适合先导出再做最小变更。`ReaderSettingsSerializer.SerializeDefaultsToJson()` / `DeserializeDefaultsFromJson()` 可保存或恢复带 Profile 来源的默认文档。

`ReaderSettings` 是唯一的托管设置读写模型：

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

普通配置也可以使用轻量 Builder；它直接生成同一个 `ReaderSettings` / `InventorySettings` record，不引入第二套配置模型：

```csharp
ReaderSettings settings = ReaderSettings.Create(reader => reader
    .Inventory(inventory => inventory
        .Antennas(1, 2)
        .Session(2)
        .Population(64)
        .ReportEveryTag()
        .ReadTid(words: 6)));

SettingsValidationResult validation = await reader.ValidateSettingsAsync(settings);
validation.ThrowIfInvalid();

await reader.ApplySettingsAsync(settings); // 部署为 Disabled
await reader.StartAsync();
```

已有配置可用 `settings.Edit(...)` 或 `settings.Inventory.Edit(...)` 做不可变编辑；未修改字段、Filters、Trigger、RF 参数和扩展值会原样保留。高级用户仍可直接构造 record，或在 Builder 结果上使用 `with` 覆盖底层字段。

`ValidateSettingsAsync()` 不发送报文，也不改变 Reader 资源。返回的 `SettingsValidationResult.Diagnostics` 包含稳定错误码、严重级别、字段路径和消息，覆盖标准字段组合、已协商协议版本、Reader Capabilities 与激活的厂商 Contributor。`ApplySettingsAsync(settings)`、`StartAsync(settings)` 和 `StartInventoryAsync(settings)` 会执行同一校验；失败时抛出带完整诊断集合的 `SettingsValidationException`，并且不会先清理资源。

`QuerySettingsAsync()` 在一个操作锁中读取 `GET_READER_CONFIG`、`GET_ROSPECS` 和 `GET_ACCESSSPECS`。它只解释 SDK 保留的 `14150`，并按协商的 1.0.1 或 1.1 参数类型还原盘点意图（包括 Filter、AttachedData、Trigger、Report 与状态感知 Singulation）；手动资源不会被伪装为托管盘点。返回的状态为实际的 `Disabled`、`Enabled` 或 `Running`。

`ApplySettingsAsync(settings)` 的行为：

- `Inventory == null`：仅写入 `ReaderConfiguration` 与已识别的托管厂商配置，不接管资源。
- `Inventory != null`：删除全部 AccessSpec、删除全部 ROSpec、写入配置、重建唯一托管 ROSpec 和 AttachedData AccessSpec，保持它们为 Disabled；随后由 `StartAsync()` 或 `StartInventoryAsync()` 启动。

清场或应用失败会停止后续写入、尽力清理，并将资源模式置为 `StateUnknown`。调用 `SynchronizeStateAsync()` 只读取事实并回到 `Idle`，不会恢复旧意图。

`ReaderConfiguration` 仍是 `ReaderSettings.Configuration` 的版本无关子模型。需要逐字段、原始或设备特有的配置控制时，请直接使用 `reader.Protocol`，而不是期待 SDK 把手动状态合并回托管 Settings。

SDK 会将已应用的声明式盘点意图保留为 Reader 上的 `14150` ROSpec（及需要时的 `14151` AccessSpec）。`StopAsync()` 只停止并禁用它，因此 `QuerySettingsAsync()` 仍返回真实的 `Inventory`，而无参 `StartAsync()` / `StartInventoryAsync()` 可再次启动。应用仍应保存未应用的 `ReaderSettings` 草稿；要释放托管资源域则调用 `ClearManagedSettingsAsync()`。

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

状态感知过滤必须同时设置 `InventorySelectFilter.StateAwareAction` 和 `InventorySettings.StateAwareSingulation`。SDK 会要求 Reader 声明 `Capabilities.CanDoTagInventoryStateAwareSingulation`；缺少配套 Singulation 设置或设备不支持时，编译会明确失败，不会降级成普通 Filter。

`InventoryStateAwareSingulation.SelectedFlag` 在两个协议版本中的映射不同：`Set` 与 `Clear` 分别映射为 `S=SL` 和 `S=~SL`；`All` 使用 LLRP 1.1 新增的 `S_All=1`，因此对 LLRP 1.0.1 Reader 会明确拒绝，而不会伪装为任一 SL 状态。

报告使用 `InventorySettings.Report.Trigger` 与 `ReportEveryNTags` 直接表达标准 `ROReportSpec`：`UponNTagsOrEndOfRoSpec` 配合 `ReportEveryNTags = 0` 表示缓存至 ROSpec 结束后再报告（旧 UI 的 `BatchAfterStop`）。其他触发类型要求 `ReportEveryNTags >= 1`；`None` 配合 `GetTagReportsAsync()` 表示由应用主动拉取 Reader 缓存。

`StartAsync(InventorySettings)` 和 `StopAsync()` 是兼容的持续盘点控制方法；它们使用同一托管资源生命周期，但不提供 session 专属报告流。

周期盘点可同时设置 `StartTrigger.OffsetMilliseconds`、`PeriodMilliseconds` 和可选的 `StartAtUtc`。后者会编译为标准 LLRP `PeriodicTriggerValue.UTCTimestamp`；仅应在 `reader.Capabilities.HasUtcClockCapability` 为 `true` 的设备上使用。

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

手动模式不能使用保留 ID `14150` 或 `14151`。存在已停止或运行中的托管配置时，先调用 `ClearManagedSettingsAsync()`，然后才能进入手动模式。调用带 `InventorySettings` 的托管启动操作会清场并自动接管资源域。

## Impinj 扩展

调用 `.UseImpinj()` 后，生成的 XML 类型仍只用于 wire 编解码；托管模型由扩展包手写：

- `ImpinjReaderConfiguration` 位于 `ReaderSettings.Configuration.Extensions["impinj.configuration"]`，包含 Search Mode、频率、低占空比、GPI 防抖、Link Monitor、Report Buffer、AccessSpec 和 Advanced GPO。
- `ImpinjReaderFacts` 位于 `ReaderSettings.Configuration.Extensions["impinj.facts"]`，是区域、温度等只读事实。
- `ImpinjInventoryReportOptions` 位于 `InventorySettings.Extensions["impinj.inventoryReport"]`。

常用 Inventory 扩展无需接触字典 Key：

```csharp
InventorySettings inventory = InventorySettings.Create(settings => settings
    .Antennas(1, 2)
    .Session(2)
    .Impinj(impinj => impinj
        .IncludeSerializedTid()
        .IncludeRfPhaseAngle()
        .IncludePeakRssi()
        .EnableTagPopulationEstimation()));
```

`.Impinj(...)` 仍然生成上述强类型扩展对象，因此现有 Contributor、JSON schema 和高级 record 配置方式保持兼容。

扩展按照已识别的型号/固件能力目录拒绝未经验证的字段。当前 R420 6.4.1 Profile 已验证 Serialized TID、RF Phase 和 Peak RSSI 报告选择；其他字段不会静默下发。

`ReaderSettingsSerializer` 可接收已启用扩展提供的 `IReaderSettingsSerializationContributor`，把这些强类型值写成版本化 Settings JSON；没有对应 contributor 的扩展字段会明确失败，避免导出后丢失。Live CLI 会自动使用连接读写器的扩展集合。`impinj.facts` 是只读事实，可随快照导出但不会由 `ApplySettingsAsync` 写回设备。

## Seuic 默认 Profile

`.UseSeuic()` 识别 UF40（Manufacturer `57690`、Model `40`、LLRP 1.0.1）后，`GetDefaultSettingsAsync()` 返回 `seuic.uf40.llrp-1.0.1`。它根据能力表选择全部实际天线、最高 Tx Power Index，以及 Rx Sensitivity Index `1`（不可用时选择最低可用值），并将 HopTable / Channel 设为 `1`。

这些是标准 AISpec 参数，直接以 `InventorySettings.AntennaConfigurations` 表达：每项包含 Antenna ID、Rx Sensitivity、Tx Power、Hop Table 与 Channel。Seuic 扩展只负责依据能力表计算推荐值并填入核心模型；编译器不再读取 Seuic 的隐藏 inventory extension。真正无法由标准 LLRP 表达的厂商参数才放在 `Extensions`。

## CLI 与同步

Live CLI 使用：

```text
settings show reader
settings save settings.json --source reader
settings edit --from defaults
settings validate
settings apply --yes
inventory start
inventory stop
resources manual enter
resources manual exit
resources clear
```

`settings apply` 始终必须显式 `--yes`：仅含 Configuration 的文件不会接管资源；含 Inventory 的文件会执行独占清场和重建。`config` 命令已移除；专家使用 `raw transact` 或 SDK 的 `reader.Protocol`。

Agent 或脚本的一次性寻卡使用根命令 `inventory <host> [--settings file] --duration <seconds> --yes`。它与 Live CLI 共用 Settings 加载、校验和 Apply 工作流，默认输出 JSON；结束时停止并清除托管 Inventory 资源，但保留已应用的 Reader 全局 Configuration。

Raw 成功调用、网络中断、清场失败都会使资源状态未知。执行 `sync` / `SynchronizeStateAsync()` 后再进行下一次托管操作。
