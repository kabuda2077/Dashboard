# Mihomo Dashboard

一个轻量的 Windows 桌面管理器，用 WinForms + WebView2 承载 zashboard 静态 UI，并负责启动/停止 mihomo 内核、托盘驻留和开机自启。

## 功能

- 内嵌 `Zephyruso/zashboard` 的 `gh-pages` 静态 UI。
- 启动、停止 mihomo 内核，并显示 stdout/stderr 日志。
- 系统托盘图标，支持显示窗口、启动内核、停止内核、退出。
- 当前用户开机自启，写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。
- 应用设置保存在 `%LOCALAPPDATA%\MihomoDashboard\settings.json`。

## 目录

- `resources/dashboard`: zashboard 静态文件。
- `cores`: 建议放置 `mihomo.exe`。
- `config`: 建议放置 `config.yaml`，已提供 `config.yaml.example`。
- `src`: 桌面管理器源码。

## 使用

1. 安装 .NET 9 SDK 和 Microsoft Edge WebView2 Runtime。
2. 将 mihomo Windows 内核放到 `cores/mihomo.exe`，或在界面里选择你的内核路径。
3. 将配置复制为 `config/config.yaml`，或在界面里选择你的配置路径。
4. 确保 mihomo 配置包含：

```yaml
external-controller: 127.0.0.1:9090
secret: ""
```

5. 编译运行：

```powershell
dotnet restore
dotnet run
```

发布单文件：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

输出位置通常是 `bin\Release\net9.0-windows\win-x64\publish`。

## GitHub Actions 编译

项目已包含 `.github/workflows/build-windows.yml`。推送到 GitHub 后会自动编译 Windows x64 版本，也可以在仓库的 Actions 页面手动运行 `Build Windows`。

下载方式：

1. 打开 GitHub 仓库的 `Actions` 页面。
2. 进入最新一次 `Build Windows` 任务。
3. 在页面底部下载 `MihomoDashboard-win-x64` artifact。

当前 workflow 使用 `--self-contained false`，产物更小，但目标电脑需要已安装 .NET 9 Desktop Runtime。你的电脑已经有 .NET 9 Desktop Runtime，所以适合这个模式。

## 说明

zashboard 本身仍然通过 Clash/Mihomo external-controller API 工作。应用会启动一个本地临时端口来托管 zashboard 静态文件，并默认把 API 地址设置为 `http://127.0.0.1:9090`。

如果 zashboard 首次打开时要求你选择后端，请填写界面左侧的 API 地址和 Secret，保存后点击“刷新 UI”。
