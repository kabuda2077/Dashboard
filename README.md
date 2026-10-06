# Dashboard

[简体中文](README.md) | [English](README.en.md)

基于 [zashboard](https://github.com/Zephyruso/zashboard) 的 Windows 桌面代理管理面板。WinForms + WebView2 承载本地 UI，宿主管理 mihomo / sing-box、窗口、托盘、自启、凭证和升级；面板通过 Clash-compatible API 读取数据。

## 2.0 配置格式

**2.0 是不兼容升级，使用 schemaVersion=2 的 settings.json 和独立的 `resources/webview-data-v2/`。不迁移旧 Dashboard 设置、历史、图片、旧明文凭证或旧启动项。**

首次使用建议解压到一个新目录，重新选择核心和配置。旧文件不会自动删除或覆盖；遇到旧格式/损坏设置时程序明确报错。已存在的 v2 文件必须保留完整的根字段、两个 profile 和桌面选项字段，缺失不会被静默补成默认值。请保留原目录，不要删除唯一的数据副本。

同版本的窗口重建、应用重启或前端资源替换保留浏览器数据。应用版本变化或缺少有效版本标记时，在 WebView 启动前删除 v2 自己的 `resources/webview-data-v2/EBWebView`，成功后保存纯版本号；不删除 1.x 的 `resources/EBWebView`。连接历史、上传的本地背景图片等浏览器数据会清空；`settings.json`、核心、配置、icon-cache 和日志保留，已提交的界面偏好从宿主恢复。清理失败不继续启动、不标记完成；首次从旧缓存策略切换也会重置一次。

Secret 由 Windows 当前用户 DPAPI 保护；换用户后无法解密时必须在 Core 页面明确替换，即使新值为空。普通设置保存不会覆盖无法解密的密文。

## 运行条件

- Windows x64、[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)。
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。
- 自备配置好的 mihomo 或包含 Clash API 的 sing-box。发布包不包含核心和用户配置。

示例目录：

```text
Dashboard/
  Dashboard.exe
  mihomo/mihomo.exe
  mihomo/config.yaml
  sing-box/sing-box.exe
  sing-box/config.json
```

在 Core 页面选择要编辑的核心槽，填写 exe/config/API/Secret 并保存。两套草稿独立；Windows 开关不会顺带保存未提交核心字段。文件选择只回填草稿。启动或重启需要提权时会请求 UAC。

mihomo 必须配置 `external-controller`；sing-box 使用 `experimental.clash_api.external_controller`。例如 API 地址为 `http://127.0.0.1:9090`，Secret 与内核配置一致。进程启动与 API 就绪是不同状态；API 未就绪时可修正地址/凭证并重试。

## 功能和边界

- 一个活动内核、两份独立 profile，支持启动、停止、重启、切换、PID/输出日志。
- Core 内嵌设置；代理、连接、概览、规则、日志、主题、标签、测速、当前格式的背景/历史和偏好导入导出。
- 托盘、窗口拖拽/缩放/最大化，关闭到托盘；轻量模式默认隐藏 60 秒后释放 WebView，提前重开取消释放。
- view 挂起/释放前等待偏好、历史及图片读取/解码/提交；退出未确认保存时给出反馈，不把发送消息当成已经落盘。提权前先完成保存/退出确认，失败或取消不关闭旧宿主。
- 当前用户计划任务 `\Dashboard\Autostart` 登录后约 5 秒静默启动。写入/删除前核对用户、exe 和工作目录；同名任务属于其他用户/目录或无法确认归属时拒绝操作，不强制覆盖，也不迁移旧注册表 Run 项。
- mihomo 升级通过运行中核心的 `/upgrade`；sing-box 内置升级仅选择 reF1nd Windows amd64v3 构建。官方版/其他 fork 用户如不希望替换为该构建，应自行升级。
- sing-box 有有效发布摘要时验证 SHA256；没有摘要时在执行候选/替换前要求明确确认。下载、解压、版本验证、备份/原子替换有边界。
- Dashboard 应用更新只检查 Release 并打开下载页，不自动替换 Dashboard.exe。
- 未经可靠验证的旧 TUN 睡眠唤醒自动重启逻辑已撤下；自动恢复需另行实现和验证，目前不宣称已解决。
- 当前桌面版没有 sing-box native API / Tools / Terminal 等功能。浏览器 Clash 预览仍可用，但不具备本机宿主权限。
- UI 保持 `http://127.0.0.1:33291/`，端口占用明确报错。HTTPS 虚拟主机映射会阻止现有明文远端 API，本版本未通过关闭浏览器安全来规避它。

同格式覆盖更新：完全退出，覆盖应用包文件后重开，不删除整个目录。包不包含 settings.json、profile、核心、日志。基础日志在 `resources/logs`；可用 `--diagnostic-log` 开启详细诊断，日志队列和文件轮转均有界。

## 开发与验证

需要 **PowerShell 7**（`pwsh`）、.NET 9 SDK、Node.js **24**、pnpm **11.20.0**。构建和测试脚本只支持 PowerShell 7；运行打包后的 Dashboard 不需要 PowerShell。两个应用入口按目的二选一，不要先完整 Check 再重复 Release：

```powershell
# 安装依赖、类型检查、构建、宿主测试及真实 JSON fixture、前端测试
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1

# 对本次输入重新检查，再发布、核对资源与 ZIP；不信任旧产物
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\create-release.ps1
```

发布目录 `artifacts/releases/` 只放 ZIP；每个包的输入清单与验证摘要放在 `artifacts/verification/<包名>/`，不需要复制到安装目录。相同包名可直接重新生成，不再为每次试编译创建新名称。历史候选可归档到 `artifacts/archive/`。

日常修改先运行相关测试；前端全套很短，可直接运行。需要前端 wire fixture 时先运行宿主测试。UI 修改加跑相关 WebView 测试即可；定稿后再执行一次 Release。构建工具和文档修改运行 `pwsh -NoProfile -File .\tools\check-maintenance.ps1`；CI 同时运行此入口和普通 Check，应用构建不重复执行这些维护检查。

`check.ps1 -IncludeWebViewIntegration` 增加真实 WebView/生产窗口测试，不包含真实等待60秒的空闲回收测试。增加 `-IncludeSlowIntegration` 才运行该慢测试（必须同时启用 WebView）。真实核心验证需显式准备只用于测试的核心：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\PrepareValidationCores.ps1
$env:DASHBOARD_TEST_CORES_DIR = (Resolve-Path .tmp/validation-cores).Path
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\check.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration
```

上述检查用于专项验证；最终交付时直接把同样的参数传给 `create-release.ps1`，需要完整生命周期验收时另加 `-IncludeSlowIntegration`，无需先再跑一次 Check。

准备脚本仅下载并校验 GitHub 发布摘要，不更改系统配置。真实核心测试使用临时目录、无 TUN/代理监听的最小配置，不接管用户核心。

在上面的完整检查命令中增加 `-IncludePerformanceIntegration`，可运行独立测试宿主中的真实 WebView 固定负载测量（3 轮，100/1000/10000 条合成连接）。报告写入 `.tmp/performance-v2/desktop.json`；它包含测试宿主开销，不是物理显示器帧率或纯 Dashboard.exe 内存。局部计算基准可在 `dashboard-src/` 下运行 `node.exe node_modules/vitest/vitest.mjs run --config bench/vitest.config.ts`。

内部构建/图标/资源工具在 `tools/internal/`，无需手工拼接执行。前端资源从 `dashboard-src/dist/` 生成到 `resources/dashboard/`，不入 Git；没有 UI 资源时 .NET 构建会明确失败。前端 wire 测试读取本次 .NET 测试生成的 `.tmp/bridge-fixtures-v2/`，不要用旧 fixture 冒充当前协议验证。

自动检查和 ZIP 成功不等于手工系统场景全部通过；仍需补齐的验收边界见[维护说明](docs/maintenance.md)。项目版本、InformationalVersion 和正式 Release tag 必须一致。

## 维护文档

- [维护说明](docs/maintenance.md)：当前配置约定、验证与产物规则、剩余验收事项；不维护实施流水账。
- [上游跟进指南](docs/upstream-merge.md)：唯一正式跟进入口，保护产品保证，允许有证据地替换实现。
- [样式规则](docs/style.md)：视觉原则与当前默认，不冻结类名/组件内部实现。
- [上游基线](docs/upstream/v3.26.0.md)：下次合并所需的确切版本与保留取舍。

MIT License；内置前端原版权见 `dashboard-src/LICENSE`。
