# 免责声明
- [官方网站](http://www.clearunit.com/clearup/setuna2)（地址已挂）
- 官方已不再维护该软件，本人基于高分屏截图不全原因，优化并维护该软件，如有侵权请联系删除！
- 当前项目是基于长久未更新的项目上的二次开发，如果直接迁移可能造成数据丢失。[原项目](https://github.com/tylearymf/SETUNA2)

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
  
  - 下载有两种单文件 exe，任选其一：
    - `SETUNA_Release_x64.exe` / `SETUNA_Release_x86.exe`：**无需预装运行时**，.NET 8 运行时已打包在内（约 47 / 44 MB）
    - `SETUNA_Release_x64_Portable.exe` / `SETUNA_Release_x86_Portable.exe`：体积只有十分之一（约 4 MB），但目标机器要预装同架构的 [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)——架构必须对得上，x86 的 exe 不会去用 x64 的运行时


**注意：**

- 如果系统不达要求的，请继续使用[原项目](https://github.com/tylearymf/SETUNA2)

---

## 功能说明
- 支持多台不同DPI显示器

- 支持多语言切换

  - ###### 配置方法：选项- > 常规 -> 语言

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

构建环境：.NET 8 SDK。不再需要 Visual Studio、MSBuild ——项目已是 SDK-style 的 `net8.0-windows`。

```powershell
dotnet restore SETUNA.sln
dotnet build SETUNA.sln -c Debug -p:Platform=x64
```

`./scripts/verify-build.ps1 -Configuration Debug -Platform x64` 会依次跑还原、构建和测试，并打印产物路径。

解决方案支持 `Debug`/`Release` 与 `x86`/`x64` 组合，一次 `dotnet restore` 同时覆盖 `win-x86` 与 `win-x64`。

发布前的完整门槛是 `./scripts/verify-matrix.ps1`：四个配置各做一次非增量构建与测试，把逐配置的警告清单、测试 TRX 和一份汇总表写到 `TestResults/build-matrix/`。跨显示器行为由几个自带清单的探针验证（清单是 DPI 感知生效的前提，测试宿主没有清单，所以这类检查只能放在探针里）：`verify-dpi-awareness.ps1`、`verify-dialog-relayout.ps1`（27 个对话框 × 2 语言 × 5 档 DPI，含逐档截图）、`verify-surface-geometry.ps1`（贴图/放大镜/截图覆盖层的像素不随 DPI 变）、`verify-menu-dpi.ps1`、`verify-webp-probe.ps1`。



## 引用
- [JosePineiro/WebP-wrapper](https://github.com/JosePineiro/WebP-wrapper)
