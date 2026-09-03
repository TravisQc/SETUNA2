# SETUNA DPI Migration Baseline

记录日期：2026-09-01  
基线运行时：.NET Framework 4.8，经典 WinForms 项目，`Debug|x64`   
参考变更：`openspec/changes/per-monitor-dpi-relayout`

## 可复现门禁

> 本节记录的是 net48 时代的门禁。`modernize-dpi-scaling` 删除 net48 工程之后，
> 门禁改为 `./scripts/verify-build.ps1`（`dotnet` 还原 + 构建 + 测试）；下面的
> MSBuild/vstest 路径只作为基线采集时的环境说明保留。

在仓库根目录运行：

```powershell
./scripts/verify-build.ps1
```

原 net48 脚本默认寻找 Visual Studio 2022 的 MSBuild 和 vstest；也可以通过
`-MsBuildPath`、`-VSTestPath` 显式指定路径。它不会删除 `bin`、`obj` 或测试结果，构建输出位于
`SETUNA/bin/x64/Debug`，测试程序集位于 `SETUNATests/bin/x64/Debug/SETUNATests.dll`。

当前基线结果：

| 检查 | 结果 |
|---|---|
| `SETUNA.sln` Debug x64 Rebuild | 通过 |
| MSTest | 262/262 通过 |
| 编译警告 | 1 个既有 `CaptureForm.components` CS0649，不影响通过 |
| 生成的程序 | `SETUNA/bin/x64/Debug/SETUNA.exe` |
| 生成的测试程序集 | `SETUNATests/bin/x64/Debug/SETUNATests.dll` |

命令等价于：

```powershell
& $msbuild SETUNA.sln /restore /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /m
& $vstest SETUNATests/bin/x64/Debug/SETUNATests.dll /Platform:x64
```

## 行为基线

当前 manifest 声明 `PerMonitorV2`，但 net48 无 `app.config` 动态 DPI 管线。系统在交互式跨屏拖拽时
会改窗口外框，旧 WinForms 内容不会自动重排。已有实测环境为 175%（168 DPI）主屏和 100%（96 DPI）
副屏：

| 窗体 | 168 DPI 原生（外框/客户区） | 重排到 96 DPI | 96 DPI 原生对照 |
|---|---:|---:|---:|
| `OptionForm` | 1069x746 / 1063x700 | 586x429 / 580x400 | 586x429 / 580x400 |
| `StyleEditForm` | 1460x731 / 1436x667 | 799x420 / 783x381 | 799x420 / 783x381 |
| `HotkeyMsg` | 954x289 / 948x243 | 523x168 / 517x139 | 523x168 / 517x139 |
| `LoginInput` | 559x337 / 535x273 | 308x195 / 292x156 | 308x195 / 292x156 |

主窗口边界的当前生产基线是 168 DPI：最小外框 260x160，最大外框 640x360。按现有纯函数得到：

| 缩放 | DPI | 最小 | 最大 | 证据 |
|---|---:|---:|---:|---|
| 100% | 96 | 148x91 | 365x205 | 自动化数学覆盖 |
| 125% | 120 | 185x114 | 457x257 | 自动化数学覆盖，尚无该实体显示器实测 |
| 150% | 144 | 222x137 | 548x308 | 自动化数学覆盖，尚无该实体显示器实测 |
| 175% | 168 | 260x160 | 640x360 | 实测基线并由测试锁定 |

窗口默认客户区为 415x180；已测非客户区厚度为 96 DPI 下 16x39、168 DPI 下 24x64。

捕获、贴图、画笔和放大镜使用物理屏幕坐标与位图像素。`ScrapGeometry`、`MagnifierGeometry`、
`CaptureResourceTests`、`MagnifierRenderTests` 和 `ScrapGeometryTests` 锁定了负坐标、裁剪、BitBlt
目标和资源释放行为。WebP 原生库由架构资源提取并加载，`ResourceExtractorTests` 覆盖 x64/x86
资源存在、幂等提取、临时文件清理和 `LoadLibrary` 路径。

选项 XML 使用 `XmlSerializer` 和原有字段名，`OptionPersistenceTests` 覆盖原子写入、旧配置缺失
窗口尺寸字段、读写往返和失败时保留旧文件。第二实例当前经 `SingletonApplication` 的 .NET Remoting
IPC 转发到 `ISingletonForm.DetectExternalStartup`；现有行为需要在 named-pipe 迁移后保持参数顺序。

125% 和 150% 在当前单显示器宿主中是数学/消息路径覆盖，不应表述为实体双屏截图证据。

## 迁移盘点

### 项目与输出

| 项目 | 当前形态 | 迁移目标 |
|---|---|---|
| `SETUNA/SETUNA.csproj` | .NET Framework 4.8，显式 Compile/EmbeddedResource，Costura.Fody | SDK-style `net8.0-windows` WinForms |
| `SETUNATests/SETUNATests.csproj` | .NET Framework 4.8 MSTest | matching `net8.0-windows` test project |
| `SETUNA.sln` | VS classic solution | SDK project becomes default only after gates |
| `SETUNA/app.manifest` | `PerMonitorV2` | runtime setting moves to generated WinForms configuration |

### 窗体分类

`BaseForm` 派生窗体：`Mainform`、`ClickCapture`、`CaptureForm`、`CaptureInfo`、`CaptureSelLine`、
`HotkeyMsg`、`ScrapBase`、`SplashForm`、`OptionForm`、`StyleEditForm`、`CompactScrap`、
`LayerRenameWindow`、`LoginInput`、`PaintForm`、`PicasaBar`、`ScrapDrawForm`、`ScrapPaintLayer`、
`ScrapPaintPenTool`、`ScrapPaintTextTool`、`ScrapPaintToolBar`、`ToolBoxForm`。

直接派生 `Form`：`Magnifier`。通过 `ScrapDrawForm` 或 `ToolBoxForm` 间接托管的画布/工具窗体还包括
`ScrapPaintWindow`、`TrimWindow`、各 `*StyleItemPanel`、`ScrapPaintLayerItem` 和文本工具编辑框。

迁移分类：`Mainform`、`OptionForm`、`StyleEditForm`、`HotkeyMsg`、`LoginInput`、`ToolBoxForm` 及
普通对话框为 `LogicalUi`；`ScrapBase`、`CaptureForm`、`CaptureSelLine`、`CaptureInfo`、
`Magnifier`、`PaintForm`、`ScrapDrawForm`、`ScrapPaint*`、`TrimWindow` 为 `PhysicalSurface`。
`LayerRenameWindow` 保留为待修复的既有布局缺陷，不在未解决裁切前强行接入自动重排。

### 当前 AutoScaleMode / 设计基线

| 状态 | 文件 |
|---|---|
| `None` | `Mainform.Designer.cs`、`CaptureSelLine.Designer.cs`、`ScrapBase.Designer.cs` |
| `Font` | 其余已生成窗体/控件设计器，包括 `OptionForm`、`StyleEditForm`、`HotkeyMsg`、`LoginInput`、`Magnifier`、`CaptureForm`、`PaintForm`、`ToolBoxForm` |
| 污染基线 | `OptionForm.Designer.cs` 的 `AutoScaleDimensions = 15x30`、`ClientSize = 1449x1000`，是全仓唯一非 96-DPI 对话框基线 |
| 无显式模式 | 若干 `UserControl`/owner-drawn panel，仅继承父容器的布局和字体 |

### DPI 来源和硬编码清单

生产代码当前仍需迁移的 DPI/96 相关点：

* `Mainform.cs`、`WindowsAPI.cs`、`BaseForm.cs`、`DpiRelayout.cs`：当前手工 `WM_DPICHANGED`、系统/窗口 DPI 查询和换算入口。
* `Magnifier.cs`：`DefaultGap * DeviceDpi / 96`，必须改为 `DpiContext` 的物理间隙策略。
* `ContextStyleMenuStrip.cs`：菜单字号、图标列和项高的 96-DPI 基准说明及手工缩放路径。
* `StyleItemListBox.cs`、`OptionForm.cs`、`LayerRenameWindow.cs`：独立字体、固定像素排版量和已知布局例外。
* `MainWindowGeometry.cs`：168-DPI 尺寸基线及 `dpi / 168` 换算；迁移后由 `DpiContext` 统一舍入。
* `SETUNATests/Main/Window/*Dpi*.cs`、`MainWindowLayoutTests.cs`：96/120/144/168/192/240/288 的纯数学和消息覆盖，保留作为回归样本。

`Control.DeviceDpi` 的生产读取已经只剩迁移目标中的 `Magnifier.cs`；所有新增代码不得将不可用的
`DeviceDpi` 当作窗口 DPI。裸常量 `96` 在测试、注释、资源坐标和上述换算中均需逐项分类，不能用全局替换。

### 运行时依赖

* IPC：`com/clearunit/SingletonApplication.cs`、`SingletonAppRemoteObject.cs` 使用
  `System.Runtime.Remoting` / `IpcChannel`，迁移为当前用户 mutex + ACL named pipe。
* WPF：`SETUNA.csproj` 显式引用 `PresentationCore`、`WindowsBase`；`Main/Common/Utils.cs` 的
  `BitmapSource` 扩展没有发现调用方，确认后删除引用或改为 `System.Drawing` 内存复制。
* 资源：`Plugins/ResourceExtractor.cs`、`WebPWrapper.cs` 保留架构特定嵌入资源和受控临时目录提取。
* NuGet：Costura.Fody 6.2.0、Newtonsoft.Json 13.0.4、Svg 3.4.8、System.Drawing.PSD 0.1.0、
  TgaLib 1.0.2；测试使用 MSTest.TestAdapter/Framework 2.1.1。迁移前必须用干净缓存确认 net8 资产。

## net8 运行时 DPI 感知验证

记录日期：2026-09-02。命令：

```powershell
./scripts/verify-dpi-awareness.ps1 -Platform x64 -Configuration Debug
```

`probes/DpiAwarenessProbe` 链接 SETUNA 自己的 `app.manifest`，并按生成的
`ApplicationConfiguration.Initialize()` 的同一顺序调用那三个 `Application` 方法。
测试套件回答不了这个问题：DPI 感知是进程属性，由清单在进程创建时确定，而测试宿主没有清单。

| 观测量 | 实测值 |
|---|---|
| 清单在进程创建时授予 PerMonitorV2 | True |
| `Application.SetHighDpiMode(PerMonitorV2)` 返回 | **False** |
| 第一个窗体之前的 `Application.HighDpiMode` | `PerMonitorV2` |
| 第一个窗体的 `DeviceDpi` | 168（DPI 不感知进程恒为 96） |

结论：清单赢得竞争，`SetHighDpiMode` 因为感知级别已被设定而失败并返回 `false`——这不是缺陷，
它只是清单被剥离时的兜底。因此 `ApplicationHighDpiMode` 与清单同时保留，编译器的 `WFAC010`
（要求删掉清单里的高 DPI 设置）在 `SETUNA.csproj` 里带理由抑制：清单先于任何托管代码生效，
且不会与单文件产物失散。

## 菜单 DPI 归属验证

记录日期：2026-09-02。命令：

```powershell
./scripts/verify-menu-dpi.ps1 -Platform x86,x64 -Configuration Debug
```

菜单是 component，不进 `Control.Controls`，下拉窗口又是系统在弹出那一刻才建的，所以它既不在控件树
缩放的路径上，也收不到 `WM_DPICHANGED`。手工重排年代因此有一层 `ContextStyleMenuStrip.ApplyMonitorDpi`
自己换算 `Font` 与 `ImageScalingSize`。`probes/MenuDpiProbe` 借 SETUNA 的清单，把两个配置完全相同的
菜单（一个普通 `ContextMenuStrip` 作对照组，一个应用真正使用的 `ContextStyleMenuStrip`）分别弹到每块
显示器上，测量它们实际实现出来的度量。测试套件回答不了：宿主没有清单，下拉窗口不会按显示器定尺寸。

| 菜单 | 96 DPI 副屏 | 168 DPI 主屏 |
|---|---|---|
| 普通 `ContextMenuStrip` | 5.14pt/16px 字、图标列 16、项高 24、整体 123x52、嵌套 16px/16 | 9pt/27px、20、34、153x72、嵌套 27px/20 |
| `ContextStyleMenuStrip`（删除手工层之后） | 与对照组逐项相同 | 与对照组逐项相同 |

结论：net8 的 `ToolStrip` DPI 管线自己按显示器定尺寸，嵌套下拉一并跟上，手工层是重复劳动——它加上去
时唯一的差别是 96 DPI 那档的整体宽度 120 而非 123。因此手工层删除，探针改为断言「应用真正使用的菜单
实现出来的字高跟随显示器 DPI」，对照组一并测量，用来区分「框架变了」和「应用改坏了」。

一处已知的框架侧不一致：设计器给的 `ImageScalingSize = 20x20` 只在与进程 DPI 相同的那块显示器上原样
保留，换到另一档时框架按它自己的 16x16 默认值重算（96 DPI 上得到 16，而按设计值折算应为 11）。眼下
两个菜单里没有一个项带图标，所以看不出来；往菜单里加图标之前必须先回答「20 是哪一档 DPI 的设计值」。
项高也不是比例关系（96→168 是 24→34，倍率 1.42，因为它是字高加固定内边距），所以探针只把字高按倍率
考核，其余数值只记录。

## 双屏验收的未完成项

旧变更最后一项人工验证要求真实副屏完成截图/贴图、画笔、主窗口边界拖拽。当前环境无法提供该实体
双屏，因此不宣称已完成；该项已由 `modernize-dpi-scaling` 的任务 7.5、10.2、10.4 接管，必须在
新运行时和 SDK 构建通过后按 100%↔175%、125%↔150%、左右负坐标矩阵重新执行。
