# Dashboard

[简体中文](README.md) | [English](README.en.md)

基于 [zashboard](https://github.com/Zephyruso/zashboard) 的 Windows 桌面代理管理面板，支持 mihomo 和提供 Clash API 的 sing-box。

## 功能

- 管理两套独立核心配置，启动、停止、重启和切换当前核心。
- 选择代理、测速，查看连接、流量、规则和日志。
- 自定义主题、背景和连接标签，导入导出界面偏好。
- 托盘、开机自启、关闭到托盘和轻量模式；轻量模式隐藏一分钟后释放界面，重新打开时恢复。
- 可选来源 IP 主机名解析，默认关闭；手动标签优先，不支持解析时显示原 IP。
- 概览提供八目标 HTTP 请求耗时检查，每站五次，数字显示成功样本中位数，圆点保留失败情况。滚动切换左侧延迟/分流卡片，悬停显示细线滚动条。分流检测覆盖八个目标，由宿主通过当前核心已有的本地代理入站建立独立 HTTPS 连接，配对后显示最终节点或直连；没有可用入站时无法检测，不修改配置。进程和入站规则可能与 WebView、TUN 或其他应用不同，结果不代表所有应用；无法配对时显示未确认。
- 延迟与分流可点击单个站点重测，顶部闪电检测当前页全部站点。检测期间保留已有结果；同一应用会话内可跨页面及轻量模式释放恢复，完全退出后清空。只在检测卡片可见时自动检查：延迟遵循自动检查开关，超过五分钟后再次查看会刷新；分流首次查看或路径变化后检测，不定时轮询。节点、模式、规则或网络变化会合并触发当前页重测，不显示待更新标签。
- 网络信息分别显示 ipip.net 与 Cloudflare 目标实际看到的单个出口 IP，不再单独探测或分列显示 IPv6。国外出口独立展示 Location、ISP/组织和 ASN，缺失字段不作推测；先显示地址再补国家、地区、城市及运营商；归属服务失败时保留地址。默认遮掩 IP 后半段及国家/地区之后的位置详情；ISP 保留首词、ASN 保留编号，后续各词替换为 `**`，可手动显示完整信息；国内出口不增加归属查询。查询遵循当前网络及分流规则，不代表所有代理节点；请求耗时不等于 Ping，分流检查不保证网站解锁或完整功能可用。

## 安装与使用

需要 Windows x64、[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

1. 从 [Releases](https://github.com/kabuda2077/Dashboard/releases) 下载压缩包，解压后运行 `Dashboard.exe`。
2. 自备 mihomo 或启用 Clash API 的 sing-box，以及对应配置文件。发布包不包含核心和配置。
3. 在 Core 页面选择核心，填写可执行文件、配置文件、API 地址和 Secret，保存后启动。需要管理员权限时会请求 UAC。

mihomo 使用 `external-controller`，sing-box 使用 `experimental.clash_api.external_controller`。例如 API 地址为 `http://127.0.0.1:9090`，Secret 应与核心配置一致。启动后仍无法连接时，检查 API 地址和 Secret。

设置位于 Core 页面。连接页面可切换活跃／全部连接；再次点击“全部”可选择仅已关闭。连接统计的聚合方式和表格排序作为界面偏好保存，可随偏好导入导出；不包含统计数据。

## 更新与注意事项

- **从 1.x 升级到 2.x 请解压到新目录并重新配置。** 旧设置、历史、背景图片和启动项不会自动迁移；保留旧目录作为备份。
- 更新 2.x 时，完全退出程序后覆盖应用文件，保留 `settings.json`、核心和配置文件。
- 应用版本变化会清空连接历史、本地背景图片等浏览器数据；已保存的界面偏好会恢复。旧缓存首次切换到当前策略时也会重置一次。
- Secret 受 Windows 当前用户保护。换用户后无法解密时，在 Core 页面重新填写并保存。
- Dashboard 更新会打开下载页，由你手动替换。mihomo 可通过核心接口升级；sing-box 内置升级使用 **reF1nd Windows amd64v3** 构建，其他版本可自行升级。
- sing-box 通过 Clash API 管理，桌面 TUN 栈只读；不支持 native API、Tools 或 Terminal。TUN 睡眠唤醒后的自动恢复尚未验证。
- 本地界面使用端口 `33291`，被占用时无法启动。自启任务存在归属冲突时不会覆盖，请检查已有任务。

遇到问题可查看 `resources/logs`；需要详细日志时，以 `Dashboard.exe --diagnostic-log` 启动。

开发与验证见[开发约定](docs/development.md)；跟进上游时见[上游跟进](docs/upstream.md)。

MIT License；上游前端版权见 [dashboard-src/LICENSE](dashboard-src/LICENSE)。
