# Developer Tools & Utility Scripts

本目录包含 `LLRPCSharp` 仓库的开发辅助工具、自动化脚本与测试验证小工具。

## 脚本与工具清单

| 文件 / 目录 | 类型 | 说明 | 常用命令 |
|---|---|---|---|
| `Generate-ProtocolCode.ps1` | PowerShell 脚本 | 自动化触发 `LlrpNet.ProtocolGenerator.Tool` 重新生成 LLRP 1.0.1/1.1/Impinj/2.0 的 `.g.cs` 协议代码。 | `./tools/Generate-ProtocolCode.ps1` |
| `Verify-SourceEncoding.ps1` | PowerShell 脚本 | 扫描检查源代码文件的编码格式，确保遵守仓库的编码规范（UTF-8 with BOM）。 | `./tools/Verify-SourceEncoding.ps1` |
| `LlrpSdk.LiveSmoke/` | 控制台工具 | 读写器实机/烟雾测试实用小工具，用于快速连接硬件并验证端到端盘点与事件流。 | `dotnet run --project tools/LlrpSdk.LiveSmoke` |

---

## 协议代码生成 (`Generate-ProtocolCode.ps1`) 详细用法

在仓库根目录下运行该脚本：

```powershell
# 1. 重新生成全部代码（LLRP 1.0.1、1.1 以及 Impinj 扩展）
./tools/Generate-ProtocolCode.ps1

# 2. 仅重新生成 LLRP 1.0.1 标准代码
./tools/Generate-ProtocolCode.ps1 -Target 1.0.1

# 3. 仅重新生成 LLRP 1.1 代码
./tools/Generate-ProtocolCode.ps1 -Target 1.1

# 4. 仅重新生成 Impinj 厂商扩展代码
./tools/Generate-ProtocolCode.ps1 -Target Impinj

# 5. 生成 LLRP 2.0 增量代码
./tools/Generate-ProtocolCode.ps1 -Target 2.0

# 6. 校验模式（仅检查生成的代码是否最新，不修改磁盘文件，适用于 CI）
./tools/Generate-ProtocolCode.ps1 -Verify
```
