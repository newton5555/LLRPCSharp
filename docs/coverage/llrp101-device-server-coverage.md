# LLRP 1.0.1 设备端对齐完成度

> 基准日期：2026-08-17
>
> 本表以现有 `LlrpSdk` 客户端 1.0.1 的实际业务路径为对齐基线，覆盖通用
> `LlrpDevice.Server` 与 `VirtualLlrpDevice`。客户端、`LlrpNet`、协议定义和生成代码
> 在本专项中冻结。

## 结论

当前已完成“客户端可用范围”的 1.0.1 虚拟设备闭环，并由 SDK 端到端测试验收：连接、
能力/配置、ROSpec、AccessSpec、寻卡触发、报告、事件、标准 C1G2 Tag Access、主动关闭
和故障边界均由通用 Server 承担；Virtual 只提供确定性标签、内存和 RF 可观察行为。

这不是对真实 RFID 芯片、电磁波形、区域法规或厂商扩展的仿真。`CLIENT_REQUEST_OP` 与
RF Survey 仍按客户端能力门控保持不接线，因为当前客户端实现和已验证设备均不使用它们。

## 客户端功能与设备端实现

| 客户端 1.0.1 功能 | Server / Virtual 对齐实现 | 验收状态 |
|---|---|---|
| TCP 连接、初始化 Reader Event | `LlrpDeviceServer` accepted TCP、Session、ConnectionAttemptEvent、客户端隔离 | ✅ |
| GET_READER_CAPABILITIES | General、LLRP、Regulatory（严格 profile）、C1G2 能力；按 RequestedData 筛选 | ✅ |
| GET/SET_READER_CONFIG | 天线、GPO、GPI 当前状态、事件通知、RO/Access ReportSpec、Keepalive、EventsAndReports、配置状态值、Factory Reset | ✅ |
| ROSpec CRUD 与状态机 | Add/Get/Delete/Enable/Disable/Start/Stop；ID=0 批量语义；Disabled/Inactive/Active | ✅ |
| ROSpec Start Trigger | Null/Immediate、Periodic（Offset/Period/UTC）、GPI 电平触发 | ✅ |
| ROSpec Stop Trigger | Null、Duration、GPI With Timeout；结束事件和尾报告 | ✅ |
| AISpec / InventoryParameterSpec | 天线集合、每天线 RFReceiver/RFTransmitter、InventoryParameterSpecID、C1G2 InventoryCommand、RFControl、Singulation 进入 `LlrpInventoryPlan` | ✅ |
| C1G2 Select | EPC/TID/User/Reserved memory bit mask、Select/Unselect/DoNothing、顺序规则 | ✅ |
| State-aware Singulation | Session A/B、SL Set/Clear、状态感知 Filter action，状态跨轮次保留 | ✅ |
| RF 可观察行为 | `static` / `moving-tags` / `noisy`、检测概率、RSSI 抖动、presence window、seed、轮次上限 | ✅ |
| ROReportSpec | None、Upon N Tags or End of AISpec、Upon N Tags or End of ROSpec；N=0 结束时汇总 | ✅ |
| TagReportContentSelector | ROSpecID、SpecIndex、InventoryParameterSpecID、天线、信道、RSSI、First/Last Seen、SeenCount、AccessSpecID、PC/CRC | ✅ |
| GET_REPORT 与报告缓冲 | 无自动投递时入缓冲，GET_REPORT 以异步 RO_ACCESS_REPORT 返回；容量、warning、overflow | ✅ |
| Hold/Release | 重连 HoldEventsAndReportsUponReconnect；ENABLE_EVENTS_AND_REPORTS 释放事件和报告 | ✅ |
| READER_EVENT_NOTIFICATION | GPI、ROSpec、Antenna、Report Buffer、Reader Exception；按通知开关发送 | ✅ |
| AccessSpec CRUD 与标准 Tag Access | Add/Get/Delete/Enable/Disable；Read、Write、BlockWrite、Lock、Kill、BlockErase；Access report 结果映射 | ✅ |
| KEEPALIVE / KEEPALIVE_ACK | Reader KeepaliveSpec 动态配置与 ACK；保留 Server fallback interval | ✅ |
| CLOSE_CONNECTION | 客户端请求响应、Virtual 设备主动 CLOSE_CONNECTION、连接生命周期标记 | ✅ |
| ERROR_MESSAGE / CUSTOM_MESSAGE | 参数错误、未支持消息、未知 Vendor 参数策略；自定义模块注册边界 | ✅ |
| CLIENT_REQUEST_OP / RF Survey | 能力门控和标准 Unsupported 路径；与现有 SDK 设计一致，不伪造设备能力 | ⬇ |

## 设备端分层

```text
LlrpDevice.Server
  ├─ LLRP 1.0.1 canonical protocol/resource runtime
  ├─ report/event/keepalive/connection pipeline
  └─ ILlrpDevice
       └─ VirtualLlrpDevice
            ├─ deterministic tags and memory banks
            ├─ C1G2 lock/kill/access state
            └─ static/moving/noisy RF observation
```

未来真实下位机只需实现 `ILlrpDevice` 的设备行为合同，复用 Server 的 LLRP 服务和资源
状态机；真实 RFID 模块替换的是寻卡、Tag Access、配置和事件实现，不复制 LLRP 报文层。

## 进程边界

本专项只实现单台设备 SDK/Host 和单台 CLI 生命周期。配置文件保存设备声明式预设，
不保存运行中的 ROSpec/AccessSpec，也不在进程重启后自动恢复；多设备服务、跨进程控制、
UI 和持久化运行态属于后续独立阶段。

## 验收

- 解决方案构建：0 warning / 0 error。
- 全量自动化测试：529 passed、0 failed、0 skipped。
- `Interop.Tests`：32 passed，包含 1.0.1 报告缓冲、报告触发尾部、状态感知过滤、
  附加数据、GPI 事件、主动关闭、标准 Tag Access，以及 1.0.1/1.1/2.0 Server 基线。
- 冻结边界：`src/LlrpNet`、`src/LlrpSdk`、`definitions` 和生成 `.g.cs` 无修改。
