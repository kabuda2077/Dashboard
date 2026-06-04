# Mihomo Dashboard

一个轻量的 Windows 桌面管理器，用 WinForms + WebView2 承载 zashboard UI，并负责启动/停止 mihomo 内核、托盘驻留和开机自启。

## 功能

- 在 GitHub Actions 中构建仓库内的 `dashboard-src`，并提供原生 `内核` 路由。
- 启动、停止 mihomo 内核，并显示 stdout/stderr 日志。
- 重启 mihomo 内核，或从 `MetaCubeX/mihomo` 最新 release 升级 Windows x64 内核。
- 系统托盘图标，支持显示窗口、启动内核、重启内核、停止内核、退出。
- 当前用户开机自启，写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。
- 应用设置保存在 `%LOCALAPPDATA%\MihomoDashboard\settings.json`。

## 目录

- `resources/dashboard`: zashboard 静态文件。GitHub Actions 会在发布前从 `dashboard-src` 重新生成。
- `dashboard-src`: zashboard 源码副本，已直接集成桌面应用需要的原生 `内核` 页面和宿主逻辑。
- `tools/build-zashboard.ps1`: 构建 `dashboard-src` 并同步静态面板资源的脚本。
- `cores`: 建议放置 `mihomo.exe`。
- `src`: 桌面管理器源码。

## 使用

1. 安装 .NET 9 Desktop Runtime 和 Microsoft Edge WebView2 Runtime。正式使用不需要 .NET SDK。
2. 将 mihomo Windows 内核放到 `cores/mihomo.exe`，或在界面里选择你的内核路径。
3. 可以将配置放到程序目录下的 `config.yaml`，或在界面里选择你的配置路径。
4. 确保 mihomo 配置包含：

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

5. 如需本地开发或本地构建，需要额外安装 .NET 9 SDK、Node.js 24 和 pnpm 10，然后运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1
```

6. 编译运行：

```powershell
dotnet restore
dotnet run
```

发布：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

输出位置通常是 `bin\Release\net9.0-windows\win-x64\publish`。

## GitHub Actions 编译

项目已包含 `.github/workflows/build-windows.yml`。推送到 GitHub 后会自动编译 Windows x64 版本，也可以在仓库的 Actions 页面手动运行 `Build Windows`。

下载方式：

1. 打开 GitHub 仓库的 `Actions` 页面。
2. 进入最新一次 `Build Windows` 任务。
3. 在页面底部下载 `MihomoDashboard-win-x64` artifact。

当前 workflow 使用 `--self-contained false`，产物更小，但目标电脑需要已安装 .NET 9 Desktop Runtime。你的电脑已经有 .NET 9 Desktop Runtime，所以适合这个模式。下载 artifact 后请先完整解压，再运行里面的 `MihomoDashboard.exe`，不要只单独拷贝 exe。

## 说明

zashboard 本身仍然通过 Clash/Mihomo external-controller API 工作。应用会启动一个本地临时端口来托管 zashboard 静态文件，并默认把 API 地址设置为 `http://127.0.0.1:9090`。

内核启动/停止、路径设置、日志和开机自启控件会作为 zashboard 原生路由集成在侧栏的 `内核` 页面中。应用打开后会直接进入这个页面；内核启动成功后，侧栏会保留 `内核`、`概览`、`代理`、`连接` 等页面入口。

内核的启动、停止、重启和升级由桌面应用统一管理；`dashboard-src` 中已移除 zashboard 原生的升级核心、重启核心、更新配置、升级面板和自动升级面板入口，避免和便携包内置资源产生冲突。面板更新请通过 GitHub Actions 下载新的便携包。

内核升级默认优先选择 `mihomo-windows-amd64-v3-go125-*.zip`。如果当前 release 没有这个构建，会自动回退到其他 Windows x64 构建。

如果配置启用了 TUN，点击 `启动内核` 时应用会自动请求管理员权限并在提权后继续启动内核。

`内核` 页面不依赖 Mihomo API，可在内核未启动时直接打开。应用仍会在启动内核后确认 Mihomo API 是否可访问，并把结果提示在当前页面里。

如果 zashboard 首次打开时要求你选择后端，请在 `内核` 页面里填写 API 地址和 Secret，保存后点击“刷新 UI”。
