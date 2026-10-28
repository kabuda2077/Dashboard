# 开发约定与验证

本文供修改相关模块或使用检查、打包工具时按需查阅，不要求每次任务阅读。工作规则见 [AGENTS.md](../AGENTS.md)；上游比较、采用与登记仅使用 [upstream.md](upstream.md)。功能和使用限制见 README，具体行为、尺寸和参数以代码及测试为准，不在本文维护完整功能清单。

| 本次涉及 | 查阅位置 |
| --- | --- |
| 核心进程、提权、自启或本机权限 | [宿主边界](#desktop-shell) |
| 切核、异步结果、页面或共享任务生命周期 | [会话与生命周期](#host-session) |
| 配置、凭据、偏好或浏览器数据 | [持久化](#preferences) |
| 布局或控件 | [视觉](#desktop-visuals)或[共享控件](#shared-controls) |
| 网络结果、批量断开或缓存 | 下方对应模块的边界说明 |
| 选择检查或生成 ZIP | [验证选择](#validation)或[验证与产物](#verification) |

<a id="excluded-product-scope"></a>
## 产品与宿主边界

Dashboard 面向 Windows 桌面，核心接口为 mihomo / sing-box 的 Clash-compatible API，一个活动核心、两个独立 profile。多核心同时运行、其他原生协议和移动平台支持不属于当前架构；新增支持需先确定产品范围。上游模块的具体排除项仅在跟进时查阅 upstream。

<a id="desktop-shell"></a>
### 核心与系统操作

- 宿主管理进程、窗口、托盘、自启、文件、凭证和核心升级；浏览器预览没有本机宿主权限，连接记录独立。核心操作走统一入口，不排队；运行状态不得覆盖未保存草稿，其他桌面设置不得顺带提交草稿。
- 启动成功、API 就绪、配置保存完成分别确认。提权前完成保存与退出确认，取消或失败保留旧宿主和恢复入口；可信桥和 DPAPI 不构成宿主与 WebView 间的强权限隔离。
- TUN 栈仅在 API 确认能力后可写，桌面 sing-box 只读；修改保留当前开关并回读实际状态，不视为 profile 保存。
- 自启变更核对用户、exe 和工作目录，归属不明或不匹配时拒绝覆盖；退出等待已接受的变更和保存结果。核心升级失败恢复原文件并尝试重启，无摘要升级需明确确认并绑定当前配置和运行上下文。
- 关闭到托盘、挂起、释放 WebView、退出各有生命周期。生产 UI 来源为 `http://127.0.0.1:33291/`，占用报错；更改来源需检查 API/WS 与安全策略，不关闭浏览器安全限制。

<a id="host-session"></a>
## 会话与数据生命周期

- 宿主拥有身份、会话代次和操作结果。重启或切核即使端点不变，也使旧请求失效；旧结果不得回写，命令与响应必须配对。
- 所有页面离开销毁、返回重建，包括概览；不使用整页 KeepAlive。配置和数据流由共享任务统一管理，跨导航存活，不因页面或图表挂载重复启动。
- 未知、失败、只读和关闭状态不能混用。清空内容不清除当前鉴权或连接错误；写入失败恢复已确认值，取消不得提交草稿。

<a id="preferences"></a>
## 配置、凭据与持久化

- 宿主与前端使用配套的 v2 配置、桥接和独立 WebView 数据目录，整包更新；不迁移或删除 1.x 数据、凭证和启动项。无配置时创建默认值，格式损坏保留原件并报错，不静默修补。校验入口为 [AppSettings.cs](../src/AppSettings.cs) 和 [SettingsStore.cs](../src/SettingsStore.cs)。
- Secret 使用当前用户 DPAPI；解密失败时要求显式替换，普通保存保留原密文。显隐状态不持久化，离开字段或切 profile 后复位。
- 启动先恢复偏好再初始化模块。长期界面偏好归 `config/` 并同步宿主、参与导入导出；查询缓存、内容和操作现场不混入设置。保留独立草稿、原子保存与保存确认，挂起/释放前等待偏好、历史和图片处理完成，退出保存未确认须反馈。
- 同版本重启、WebView 释放后重开、窗口重建和资源替换保留浏览器数据。版本变化或版本标记不匹配才重置 v2 浏览器数据，清空浏览器历史与本地背景，保留宿主设置、核心、配置、图标缓存、日志及 1.x 数据，随后恢复已提交偏好。重置和版本记录失败不启动 WebView；旧缓存策略切换只重置一次。

<a id="desktop-visuals"></a>
## 布局与视觉

保持紧凑、对齐、可读，复用 DaisyUI 语义色和容器。桌面适配优先放在使用组件的页面或最后导入的 `dashboard-desktop.css`，避免修改共享默认值影响其他页面。

Core 的 profile/options 分区、紧凑表单、就近保存、独立模态草稿和简洁运行信息，以及侧栏的既定文字/图标密度、选中对比和工具栏等高对齐，均为已确认选择。具体尺寸以现有样式为准；无关任务不顺带改版。应用、托盘和网页保留本地品牌，图标生成来源一致。

按可用空间和输入方式调整布局，不用窗口宽度判断移动设备。长译文、长路径、缩放和低高度下仍保留窗口控件、键盘入口与必要操作；自动布局不覆盖用户选择。dock、抽屉、触摸、滑动与动画可用于桌面，按实际用途判断。

<a id="shared-controls"></a>
### 共享控件

修改共享控件或样式前确认使用页面，登记中的 watch 不能替代实际依赖查询。手势不得抢占编辑、拖动、横向表格或纵向滚动；浮层维持关闭、焦点返回及窗口变化后的定位。具体键盘行为由组件和既有测试维护。

<a id="notifications"></a>
## 外部内容与诊断

API、宿主和外部错误、tooltip 按文本显示，富内容单独清洗。表单或会话变化取消诊断，过期结果不提示或保存；辅助探测不带凭证且有超时，无法区分网络原因时保留不确定性。复制或保存成功后才提示成功。

<a id="proxy-experience"></a>
## 代理交互

虚拟化与渲染可替换，修改时保持选择、定位和滚动恢复结果。卡片展开和遮罩交互复用既有组件；关闭、焦点及窗口变化后的定位按本次影响验证。

<a id="tables-history"></a>
## 批量操作、日志与缓存

- 批量关闭只处理可见且仍活跃的连接；空结果、仅已关闭、暂停及恢复后新快照到达前不发批量断开。
- 来源标签和 GeoIP 等展示信息不改变底层地址或匹配输入；后端、配置或会话变化后不得复用错误范围的缓存。反向 DNS 手动标签优先，查询有超时、并发和容量上限，关闭后不回写；持久缓存不保存 Secret。
- 日志采集有容量上限，暂停/隐藏仅冻结显示，清空包含待显示记录。WebView 释放后不保证继续采集，前端日志不能替代宿主日志服务。

<a id="overview-charts"></a>
## 网络检查与图表

- 共享统计和网络任务遵循会话生命周期；图表只负责绘制，数据有容量上限，隐藏、脱离文档或暂停时不绘图。
- HTTP 请求耗时不等于 Ping、节点延迟或网站完整可用性。分流结果只采用与宿主测试连接配对的证据，不猜策略组选择、不改配置；缺证据时显示未确认，测试进程和入站不能代表所有应用。
- 出口 IP 以实际请求观测为准，归属查询不能替换地址；归属缺失时仍保留 IP，不擅自切换服务。隐私显示同时处理地址与详细归属，避免部分结果泄漏已隐藏的信息。

<a id="validation"></a>
## 验证选择

通用范围按 AGENTS 执行，按本次改动选择以下入口：

| 改动 | 项目检查条件 |
| --- | --- |
| 文档 | 已有 Documentation 检查；涉及登记时核对规则引用 |
| 前端逻辑或接口 | 对应行为测试和类型检查；运行 wire 测试前由本次宿主测试生成 `.tmp/reports/bridge-fixtures-v2/`，不用旧 fixture |
| 宿主桥接、会话或页面销毁/重建 | 对应宿主和 WebView 检查；修改闲置释放时核对生产 60 秒及重开取消，再加速测试计时器 |
| 样式 | 核对实际页面；作前后比较时保持数据、字体、主题、语言与窗口尺寸一致。需要 CoreLayout 完整比较采集时可设 `DASHBOARD_UI_VISUAL_COMPARISON=1`，结束清除 |
| 代理过滤、测速、虚拟滚动或折叠动画 | 对应 Chrome 脚本；不加入日常 Check/Release，修改脚本本身时验证相应入口 |
| 性能、在线升级、系统集成 | 单独选择相关检查；宿主测量不等于应用内存或物理帧率，`alreadyLatest` 不证明实际升级 |

<a id="test-cases"></a>
### 用例取舍

- 优先更新或扩展已有测试；新增须对应尚未覆盖的重要行为、接口约定或可复现缺陷，验证行为而非复刻实现。参数化只覆盖不同路径和必要边界，不机械增加组合；纯样式按实际页面验证。
- 替换实现时删除失效预期；合并同条件、同路径的重复用例并保留独有断言，不因名称相似删除不同契约或异步阶段的覆盖。按独立故障发现能力和维护成本取舍，不追求数量增减。

<a id="local-tooling"></a>
## 环境与工具

需要 PowerShell 7、.NET 10 SDK 和 pnpm 启动器。Node/pnpm 版本由 `.node-version` 和 `dashboard-src/package.json` 固定，使用冻结 lockfile 和用户缓存；现有入口准备依赖并构建前端，不改全局 Node 或另建项目工具链缓存。应用版本与前端包版本分别管理。

Windows 后台进程显式隐藏窗口，保留日志、退出码并确认退出；可见交互事先说明，不改全局 TEMP/TMP。项目只编译 `src/**/*.cs`，显式排除临时和依赖目录，不能只靠 `.gitignore`。

<a id="verification"></a>
## 检查与打包入口

单项改动使用已有对应检查。需要整组检查时，`check.ps1` 默认 `-Scope Application`，文档/工具为 `-Scope Maintenance`，两者为 `-Scope All`。正式候选直接运行 Release，不先重复完整 Check：

```powershell
pwsh -NoProfile -File .\tools\check.ps1

# 正式候选：准备隔离核心，一次完成检查、编译和打包
pwsh -NoProfile -File .\tests\scripts\PrepareValidationCores.ps1
$env:DASHBOARD_TEST_CORES_DIR = (Resolve-Path .tmp/tests/fixtures/validation-cores).Path
pwsh -NoProfile -File .\tools\create-release.ps1 -IncludeWebViewIntegration -IncludeRealCoreIntegration
```

`check.ps1` 同样支持 WebView/核心集成开关；性能检查按需加 `-IncludePerformanceIntegration`。Release 对 HEAD 和 tracked/untracked 输入做快照，验证与归档前重新比对，变化即停止并保留旧包。派生 app/tray.ico 不参与源快照，ZIP 仍逐项核对文件与哈希；Version、InformationalVersion 和正式 tag 一致。`-OutputZip` 只接受 ZIP 文件名。

代理 Chrome 脚本在 `dashboard-src/` 下按需运行，先构建当前前端；需要本机 Chrome/Chromium，可用 `CHROME_BIN` 指定。默认无界面，使用隔离模拟后端、构建快照和浏览器数据，性能仅作同机同负载比较：

```powershell
pnpm test:proxy:verify
pnpm test:proxy:collapse
pnpm test:proxy:bench --groups 150 --nodes 60 --cpu 10
```

| 路径 | 用途 |
| --- | --- |
| `.tmp/tests/<run>/<case>/` | 隔离测试数据，复用核心放 `fixtures/` |
| `.tmp/experiments/`、`.tmp/reports/` | 调查副本；日志、截图、wire 和测量证据 |
| `artifacts/releases/`、`artifacts/verification/<包名>/` | ZIP；输入清单和验证摘要；历史包放 `artifacts/archive/` |

<a id="system-boundaries"></a>
## 系统验证边界

按改动选择现场检查，使用授权的专用账户/VM 和隔离数据；不要由自动测试通过推断这些结果。仅需核对已有缺口时查 ownership 的 `knownGaps`。

| 涉及改动 | 现场检查 |
| --- | --- |
| 启动/提权/自启/凭证 | UAC 同意/取消、登录任务、跨账户 DPAPI、缺 Runtime/损坏包、单实例与提权接力 |
| 窗口/输入 | DPI、跨显示器、触摸、原生焦点 |
| 持久化/发布 | 完全退出后覆盖启动，宿主数据保留、版本重置、同版本资源替换、不触碰 1.x |
| 升级/网络/剪贴板 | 实际替换及失败恢复、目标网络、系统剪贴板 |
| 睡眠恢复 | 专用 TUN/物理网络睡眠唤醒及期间切核/重启 |
