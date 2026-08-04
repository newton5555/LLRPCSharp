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
