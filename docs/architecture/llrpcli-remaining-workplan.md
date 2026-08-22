# LlrpCli 剩余工作详细实施计划 (WP4 收尾 + WP7)

> 状态：已完成（归档）。承接的 WP4 收尾、Settings 编辑器和 WP7 诊断工作均已实施；
> 本文只保留实施锚点和验收历史。
> 目标读者:接手的编码模型。本文件给出到函数/行/字段级的实施锚点,不重复父计划的差距分析。
> 当前命令行为以 [CLI 用户指南](../guides/cli-user-guide.md) 和 `docs/status.md` 为准。

## 实施进度速览

- [x] A 段(WP4):旧资源模式流程已由两控制面架构取代；当前专家资源在 Ready 后直通，
  托管部署统一使用 PreserveForeign/显式 ReplaceAll 与容量预检。
- [x] B E2:能力交叉引用(`RenderRfModeHint`/`RenderTxRxHint` 已加)。
- [x] B E1:厂商扩展泛化(`EditVendorExtensions` 拆为 Impinj + Zebra 分支)。
- [x] B E4:内联校验(population/ReportEveryN 加 `.Validate`)。
- [x] B E3:diff 预览(`RenderReviewDiff`,Review 时打印改前→改后)。
- [x] B E5:事件开关改 `EditEventsMultiSelect` 一次多选勾选。
- [x] B E6:`Discard` 未保存变更确认;主菜单编号直达(1..10 编号显示)+ Back(返回上一步)。
- [x] B E7:JSON 导入(`ImportJson` 加载+校验)与导出(`SaveToFile`/export JSON)。
- [x] WP7 全部 E 项完成。

---

## A. WP4 收尾（已被两控制面架构取代）

父计划 8/13 节已定行为:

1. 托管调用统一在同一 operation lock 下完成能力查询、容量预检和部署；
2. PreserveForeign 保留 foreign 资源，容量冲突提示显式 ReplaceAll；
3. 专家资源调用在 Ready 后直接可用，写入后观测 stale、DesiredState 保留。

### A.1 现状核实

- 前端资源模式入口已删除；CommandCatalog 只保留托管入口和专家协议入口。
- SDK 原语齐备(勿在 CLI 重新实现):
  - reader.RoSpecs.GetAllAsync(ct) / reader.AccessSpecs.GetAllAsync(ct) -> IReadOnlyList<ILlrpParameter>,直接 .Count(src/LlrpSdk/Resources/*Service.cs)。
  - `reader.RoSpecs` / `reader.AccessSpecs` 在 Ready 后直接调用；SDK 不再提供资源模式切换 API。
- 托管 apply 入口两处:
  - LiveSettingsHandler.ApplyAsync(LiveSettingsHandler.cs:156 附近,--defaults|--yes|--json)
  - LiveInventoryHandler.HandleAsync 的 start 分支 one-shot 路径(LiveInventoryHandler.cs:58-80,deploy 前后)。

### A.2 实施步骤

旧的模式守卫 helper 已删除；ManagedSettingsWorkflow 统一承载
`ResourceTakeoverPolicy.PreserveForeign` / `ReplaceAll`，并在同一 operation lock 内完成容量预检。
`RoSpecs.GetAllAsync` 始终直接发送 GET_ROSPECS，不缓存也不受额外模式门控。

### A.3 直接专家资源调用

现状:专家资源调用无需先同步；写入后 SDK 将观测标记 stale，调用方可直接 sync 或由托管 API 重新接管:

```csharp
await _session.Reader.RoSpecs.GetAllAsync(cancellationToken);
await _session.Reader.RoSpecs.AddAsync(roSpec, cancellationToken);
```

### A.4 验收

- 单测:两分支(有资源确认拒绝/接受、无资源静默)走 fake IAnsiConsole;helper 提为 internal static 供测试。
- dotnet test tests/LLRPCli.Tests 绿色(当前 31,不得回归)。
- 实机(可选,记入 docs/acceptance/reader-interoperability.md):验证专家 CRUD、stale/DesiredState 和 PreserveForeign/ReplaceAll 接管。

---

## B. WP7:Settings 编辑器增强(E2->E1->E4->E3->E5->E6->E7)

原则:每项独立小步,每步带测试,逐步交付;不做一次性大重写。锚点均在 src/LlrpCli/Commands/SettingsEditor.cs(约 475 行)。

### 全局事实(实施前必须读)

- 主循环 EditAsync(SettingsEditor.cs:22):SelectionPrompt<SettingsArea> 循环,switch 分发 13 个 area;当前无编号、无返回上一步、无未保存提示。
- EditVendorExtensions(SettingsEditor.cs:417)当前硬编码 Impinj:report.IncludeSerializedTid、IncludeRfPhaseAngle、IncludePeakRssi、control.EnableTagPopulationEstimation。
- EditReaderConfiguration(SettingsEditor.cs:367)10 个事件开关逐个 console.Confirm(E5 目标:一次多选)。
- EditAntennasAndRf(SettingsEditor.cs:117)用 TextPrompt<ushort> 收集 ModeIndex/Tari/Tx/Rx/Hop/Channel(E2 目标:就地显示能力表)。
- 能力数据源:reader.Capabilities(LlrpReader.cs:197,类型 ReaderMetadata.cs 内 C1G2RfModeEntry(ModeIdentifier/DrValue/MValue/BdrValue/MinTariValue/MaxTariValue/StepTariValue)、TxPowerEntry(Index/TransmitPowerDbm)、RxSensitivityEntry(Index/ReceiveSensitivityDb)、HopTables、RfModes)。
- 扩展发现源:reader.Extensions(LlrpReader.cs:238,IReaderExtensionCollection.Get<T>());Impinj/Zebra 均实现 IReaderSettingsContributor + IReaderSettingsSerializationContributor,Seuic 仅 IReaderSettingsDefaultsContributor。Zebra 扩展键 zebra.configuration/zebra.inventoryReport;Impinj impinj.configuration/impinj.inventoryReport/impinj.inventoryControl。

### B.1 E2:能力交叉引用(优先级最高)

目标:编辑 ModeIndex/Tari/Tx/Rx/Hop 时,在提示旁打印 reader.Capabilities 对应表;Capabilities == null(未连接)时降级为纯提示,不报错。

落点:EditAntennasAndRf(SettingsEditor.cs:117)内每个 TextPrompt 前调用新 helper:

```csharp
// 新增 private static void RenderCapabilityHint(IAnsiConsole console, LlrpReader reader, CapabilityKind kind)
// kind ∈ { RfModes, TxPower, RxSensitivity, HopTables }
```

- Mode/Tari:打印 Capabilities.RfModes 表(DR/M/Tari min-max-step);
- Tx index:打印 TxPowerEntry.Index -> TransmitPowerDbm;
- Rx index:打印 ReceiveSensitivityDb;
- Hop:打印 HopTables(HopTableId/Frequencies)。

测试:离线构造 Capabilities(若构造器 internal,用反射/测试内 builder,或在 LlrpSdk 增加 internal ctor 供 InternalsVisibleTo)。

### B.2 E1:厂商扩展泛化

目标:删除 EditVendorExtensions 的 is ImpinjReaderExtension 硬编码,按 reader.Extensions 动态发现,补 Seuic/Zebra 编辑片段。

实施:
1. 抽 helper GetActiveExtensionKeys(reader) -> string[](枚举 IReaderSettingsContributor + 序列化 contributor 的扩展键,或扩展类型名);
2. EditVendorExtensions 改为:if Impinj active -> Impinj 片段;if Zebra active -> Zebra 片段(复用 ZebraInventoryReportOptions/ZebraReaderConfiguration 的布尔字段,如 IncludeZoneId/IncludeZoneName/IncludePhase/IncludeGps);Seuic 无写路径 -> 打印 [grey]Seuic contributes defaults only[/] 并跳过。
3. 不破坏:两个扩展都不 active 时显示 [grey]No vendor extensions active[/]。

测试:三种分支(Impinj active/Zebra active/none)。

### B.3 E4:内联校验反馈

目标:字段录入后立即局部校验(范围/格式),Validate 保留为全量编译校验。

落点:为 EditSingulation/EditReports/EditAntennasAndRf 的 TextPrompt 加 .Validate 回调(Spectre TextPrompt<T>.Validate),例如 population 上限、Tari 0=default、word count>0、hex 偶数长。不改变数据结构。

测试:非法输入被 prompt 拒绝路径;如成本高,改为抽纯函数 ValidatePopulation(ushort) 单测。

### B.4 E3:变更 diff 预览

目标:Review 不只显示全量,高亮本会话改动字段(改前 -> 改后)。

实施:EditAsync 记录 source 快照 + 每次 area 编辑后的 working,在 SettingsArea.Review 分支(SettingsEditor.cs:59)调用新 RenderDiff(console, source, working);手写 ReaderSettings 顶层字段 + 各子记录浅比较,输出 [yellow]- old[/] / [springgreen2]+ new[/] 行。

测试:同一 settings 对象改 1 字段,断言 diff 输出含该字段;无改动时输出 No changes。

### B.5 E5:减少确认疲劳

目标:EditReaderConfiguration 的 10 个事件开关改 MultiSelectionPrompt 一次勾选;天线逐根询问默认 所有同值,需要时才逐根覆盖。

实施:
1. 事件开关:collect MultiSelectionPrompt<string>(显示当前 enabled 集合),输出 EventsConfiguration 覆盖;
2. 天线:在 EditAntennasAndRf 前先问 console.Confirm("Use same values for all antennas?") 默认 true;false 才进逐根循环。

测试:多选回调纯函数(勾选集 -> events 记录)单测。

### B.6 E6:导航增强

目标:主菜单编号直达、返回上一步、未保存变更提示。

实施:EditAsync 循环改:
1. 编号:prompt 提示里加 (1) Antennas … (N) Discard(用 FormatSettingsArea 前缀序号),接受数字直接跳转;
2. 返回上一步:维护 SettingsArea? previous,主菜单加入 (B) Back(disabled 于无上一步);
3. 未保存提示:ApplyToReader/SaveToFile/Discard 前若 !ReferenceEquals(working, source) 且未确认过,显示 [yellow]Unsaved changes — (A)pply (S)ave (D)iscard[/] 三选。

测试:决策纯函数(输入序列 -> 跳转)单测;交互仅冒烟。

### B.7 E7:JSON 融合

目标:编辑过程 import/export JSON,打通表单与文件流。

实施:主菜单加两项(或并入 SaveToFile):
1. Export JSON:ManagedSettingsWorkflow.Save(reader, path, working)(路径用 TextPrompt,复用 SettingsRenderer.RenderJson 或 SaveToFile);
2. Import JSON:ManagedSettingsWorkflow.Load(reader, path) 后立即 ValidateAsync + RenderValidation,通过则替换 working 并显示 diff。

测试:文件往返(Save->Load->EditorResult.Settings 等价)单测(临时目录)。

### B.8 交付顺序与验收口径

按 E2 -> E1 -> E4 -> E3 -> E5 -> E6 -> E7,每步:

1. dotnet build src/LlrpCli/LlrpCli.csproj 0 警告 0 错误;
2. dotnet test tests/LLRPCli.Tests 不回归(当前 31);
3. 新增该步测试(见各节);
4. 提交信息按仓库规范(agent 不自动 commit,需用户许可)。

---

## C. 完成后统一收口(WP8 续)

- 同步 docs/guides/cli-user-guide.md(settings 编辑器/盘点/两控制面段落)、docs/status.md、docs/roadmap.md，父计划 llrpcli-optimization-plan.md 状态更新为已实施。
- docs/architecture/llrpcli-target-commands.md 无需大改(A/B 均已在其目标态内)。

---

## D. 依赖与风险

- reader.Capabilities 在未连接为 null:所有 E2 路径必须容 null。
- RoSpecs.GetAllAsync 的直查与容量预检需保持同一 operation lock；设备最终状态码仍是专家 API 的权威裁决。
- E1 依赖 Zebra 扩展布尔字段名,实施时以 src/LlrpSdk.Extensions.Zebra 实际代码为准(勿信记忆)。
