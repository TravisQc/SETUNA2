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
| 污染基线 | `OptionForm.Designer.cs` 的 `AutoScaleDimensions = 15x30`、`ClientSize = 1449x1000`，是全仓唯一非 96-DPI 对话框基线（**2026-09-03 已彻底清除，见下节**） |
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

## OptionForm 设计器几何的回缩

记录日期：2026-09-03。

上表那条「污染基线」当年只被清掉了一半。任务 5.2 把 `AutoScaleDimensions` 从 `(15F, 30F)` 换成
`(96F, 96F)`、模式换成 `AutoScaleMode.Dpi`，**但没有动控件坐标**。那些坐标是 Visual Studio 在 175%
显示器上保存这个窗体时按字体基线整体乘过的（`8x15 → 15x30`，即 X 乘 15/8、Y 乘 2，git 上是
`ab04855 → 0a251b5`）：在 `AutoScaleMode.Font` 时代运行时按「当前字体 ÷ 基线」又除回去，改成 DPI
基线之后除数没了，膨胀于是变成绝对值——**每一档 DPI 下这个对话框都是设计意图的 1.875 倍宽、2 倍
高**，而字号是对的，所以留白按平方级显得空。

逆运算已施加（X 取 `round(x × 8/15)`，Y 取 `y ÷ 2`，作用于 `Location`/`Size`/`Margin`/`Padding`
共 372 处字面量）：`ClientSize` 1449x1000 → **773x500**，168 DPI 下建出来的客户区 2536x1750 →
1353x875。逐项与膨胀前的 `ab04855` 核对一致（`groupBox5` 564x87、`btnOK` (501,7) 130x33、
`flowLayoutPanel1` 565x377 等）。

三类量**不能**跟着除，这也是这条记录存在的理由：

- `ItemHeight` / `LeftSpace` 这类自定义属性 VS 那次根本没碰（`8353ae8` 与 `ab04855` 逐字相同），
  任务 6.2 已把它们确立为正确的 96-DPI 基线。
- `AutoSize` 控件的 `Size`、以及高度由字体决定的控件（`ComboBox`、`NumericUpDown`）的高度，记的是
  设计器当场量出来的字体尺寸而不是被缩放的坐标，运行时会重算。
- 字号本身。**point 单位的字体在 WinForms 里不会自己跟随显示器**——上一节那张菜单表就是证据
  （同一个菜单在 96 DPI 副屏是 5.14pt/16px、在 168 DPI 主屏是 9pt/27px），框架的做法是把点值乘上
  DPI 之比、再按恒定的进程 DPI 去实现 HFONT。所以 `BaseForm.RescaleOwnedFonts` 对显式
  `Control.Font` 做同一件事是与框架一致的，不是重复缩放。

一处因此新暴露的度量噪声：`flowLayoutPanel1` 的子控件 `panel4` 在 96↔120 往返后 Y 差 2px。流式面板
的子控件位置是前面所有兄弟的「舍入后尺寸 + 边距」的累加，每跳要舍好几次，而
`DialogRelayoutProbe.RoundTripSlop = 1` 的理由是「框架直接缩放的控件只舍一次」。坐标砍半之后同一份
累加误差第一次越过一个像素，因此新增 `LayoutOwnedRoundTripSlop = 3` 只给布局引擎定位的控件放宽，
与 `LayoutOwnedByParent` 在比例考核上已有的豁免同一个理由。

## 自绘文字与显式字体的 DPI 归属

记录日期：2026-09-03。命令：

```powershell
$env:DIALOG_PROBE_MEASURE_OWNERDRAW='1'; ./scripts/verify-dialog-relayout.ps1 -Platform x64
```

上面几节都靠**合成** `WM_DPICHANGED`，而这一节的问题合成消息答不了：窗口从未真的换过显示器，它的
设备上下文当然还是进程那一档 DPI，读数两种解释都成立。所以 `OwnerDrawText` 改为**把窗体真的摆进每块
显示器**（`Screen.FromControl` 确认落位，落不上就跳过而不是当失败——副屏休眠时窗口会留在主屏）。

**结论一：控件自己的 `Graphics` 恒报进程那一档 DPI。** 本机进程是 168，把 `StyleEditForm` 摆到 96 DPI
副屏上（`form.DeviceDpi=96`，落位确认）之后 `CreateGraphics()` 仍然报 `168x168`。所以 GDI+ 把点值换成
像素用的是进程 DPI，**点值不会自己跟随显示器**；让文字跟随显示器的唯一机制就是把点值乘上 DPI 之比，
这正是框架对 `Control.Font` 做的事。`StyleItemListBox` 原先那段注释（「`Graphics` 带着目标显示器的
DPI，8pt 在 96 DPI 上就是 11 像素」）据此是错的，已改写。

**结论二：`HelpFont` 因此是真缺陷，已修。** 它不是 `Control.Font`，框架碰不到；原先由
`StyleEditForm.OnDpiContextChanged` 换算，而那条路**只在换档时走**——窗体直接建在副屏上时首次建立
上下文不发通知（这是 `BaseForm` 刻意的）。实测副屏上：

| | 96 DPI 副屏（修复前） | 96 DPI 副屏（修复后） | 168 DPI 主屏 |
|---|---|---|---|
| `ItemHeight` | 39 | 39 | 68 |
| `Font` | 5.71pt / 渲染 15.0px | 5.71pt / 15.0px | 10.00pt / 26.2px |
| `HelpFont` | **8.00pt / 21.0px** | 4.57pt / 12.0px | 8.00pt / 21.0px |
| `HelpFont ÷ Font` | **1.40** | 0.80 | 0.80 |

即修复前**说明文字比标题文字还大**，两行合计 36px 塞进 39px 的行里。修法是挂到
`StyleItemListBox.OnFontChanged` 上，按主字体前后字号之比换算。**不能用 `ScaleControl` 的倍率**——试过：
构造期的 `PerformAutoScale` 会调 `ScaleControl`、却不动显式指定的 `Control.Font`，于是 168 DPI 上
`HelpFont` 被乘成 14pt 而 `Font` 还是 10pt，跨屏一致了但比例整体偏 1.75 倍。探针新增
`CheckHelpFontProportion` 断言这个比值在每块显示器上都是 0.80，去掉修复会报 2 条（两个样式列表在副屏上
1.40），确认有牙；只有一档 DPI 时打印 inconclusive。

**结论三：显式 `Control.Font` 没有同样的问题**，所以 `RescaleOwnedFonts` 的其余六个调用方不必动。
把 `HotkeyMsg` 摆到两块显示器上量（`ReportExplicitFonts`）：副屏 `form.Font` 5.14pt、`lblKey` 5.14pt
（×1.00）、`label1` 5.71pt（×1.11）；主屏 9.00pt / 9.00pt（×1.00）/ 10.00pt（×1.11）——两块屏上的比值
逐项相同，说明设计器指定的字体确实跟着显示器走。

> **2026-09-03 修正**：结论三只量了「生在哪块屏上」，据此说 `RescaleOwnedFonts` 不必动是**错的**——
> 它量到的比值一致，正是因为框架已经把活干完了，而那七个调用方是在**换档时**再乘一遍。见下一节。

## 应用不得按 DPI 之比再乘一遍：合成消息制造的两处假缺陷

记录日期：2026-09-03。用户报告：程序在 100% 显示器上打开时选项窗口里「张为上限」左边的数字输入框
整个不见了；拖到 175% 显示器之后左侧六个导航标签的文字异常放大并被裁掉下半截。命令：

```powershell
./scripts/verify-monitor-birth.ps1 -Platform x64
```

**根因是一条框架事实，之前所有测量都碰不到它：真实换屏时系统会把 `WM_DPICHANGED_BEFOREPARENT`
逐个发给每个子窗口，`Control.WmDpiChangedBeforeParent` 就在那里把这个子控件的矩形、**设计器显式指定的
`Control.Font`** 和字体派生的常量全部按新 DPI 换算好。** 合成 `WM_DPICHANGED` 只到顶层窗口，也**无法**
伪造子消息——那个处理函数重新读 `GetDpiForWindow(child)`，而子窗口的真实 DPI 并没有变，手工投递必然是
空操作。于是合成探针下这些量看起来「框架漏掉了」，两段应用代码就是为了让它们动起来才写的：

| 被删掉的机制 | 它按 DPI 之比乘的东西 | 真机后果（实测 96↔168，真实拖拽） |
|---|---|---|
| `BaseForm.RescaleOwnedFonts`（7 个窗体调用） | 显式 `Control.Font` 的点值 | 168 DPI 上导航标签 9pt→**15.75pt**（渲染 47px，而标签高只有 30px，文字被裁）；96 DPI 上 5.14pt→**2.94pt** |
| `BaseForm.CompensateSkippedContainersWhenLayoutSettles` | 嵌套 `ContainerControl` 的外框 | 96 DPI 上 `numDustBox` 从 (50,50,46x21) 变成 **(29,29,26x21)**，正好落在 `chkDustBox` 的矩形里且 z 序在其之下，**整个看不见**；`hotkeyControl1` 反过来 200x23→**613x70** |

两段都删除，`DpiContext.ScaleFont` 随之删除（唯一调用方是前者）。删掉之后逐项正确：`numDustBox` 在
96 DPI 是 (50,50,46x21)、168 DPI 是 (88,88,80x31)；每个控件的字号与所属窗体字号之比在两块屏上、以及
来回拖拽之后都保持设计值。

**那段补偿代码为什么会误判**：它的判据是「换档前后外框一点没动 ⇒ 框架跳过了它 ⇒ 乘上比例」。但
`Form.WmDpiChanged` 是先 `DefWndProc`（系统在这里发子消息、框架在这里换算子控件）再触发
`OnDpiChanged`，所以 `SnapshotContainerBounds()` 拍到的已经是**换算后**的值，「没动」实际含义是
「已经对了」。而且它是 ratio 乘法，每次通知乘一遍——`Show()` 一次可以走四次换档通知，误差因此还会
叠加（实测 `numSelectAreaTrans` 到过设计值的 1.75² 倍）。**因此本仓库的规矩是：`OnDpiContextChanged`
里只允许按新 DPI 做绝对换算（如 `Mainform.ApplyWindowSizeBounds`），或挂到依赖量自己的变更通知上
（如 `StyleItemListBox.OnFontChanged` 之于 `HelpFont`）；绝对算法幂等，通知多一次少一次都无害。**

**两个探针的分工由此定下来**：

* `DialogRelayoutProbe`（合成、5 档 DPI、与硬件无关）现在显式声明自己的盲区。`Layout.cs` 的
  `Synthesis` 记下两类不可测量：控件自带字体（字号、以及由它决定的 `AutoSize` 外框和文字是否被裁），
  以及自己缩放的嵌套 `ContainerControl`（自己的外框）。这些读数被排除并**计数打印**（本机一轮 4370
  条），排除不是静默的。
* `MonitorBirthProbe`（真实显示器、真实拖拽）补上那块盲区，考核的是**关系**而不是绝对值，所以不需要
  一张设计器坐标表：同一控件的字号与窗体字号之比在每块显示器上必须相同（±0.02），矩形除以显示器 DPI
  之后在每块显示器上必须相同（±6 个 96-DPI 单位——刻意粗，要抓的是 1.75/3.06/0.57 这种整倍错误）。
  27 个对话框 × 每块显示器 ×（生在那里 / 拖过去 / 拖回来）= 162 组读数。**有牙**：把上面两段机制放回去
  会报 68 条。只有一档 DPI 时退出码 4（inconclusive），与 `MenuDpiProbe` 同一约定。

## 双屏验收的未完成项

旧变更最后一项人工验证要求真实副屏完成截图/贴图、画笔、主窗口边界拖拽。当前环境无法提供该实体
双屏，因此不宣称已完成；该项已由 `modernize-dpi-scaling` 的任务 7.5、10.2、10.4 接管，必须在
新运行时和 SDK 构建通过后按 100%↔175%、125%↔150%、左右负坐标矩阵重新执行。
