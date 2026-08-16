# Dashboard

[简体中文](README.md) | [English](README.en.md)

Dashboard 是一个基于 [zashboard](https://github.com/Zephyruso/zashboard) 的 Windows 桌面启动器和管理面板，支持 mihomo 和 sing-box。

它使用 WinForms + WebView2 承载本地打包的 zashboard UI，同时由桌面宿主管理代理内核进程、托盘行为、开机自启、窗口控制和本地设置。

本项目保留 zashboard 的主面板体验，在此基础上加入了 Windows 桌面壳、内核启动/停止/切换、托盘与窗口控制、便携式本地设置、mihomo / sing-box Clash-compatible API 接入，以及少量适配桌面使用的界面调整。

## 下载

从 [GitHub Releases](https://github.com/kabuda2077/Dashboard/releases) 下载最新版 ZIP。

便携包只包含 Dashboard 应用文件。你还需要：

- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- 已配置好的 `mihomo` 或 `sing-box` 可执行文件

## 快速开始

1. 解压 ZIP 到一个目录，例如：

```text
Dashboard/
  Dashboard.exe
  mihomo/
    mihomo.exe
    config.yaml
  sing-box/
    sing-box.exe
    config.json
```

2. 打开 `Dashboard.exe`，在内核页面选择内核可执行文件和配置文件。
3. 确认所选内核已开启 Clash-compatible API，然后点击启动。

默认路径相对于 Dashboard 程序目录：

```text
.\mihomo\mihomo.exe
.\mihomo\config.yaml
.\sing-box\sing-box.exe
.\sing-box\config.json
```

## 更新 Dashboard

Dashboard 会在启动后自动检查 GitHub Release，也可以在内核页手动检查。发现新版本后，点击 Release 按钮打开下载页面。

更新便携版只需要：

1. 完全退出 Dashboard。
2. 解压新版 ZIP，将其中全部文件复制到原 Dashboard 目录并选择覆盖。
3. 重新启动 `Dashboard.exe`。

不要先删除整个 Dashboard 目录。发布包不包含 `settings.json`、`mihomo\`、`sing-box\` 和运行日志，直接覆盖会保留设置、内核及配置。程序检测到应用版本或内置前端资源变化后，会在创建 WebView2 前自动清理 `resources\EBWebView`，无需手动删除缓存。

## 内核配置

`mihomo` 需要在 `config.yaml` 中开启 `external-controller`，例如：

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

`sing-box` 需要开启 Clash-compatible API。Dashboard 会通过 Clash API 显示概览、代理、规则、连接、日志、重载配置等页面。桌面前端不包含 sing-box native API / Tools；如有需要，可让浏览器版面板连接单独的 native API 端口。

推荐使用 reF1nd [`sing-box-releases`](https://github.com/reF1nd/sing-box-releases) 构建。官方 GitHub Release 中包含 Clash API 的 sing-box 也可以使用；自行编译或使用第三方精简构建时，必须确认启用了 `with_clash_api` 构建标签。Dashboard 的内置 sing-box 升级功能仅支持 reF1nd Windows amd64v3 构建：如果当前使用官方版或其他分支，请手动升级，点击面板内的升级会将内核替换为 reF1nd 构建。

`sing-box` 配置片段示例：

```json
{
  "experimental": {
    "clash_api": {
      "external_controller": "127.0.0.1:9090",
      "secret": ""
    }
  }
}
```

## 功能

- 内置 zashboard UI，并加入桌面端集成。
- 单一活动内核模式：`mihomo` / `sing-box` 二选一。
- mihomo 和 sing-box 分别保存独立的内核路径、配置路径、API 地址和 Secret。
- 在内核页面启动、停止、重启、切换和查看当前内核。
- 显示内核 PID、运行状态、stdout/stderr 日志和当前下载较高的连接。
- 支持从 MetaCubeX releases 升级 `mihomo`。
- 内置 sing-box 升级仅支持 reF1nd `sing-box-releases` 的 Windows amd64v3 构建。
- mihomo 和 sing-box 都通过 Clash-compatible API 驱动主面板页面。
- 系统托盘菜单支持显示窗口、重启内核、停止内核和退出。
- 支持关闭到托盘和轻量模式，用于控制 WebView 生命周期。
- 支持启动后自动检查 Dashboard 更新，也可在内核页手动检查并打开 GitHub Release。
- 支持当前用户开机自启，通过 `\Dashboard\Autostart` 计划任务在登录 5 秒后以最高权限静默启动托盘宿主。
- 设置保存到便携目录旁的 `settings.json`。
- Secret 使用 Windows DPAPI 保护。

## 常见问题

**Dashboard 无法启动，Windows 提示缺少 .NET。**  
安装 .NET 9 Desktop Runtime 后重新打开 Dashboard。

**Dashboard 显示缺少 WebView2 Runtime。**  
根据提示链接安装 Microsoft Edge WebView2 Runtime，然后重新打开 Dashboard。

**内核已启动，但 Dashboard 无法连接 API。**  
检查内核页面里的 API 地址是否和内核配置一致。大多数情况下是 `http://127.0.0.1:9090`。如果配置里设置了非空 `secret`，内核页面也要填同样的值。

**TUN 启动失败或要求管理员权限。**  
Windows 上 TUN 通常需要管理员权限。请以管理员身份启动 Dashboard，或允许应用弹出的 UAC 重启提示。

**开启开机自启时为什么会弹一次 UAC？**
Dashboard 需要创建最高权限计划任务。任务创建并验证成功后，后续登录不会再弹 UAC；登录约 5 秒后只启动托盘、内核管理和本地服务，打开窗口时才创建 WebView2。

**移动软件目录后需要重新设置开机自启吗？**
不需要。下次手动启动 Dashboard 时会检测计划任务路径不一致并请求修复。修复成功前不会删除旧的注册表启动项；关闭开机自启会同时清理计划任务和遗留启动项。

**可以只用 sing-box native API 吗？**  
不可以。当前桌面版主面板页面使用 sing-box 的 Clash-compatible API。

**sing-box 必须使用 reF1nd 构建吗？**

不是。官方 GitHub Release 中包含 Clash API 的构建也可以使用，但本项目主要适配和测试 reF1nd 构建，内置升级器也只会下载 reF1nd Windows amd64v3。官方版、其他分支或不支持 amd64v3 的设备应自行升级内核。

**日志保存在哪里，怎样临时开启详细诊断？**

基础日志保存在程序目录的 `resources\logs`。Release 版默认只记录关键操作、错误和 WebView 冷恢复摘要；需要排查启动或托盘恢复问题时，可使用 `Dashboard.exe --diagnostic-log` 启动，本次会话会额外记录窗口生命周期、托盘时序、前端首屏和 WebSocket 首包。Debug 构建默认开启详细诊断。单个日志文件超过 2 MB 时自动轮转，并保留最近 3 个归档。

## 开发

开发环境需要 .NET 9 SDK、Node.js 24 和 pnpm 11.20.0。

```powershell
pnpm --dir dashboard-src type-check
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release -Runtime win-x64
powershell -ExecutionPolicy Bypass -File .\create-release.ps1 -OutputZip Dashboard-vX.Y.Z-win-x64.zip
```

发布新版本前需要同步更新 `Dashboard.csproj` 中的 `Version` 和 `InformationalVersion`，并与 GitHub Release tag 保持一致。

主要目录：

- `src/`：Windows 桌面宿主。
- `dashboard-src/`：基于 zashboard 的前端源码。
- `resources/dashboard/`：构建后的前端静态资源。
- `STYLE.md`：本项目 UI 规则。
- `UPSTREAM_MERGE.md`：跟进 zashboard 上游时的检查清单。

## 许可证

本项目使用 MIT License 发布。

内置前端基于 zashboard，zashboard 同样使用 MIT License。上游版权声明见 `dashboard-src/LICENSE`。
