# Dashboard

[简体中文](README.md) | [English](README.en.md)

基于 [zashboard](https://github.com/Zephyruso/zashboard) 的 Windows 桌面代理管理面板，支持 mihomo 和提供 Clash API 的 sing-box。

## 功能

- 管理两套独立核心配置，启动、停止、重启和切换当前核心。
- 选择代理、测速，查看连接、流量、规则和日志。
- 自定义主题、背景和连接标签，导入导出界面偏好。
- 托盘、开机自启、关闭到托盘和轻量模式；轻量模式隐藏一分钟后释放界面，重新打开时恢复。
- 可选来源 IP 主机名解析，默认关闭；手动标签优先，不支持解析时显示原 IP。

## 安装与使用

需要 Windows x64、[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) 和 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

1. 从 [Releases](https://github.com/kabuda2077/Dashboard/releases) 下载压缩包，解压后运行 `Dashboard.exe`。
2. 自备 mihomo 或启用 Clash API 的 sing-box，以及对应配置文件。发布包不包含核心和配置。
3. 在 Core 页面选择核心，填写可执行文件、配置文件、API 地址和 Secret，保存后启动。需要管理员权限时会请求 UAC。

mihomo 使用 `external-controller`，sing-box 使用 `experimental.clash_api.external_controller`。例如 API 地址为 `http://127.0.0.1:9090`，Secret 应与核心配置一致。启动后仍无法连接时，检查 API 地址和 Secret。

设置位于 Core 页面。连接页面可切换活跃／全部连接；再次点击“全部”可选择仅已关闭。

## 更新与注意事项

- **从 1.x 升级到 2.x 请解压到新目录并重新配置。** 旧设置、历史、背景图片和启动项不会自动迁移；保留旧目录作为备份。
- 更新 2.x 时，完全退出程序后覆盖应用文件，保留 `settings.json`、核心和配置文件。
- 应用版本变化会清空连接历史、本地背景图片等浏览器数据；已保存的界面偏好会恢复。旧缓存首次切换到当前策略时也会重置一次。
- Secret 受 Windows 当前用户保护。换用户后无法解密时，在 Core 页面重新填写并保存。
- Dashboard 更新会打开下载页，由你手动替换。mihomo 可通过核心接口升级；sing-box 内置升级使用 **reF1nd Windows amd64v3** 构建，其他版本可自行升级。
- sing-box 通过 Clash API 管理，桌面 TUN 栈只读；不支持 native API、Tools 或 Terminal。TUN 睡眠唤醒后的自动恢复尚未验证。
- 本地界面使用端口 `33291`，被占用时无法启动。自启任务存在归属冲突时不会覆盖，请检查已有任务。

遇到问题可查看 `resources/logs`；需要详细日志时，以 `Dashboard.exe --diagnostic-log` 启动。

开发与维护说明见[维护约定](docs/maintenance.md)。

MIT License；上游前端版权见 [dashboard-src/LICENSE](dashboard-src/LICENSE)。
