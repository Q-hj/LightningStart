# 闪电启动

一个轻量的 Windows 托盘快捷启动器，免安装、无需管理员权限。拥有两种形态：

- **配置窗口**：管理要出现在快捷菜单中的程序（添加、排序、编辑参数/图标）。
- **托盘面板**：常驻系统托盘，悬浮或单击图标弹出小面板，单击程序图标即可启动。

> 显示名：**闪电启动**；发布的可执行文件：`LightningStart.exe`。
> 源码工程、命名空间与解决方案仍沿用 `QuickStart`，这只影响开发目录，不影响发布身份。

## 功能

- 主窗口采用左侧导航，分为“应用列表”和“系统设置”两个页面
- 拖入 `exe / lnk / 文件夹 / url` 文件即可添加，支持多选
- 程序名称、启动参数、工作目录、自定义图标、管理员身份运行
- 列表支持拖动排序（带拖拽预览与插入指示）、上移/下移、逐项隐藏与删除
- 隐藏项保留在列表中，但不会出现在托盘面板
- 启动时默认只驻留托盘，不弹出配置窗口
- 托盘面板支持悬浮打开或单击打开
- 面板跟随鼠标位置弹出：底部任务栏时在光标上方（空间不足翻到下方），
  左侧/右侧任务栏时出现在图标侧方并垂直居中对齐鼠标
- 拖动面板顶部 Header 可临时调整弹出位置，仅在本次运行期间有效
- 鼠标移出面板约 150ms 后自动隐藏
- Shell 原生大图标提取 + 本地缓存，二次启动秒开
- 单实例：重复启动会唤醒已运行实例
- 关闭主窗口仅隐藏到托盘，托盘菜单可退出
- 开机自启（可选，写入当前用户 Run 键，无需管理员）
- 配置实时保存为 JSON
- 首次运行显示一次托盘气泡提示，不打扰使用
- 未处理异常写入本地日志，便于排查

## 环境要求

- Windows 10 / 11（推荐 x64）
- 开发：.NET 10 SDK
- 运行：
  - 使用自包含发布包：无需安装 .NET 运行时
  - 使用框架依赖发布包：需要 .NET 10 Desktop Runtime

## 构建与运行

```bash
dotnet build QuickStart.sln -c Debug
dotnet run --project src/QuickStart
```

也可以直接运行：

```text
src/QuickStart/bin/Debug/net10.0-windows/LightningStart.exe
```

## 发布

推荐使用发布脚本，自动完成自包含单文件打包、文档复制、去 PDB、压缩和 SHA256：

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
```

产物：

```text
dist\LightningStart-<version>-win-x64\
dist\LightningStart-<version>-win-x64.zip
dist\LightningStart-<version>-win-x64.zip.sha256
```

框架依赖发布（体积更小，目标机器需安装 .NET 10 Desktop Runtime）：

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -FrameworkDependent
```

脚本默认使用 `win-x64`，可通过 `-Runtime win-arm64` 指定其他 RID。

手动发布命令（PowerShell）：

```powershell
dotnet publish src/QuickStart/QuickStart.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o dist\manual
```

发布前请先退出正在运行的 `LightningStart.exe` / 旧版 `QuickStart.exe`，否则文件可能被占用。

## 安装与升级

### 安装

1. 解压发布包到任意目录（建议固定目录，不要放在临时目录）。
2. 双击 `LightningStart.exe`。程序默认只驻留托盘，不打开主窗口。
3. 在托盘图标上右键 →“打开设置”，或在快捷面板中点击“打开主视图”。
4. 如需要开机自启，在“系统设置 → 行为”中勾选“开机自动启动”。

程序没有安装程序；除配置、缓存、日志和可选的当前用户 Run 注册表值外，不会向系统目录写入内容。
如需开始菜单快捷方式，可自行创建指向 exe 的快捷方式。

### 升级

1. 托盘右键 →“退出 闪电启动”。
2. 用新版本文件替换旧版本 exe（或解压到新目录）。
3. 启动新版本。配置会自动沿用；旧的 `QuickStart` 配置会在首次启动时迁移到 `LightningStart` 目录。

### 从旧版 QuickStart 迁移

新版把发布身份统一为 `LightningStart`，并自动处理旧数据：

- 若 `%AppData%\LightningStart\config.json` 不存在，而 `%AppData%\QuickStart\config.json` 存在，
  首次启动会把旧配置复制到新目录；旧文件保留作备份。
- 旧的 `QuickStart` 开机自启注册表值会被移除；如果配置中开启了自启，会改为写入
  `LightningStart` 值，并指向当前 exe。
- 旧图标缓存不会复用，新版本会重新提取一次图标。
- 新版本与旧版使用不同的单实例名，升级前请退出旧版进程，避免同时驻留托盘。
- 成功迁移的旧用户不会被当作首次运行，因此不会弹出首启气泡。

### 卸载

1. 托盘右键 →“退出 闪电启动”。
2. 删除程序目录。
3. 可选：删除配置、图标缓存和日志：
   - `%AppData%\LightningStart\`
   - `%LocalAppData%\LightningStart\`
4. 若仍残留开机自启项，可在“Windows 设置 → 应用 → 启动”中关闭，或删除注册表
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `LightningStart` 值。

## 使用说明

1. 启动后默认只驻留托盘，不弹出主界面；重复运行也不会打开窗口。打开配置窗口的方式：托盘右键 →“打开设置”、面板右上角“打开主视图”、或使用 `--settings` 参数启动。
2. 首次启动会创建示例条目（文件资源管理器、记事本、PowerShell、Windows 终端，按系统中实际存在的程序创建）。
3. 添加程序：打开配置窗口后点击“添加程序”，或直接把文件拖进窗口。
4. 选中条目后可在右侧修改名称、参数、工作目录、图标等，修改会自动保存。
   拖动列表项可调整托盘面板中的顺序；每行右侧的图标按钮可单独隐藏/删除该项。
   隐藏项仍保留在列表中，但不会出现在托盘面板。
5. 系统设置页可调整面板图标大小、列数、触发方式、开机自启，并可打开配置目录或退出程序。
6. 托盘图标可能被 Windows 11 收进“隐藏的图标”溢出区。若看不到，请到 **设置 → 个性化 → 任务栏 → 其他系统托盘图标** 中打开闪电启动。
7. 首次运行会弹出一次托盘气泡说明。若气泡未出现，通常是专注助手或通知设置拦截，可按上面的方式手动打开主视图。
8. 部分第三方/侧边任务栏只发送传统鼠标悬浮消息，程序已做兼容；若悬浮仍不生效，可改用单击打开或将该图标固定到任务栏。
9. 默认悬浮打开面板；也可以在“系统设置 → 行为”中切换为单击打开。
   面板顶部的 Header 可以拖动，调整后的位置仅在本次运行有效，重启后恢复跟随托盘鼠标。
10. 退出程序：托盘右键菜单 →“退出 闪电启动”，或系统设置页底部的“退出 闪电启动”。关闭主窗口只会隐藏到托盘。

## 配置文件与日志

- 配置：`%AppData%\LightningStart\config.json`
- 图标缓存：`%LocalAppData%\LightningStart\icons\`
- 崩溃日志：`%LocalAppData%\LightningStart\logs\crash-YYYYMMDD.log`
- 旧版配置备份：`%AppData%\QuickStart\config.json`
- 配置损坏时会自动备份为 `config.json.bad` 并回退到默认配置

日志只在发生未处理异常时写入，不会持续增长。

## 常见问题

**托盘里找不到图标**
Windows 11 默认会折叠新图标。打开“设置 → 个性化 → 任务栏 → 其他系统托盘图标”，把“闪电启动”设为显示。资源管理器重启后程序会自动重新添加图标。

**首次运行没有看到气泡**
气泡可能被专注助手、通知权限或“勿扰模式”拦截。它只用于提示，不影响使用；请直接通过托盘右键菜单打开主视图。

**悬浮面板不弹出**
确认“系统设置 → 行为 → 触发方式”为“悬浮打开”；某些第三方任务栏不发送悬浮消息，可改用单击打开。

**开机没有自动启动**
检查“系统设置 → 行为”中的开关，以及 Windows“设置 → 应用 → 启动”是否禁用了本程序。更换 exe 路径后重新启动一次程序，自启命令会自动更新到新路径。

**升级后像是配置丢了**
新版使用 `%AppData%\LightningStart\config.json`。如果首次启动时新配置已存在，迁移不会覆盖它；请检查是否手动创建过该文件。旧文件仍在 `%AppData%\QuickStart\config.json` 作为备份。

**程序异常退出**
查看 `%LocalAppData%\LightningStart\logs\` 下最新的 `crash-*.log`，其中包含版本、时间和异常堆栈。

**SmartScreen 提示“Windows 已保护你的电脑”**
发布包默认未做代码签名。确认下载来源可信后，点击“更多信息 → 仍要运行”。如用于组织内分发，建议后续加入 Authenticode 签名。

## 技术实现

| 模块     | 方案                                                                                                                                                      |
| -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| UI       | WPF / .NET 10（`net10.0-windows`，PerMonitorV2 DPI）                                                                                                      |
| 托盘图标 | `Shell_NotifyIcon` + `NOTIFYICON_VERSION_4`，处理 `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` / `NIN_SELECT`，并兼容传统 `WM_MOUSEMOVE` / `WM_MOUSELEAVE` 悬浮通知 |
| 悬浮面板 | 无边框 + 透明分层窗口 + `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW`，不抢焦点                                                                                 |
| 面板定位 | `GetMonitorInfo` + `GetDpiForMonitor`，按物理像素 `SetWindowPos` 贴边                                                                                     |
| 自动隐藏 | 80ms 轮询光标位置，移出面板/托盘热区 150ms 后隐藏                                                                                                         |
| 图标提取 | `IShellItemImageFactory`（128px）+ `SHGetFileInfo` 兜底，PNG 缓存                                                                                         |
| 图标线程 | 专用 STA 线程串行提取，规避并发 MTA 调用出现的 `E_PENDING`                                                                                                |
| 启动程序 | `Process.Start` + `UseShellExecute`，可选 `runas` 提权                                                                                                    |
| 单实例   | 命名 `Mutex` + 命名 `EventWaitHandle`（`LightningStart` 前缀）                                                                                            |
| 配置     | `System.Text.Json`，临时文件 + 覆盖写                                                                                                                     |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `LightningStart`，启动参数 `--tray`                                                             |
| 首启提示 | `Shell_NotifyIcon(NIM_MODIFY, NIF_INFO)` 托盘气泡                                                                                                        |
| 异常日志 | `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`                                                 |

## 开发参数

| 参数              | 用途                                               |
| ----------------- | -------------------------------------------------- |
| `--tray`          | 开机自启参数：仅驻留托盘（普通启动已是该行为）     |
| `--settings`      | 启动后直接打开配置窗口；若已有实例则唤醒其设置窗口 |
| `--flyout`        | 调试用：启动后直接显示快捷面板，并禁用自动隐藏     |
| `--tray --flyout` | 只显示快捷面板，方便截图/调试                      |

## 目录结构

```text
QuickStart.sln
src/QuickStart/
  App.xaml / App.xaml.cs          应用入口、单实例、托盘与面板协调
  Views/MainWindow.*              主窗口（应用列表 + 系统设置）
  Views/FlyoutWindow.*            托盘悬浮面板
  ViewModels/MainViewModel.cs     列表、编辑、保存逻辑
  Services/TrayIconService.cs     Shell_NotifyIcon 封装、右键菜单、首启气泡
  Services/IconLoader.cs          图标提取与缓存（STA 工作线程）
  Services/AppLauncher.cs         启动程序
  Services/ConfigStore.cs         JSON 配置读写与默认条目
  Services/AutoStart.cs           开机自启注册表
  Services/CrashLogger.cs         未处理异常日志
  Infrastructure/AppPaths.cs      配置、缓存、日志路径与旧配置迁移
  Interop/                        Win32 / Shell P/Invoke
  Styles/Theme.xaml               颜色、按钮、输入框、Tab 样式
tools/generate-icon.ps1           重新生成 Assets/app.ico
tools/publish.ps1                 发布自包含单文件 ZIP 与 SHA256
LICENSE                           MIT 许可证
```

## 已知限制

- Windows 11 不再支持第三方任务栏 Deskband，因此“任务栏形态”通过托盘悬浮面板实现。
- `.lnk` 的启动参数可能被系统忽略；需要传参时建议直接指向 `exe`。
- 面板当前为纯鼠标交互，未接入键盘 Esc/方向键/搜索框（避免抢焦点）。
- UWP/商店应用可通过 `shell:AppsFolder\<AUMID>` 作为目标添加，属于实验性用法。
- 未签名的 exe 在分发后可能触发 SmartScreen 提示。
- 首次运行提示依赖系统通知；被专注助手拦截时不会重复弹出。

## 许可证

本项目使用 [MIT License](LICENSE)。

## 仓库

<https://github.com/Q-hj/quick-star>
