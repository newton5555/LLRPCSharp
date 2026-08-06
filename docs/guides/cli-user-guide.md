# CLI 命令行工具指南 (`LlrpCli`)

`LlrpCli` 是基于 `LlrpSdk` 构建的命令行工具，提供两种使用模式：
1. **交互式 Live Shell 终端**：用于现场设备连接、调试、配置编辑与盘点测试；
2. **单次自动化命令**：用于 Agent 脚本与自动化运维调度，默认输出 JSON 格式标签数据。

---

## 🚀 1. 启动交互式 Live Shell

在命令行执行：

```powershell
dotnet run --project src/LlrpCli
```

### 1.1 设备连接与状态查看

```text
> connect 192.168.1.100     # 连接到目标 RFID 读写器
> status                    # 查看当前连接与设备生命周期状态
> caps                      # 查询读写器天线数量、功率表与 RF 模式表
```

---

## 🔧 2. 配置编辑与下发 (Settings)

Live Shell 维护了一个本地配置草稿 (Draft)，可以先编辑校验，确认无误后再下发给读写器：

```text
> settings edit --from defaults   # 从当前设备推荐默认值创建草稿
> settings show draft             # 查看当前草稿配置
> settings validate               # 校验草稿合法性
> settings apply --yes            # 正式下发配置到读写器
```

### 配置来源选项

* `defaults`：连接设备的推荐配置；
* `reader`：读写器当前正在运行的配置；
* `generic`：通用标准协议配置；
* `settings load <file.json>`：从本地 JSON 文件加载。

---

## 📡 3. 标签实时盘点 (Inventory)

```text
> inventory start                 # 启动标签盘点，终端实时滚动显示刷卡数据
> inventory status                # 查看当前盘点运行状态
> inventory stop                  # 停止盘点
```

### 监控模式选项

```text
> inventory start --monitor live                  # 实时汇总标签数据 (默认)
> inventory start --monitor frames                # 实时打印原始 LLRP 协议数据帧
> inventory start --monitor live --monitor-duration 30  # 前台监控 30 秒后退出前台（后台仍运行）
```

---

## 🔓 4. 标签内存读写与锁定 (Tag)

```text
# 读取 EPC 标签的 User 区
> tag read E28011910000000000000001 --bank user --word 0 --count 2

# 写入 User 区 (需要 --yes 确认下发)
> tag write E28011910000000000000001 --bank user --word 0 --data CAFEBABE --yes

# 锁定指定区域
> tag lock E28011910000000000000001 --target user --privilege lock --yes

# 销毁标签
> tag kill E28011910000000000000001 --yes
```

---

## 🤖 5. 脚本与自动化单条命令 (Agent 命令)

无需进入交互式 Shell，用单条命令直接完成自动化任务，默认输出标准 JSON：

```powershell
# 连接 192.168.1.100，执行 10 秒盘点并输出 JSON 数据
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --duration 10 --yes

# 使用自定义配置文件进行 30 秒自动化盘点
dotnet run --project src/LlrpCli -- inventory 192.168.1.100 --settings config.json --duration 30 --yes
```

---

## 🔍 6. 离线协议诊断工具

不连接真实读写器，直接分析或构造 LLRP 二进制十六进制数据帧：

```powershell
# 解析十六进制 LLRP 字节帧结构
dotnet run --project src/LlrpCli -- inspect "043E0000000A01020304"

# 解码二进制帧为 JSON 文本
dotnet run --project src/LlrpCli -- decode "043E0000000A01020304"

# 构造 GET_ROSPECS 消息的十六进制字节串
dotnet run --project src/LlrpCli -- encode get-rospecs --message-id 1
```

> 注：离线工具当前只注册 LLRP 1.0.1 标准编解码模块；`inspect` 仍可检查 1.1
> 帧的 Header，但 `decode`/`validate` 无法展开 1.1 消息字段，1.1 `encode` 暂不支持。

---

## 🌐 7. 协议版本支持 (LLRP 1.0.1 / 1.1)

CLI 的协议版本能力取决于命令的实现层。实时 Reader 命令走 `LlrpReader`，
离线协议工具则直接使用 CLI 自己创建的 Codec 注册表，因此两者的覆盖范围不同。

### 7.1 实时功能 (Live):由 SDK 协商或强制选择版本

`connect` / `inventory` / `tag` / `monitor` / `settings` 等实时命令通过
`LlrpReader`（SDK 门面）与设备交互，协议版本由 SDK 在连接阶段协商：

```text
> connect 192.168.1.100 --llrp auto    # 自动协商（默认）
> connect 192.168.1.100 --llrp 1.0.1   # 强制 1.0.1
> connect 192.168.1.100 --llrp 1.1     # 强制 1.1
```

* `auto`（默认）：先建立 TCP 连接并以 1.0.1 Adapter 作为初始状态，然后发送
  LLRP 1.1 的 `GET_SUPPORTED_VERSION`。如果 Reader 声明支持 1.1，SDK 再发送
  `SET_PROTOCOL_VERSION(1.1)` 并切换到 1.1 Adapter；如果 Reader 明确返回不支持
  （或声明的最高版本低于 1.1），则保留 1.0.1。
* `1.0.1`：跳过版本协商，直接使用 1.0.1。设备可能在后续协议初始化或操作阶段
  报告不兼容，而不一定在 TCP 连接阶段失败。
* `1.1`：必须完成 1.1 版本协商；Reader 不支持、拒绝、超时或返回无效响应时，
  连接初始化失败，不会静默回退到 1.0.1。

成功切换后，SDK 的标准能力（Reader 初始化、Settings、Inventory、TagReport 和
标准 Tag Access）使用 1.1 对应的消息、参数和 Adapter。CLI 通常不需要区分版本，
但 1.1 目前属于可用基线，真实 Reader 型号/固件的互操作仍需单独验证；厂商扩展也
可能存在版本边界（例如当前 Impinj 扩展只在 1.0.1 下激活）。

### 7.2 离线协议工具 (Inspect/Decode/Validate/Encode):标准 Codec 当前仅 1.0.1

`inspect` / `decode` / `validate` / `encode` 直接操作 LLRP 报文（不连接设备），
走协议层（`LlrpNet.Protocol`）。CLI 当前注册了标准 1.0.1 编解码模块，另外注册了
1.0.1 的 Impinj 扩展模块：

* `inspect` 只读取 Header，因此 1.1 帧仍可显示协议版本、消息类型、消息 ID 和长度；
* `decode` / `validate` 可以校验帧结构，但未注册的 1.1 消息主体会得到
  `UnknownMessage`，无法展开字段；
* `encode` 当前没有 `--version` 参数，消息模板和编码版本固定为 1.0.1，构造 1.1
  消息暂不支持。

这是已记录的待办(`docs/roadmap.md`),补齐方式是在 `Helpers.CreateRegistry()`
中注册 `Llrp11StandardModule`，并让 `encode` 支持 `--version 1.0.1|1.1` 参数，
同时按所选版本构造对应的消息、参数和枚举类型。

### 7.3 为什么两层能力不同

```
实时命令 → LlrpReader(SDK 版本协商) → LlrpNet(1.0.1 + 1.1 双模块)
离线工具 → LlrpNet.Protocol 直连(注册表只挂了 1.0.1)
```

`LlrpNet` 已具备 1.0.1/1.1 的协议模型和编解码基础，`LlrpSdk` 已具备两套对应
Adapter 和自动协商能力；CLI 实时命令可以使用两种版本，而离线工具目前只注册了
标准 1.0.1 编解码模块。补齐离线注册表和版本化消息构造后，离线工具才能获得更
完整的 1.1 能力。

### 7.4 SDK 连接真实 1.1 Reader 时会发生什么

以 `LlrpReader` 为入口时，实际流程如下：

1. 建立 TCP 连接，状态进入 `Negotiating`，默认 Adapter 暂时为 1.0.1。
2. `auto` 或 `1.1` 策略发送 1.1 的 `GET_SUPPORTED_VERSION`。
3. Reader 支持 1.1 时，SDK 发送 `SET_PROTOCOL_VERSION(1.1)`，随后选择
   `Llrp11ProtocolAdapter` 并继续能力初始化。
4. 后续标准请求、响应、Reader Event、TagReport、Inventory 和 Tag Access 都按
   1.1 的消息/参数类型处理。

因此，连接一个实现正常的真实 1.1 Reader，预期结果是连接完成后状态为 `Ready`，
并在 1.1 Adapter 下运行。当前仓库的自动化测试主要覆盖 1.0.1 和虚拟 Reader；
1.1 的真实设备兼容性仍需要使用目标 Reader 执行硬件验收，不能仅凭 CLI 构建通过
就视为已验证。
