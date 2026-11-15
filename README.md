# Filesa COChat 7.0 · WinUI 3 重写版

原 `FilesaCOChat.exe`（PyInstaller / Python 3.13 / Tkinter 单文件应用）的 **C# + WinUI 3** 重写版，
端口、报文、文件握手与用户操作流程保持兼容，并逐项修复逆向报告中的 9 项已知缺陷。
产品版本自本版起为 **7.0**（原版 5.1 是重写所依据的基线），版本号只在
`src/FilesaCOChat.Core/AppInfo.cs` 定义一次：窗口标题、关于页、更新检查与 MSI 打包脚本
（`installer/gen-wxs.py` 直接解析该文件）都从这一处读取。

完整交付说明见 `docs/FilesaCOChatWinUI_重写交付说明.html`。

## 技术栈

| 项 | 取值 |
|---|---|
| 语言 | C# 12（`LangVersion 12.0`，nullable enable） |
| 核心层 | `net8.0`，纯 BCL、**零 NuGet 依赖**、UI 无关 |
| 界面层 | `net8.0-windows10.0.19041.0`（最低 `10.0.17763.0`），WinUI 3 / XAML |
| Windows App SDK | `1.6.250108002` |
| 部署 | Unpackaged（`WindowsPackageType=None`）+ **Self-Contained**（运行时 DLL 随 exe 输出） |
| 构建 | .NET SDK 11.0.100（编译 net8 目标）+ Windows SDK 10.0.28000.0；不需要 Visual Studio |

## 项目结构

```
FilesaCOChat.sln
├── src/FilesaCOChat.Core/          协议、网络、安全策略、门面（17 文件 / 2606 行）
│   ├── AppInfo.cs                  产品名与版本号单一来源（installer 也从这里读）
│   ├── DirectEndpoint.cs           「地址:端口」解析与缺省端口回落
│   ├── Protocol/LegacyCodec.cs     原版裸报文（逐字节兼容 5.1）
│   ├── Protocol/SecureFrame.cs     FCCH 长度帧 + HMAC-SHA256 + 会话 ID / 序列号
│   ├── Net/ChatSession.cs          服务端/客户端双角色、显式广播、迁移
│   ├── Net/DiscoveryService.cs     UDP 11452 广播 + /24 扫描 + 11453 能力探测
│   ├── Net/FileTransferService.cs  TCP 11450，4096 B 分块 + SHA-256
│   ├── Net/UpdateChecker.cs        GitHub releases 检查
│   ├── Security/CommandPolicy.cs  D1：远程命令执行永久禁用
│   ├── Security/FilePolicy.cs     D5：文件名白名单 / 大小上限 / 重名递增
│   ├── Security/MigrationCoordinator.cs  D6：确定性选举 + 任期 + ACK
│   ├── SettingsStore.cs            主题 / 字体 / 通知偏好持久化（PSK 不落盘）
│   ├── ChatApplication.cs          UI 无关门面（对应 main.py 的 FilesaCOChat 类）
│   ├── UiText.cs                   原字节码中文文案，逐字保留
│   └── ProtocolOptions.cs          全部端口 / 超时 / 阈值常量
├── src/FilesaCOChat.WinUI/         WinUI 3 界面（13 文件 / 2491 行）
│   ├── MainWindow.xaml(.cs)        气泡消息区 / 用户列表 / 输入 / 文件 / 命令行 / 登录
│   ├── SettingsWindow.xaml(.cs)    独立设置窗口：侧边栏分类 / 主题 / 字体 / 通知 / 安全
│   ├── SystemNotifier.cs           Windows 通知中心 toast（AppNotificationManager）
│   ├── ChatTemplateSelector.cs     三模板分类：对方 / 自己 / 系统
│   ├── Converters.cs               消息类型配色 + HH:mm 时间线
│   ├── ViewModels/MainViewModel.cs 所有核心事件经 DispatcherQueue 编组（D9）
│   └── Assets/                     从原 exe 提取的 image/*.png、icon.ico
└── tests/FilesaCOChat.Tests/       35 个可运行用例（9 文件 / 1293 行），零第三方依赖
```

## 快速开始

```bash
cd FilesaCOChatWinUI

# 还原 + 构建整个解决方案
dotnet build FilesaCOChat.sln

# 运行验证套件（结果写入 test-results.json）
dotnet run --project tests/FilesaCOChat.Tests/FilesaCOChat.Tests.csproj

# 4) 构建正式 Release 可执行文件（交付产物）
dotnet publish src/FilesaCOChat.WinUI/FilesaCOChat.WinUI.csproj -c Release -r win-x64 --self-contained false -o dist/FilesaCOChat-7.0-win-x64
```

## 可执行文件（Release 交付产物）

| 文件 | 说明 |
|---|---|
| `dist/FilesaCOChat-7.0-win-x64/FilesaCOChat.WinUI.exe` | **主可执行文件**，308,224 B，SHA-256 `33fb25bfc7bd…`；双击即可运行，无需安装 Windows App Runtime。托管代码与编译后的 XAML 都在同目录的 `FilesaCOChat.WinUI.dll`（299,520 B）和 `FilesaCOChat.Core.dll`（151,552 B），二者必须与 exe 一起分发 |
| `dist/FilesaCOChat-7.0-win-x64/` | 同目录 266 个文件 / 90 MB，**必须与 exe 一起分发**（含 WinUI 运行时 DLL、`resources.pri`、`Assets/`） |
| `dist/FilesaCOChat-7.0-win-x64.zip` | 绿色分发包，30,733,846 B（SHA-256 `053e4130…`），266 项校验通过；解压后运行 exe |
| `dist/FilesaCOChat-7.0-Setup.msi` | **安装包**，24,662,013 B（SHA-256 `e4edb5d9…`）；双击安装、走标准卸载流程，详见下节 |
| `dist/_experiments/` | 失败的单文件尝试，仅作证据，**勿使用** |

实测：Release exe 启动后主窗口 `Filesa COChat 7.0 · WinUI 3`、Responding=True、无运行时弹窗。

其他架构：

```bash
dotnet build src/FilesaCOChat.WinUI/FilesaCOChat.WinUI.csproj -r win-arm64
```

`RuntimeIdentifier` 默认 `win-x64`，可通过 `-r` 覆盖为 `win-x86` / `win-arm64`。

## 安装包（MSI）

`dotnet publish` 的产物是 266 个散文件，直接分发对普通用户不友好，因此用 **WiX Toolset 5.0.2**
打成单个 MSI：

| 项 | 值 |
|---|---|
| 产物 | `dist/FilesaCOChat-7.0-Setup.msi`，24,678,397 B（24.7 MB），`ProductVersion 7.0.0` |
| 安装位置 | `%LOCALAPPDATA%\Programs\FilesaCOChat`（**per-user，不弹 UAC、不需要管理员**） |
| 注册项 | 设置 → 应用 中显示「Filesa COChat 7.0 / Filesa」；卸载或 `msiexec /x` 均可 |
| 快捷方式 | 开始菜单 → `Filesa COChat`（图标取自 `Assets\icon.ico`） |
| 升级 | `MajorUpgrade` + 固定 `UpgradeCode`，同版本重装会替换旧文件 |
| 卸载清理 | 安装目录、开始菜单文件夹、ARP 与 `Installer\Products` 全部移除（已实测） |

**构建方式**：

```powershell
# 1) 先产出散文件
dotnet publish src/FilesaCOChat.WinUI/FilesaCOChat.WinUI.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=false -o dist/FilesaCOChat-7.0-win-x64

# 2) 生成 WiX 源码并打包（本机执行策略禁止直接跑 .ps1 时用 Bypass）
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

- `installer/gen-wxs.py`：**自动枚举** `dist\FilesaCOChat-7.0-win-x64` 生成 `Package.wxs`；
  目录名、`ProductName`、`ProductVersion` 全部由 `src\FilesaCOChat.Core\AppInfo.cs` 的
  `Version` 推导（`7.0` → MSI 用 `7.0.0`），改版本只需改这一处。
  两件事必须靠脚本而不是手写：目录树要 1:1 映射（否则 `zh-CN\*.mui`、`Assets\` 会被摊平到
  安装根目录——第一版就是这样只装进去 96 个文件）；组件 GUID 用「相对路径的 uuid5」，重打包
  保持稳定，`MajorUpgrade` 才能正确替换而不是并存两个版本。
- `installer/build-installer.ps1`：同样从 `AppInfo.cs` 读版本决定路径，调 `wix build -arch x64`，
  再 `wix msi validate` 跑 ICE。
- **为什么是 WiX 5**：`dotnet tool install --global wix` 现在默认装 v7，v7 构建时要求接受
  OSMF 商业授权协议；本项目不代用户接受此类条款，因此固定在最后一代开源的 5.0.2。
- 尝试过再用 Burn 包一层 `Setup.exe`（欢迎/进度/完成界面）：v5 的 `WixToolset.Bal.wixext`
  与 CLI 的扩展加载不兼容（`wixext5` 目录被识别为 damaged），故未交付，MSI 双击即由 Windows
  Installer 显示标准进度框。

**已验证**：静默安装 `msiexec /i … /qn` 退出码 0，落地 **266/266** 个文件（`Assets\icon.ico`、
`zh-CN\Microsoft.ui.xaml.dll.mui` 均在原位），exe 的 `FileVersion 7.0.0.0` / `ProductVersion 7.0.0`，
开始菜单快捷方式指向安装目录，安装后的 exe 可正常启动（截图 `.workbuddy/analysis/ui-v7-preview-dark.png`）；
`msiexec /x … /qn` 退出码 0，目录、快捷方式、ARP 注册项全部清除。

**限制**：MSI 未做 Authenticode 签名，首次运行/安装会有 SmartScreen 提示；未做多用户共享
安装（固定 per-user）；未实测「从 5.1 升级到 7.0」的跨版本路径（`UpgradeCode` 未变、
`MajorUpgrade` 已配置，但没有 5.1 的 MSI 可测）。

## 为什么用 Self-Contained

最初按常规 Unpackaged 配置构建时，启动会弹出「This application requires the Windows App
Runtime Version 1.6 (MSIX package version >= 6000.373.1641.0)」并立即退出：SDK 包硬绑定
`6000.373.1641.0`，而本机只有 `6000.519.329.0`，且系统缺失 1.6 配套的 **DDLM 动态依赖包**。

改用 `WindowsAppSDKSelfContained=true` 后，构建时把 NuGet 包内自带的 12 个 MSIX payload
解包到输出目录，运行时 DLL 随 exe 走，**彻底移除机器级运行时前置条件**，也顺带禁用了弹窗
用的自动引导初始化器（`WindowsAppSdkBootstrapInitialize=false`）。这保持了原 PyInstaller
单文件版「用户无需安装依赖」的行为。

> 代价：输出从单文件 23 MB 变成 266 个文件 / 89.1 MB 的文件夹（zip 29.2 MB）。
>
> **单文件 exe 已实测不可行**：尝试 `PublishSingleFile=true --self-contained true` 时 exe
> 能生成（63 MB），但启动即崩溃 `COMException 0x80040111 ClassFactory 无法供应请求的类`
> （`Microsoft.UI.Xaml.Application.Start`）。WinUI 3 的 XAML/WinRT 组件必须从磁盘按原路径
> 加载，单文件 bundle 的内存加载机制不适用；本机 NanaZip 也没有 SFX 自解压模块。若必须单
> 文件，只能走 MSIX 打包或第三方安装器封装，本次均未采用。

## 聊天气泡界面

消息区使用 `DataTemplateSelector` 在渲染时按 `(Kind, Sender)` 选择五种模板，只改展示层，
线上报文格式零改动：

| 消息 | 模板 | 表现 |
|---|---|---|
| 对方（其他人） | `IncomingBubble` | 左侧圆角气泡，上方左对齐显示「用户名 + HH:mm」 |
| 自己（`Sender == 本地用户名`） | `OutgoingBubble` | 右侧蓝色圆角气泡，上方右对齐显示「HH:mm + 用户名」 |
| 自己发出的命令行请求（`Kind == CommandRequest`） | `CommandBubble` | 右侧「终端」式气泡：暖色标题栏（终端图标 + 「命令行请求 → 目标」）、Consolas 等宽命令正文、底部完整安全提示 |
| **收到的命令帧**（`Kind == CommandReceived`） | `ReceivedCommandBubble` | **左侧「终端」式气泡**：冷色标题栏（「收到命令 ← 发起方 → 目标」）、Consolas 等宽命令正文、底部完整安全提示 |
| 系统 / 错误 / 命令结果 / `Sender == "系统"` | `SystemLine` | 居中纯文本 + 时间戳，**无气泡** |

- **消息内链接可点击**：四个模板的正文都通过附加属性 `local:LinkifiedText.Text` 绑定，
  由纯 C# 的 `LinkParser.Tokenize` 把文本切成普通片段与 `Hyperlink` 内联。只识别
  `http://` / `https://` / `www.`（自动补 `https://`），CJK 字符与常见尾部标点会截断
  URL，非 http(s) 协议保持纯文本。只设置 `NavigateUri`，由 WinUI 在默认浏览器打开，
  不再挂 `Click` 以免打开两次；原文逐字符保留，复制 / tooltip / 审计文案不变。
- **命令消息双气泡**：`ChatEntryKind` 末尾追加 `CommandRequest` / `CommandReceived`
  （旧枚举数值不变），`ChatEntry.CommandRequest(...)` / `CommandReceived(origin, target, command)`
  带结构化 `Target` / `Command` / `Origin`。`ChatSession.SendCommandAsync` 与 `HandleCommand`
  分别使用这两个工厂，审计文案仍是 `[命令] 发送给 X: ...` / `[收到命令] ...`。分类规则集中在
  不依赖 WinUI 的 `ChatTimeline.Classify`（返回 `BubbleCategory`），`ChatTemplateSelector`
  只做模板映射，便于无界面测试；`IsSystemLine` 显式排除这两个类别。D1 安全策略不变：
  命令仍**永不执行**，气泡只做展示。

- **排查：为什么收到的命令曾经不是气泡** —— 旧 `HandleCommand` 用
  `ChatEntry.Chat(UiText.System, "[收到命令] ...")` 发射：`Kind == Chat` 看似该走气泡，
  但 `ChatTimeline.IsSystemLine` 还有一条 `Sender == "系统"` 的兜底规则，于是被判为
  `BubbleCategory.System`，落到 `SystemLine` 模板（居中纯文本、无气泡、无独立气泡时间戳、
  不参与入场动画）；紧随其后的 `Error(RemoteCommandDisabled)` 同样是系统行，所以整组
  命令提示都以居中文字呈现，与右侧已有的暖色请求气泡不一致。
  排查顺序：① 在 Core 的 `MessageAdded` 打印 `entry.Kind` / `entry.Sender` / `entry.Text`；
  ② 调 `ChatTimeline.Classify(entry, 本地用户名)` 看返回的 `BubbleCategory`；
  ③ 检查 `BubbleSelector` 是否绑定 `ReceivedCommandTemplate`、XAML 资源键是否存在；
  ④ 检查 `IsSystemLine` 与 `KindToBrushConverter` 是否把新类别重新归类 / 吞掉配色；
  ⑤ 用 `FILESA_UI_PREVIEW=1` 亮 / 暗截图核对模板与主题色。
  修复：`HandleCommand` 改用 `ChatEntry.CommandReceived`，新增左侧冷色终端气泡；
  Core 仍保留 `Error` 事件供日志与审计，UI 用 `ChatTimeline.IsRedundantCommandNotice`
  只折叠「紧随命令气泡」的那一条重复提示，完整安全文案改由气泡底部显示。FCCH 帧在服务端
  转发时透传 `User` 作为发起方，legacy 帧无该字段时显示「其他用户」。

- **顺序与时间线**：仍按 `ObservableCollection.Add` 的追加顺序渲染，不重排；聊天气泡显示
  `HH:mm`，tooltip 为 `yyyy-MM-dd HH:mm:ss`。新消息只在用户原本停留在底部时自动滚动，
  查看历史不会被打断。
- **系统消息时间戳分组**：连续且处于同一分钟的系统行（`SystemLine` 模板覆盖的
  System / Error / CommandResult / `Sender == "系统"`）视为一组，只在**该组最后一行下方**
  显示一次 `HH:mm`；跨分钟、被聊天消息隔开、以及单独一行各自成组。规则由纯 C# 的
  `ChatTimeline.ShouldShowTimestamp` 计算，写入 `ChatLine.ShowTimestamp`（`Collapsed` 隐藏），
  追加新消息时旧组尾通过 `INotifyPropertyChanged` 就地收敛，不重排、不重绑整列。
- **单条消息入场动画**：`ListView` 默认的 `ItemContainerTransitions` / 面板
  `ChildrenTransitions` 会在集合增删时让**所有**已实现的项容器一起播放
  AddDelete / Entrance 动画，现已清空；改由 `ChatLine.PlayEntranceAnimation` 一次性标记
  **仅新追加的气泡**，在模板根的 `Loaded` / `DataContextChanged` 上播放淡入 + 轻微上滑，
  消费后立即清零，滚动、容器回收、历史消息都不会重播。系统行不参与动画。
- **自适应**：气泡列 `0.72* / 0.28*` 比例随消息区宽度伸缩，`MaxWidth=520` 限制超宽屏，
  `TextWrapping=Wrap` 处理长文本，短文本按内容收缩；窗口 <660 px 时隐藏在线用户列表、
  消息区占满，工具栏改两行并对长状态文字截断。
- **一致的对齐与间距**：统一 `Margin=0,5`、气泡 `Padding=12,8`、组内 `Spacing=4`；
  `ItemContainerStyle` 归零内边距并拉伸内容；气泡色随 `ActualThemeChanged` 刷新，
  亮 / 暗主题下与背景均有足够对比度；正文仍继承设置对话框的字体设置。
- **保持不变**：消息文本、追加顺序、颜色语义、右键复制所选消息、清空记录、系统通知、
  用户列表与全部网络行为。

**验证**：整解决方案构建 0 警告 0 错误、**40/40** 测试回归通过（T21–T27 覆盖同分钟分组、
跨分钟 / 跨日边界、聊天消息打断分组、分组动态收敛、动画标记唯一与不重播；T36–T38
覆盖 URL 切分不丢字符 / 标点截断 / CJK 终止 / 非 http 协议、CommandRequest 分类与属性、
真实 `SendCommandAsync` 只产生请求条目且无命令结果；**T39** 复现旧 `Chat + Sender == 系统`
被判为 `System` 的缺陷并验证 `CommandReceived` 分类、结构化字段、折叠规则的边界；
**T40** 用真实 FCCH `COMMAND` 帧经服务端转发，验证接收端产生 `CommandReceived` 且
`Origin = attacker`、`Target / Command` 完整、无 `CommandResult`）；用 `PrintWindow` 实机截取
亮色、暗色 1100×760 窗口，链接显示为带下划线的强调色文字，左右两个命令气泡的标题 /
等宽正文 / 完整安全提示齐全且配色可区分；`FILESA_UI_APPEND=1` 运行时自检输出
`animatedContainers=1 / pending=0 / messages=15`，证明追加一条消息时只有那一条气泡播放动画。

可视化自检开关（默认关闭，仅调试用，不联网、不启动发现）：

```bash
FILESA_UI_PREVIEW=1 FILESA_UI_THEME=Light FILESA_UI_SIZE=600x640 FilesaCOChat.WinUI.exe
```

`FILESA_UI_PREVIEW=1` 注入示例时间线（含同分钟连续系统消息、一条带两个 URL 的消息、
一条发出的命令行请求和一条收到的命令帧，便于核对时间戳分组 / 链接 / 两种命令气泡）；
`FILESA_UI_THEME` 为 `Light` / `Dark`；`FILESA_UI_SIZE` 指定窗口尺寸；
`FILESA_UI_APPEND=1` 在预览加载后再追加一条消息以实测单条动画；布局尺寸 / 分组标记写到
`%TEMP%/fc-ui-metrics.txt`，动画触发次数写到 `%TEMP%/fc-ui-animation.txt`。

## 用户列表操作、设置分类与主题

**每用户操作改为右键菜单**（`UserList.ContextFlyout`）：

- 顶栏只保留「设置」，「命令行」「发送文件」两个按钮已移除，改由用户列表项右键弹出，
  菜单项带图标。
- 右键先命中行再选中（`RightTapped` 沿视觉树找 `ListViewItem`），因此菜单永远作用于
  鼠标下的用户，而不是上一次选中项；`Opening` 时复核目标，未选中 / 已离线 / 目标是自己
  则两项 `IsEnabled=false`。
- 「命令行」对话框把右键选中的用户预选进「目标用户」，房间名单变化时回退到手动选择；
  发送仍走 `ChatApplication.SendCommandAsync`，D1 的 `CommandPolicy` 依旧拒绝执行。
- 「发送文件」沿用原有选择器 → 握手 → 4 KB 分块 → SHA-256 校验，仅入口变化。

**设置改为独立窗口**（`SettingsWindow.xaml`）：左侧 5 项导航（外观 / 字体 / 通知 / 安全 /
关于）+ 右侧可滚动内容面板。登录页齿轮与聊天页「设置」打开的是同一个窗口，已打开时再次
点击只做激活，不会叠加实例；关闭聊天主窗口时一并关闭。窗口默认 880×660 DIP（经
`GetDpiForWindow` 换算为物理像素）、居中、可缩放，内容超出可滚动，因此不再受登录窗口
尺寸限制，也不会再出现对话框右侧被裁切的问题。窗口标题栏用 `DwmSetWindowAttribute`
（Win11 属性 20 / Win10 1809+ 属性 19）跟随应用深浅色。

**登录页窗口尺寸**（`ApplyLoginWindowSize` / `ApplyChatWindowSize`）：

- 登录页窗口宽度固定为 420 DIP；**高度在启动时测量一次后冻结**（`_loginWindowHeight`
  缓存），因此「搜索LAN」与「连接指定频道」两个选项卡来回切换时窗口尺寸不变
  （100% DPI 实测均为 436×636 物理像素）。
- **卡片宽度同样固定**：`LoginPanel` 是 `HorizontalAlignment="Center"` 的 `Grid`，若不给
  `Width`，它会「收缩包裹」到当前可见子项的最大自然宽度。定向模式里
  `ChannelAddressBox` 的长占位文字「例如 192.168.1.8 或 192.168.1.8:11451」、
  端口提示以及更长的状态文字把卡片撑到 300 DIP，而 LAN 模式只有 240 DIP；所有
  `HorizontalAlignment="Stretch"` 的 `TextBox` 跟着父级变化，于是实测输入框分别是
  **252 px（定向）与 192 px（LAN）**，且都以窗口中线 218 为中心。构造函数在执行任何
  测量前把 `LoginPanel.Width` 固定为 `LoginCardWidth = 300` DIP（= 252 内容 +
  左右各 24 padding，正是定向模式原本的正确宽度），并给两个字段组与四个 `TextBox`
  显式写上 `HorizontalAlignment="Stretch"`；长状态文字改为换行而不是撑宽卡片。
  修复后两种模式（含来回切换）输入框恒为 `x=92..343`、宽 252 px。
- 两种模式的字段组放在同一个 `Grid LoginFieldsHost` 单元中，`FreezeLoginFieldSlot()`
  在显示前按**真实内容宽度** `LoginContentWidth = 252` DIP 分别测量两个组，取较高者
  写入 `MinHeight`（下限 210 DIP，防止测量异常导致定向模式字段被裁切）；`LanFields`
  在该固定槽位内垂直居中，登录按钮 / 进度条 / 状态文字在两种模式下保持同一 Y 位置。
  测量宽度必须与实际卡片宽度一致，否则在 252 DIP 下会换行的提示会被按 372 DIP
  低估高度而裁切。`ApplyLoginMode` 已删除原先的重新测高与 resize。
- 登录期间窗口不可缩放 / 最大化；`LoggedIn` 变为 true 后恢复可缩放并切到聊天尺寸
  （1100×760，按工作区减去边距收敛，避免小屏 / 高缩放下超出屏幕）。
- 所有尺寸都做 DPI 换算，`LoginStatus` 允许换行，避免长状态文字被裁切。

**同类问题的排查顺序**（本次定位输入框宽度不一致时实际采用的步骤）：
① 分别截取两个选项卡的**完整窗口**，用像素扫描找出输入框背景矩形的左右边界
（本次为 LAN `122..313`、定向 `92..343`，中心都是 218 → 说明不是某个控件的对齐或
`Margin` 偏了，而是整张卡片在伸缩）；② 检查登录卡片自身的对齐与宽度约束，确认它是
`Center` + 无 `Width` 的收缩包裹容器；③ 找出各模式下**可见**子项里最宽的那个
（长 `PlaceholderText` / 提示文字 / 状态文字都会参与 `DesiredSize` 竞争）；
④ 检查 `TextBox` 是否 `Stretch` —— 它不会自己定宽，只会跟随父级；
⑤ 固定父级宽度后再实机像素复测，并额外验证「切过去再切回来」不产生漂移。

**主题切换与持久化**：

- 外观页 `ComboBox` 提供「跟随系统 / 浅色 / 深色」→ `RootGrid.RequestedTheme`
  （`ElementTheme.Default` 即跟随系统），聊天气泡配色随 `ActualThemeChanged` 重新计算，
  设置窗口与标题栏同步切换。
- 选择在下拉关闭后提交（在下拉打开时翻转主题会重建 ComboBox 模板），立即生效并写入
  `%LOCALAPPDATA%\FilesaCOChat\settings.json`，下次启动由 `SettingsStore.Load` 恢复。
- 一并持久化字体（族 / 字号 / 粗体）与通知开关；`PreSharedKey` **不落盘**，与 5.1 一样仅
  本次运行有效；文件损坏或数值越界回退默认值，读写失败不影响聊天。

**系统级通知**（`SystemNotifier`）：使用 Windows App SDK 的 `AppNotificationManager`。
启动时 `Register()`（未打包应用会在 HKCU 登记 AUMID 与激活用 COM 服务器），**仅在主窗口
最小化时**（`IsIconic`）把他人消息用 `AppNotificationBuilder` 构造成 toast 投递到
**Windows 通知中心**；点击 toast 会把主窗口还原并置前。原来应用内右下角的 `InfoBar` 提示栈
（`NotificationHost`）已整体删除——「通知」现在只指系统通知。开关关闭后不再发送 toast，聊天不受影响；设置页同时显示 Windows
通知权限状态（`AppNotificationManager.Setting`），并提供「发送测试通知」便于自检。

**登录页设置入口**：登录面板右上角齿轮，与聊天页「设置」打开同一个独立窗口，可在入群前
调整主题与字体并即时预览。

**窗口图标**：`AppWindow.SetIcon("Assets\icon.ico")`，设置窗口同样设置；标题栏 / 任务栏与
exe 内嵌图标一致（`ApplicationIcon` 仍保留）。

**窄屏**：在线用户列表是上述操作的唯一入口，因此保留到 660 px；窗口窄于 660 px 时列表隐藏
（与改版前一致），此时无法发起按用户操作，聊天与文件接收不受影响。

**验证**：整解决方案 **0 警告 0 错误**、**35/35** 用例通过（T28–T31 覆盖默认值、主题与界面
偏好往返、PSK 不落盘、损坏文件回退）；实机截图（`.workbuddy/analysis/ui-v2-*.png`）：

- 登录窗口 **436×459**，内容完整、无大块空白，深色主题下标题栏同步变深；
- 独立设置窗口 **896×699**，五个分类齐全、右侧内容无裁切，字体页与通知页排版正常；
- 通知页显示「Windows 通知权限：已允许」，`FILESA_UI_TOAST=1` 触发后系统通知中心实际
  弹出 toast（标题 `Filesa COChat` + 正文），证明已从应用内提示改为系统级通知；
- 设置窗口切换「深色」后 `settings.json` 变为 `"Theme": "Dark"`，窗口即时变深，重启
  （不设任何主题环境变量）仍为深色；通知开关关闭后 `"NotificationsEnabled": false`；
- 聊天预览 1100×760 布局、气泡与时间戳分组无回归；`WM_GETICON` 仍返回非零句柄。

## 消息提示、发送按钮与滚动跟随

**`InfoBar` 只用于「发现新版本」**：聊天页顶部保留状态文字行（`StatusLabel`，显示
`StatusChanged.Detail`），房间名下方另有一个默认关闭的 `InfoBar`（`NoticeBar`）。它**只在
`UpdateInfo.HasUpdate == true` 时打开**，标题「发现新版本 x.y.z」、正文取 `info.Detail`
（为空回退 `UiText.UpdateAvailable`）、附动作按钮「查看更新」（默认浏览器打开
`UpdateInfo.ReleaseUrl`，为空回退 `ProtocolOptions.ReleasesPageUrl`）。没有新版本时不显示，
任何状态文案（服务器 IP、扫描进度、已连接……）都不会再打开它——这正是上一版把它当成状态栏
后出现的「IP 被当成更新提示」的问题。同一个版本只提示一次；用户手动关闭后不再重开。
`MainViewModel.UpdateNoticed` 只在检测到更新时触发，且不受系统通知开关影响（InfoBar 是
应用内提示，不是 toast）。

**发送按钮改为图标**：`SendButton` 的内容由文字「发送」换成 `FontIcon Glyph="&#xE724;"`
（Segoe Fluent Icons 的 Send 字形，`SymbolThemeFontFamily`），`ToolTipService.ToolTip` 与
`AutomationProperties.Name` 仍为「发送」，键盘回车与点击行为不变。

**系统通知只在最小化时发出**（`IsWindowMinimized`）：`NotificationRequested` 先经
`user32!IsIconic` 判断主窗口是否最小化，只有最小化时才投递 toast；窗口在屏幕上时新消息已经
出现在列表里，不再弹通知。

**设置窗口强制位于主窗口之上**（`SettingsWindow.AttachToOwner`）：构造时用
`SetWindowLongPtrW(GWLP_HWNDPARENT)` 把主窗口设为其 owner，成为真正的 owned window——始终
浮在主窗口之上、随主窗口最小化，但不会被强行置顶到其他应用之上（Alt+Tab 行为正常）。
单实例复用，重复点击「设置」只会把已有窗口提到前面。

**新消息滚动跟随**（`OnMessagesChanged` / `ScrollToLatest` / `SnapToBottom`）：
`ScrollViewer.ViewChanged` 只在**非中间帧**时更新 `_stickToBottom`（`ScrollableHeight -
VerticalOffset <= 24` DIP），因此追加消息导致的滚动范围增长不会被误判为用户上滚：

- 处于底部时收到消息 → 立即滚到 `ScrollableHeight`（`disableAnimation: true`）；
- 用户已上滚查看历史 → 不触发任何滚动，新消息静默进入列表；
- 用户自己发出的消息总能拉回底部（发送是主动行为，避免消息发出去却看不到）；
- 登录成功、列表首次加载、窗口重新激活（从任务栏还原）时，若此前在底部则重新贴底；
- `Reset`（清空/重载）同样回到底部。

修复「消息列表偶尔卡回顶部」的三个改动：

1. **不再调用 `ListView.ScrollIntoView`**：它会让虚拟化 `ItemsStackPanel` 重新锚定，是列表
   跳回顶部的直接来源；改为直接对内部 `ScrollViewer` 调 `ChangeView`。
2. **`ScrollableHeight <= 0` 时不再滚动**：布局尚未测量时该值为 0，滚到 0 就是「跳到顶部」。
3. **先 `UpdateLayout()` 再取 extent，并在下一个调度周期校正一次**：低优先级回调可能早于
   渲染帧执行，此时 extent 还是旧值，会停在新消息上方；一次性重试只在「没到底」时触发，
   不会和用户抢滚动条。

## 登录页双模式：搜索LAN / 连接指定频道

用户名输入框上方新增 `SelectorBar` 选项卡（默认选中「搜索LAN」）：

| 模式 | 显示字段 | 点击按钮后的行为 |
| --- | --- | --- |
| 搜索LAN | 原有的「用户名」输入框 | `_vm.LoginAsync` → `ChatApplication.LoginAsync`：广播/扫描发现 → 加入已有房间，找不到则创建房间。**布局与行为与旧版完全一致** |
| 连接指定频道 | 「地址:端口」+「用户名」两个输入框 | `_vm.ConnectChannelAsync` → `ChatApplication.ConnectChannelAsync`：跳过发现、不创建房间，直接向该地址发起 TCP JOIN |

- 界面状态切换由 `ApplyLoginMode()` 统一处理：字段组 `Visibility`、按钮文案（登录 / 连接）、
  状态提示、以及「窗口按登录卡片内容测量」的高度重算（LAN 436×497，定向 436×594，
  切回 LAN 会缩回）。用户名在两种模式间切换时会被带到另一侧（不覆盖已填内容），
  连接进行中选项卡暂时禁用。
- **端口缺省**：`DirectEndpoint.TryParse` 接受 `host`、`host:`（都视为未填端口）、
  `host:port`、`[IPv6]:port` 与裸 IPv6；缺省时回落到 `ProtocolOptions.DefaultChatPort`
  （11451）。非法端口/空地址会弹对话框说明原因，不会发起连接。
- 解析出的端口写进 `ProtocolOptions.ChatPort`，因此 JOIN、后续迁移重连都使用同一端口；
  切回「搜索LAN」时 `LoginAsync` 把端口恢复为构造时配置的值，避免定向连接污染发现流程。
  PSK/FCCH 策略与 LAN 路径相同：先做能力探测，启用 PSK 时拒绝明文房间（T34、T35 覆盖）。
- 定向连接同样会触发 `AppStatus.Connected` → 登录卡片切换为聊天界面；失败时按钮重新可用，
  状态行显示「连接失败: 地址:端口」。

## 保持不变的契约

- **端口**：TCP 11451 聊天/控制、UDP 11452 广播发现、TCP 11450 文件传输（新增 UDP 11453
  仅用于新版之间的能力探测，旧版不感知）。
- **报文**（默认模式逐字节兼容，测试 T01 冻结）：`COCHAT_DISCOVERY:11451` /
  `COCHAT_RESPONSE:11451`、`JOIN:<用户>`、`USERNAME_EXISTS`、`USERLIST:<a,b,c>`、
  `USERLIST_REQUEST`、`<用户>:<文本>`、`MIGRATE:<ip>`、`PROMOTE:<ip>`、
  `COMMAND:<目标>:<命令>`。
- **文件握手**：`<文件名>|<大小>` → `ACCEPT` / `REJECT` → 4096 B 分块 → `SEND_HASH` →
  SHA-256 比对；接收目录 `files/`，重名 `name_1.ext`。
- **常量**：版本 `7.0`（见 `Core/AppInfo.cs`；仅用于更新检查的 User-Agent 与 tag 比较，
  **不进入任何报文**，因此不影响与 5.1 的互通）、广播等待 3 s、/24 扫描 50 线程 / 0.1 s、
  `recv(1024)` 上限、启动 5 s 后自动检查更新、GitHub releases 接口与发布页 URL。
- **用户可见行为**：登录 → 搜索/创建聊天室 → 消息收发 → 在线用户 → 发送文件（接受/拒绝
  确认）→ 命令行 → 设置（字体/字号/加粗/通知）→ 检查更新 → 关闭时服务器迁移；中文文案
  与原字节码逐字一致；图标沿用原 exe 素材。

## 缺陷修复与验证

缺陷清单来源：`docs/FilesaCOChat_逆向分析报告.html` **§7 开发视角：缺陷与风险清单**
（9 项）。GitHub Issues 只有 1 条问候类记录，不存在真正的缺陷跟踪。

| 编号 | 缺陷 | 修复 | 验证 |
|---|---|---|---|
| D1 | 未认证远程命令执行 | `CommandPolicy` 恒不授权，全解决方案无 Process/shell API | T05、T15 |
| D2 | 客户端消息不互通 | 服务端 `BroadcastChatAsync` 显式广播 | T09（真实双客户端） |
| D3 | 无消息边界 / 协议注入 | FCCH 长度帧 + 静默窗合并 + 严格解析 + 保留前缀阻断 | T01、T02、T04、T07b、T10、T12 |
| D4 | 明文、无身份绑定 | 会话 ID + 序列号 + HMAC-SHA256 + 重放保护（PSK 可选） | T02、T03、T10、T17、T18 |
| D5 | 文件路径 / 大小不可信 | basename 白名单 + 设备名/ADS 拒绝 + 2 GiB 上限 + 哈希失败删文件 | T06、T13、T14 |
| D6 | 迁移无仲裁 | 确定性选举 + 任期幂等 + ACK + 超时重选 | T07、T16 |
| D7 | 依赖冗余 | 核心层零 NuGet，不再打包 Python/Tk/cryptography/PIL | 依赖静态核对 + T06/T13/T14 |
| D8 | 更新链路不校验 | 强制 HTTPS、校验 tag_name 形状与发布页域名 | T08 |
| D9 | 线程安全边界 | 所有事件单点经 DispatcherQueue 编组，状态加锁 | T19 |

**验证结果**：整解决方案构建 0 警告 0 错误；`35/35` 用例通过（原 20 项协议 / 安全 / 网络 +
显示修复 T21–T27 + 设置与主题 T28–T31 + 定向连接 T32–T35）；Debug 与 Release 两次启动
冒烟均得到主窗口 `Filesa COChat 7.0 · WinUI 3`（Responding=True，无运行时弹窗）；
Release `dotnet publish` 成功、分发 zip 266 项校验通过；气泡界面在亮 / 暗主题、
1200×820 与 600×640 两种尺寸、Debug 与 Release 发布目录均实机截图核验通过；
用户列表右键菜单、侧边栏设置、登录页设置入口与主题持久化同样实机截图核验；
登录页双选项卡在 Debug / Release 均实机切换核对（`.workbuddy/analysis/ui-v7-login-*.png`），
含端口省略时状态行显示 `连接失败: 127.0.0.1:11451`。

## 需要注意的行为变更与限制

- `COMMAND:` 菜单入口与线格式保留，但**永不执行命令**（D1 的必要代价）。
- 默认配置（PSK 为空）与原版 5.1 完全互通；**启用预共享密钥后会拒绝旧版明文节点**（T17）。
- FCCH 帧 / HMAC / 任期仲裁仅在全是新版节点的房间内完整生效。
- 仅 Windows 10 1809+，默认 x64；仅实机验证 x64。
- 框架依赖部署，目标机器需 .NET 8 Desktop Runtime（或 `--self-contained true` 发布）。
- 无 Authenticode 签名；更新仍只提示不自动下载，未校验发布物签名。
- Tk 弹窗淡入淡出动画未 1:1 复刻，改为 WinUI ContentDialog + 应用内通知。
- 未做真实多机局域网联调、抓包比对、长时间压力/弱网测试；仅实机验证 win-x64。
- 单文件 exe 不可行（见上）；`dist/_experiments/` 里的失败产物不要运行。

## 参考来源

- `docs/FilesaCOChat_逆向分析报告.html` — 缺陷清单（§7）、协议（§4）、架构（§3）、依赖（§6）
- `docs/FilesaCOChat_协议参考.md`、`docs/protocol_reference.py` — 报文常量与帧格式骨架
- `.workbuddy/analysis/` — `main.pyc`、全量反汇编、按函数常量表、CArchive/PYZ 清单、REA 证据包
- `github.com/li2012China/Filesa-COChat` — 上游项目（MIT；main 分支无 .py 源码，仅网页与资源）
