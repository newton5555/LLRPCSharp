# CLI 消重改造计划 (D1 删死代码 + D3 共享命令核)

> 状态:可执行交接文档,交其他 agent 直接照做。
> 前置:`LlrpCli.Tests` 32/32 通过,全解决方案 build 0 警告 0 错误。
> 目标:行为不变(用户可见输出一致),只消除重复;每步可独立构建+测试。

---

## 一、结论先行

- **D1 不是"合并两套"**——`LlrpFrameAnalyzer.cs` 是**从未接线的死代码**,正确做法是**删除**。
- **D3 是"抽共享核心"**——`inspect`/`decode`/`validate` 三个离线命令在 standalone 与 live-shell 各写了一遍,应抽一个共享实现,两个入口只做参数适配。

---

## 二、D1:删除死代码 LlrpFrameAnalyzer

### 2.1 事实依据(已核实)

`src/LlrpCli/Analysis/LlrpFrameAnalyzer.cs`(172 行)及其 3 个公开类型:

| 类型 | 外部引用 |
|---|---|
| `LlrpSemanticNode` | 0(仅自身) |
| `LlrpStatusInfo` | 0(仅自身) |
| `FrameAnalysisResult` | 0(仅自身) |
| `LlrpFrameAnalyzer.Analyze` | 0(仅自身) |

全仓库 `grep` 证实除该文件自身外无任何调用者、无任何测试引用。它是 2026-07-23 commit `3eae3e5` 引入的
"语义分析器"半成品,从未接线。

真正的报文树渲染由 `src/LlrpCli/Rendering/FrameRenderer.cs` 的 `BuildObjectTree`(171-267 行)承担,已被
`DecodeCommand`/`LiveProtocolDiagnostics`/`RenderObjectTree` 使用。

### 2.2 执行步骤

1. 删除文件 `src/LlrpCli/Analysis/LlrpFrameAnalyzer.cs`。
   - 若 `src/LlrpCli/Analysis/` 目录因此变空,一并删除该目录。
2. 全仓库搜索确认无残留引用:
   ```
   LlrpSemanticNode | LlrpStatusInfo | FrameAnalysisResult | LlrpFrameAnalyzer
   ```
   应只剩 0 处。
3. `dotnet build LLRPCSharp.slnx` 0 警告 0 错误。
4. `dotnet test tests/LlrpCli.Tests` 32/32 不回归。

### 2.3 验收标准

- 文件已删除,无任何编译残留引用。
- build + test 全绿,`decode --output text` 输出树与改造前完全一致(渲染路径未动)。

---

## 三、D3:抽取共享离线命令核

### 3.1 现状(已核实)

四个离线命令各有两处实现:

| 命令 | standalone(Spectre Command) | live-shell(路由方法) | 逻辑是否一致 |
|---|---|---|---|
| inspect | `InspectCommand.cs` | `LiveProtocolDiagnostics.Inspect` | 完全一致(ParseHex→DecodeExactHeader→RenderHeader) |
| decode | `DecodeCommand.cs` | `LiveProtocolDiagnostics.Decode` | 几乎一致,但 standalone 多 `--output json` 分支 |
| validate | `ValidateCommand.cs` | `LiveProtocolDiagnostics.Validate` | 完全一致(ParseHex→DecodeExactHeader→DecodeMessage→RenderValidationResult) |
| encode | `EncodeCommand.cs` | `LiveProtocolDiagnostics.Encode` | 消息构造已共享(`Helpers.CreateEncodeMessage`),仅参数解析方式不同 |

关键事实:

- **decode/validate/inspect 的"字节级解析 + 渲染"逻辑已天然收敛到 `FrameRenderer`**,重复的只是"入参解析"那一小段。
- **encode 的消息目录已在 WP1 抽到 `Helpers.CreateEncodeMessage`**(standalone 与 live-shell 都调它),D4 已消。
- 差异点:standalone 用 Spectre `[CommandOption]` 声明式解析;live-shell 用手写 token 循环。

### 3.2 设计:新建共享核 `OfflineProtocolTool`

新建 `src/LlrpCli/Commands/OfflineProtocolTool.cs`(或放 `Helpers.cs` 内),把"纯逻辑"(不含入参解析、不含 Spectre 属性)抽为静态方法:

```csharp
internal static class OfflineProtocolTool
{
    public static byte[] ParseFrame(string hex) => Helpers.ParseHex(hex);

    public static LlrpMessageHeader InspectFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        LlrpMessageHeader header = Helpers.DecodeExactHeader(frame);
        FrameRenderer.RenderHeader(header, frame.Length, console);
        return header;
    }

    public static ILlrpMessage DecodeFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderDecodedMessage(message, frame, console);
        return message;
    }

    public static void ValidateFrame(string hex, IAnsiConsole console)
    {
        byte[] frame = Helpers.ParseHex(hex);
        Helpers.DecodeExactHeader(frame);
        ILlrpMessage message = Helpers.CreateRegistry().DecodeMessage(frame);
        FrameRenderer.RenderValidationResult(isValid: true, message.GetType().Name, frame.Length, console);
    }
}
```

说明:

- `decode` 的 `--output json` 分支**留在 standalone `DecodeCommand`**,不并入共享核(它是 standalone 独有的脚本友好输出,live-shell 无此需求)。
- `encode` **不并入**共享核——消息构造已在 `Helpers.CreateEncodeMessage`,standalone 的 `[CommandOption]` 与 live-shell 的 token 解析天然不同,强行合并会引入歧义。D3 只做 inspect/decode/validate 三个。

### 3.3 改造 standalone 三命令

1. `InspectCommand.Execute`:方法体替换为 `OfflineProtocolTool.InspectFrame(settings.Hex, _console);`。
2. `ValidateCommand.Execute`:方法体替换为 `OfflineProtocolTool.ValidateFrame(settings.Hex, _console);`。
3. `DecodeCommand.Execute`:`--output json` 分支**保留原逻辑**;`else` 分支替换为 `OfflineProtocolTool.DecodeFrame(settings.Hex, _console);`。

注意:替换后 `InspectCommand`/`ValidateCommand`/`DecodeCommand` 的 `Execute` 里不再需要 `Helpers.ParseHex`/`Helpers.DecodeExactHeader` 等直接调用,清掉不再使用的 using。

### 3.4 改造 live-shell `LiveProtocolDiagnostics`

1. `Inspect(tokens, console)`:保留 `tokens.Length < 2` 的 usage 判断,主体替换为 `OfflineProtocolTool.InspectFrame(tokens[1], console);`。
2. `Decode(tokens, console)`:保留 usage 判断,主体替换为 `OfflineProtocolTool.DecodeFrame(tokens[1], console);`。
3. `Validate(tokens, console)`:保留 usage 判断,主体替换为 `OfflineProtocolTool.ValidateFrame(tokens[1], console);`。
4. `Encode(tokens, console)`:**不动**(消息构造已共享,参数解析保留手写 token 循环)。

### 3.5 验收标准

- `dotnet build LLRPCSharp.slnx` 0 警告 0 错误。
- `dotnet test tests/LlrpCli.Tests` 32/32 不回归。
- 行为等价验证(手动,各跑一条):
  ```
  llrp inspect <hex>   # standalone
  llrp decode <hex> --output text
  llrp validate <hex>
  # live-shell 内(交互)敲同命令,输出一致
  ```
  三条命令输出与改造前逐字节一致。

---

## 四、执行顺序与回归

1. 先做 D1(删死代码),独立 build+test。
2. 再做 D3(抽共享核),独立 build+test。
3. 最后全解决方案 build + test。
4. 按仓库规范:不自动 commit;完成后列 `git status`/`git diff --stat` 供用户 review。

---

## 五、风险与边界

- **不改 `FrameRenderer.BuildObjectTree` 的渲染逻辑**(深度上限 8、hex 截断 20 项等行为都是用户可见的,保持原样)。
- **不碰 `EncodeCommand`/`LiveProtocolDiagnostics.Encode` 的参数解析**(已共享消息构造,再合并有歧义风险)。
- `OfflineProtocolTool` 用 `internal static`,与 `Helpers` 同级放 `Commands` 命名空间。
- 若未来有人要复用 `LlrpFrameAnalyzer` 的"语义分析"(提取 Status/summary),那是新需求,不在本次范围;本次仅删除死代码。
