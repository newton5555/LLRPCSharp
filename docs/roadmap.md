# 路线图

> 基准日期：2026-08-03

本文档只保留未完成事项；当前实现事实见 [status.md](status.md)。

## 近期

1. 完成 LLRP 1.0.1 目标设备的最终实机验收，并补充失败场景记录。
2. 补充 LLRP 1.1 目标 Reader 的实机互操作验收，覆盖版本协商、标准 Settings、
   Inventory、TagReport 和 Tag Access，并记录具体型号/固件证据。
3. 体系化设计与扩充 `LlrpSdk.Hardware.Tests` 真机测试用例集（包含多天线配置、真实标签 Memory Bank 读写、高并发稳定性及厂商扩展字段校验）。
4. 根据真实设备证据扩充 Impinj 型号/固件能力目录。
5. 完善独立的 Reader Studio 项目，但不把 WPF 依赖带回 SDK 仓库。
6. CLI 离线协议工具（`inspect` / `decode` / `validate` / `encode`）补齐 LLRP 1.1 支持：
   在 `src/LlrpCli/Commands/Helpers.cs` 的 `CreateRegistry()` 注册
   `Llrp11StandardModule`，并让 `encode` 支持 `--version 1.0.1|1.1` 参数选择
   构造版本；`inspect` 继续作为版本无关的 Header 检查工具。实时命令（走
   `LlrpReader`）已具备 1.0.1/1.1 自动协商基线，但仍需真实设备验收。
7. `LlrpFrameAnalyzer`（死代码，未接线）的 `FrameAnalysisResult.Status` 目前
   绑定 `V1_0_1.LLRPStatus`，1.1 消息无法匹配；接线前改为版本无关的反射取值。
8. LLRP 1.0.1 收尾:`ENABLE_EVENTS_AND_REPORTS` 重连放行已实现并真机验证
   （R430）;`CLIENT_REQUEST_OP` 经实机实测（R430 固件 6.4.1.240）设备不支持,
   SDK 不接线、仅提供 `SupportsClientRequestOpSpec` 门控。事件子参数投影已完成
   `ReaderExceptionEvent`（`ReaderExceptionOccurred` 事件）;其余事件子参数按
   分析不做（ConnectionAttempt/ConnectionClose/RFSurveyEvent/Hopping/AISpec,
   可经 `ReadMessagesAsync` 取原始协议对象）。TagReport 补充投影已完成
   `C1G2_PC`（`TagReport.PcBits`）;`C1G2_CRC`/`C1G2SingulationDetails`
   低价值可暂缓。
缺口明细见
   [coverage/llrp101-sdk-coverage.md](coverage/llrp101-sdk-coverage.md)。

## 中期

1. 增加更多厂商扩展，保持扩展与核心协议、托管 SDK 解耦。
2. 扩充 Virtual Reader 的配置流、故障注入和互操作场景。
3. 根据实际使用反馈补充 CLI Settings 编辑器和 Agent 输出能力，保持已有
   命令语义稳定。

## 长期

1. 实现 `Llrp20ProtocolAdapter`，覆盖协商、ROSpec、AccessSpec 和 TagReport
   的最小闭环。
2. 增加 LLRP 2.0 Virtual Reader 和真实设备互操作测试。
3. 继续完善多厂商协议定义、生成和能力验证工具链。

## 约束

- 新的托管能力优先进入 `LlrpSdk`，CLI 只负责输入、展示和流程编排。
- CLI 与一次性命令共用 SDK 工作流，不维护第二套配置或资源生命周期。
- 新增公开能力必须同步更新 [status.md](status.md) 和对应用户指南。
- 生成协议代码只能通过 `definitions/`、生成器或生成脚本修改。
