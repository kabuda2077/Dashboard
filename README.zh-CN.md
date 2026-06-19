# Dashboard

[English](README.md) | [简体中文](README.zh-CN.md)

Dashboard 是一个基于 zashboard 的 Windows 桌面启动器和管理面板，支持 mihomo 和 sing-box。

Dashboard 使用 WinForms + WebView2 承载本地打包的 zashboard UI，同时由桌面宿主管理代理内核进程、托盘行为、开机自启和 Windows 集成能力。

## 功能

- 内置 zashboard UI，并加入桌面端集成。
- 单一活动内核模式：`mihomo` / `sing-box` 二选一。
- mihomo 和 sing-box 分别保存独立的内核路径、配置路径、API 地址和 Secret。
- 在内核页面启动、停止、重启、切换和查看当前内核。
- 显示内核 PID、运行状态、stdout/stderr 日志和当前下载较高的连接。
- 支持从 MetaCubeX releases 升级 `mihomo`。
- 支持从 reF1nd `sing-box-releases` 的 Windows amd64v3 构建升级 `sing-box`。
- mihomo 和 sing-box 都通过 Clash-compatible API 驱动主面板页面。
- 系统托盘菜单支持显示窗口、重启内核、停止内核和退出。
- 支持关闭到托盘和轻量模式，用于控制 WebView 生命周期。
- 支持当前用户开机自启，写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
- 设置保存到 `%LOCALAPPDATA%\Dashboard\settings.json`。
- Secret 使用 Windows DPAPI 保护。
- UI 样式约束见 `STYLE.md`。
- 跟进 zashboard 上游的流程见 `UPSTREAM_MERGE.md`。

## 项目结构

- `src/`：Windows 桌面宿主源码。
- `dashboard-src/`：基于 zashboard 的前端源码，包含 Dashboard 专属改动。
- `resources/dashboard/`：构建后的前端静态资源，会嵌入桌面应用。
- `resources/EBWebView/` 和 `resources/icon-cache/`：应用运行时生成的 WebView 数据和代理组图标缓存。
- `resources/app.ico`：桌面应用图标。
- `resources/tray.ico`：托盘图标。
- `tools/build-zashboard.ps1`：构建 `dashboard-src` 并同步到 `resources/dashboard`。
- `build.ps1`：构建前端并发布 .NET 桌面应用。
- `create-release.ps1`：基于最新 publish 产物创建 ZIP 发布包。
- `STYLE.md`：本项目定制 UI 使用的视觉规则。
- `UPSTREAM_MERGE.md`：跟进 zashboard 上游更新时的流程规则。

## 环境要求

运行环境：

- Windows 10/11
- .NET 9 Desktop Runtime
- Microsoft Edge WebView2 Runtime
- 已配置好的 `mihomo` 或 `sing-box` 可执行文件

如果缺少 .NET 9 Desktop Runtime，Windows 会在 Dashboard 启动前提示运行时缺失；如果缺少 WebView2 Runtime，Dashboard 会显示安装提示和 WebView2 下载地址。

开发环境：

- .NET 9 SDK
- Node.js 24
- pnpm 10.15.0

如果没有 pnpm：

```powershell
npm install -g pnpm@10.15.0
```

## 内核配置

Dashboard 默认不内置代理内核。请在内核页面选择对应的可执行文件和配置文件。

推荐的便携目录结构：

```text
Dashboard.exe
mihomo/
  mihomo.exe
  config.yaml
sing-box/
  sing-box.exe
  config.json
```

当前默认路径：

```text
E:\APP\Dashboard\mihomo\mihomo.exe
E:\APP\Dashboard\mihomo\config.yaml
E:\APP\Dashboard\sing-box\sing-box.exe
E:\APP\Dashboard\sing-box\config.json
```

`mihomo` 需要开启 external controller，例如：

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

`sing-box` 需要开启 Clash-compatible external controller。Dashboard 会通过 Clash API 显示概览、代理、规则、连接、日志、重载配置等内容。目前不包含 sing-box native API / Tools 集成。

## 构建

构建并发布桌面应用：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release -Runtime win-x64
```

输出目录：

```text
artifacts\publish\Dashboard-Release-win-x64
```

安装或覆盖便携版时，请复制整个输出目录中的所有文件，不要只复制 `Dashboard.exe`。

## 创建发布包

创建 ZIP 发布包：

```powershell
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-v1.0.0-win-x64.zip
```

输出：

```text
artifacts\releases\Dashboard-v1.0.0-win-x64.zip
artifacts\releases\RELEASE_NOTES_yyyyMMdd.txt
```

## 标准发布流程

后续发布按这个流程走：

1. 从 `main` 开始。
2. 创建发布分支，例如 `codex/release-v1.1.0`。
3. 完成代码改动；如果行为或流程有变化，同步更新 `README.md`、`STYLE.md` 或 `UPSTREAM_MERGE.md`。
4. 运行验证：

```powershell
cd dashboard-src
cmd /c npm run type-check
cmd /c npm run build
cd ..
dotnet build -c Release
```

5. 创建 ZIP 发布包：

```powershell
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-v1.1.0-win-x64.zip
```

6. 从 `artifacts\publish\Dashboard-Release-win-x64` 做一次手动冒烟测试。
7. 提交最终源码和生成后的 `resources/dashboard` 静态资源。
8. 将发布分支合并到 `main`。
9. 在 `main` 上打版本标签，例如 `v1.1.0`。
10. 推送 `main` 和 tag。
11. 创建 GitHub Release，并上传 `artifacts\releases` 中的 ZIP。

版本 tag 使用语义化版本格式：`vMAJOR.MINOR.PATCH`。

## 跟进 zashboard 上游

Dashboard 基于 zashboard，但不是 zashboard 的直接镜像。前端保留 zashboard 的主面板体验，同时加入桌面宿主通信、内核管理、窗口控制和项目自己的视觉规则。

跟进上游时：

- 每次都从新分支开始。
- 不只看 changelog，要查看 upstream commits。
- 保留 Dashboard 专属的 host messaging、内核页面逻辑、窗口控制、视觉样式规则和发布打包流程。
- 使用 `UPSTREAM_MERGE.md` 作为检查清单。

## 说明

- `resources/dashboard` 从 `dashboard-src` 构建生成；修改前端后需要重新构建。
- `resources/EBWebView` 和 `resources/icon-cache` 是运行数据目录，可以重新生成。
- `dashboard-src/dist`、`bin`、`obj`、`artifacts/publish` 都是生成产物。
- 便携包包含 Dashboard 应用文件，但仍需要 WebView2 Runtime 和所选代理内核可执行文件。
