# Dashboard

一个轻量的 Windows 桌面管理器，用 WinForms + WebView2 承载 zashboard UI，并负责管理 mihomo 内核、托盘驻留和开机自启。

## 功能

- 集成 zashboard UI，并加入桌面应用原生的“内核”页面。
- 启动、停止、重启 mihomo 内核，并显示 stdout/stderr 日志。
- 从 `MetaCubeX/mihomo` 最新 release 升级 Windows x64 内核，默认优先选择 `mihomo-windows-amd64-v3-go125-*.zip`。
- 启动内核时默认请求管理员权限，适配 TUN 场景。
- 系统托盘菜单支持显示窗口、启动内核、重启内核、停止内核和退出。
- 当前用户开机自启，写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
- 应用设置保存到 `%LOCALAPPDATA%\Dashboard\settings.json`，Secret 使用 DPAPI 保护。首次运行新版时会从旧的 `%LOCALAPPDATA%\MihomoDashboard\settings.json` 迁移设置。
- 会缓存 `config.yaml` 中 `proxy-groups` 的远程 icon，加快代理页面图标显示。

## 目录

- `src`: 桌面管理器源码。
- `dashboard-src`: zashboard 源码副本，包含桌面应用需要的页面和宿主逻辑。
- `resources/dashboard`: 本地构建后的 zashboard 静态文件。
- `tools/build-zashboard.ps1`: 构建 `dashboard-src` 并同步到 `resources/dashboard`。
- `cores`: 建议放置 `mihomo.exe`。
- `STYLE.md` / `UPSTREAM_MERGE.md`: 本项目 UI 约束和 zashboard 上游跟进流程。

## 使用

1. 安装 .NET 9 Desktop Runtime 和 Microsoft Edge WebView2 Runtime。
2. 将 mihomo Windows 内核放到 `cores/mihomo.exe`，或在界面里选择内核路径。
3. 将配置放到程序目录下的 `config.yaml`，或在界面里选择配置路径。
4. 确保 mihomo 配置包含 external-controller，例如：

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

## 本地构建

现在 GitHub 只作为代码仓库，不再使用 GitHub Actions 自动编译。发布包在本机生成。

需要安装：

- .NET 9 SDK
- Node.js 24
- pnpm 10.15.0

如果没有 pnpm，可以安装：

```powershell
npm install -g pnpm@10.15.0
```

生成发布包：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出目录：

```text
artifacts\publish\Dashboard-Release-win-x64
```

发布时请完整复制这个文件夹里的所有内容，不要只复制单独的 `Dashboard.exe`。

## 产物目录规范

- `bin/`、`obj/`: .NET 临时编译产物，自动生成，不手动复制。
- `dashboard-src/dist/`: 前端临时构建产物，自动生成。
- `resources/dashboard/`: 桌面程序内置的前端静态资源，会被打包进发布版。
- `artifacts/publish/Dashboard-Release-win-x64/`: 可直接全选复制覆盖安装目录的完整发布文件。
- `artifacts/releases/`: `create-release.ps1` 生成的 ZIP 和发布说明。

根目录不要再放 `publish`、`publish-lightmode`、`releases` 这类临时目录。

## 开发运行

如需开发运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1
dotnet restore -s https://api.nuget.org/v3/index.json
dotnet run
```

如果本机 NuGet 已配置 nuget.org，也可以直接运行 `dotnet restore`。

## 说明

zashboard 本身仍然通过 Clash/Mihomo external-controller API 工作。桌面应用会启动一个本地静态服务承载 zashboard，并默认把 API 地址设为 `http://127.0.0.1:9090`。

内核启动、停止、重启和升级由桌面应用统一管理；`dashboard-src` 中已移除 zashboard 原生的升级核心、重启核心、更新配置、升级面板和自动升级面板入口，避免和便携包内置资源冲突。

`resources/dashboard` 是本地构建产物。修改 `dashboard-src` 后，请运行 `build.ps1` 或 `tools/build-zashboard.ps1` 更新它。
