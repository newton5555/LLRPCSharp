# 路线图

> 基准日期：2026-08-03

本文档只保留未完成事项；当前实现事实见 [status.md](status.md)。

## 近期

1. 完成 LLRP 1.0.1 目标设备的最终实机验收，并补充失败场景记录。
2. 根据真实设备证据扩充 Impinj 型号/固件能力目录。
3. 完善独立的 Reader Studio 项目，但不把 WPF 依赖带回 SDK 仓库。

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
