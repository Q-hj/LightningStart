# QuickStart 快捷启动

一个轻量的 Windows 桌面快捷启动器，拥有两种形态：

- **配置窗口**：管理要出现在快捷菜单中的程序（添加、排序、编辑参数/图标）。
- **托盘面板**：常驻系统托盘，悬浮或单击图标弹出小面板，单击程序图标即可启动。

## 功能

- 拖入 `exe / lnk / 文件夹 / url` 文件即可添加，支持多选
- 程序名称、启动参数、工作目录、自定义图标、管理员身份运行
- 列表排序（上移 / 下移）与删除
- 启动时默认只驻留托盘，不弹出配置窗口
- 托盘面板支持悬浮打开或单击打开
- 面板自动贴边定位，支持多显示器与混合 DPI
- 鼠标移出面板约 350ms 后自动隐藏
- Shell 原生大图标提取 + 本地缓存，二次启动秒开
- 单实例：重复启动会唤醒已运行实例
- 关闭主窗口仅隐藏到托盘，托盘菜单可退出
- 开机自启（可选，写入当前用户 Run 键，无需管理员）
- 配置实时保存为 JSON

## 环境要求

- Windows 10 / 11（x64）
- 开发：.NET 10 SDK
- 运行：.NET 10 Desktop Runtime（如果使用 framework-dependent 发布）

## 构建与运行

```bash
dotnet build QuickStart.sln -c Debug
dotnet run --project src/QuickStart
```

也可以直接运行：

```text
src/QuickStart/bin/Debug/net10.0-windows/QuickStart.exe
```

## 发布

框架依赖单文件（体积小，目标机器需安装 .NET 10 Desktop Runtime）：

```bash
dotnet publish src/QuickStart/QuickStart.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

自包含单文件（无需运行时，体积约 70MB）：

```bash
dotnet publish src/QuickStart/QuickStart.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

产物位于 `src/QuickStart/bin/Release/net10.0-windows/win-x64/publish/`。

## 使用说明

1. 启动后默认只驻留托盘，不弹出主界面。打开配置窗口的方式：托盘右键 →“打开设置”、面板右上角“设置”、再次运行 `QuickStart.exe`，或使用 `--settings` 参数启动。
2. 首次启动会创建 4 个示例条目：文件资源管理器、记事本、PowerShell、Windows 终端。
3. 添加程序：打开配置窗口后点击“添加程序”，或直接把文件拖进窗口。
4. 选中条目后可在右侧修改名称、参数、工作目录、图标等，修改会自动保存。
5. 托盘图标可能被 Windows 11 收进“隐藏的图标”溢出区。若看不到，请到 **设置 → 个性化 → 任务栏 → 其他系统托盘图标** 中打开 QuickStart。
6. 默认悬浮打开面板；也可以在“外观与行为”中切换为单击打开。
7. 退出程序：托盘右键菜单 →“退出 QuickStart”，或设置页底部的“退出 QuickStart”。关闭主窗口只会隐藏到托盘。

## 配置文件

- 配置：`%AppData%\QuickStart\config.json`
- 图标缓存：`%LocalAppData%\QuickStart\icons\`
- 配置损坏时会自动备份为 `config.json.bad` 并回退到默认配置

## 技术实现

| 模块 | 方案 |
| --- | --- |
| UI | WPF / .NET 10（`net10.0-windows`，PerMonitorV2 DPI） |
| 托盘图标 | `Shell_NotifyIcon` + `NOTIFYICON_VERSION_4`，处理 `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` / `NIN_SELECT` |
| 悬浮面板 | 无边框 + 透明分层窗口 + `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW`，不抢焦点 |
| 面板定位 | `GetMonitorInfo` + `GetDpiForMonitor`，按物理像素 `SetWindowPos` 贴边 |
| 自动隐藏 | 120ms 轮询光标位置，移出面板/托盘热区 350ms 后隐藏 |
| 图标提取 | `IShellItemImageFactory`（128px）+ `SHGetFileInfo` 兜底，PNG 缓存 |
| 图标线程 | 专用 STA 线程串行提取，规避并发 MTA 调用出现的 `E_PENDING` |
| 启动程序 | `Process.Start` + `UseShellExecute`，可选 `runas` 提权 |
| 单实例 | 命名 `Mutex` + 命名 `EventWaitHandle` |
| 配置 | `System.Text.Json`，临时文件 + 覆盖写 |
| 开机自启 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，启动参数 `--tray` |

## 开发参数

| 参数 | 用途 |
| --- | --- |
| `--tray` | 开机自启参数：仅驻留托盘；若已有实例则不唤醒设置窗口 |
| `--settings` | 启动后直接打开配置窗口 |
| `--flyout` | 调试用：启动后直接显示快捷面板，并禁用自动隐藏 |
| `--tray --flyout` | 只显示快捷面板，方便截图/调试 |

## 目录结构

```text
QuickStart.sln
src/QuickStart/
  App.xaml / App.xaml.cs          应用入口、单实例、托盘与面板协调
  Views/MainWindow.*              配置窗口
  Views/FlyoutWindow.*            托盘悬浮面板
  ViewModels/MainViewModel.cs     列表、编辑、保存逻辑
  Services/TrayIconService.cs     Shell_NotifyIcon 封装、右键菜单
  Services/IconLoader.cs          图标提取与缓存（STA 工作线程）
  Services/AppLauncher.cs         启动程序
  Services/ConfigStore.cs         JSON 配置读写与默认条目
  Services/AutoStart.cs           开机自启注册表
  Interop/                        Win32 / Shell P/Invoke
  Styles/Theme.xaml               颜色、按钮、输入框、Tab 样式
tools/generate-icon.ps1           重新生成 Assets/app.ico
```

## 已知限制

- Windows 11 不再支持第三方任务栏 Deskband，因此“任务栏形态”通过托盘悬浮面板实现。
- `.lnk` 的启动参数可能被系统忽略；需要传参时建议直接指向 `exe`。
- 面板当前为纯鼠标交互，未接入键盘 Esc/方向键/搜索框（避免抢焦点）。
- UWP/商店应用可通过 `shell:AppsFolder\<AUMID>` 作为目标添加，属于实验性用法。
- 未签名的 exe 在分发后可能触发 SmartScreen 提示。

## 后续可扩展

搜索框、全局热键（光标处唤起）、分组/分页、导入导出配置、扫描开始菜单批量导入、UWP 应用枚举、亚克力/云母背景、面板主题切换。
