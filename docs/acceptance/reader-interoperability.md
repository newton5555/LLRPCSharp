# 最终互操作验收标准

本文件定义发布前的最终设备验收门槛。构建和单元测试通过不能替代这些验收；每项都需要保存 SDK 帧日志、设备固件版本和测试结果。

基础连接/扩展/配置验收应优先使用 SDK 直接调用工具，而不是 CLI：

```powershell
dotnet run --project tools/LlrpSdk.LiveSmoke -- <reader-host>
```

| LLRP 版本 | 验收目标 | 设备 | 必须通过的场景 |
|---|---|---|---|
| 1.0.1 | 标准协议与 Impinj 扩展 | Impinj R420、Impinj R700 | `Force101` 连接、标准身份/能力初始化、`UseImpinj()` 自动启用扩展且可解析 Custom Capability、盘点、读取并恢复配置、标准 Tag Access 读/写闭环、断开与重连。 |
| 1.1 | 标准协议与版本协商 | Zebra FX9600 | `Auto` 协商到 1.1，及 `Force11` 连接；身份/能力初始化、盘点、读取并恢复配置、标准 Tag Access 读/写闭环、报文诊断与断开重连。 |
| 2.0 | 版本 Adapter 与回归场景 | LlrpVirtualReader（2.0） | 2.0 版本协商或强制连接、初始化、ROSpec/AccessSpec、TagReport 翻译、Reader 配置流、故障注入和自动化互操作测试。 |

## 通过条件

1. 每台真实设备都能在对应版本策略下完成连接和初始化，日志中不存在 UnknownMessage、Codec 缺失或未处理接收循环故障。
2. 盘点至少持续 60 秒，能够稳定产生并翻译 TagReport；无非预期断线、重复事务错误或资源泄漏。
3. Tag Access 使用测试标签完成一次成功读和一次可恢复的写入；AccessSpec、OpSpec Result 与清理过程均可在日志中追踪。
4. 配置验收先保存设备快照；如执行 `ApplySettingsAsync()`，只允许修改约定的可恢复测试字段，并在结束时恢复快照。
5. Impinj 验收必须确认 `IMPINJ_ENABLE_EXTENSIONS` 成功响应，且后续完整 Capabilities 中的 Impinj Custom Parameter 被强类型解析。
6. Zebra 验收必须保留 `GET_SUPPORTED_VERSION` / `SET_PROTOCOL_VERSION` 相关帧，证明 1.1 不是仅靠 Header 假设。
7. 2.0 Virtual Reader 验收必须进入 CI；真实设备验收可以人工执行，但结果应记录测试日期、型号、固件、区域和操作者。

## 非破坏性约束

- 真实设备测试前确认区域、天线、功率、标签和业务现场均可用于测试。
- 不执行 Kill、不可逆 Lock、永久化配置或无法恢复的 Tag Memory 写入。
- 任何失败都必须尝试 Disable/Delete 临时 ROSpec 与 AccessSpec，并附上帧日志后才能判定为已知失败。

## 已记录证据

| 日期 | 设备 | 结果 |
|---|---|---|
| 2026-07-27 | Impinj R420，LLRP 1.0.1，Firmware 6.4.1.240 | 直接使用 `LlrpReader + UseImpinj()` 完成连接、扩展激活、配置查询和短时盘点；读取 EPC `E28011710000020D056E9BEE` 的 User Memory word 0 成功，返回 `0000`。`GetDefaultConfiguration()` 在配置查询前成功返回无网络副作用的安全基线（Keepalive=None、0 条天线/GPO 覆盖）。Impinj 设置查询返回 China 920–925 MHz、35°C、4 路 GPI 防抖、Normal Report Buffer 与 FIFO AccessSpec 设置。未写标签或设备配置。 |
| 2026-07-27 | Impinj R420，LLRP 1.0.1，Firmware 6.4.1.240 | 同时启用 `ReaderSettings.Extensions["impinj.inventoryReport"]` 的 `IncludeSerializedTid`、`IncludeRfPhaseAngle`、`IncludePeakRssi` 后，SDK 成功添加并启动 ROSpec，收到 EPC `E28011710000020D056E9BEE`，且 `TagReport.Extensions` 返回 `impinj.serializedTid = E2801171200003EEADD309A0`、`impinj.rfPhaseAngle = 1276`、`impinj.peakRssi = -6700`。停止后 `GET_ROSPECS` 返回空集合；未写标签或设备配置。 |

## 已知设备能力差异

- R420 Firmware 6.4.1 的 ItemTest 抓包已接受 `ImpinjTagReportContentSelector`；此前 SDK 的 `M_UnsupportedParameter` 来自将它误放到 `AISpec`，现已修正为 `ROReportSpec` 子项并通过直接 SDK 的 Serialized TID、RF Phase Angle 与 Peak RSSI 端到端验收。该能力仍仅对已验证的 R420 6.4.1.x Profile 启用，不作为其他 R420/R700 固件的默认盘存选项。
