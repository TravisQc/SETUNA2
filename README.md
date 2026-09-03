# 免责声明
- [官方网站](http://www.clearunit.com/clearup/setuna2)（地址已挂）
- 官方已不再维护该软件，本人基于高分屏截图不全原因，优化并维护该软件，如有侵权请联系删除！

---

## 介绍
A best screenshot small tool (support high dpi screenshots) 

一款好用的截图小工具（支持高分屏截图）

---

![效果图1](壁纸示例图/示例1.png)
![效果图2](壁纸示例图/示例2.png)
![效果图3](壁纸示例图/示例3.png)

---

## 版本说明
- **SETUNA 3.x 版本的运行要求**（持续更新）([最新版 3.x 下载链接](https://github.com/tylearymf/SETUNA2/releases))
  
  - Windows 10 17763（1809）及以上
  
    - ###### 查看Win10系统版本：设置 -> 系统 -> 关于 -> Windows 规格详情页里的 操作系统版本
  
  - 无需预装运行时：发布产物是 self-contained 的单文件 exe，.NET 8 运行时已打包在内
- **SETUNA 2.x 版本的运行要求**（停止维护）([最后一版 2.x 下载链接](https://github.com/tylearymf/SETUNA2/releases/tag/2.6.0))
  
  - 安装 .Net Framework 2.0 组件

**注意：**

- 如果系统不达要求的，**可以尝试使用 3.x 版本**，如果出现 **截图时屏幕会被缩小** 问题，那只能用回 2.x 版本.
- 3.x 的版本缓存格式变更了，所以如果之前使用的时 2.x 截取了一些图片的，可以通过保存到本地，再通过 3.x 粘贴出来。
- 如遇 **截图模糊**，请尝试打开 **杂项设置** -> **截图背景** -> **穿透截图**

---

## 功能说明
- 支持多台不同DPI显示器

  - ###### 如果是 **2.x** 版本，请前往 选项 -> 显示器DPI设置 中手动设置显示器的 DPI。

- 支持电脑重启后继续显示之前未关闭的截图（保留截图信息：位置、样式、层级）

- 支持配置开机启动、支持配置截图始终置顶

  - ###### 配置方法：选项- > 常规 -> 其他

- 点击任务栏程序图标激活一次所有截图 ，让其置顶

- 支持快捷键隐藏、显示所有截图

  - ###### 配置方法：选项 -> 截图设置
  
- 支持显示全屏十字光标

  - ###### 配置方法：选项 -> 杂项设置 -> 全屏十字光标样式

- 支持在截图中保留鼠标的显示

  - ###### 配置方法：选项 -> 杂项设置 -> 其他 -> 在截图中保留鼠标

- 支持显示放大镜以更准确的截取图片

  - ###### 配置方法：选项 -> 杂项设置 -> 其他 -> 显示放大镜

- 支持从网站上图片拖拽创建截图

  - ###### 从网站拖拽图片到某个截图中会自动创建截图（该功能需要联网，因为需要下载对应的网站图片）
  
- 支持从图片格式 **JPEG**、**PNG**、**PSD** 、**GIF**、**BMP**、**ICO**、**TIFF**、**WEBP**、**SVG**、**TGA**、**SVG** 中创建截图

---

## 构建

构建环境：.NET 8 SDK。不再需要 Visual Studio、MSBuild 或 .NET Framework 4.8 targeting pack——项目已是 SDK-style 的 `net8.0-windows`。

```powershell
dotnet restore SETUNA.sln
dotnet build SETUNA.sln -c Debug -p:Platform=x64
```

`./scripts/verify-build.ps1 -Configuration Debug -Platform x64` 会依次跑还原、构建和测试，并打印产物路径。

解决方案支持 `Debug`/`Release` 与 `x86`/`x64` 组合，一次 `dotnet restore` 同时覆盖 `win-x86` 与 `win-x64`。普通构建不需要 7-Zip。

发布前的完整门槛是 `./scripts/verify-matrix.ps1`：四个配置各做一次非增量构建与测试，把逐配置的警告清单、测试 TRX 和一份汇总表写到 `TestResults/build-matrix/`。跨显示器行为由几个自带清单的探针验证（清单是 DPI 感知生效的前提，测试宿主没有清单，所以这类检查只能放在探针里）：`verify-dpi-awareness.ps1`、`verify-dialog-relayout.ps1`（27 个对话框 × 2 语言 × 5 档 DPI，含逐档截图）、`verify-surface-geometry.ps1`（贴图/放大镜/截图覆盖层的像素不随 DPI 变）、`verify-menu-dpi.ps1`、`verify-webp-probe.ps1`。

## 发布

发布产物是**自包含单文件** exe：目标机器不需要预装任何 .NET 运行时，只要 Windows 10 1809（17763）或更新。

```powershell
./scripts/verify-publish.ps1
```

它对 `x86` 与 `x64` 各做一次发布，然后把产物单独拷进一个空目录、以 `--self-test` 启动它，逐项检查六种图片格式解码、libwebp 原生库的解包与加载、配置 XML 往返，以及「exe 旁边没有别的文件」。只想发布不想验证时直接调 MSBuild 目标：

```powershell
dotnet msbuild SETUNA/SETUNA.csproj -t:PublishReleaseSingleFile -p:Configuration=Release -p:Platform=x64
```

产物落在 `publish/SETUNA_<配置>_<平台>.exe`（例如 `publish/SETUNA_Release_x64.exe`，约 76 MB；x86 约 71 MB）。两个架构可以并存在同一目录。

约束由 `ValidateReleasePublishOutput` 目标强制，**发现问题时报错而不是删文件**：发布目录里只允许命名 exe 和可选的同名 pdb，不打压缩包，不带 `.config`／`.deps.json`／`.runtimeconfig.json`／附属资源程序集（`*.resources.dll`）／散落的原生 DLL。托管依赖由 SDK 打包进单文件，DPI 感知由内嵌清单声明，TLS 设置由启动代码应用；`libwebp_x86.dll`／`libwebp_x64.dll` 仍是嵌入资源，首次用到时按架构解包到 `%LOCALAPPDATA%\SETUNA\native\` 再加载，不放在发布目录里。从旧版（Costura 打包的 .NET Framework 4.8 版本）升级时可以直接删掉目录里残留的 `SETUNA.exe.config` 和 `SETUNA_*.zip`。

---

## 目前已知问题
- 前往 [Projects](https://github.com/tylearymf/SETUNA2/projects/1) 标签查看

---

## 后续要加的功能
- 前往 [Projects](https://github.com/tylearymf/SETUNA2/projects/1) 标签查看

---

## 引用
- [JosePineiro/WebP-wrapper](https://github.com/JosePineiro/WebP-wrapper)
