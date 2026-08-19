# ADR 0002：读写器扩展的主动初始化与双阶段能力获取

- 状态：Accepted（已实施）
- 日期：2026-07-25

## 背景

LLRP SDK 在与读写器连接（`ConnectAsync`）并初始化时，需要获取读写器的能力参数，并将其保存在 `reader.Capabilities` 中供上层应用及 SDK 内部（例如天线校验、UTC 时钟支持等）查询。

对于 Impinj 等读写器，存在以下厂商特有的协议行为约束：
1. **默认屏蔽客制化参数**：在刚刚建立连接时，读写器只工作在标准 LLRP 模式下。如果直接查询全量能力（`RequestedData = All`），读写器的响应中不会包含任何 Impinj 自定义能力参数（如 `ImpinjDetailedVersion` 等）。
2. **需要显式开启扩展**：客户端必须向读写器发送自定义消息 `IMPINJ_ENABLE_EXTENSIONS` 启用扩展。之后再次查询能力，读写器才会在响应的 `CustomItems` 中携带专属自定义参数。

在本决策形成时，SDK 的扩展机制 `IReaderExtension` 是纯静态描述接口（仅支持匹配 `Matches`），不具备在连接建立过程中发送网络报文的“主动执行”能力。该缺口现已通过 `InitializeConnectionAsync` 实施解决；以下背景和决定保留为历史依据：
* **重复的重量级查询**：应用层不得不先经由 SDK 触发一次 `All` 全量能力查询，在手动发送启用扩展命令后，再发起第二次 `All` 全量能力查询，导致网络开销翻倍。
* **业务逻辑泄漏**：应用层代码被迫去处理启用指令、等待响应、手动刷新能力快照等底层协议细节，破坏了 SDK 的黑盒封装性。

## 决定

### 1. 一阶段：轻量化身份识别查询（Lightweight Identity Fetch）

在连接初始化的第一阶段，SDK 不再直接发起重量级的 `GET_READER_CAPABILITIES (All)` 查询，而是发送仅请求通用设备信息的轻量级报文：
* 发送 `GET_READER_CAPABILITIES` 时，将 `RequestedData` 参数指定为 `General_Device_Capabilities` (值为 `1`)。
* 该请求返回的数据包非常小，仅包含厂商 ID（Manufacturer ID）、型号（Model ID）和固件版本。

### 2. 引入扩展主动初始化生命周期钩子（Active Initialization Hooks）

修改 [IReaderExtension](file:///c:/Users/yankai/source/repos/LLRPCSharp/src/LlrpSdk.Extensions.Abstractions/IReaderExtension.cs) 接口定义，为其增加一个异步初始化方法：

```csharp
public interface IReaderExtension
{
    public string Id { get; }
    public string? MutualExclusionGroup { get; }
    public bool Matches(ReaderExtensionMatchContext context);

    /// <summary>
    /// 当扩展匹配成功并被激活时，在获取全量能力之前调用。
    /// 允许厂商扩展在此阶段向通道中发送初始化/启用命令。
    /// </summary>
    public Task InitializeConnectionAsync(LlrpReader reader, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

在 `ImpinjReaderExtension` 中实现该接口：
```csharp
public async Task InitializeConnectionAsync(LlrpReader reader, CancellationToken cancellationToken)
{
    var enableMsg = new IMPINJ_ENABLE_EXTENSIONS(reader.Protocol.NextMessageId(), []);
    await reader.TransactAsync<IMPINJ_ENABLE_EXTENSIONS_RESPONSE>(enableMsg, timeout: null, cancellationToken)
        .ConfigureAwait(false);
}
```

### 3. 二阶段：全量能力查询与刷新（Full Capabilities Fetch）

在连接初始化流程中，根据步骤 1 识别出的厂商信息激活 Extensions 后，依次执行每个已激活 Extension 的 `InitializeConnectionAsync` 方法。

待所有扩展主动初始化完成后，SDK 发送第二次、也是唯一一次重量级的全量能力查询：
* 发送 `GET_READER_CAPABILITIES`，其中 `RequestedData` 参数指定为 `All` (值为 `0`)。
* 此时读写器返回的响应数据中已经完美解禁并包含了 Impinj 客制化参数（如 `ImpinjDetailedVersion` 与 `ImpinjFrequencyCapabilities`）。
* 将该响应解码并保存至 `reader.Capabilities`。

### 4. 兼容纯标准 LLRP 模式

此自动化行为是完全基于配置选入（Opt-in）的：
* 如果用户在构建 Reader 时没有显式调用 `builder.UseImpinj()`，那么在扩展匹配阶段，SDK 将无法检索并激活 `ImpinjReaderExtension`。
* 相应的 `InitializeConnectionAsync` 钩子不会被调用。
* SDK 会跳过步骤 2 的启用过程，直接进行全量获取，从而使设备完美工作在纯标准 LLRP 模式下。

## 后果

### 正面后果

* **避免重复开销**：将两次重量级的 `All` 能力包查询合并为了一次“轻量级身份查询 + 一次重量级全量查询”，大幅减少了网络包体积和读写器处理时间。
* **业务逻辑无感**：上层业务人员只需调用 `await reader.ConnectAsync()`，即可在 `reader.Capabilities` 中自动拿到包含 Impinj 客制化属性的完整能力模型。
* **优雅的架构分离**：通用的底层连接和握手状态机对厂商私有报文保持“无知”，所有的 Impinj 专有逻辑全部内聚在 `LlrpSdk.Extensions.Impinj` 扩展模块中，保证了代码的可扩展性与可维护性。

### 代价与约束

* **握手状态机复杂度增加**：SDK 内部的连接阶段需要从单次查询转变为“轻量获取 -> 扩展激活与执行 -> 全量获取”的双阶段握手状态机。
* **接口变更**：`IReaderExtension` 增加了一个生命周期方法。由于 C# 8.0 默认接口实现（Default Interface Method）的支持，非 Impinj 的第三方扩展如果没有该需求，可以直接继承默认的空实现，将兼容性破坏降到最低。
