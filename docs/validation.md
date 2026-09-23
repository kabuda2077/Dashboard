# 实施与验证摘要

P1–P9 实现及自动化边界已落地，R4 架构收敛完成，R5 自动化复核与 Release 审计通过。真实 Windows、WebView2、双核心和系统集成验收均未执行；P10/R5 仍为部分完成，所有手工项目保持 **NOT RUN**。

相关文档：[架构](architecture.md) · [手工验收](manual-acceptance.md) · [性能](performance.md) · [上游合入](upstream-merge.md)

## P1–P10 与 R4

| 项目 | 最终结果 |
| --- | --- |
| P1 | 构建、资源、发布和打包失败正确传播；临时输出不会覆盖可用产物。 |
| P2 | 恢复快捷键和既有自动 URL 设置导入，并由 App 接线测试保护。 |
| P3 | 导航、来源、出站状态和宿主命令均受校验；未知或畸形消息无副作用。 |
| P4 | 设置启动恢复、串行 ACK、失败重试、成功去重和明确写入通知已实现；轮询已移除。 |
| P5 | 核心操作 gate、事务回滚、任务登记/退出等待、旧结果失效、原子替换及释放隔离已实现。 |
| P6 | HTTP、WebSocket、队列和版本结果共享会话身份；旧会话隔离，运行数据按需清理重建。 |
| P7 | 桌面认证修正走宿主 Core；错误归属、smart 404 回退和旧列字段兼容已落实。 |
| P8 | 新凭证仅保存 DPAPI 密文；迁移失败保留原件；资源升级定向清缓存并保留 profile。 |
| P9 | 采用设置重复保存去重和停用图表保护；latency map 与启动改造因无实机收益证据而跳过。 |
| P10 | 标准工具链完整检查、隔离发布和包审计通过；实机验收未执行，未全部完成。 |
| R4 | 图标/版本直接消费响应式宿主状态；删除旧回调、孤立timer、桌面URL重复参数；统一双核心入站字段和拒绝结果；统一偏好写入口后取消轮询；共享设置CSS集中定义，删除不可达编辑/排序工具。保留有消费者或数据语义的兼容层。 |

## 当前证据

记录日期：2026-09-23。完整检查和发布使用Node **24.21.0**、pnpm **11.20.0**、.NET SDK **9.0.318**，基于当时未提交工作区的隔离源码副本`.tmp/r4-validation`；不能仅用HEAD复现该候选。源码清单为`.tmp/architecture-consolidation/r4-validation-manifest.csv`，检查/发布日志及包审计位于同目录的`r4-check.*`、`r5-release.*`和`r5-package-audit.json`。这些临时证据不随仓库自动分发。

- 架构收敛阶段完整 `tools/check.ps1`：前端 **54 文件 / 145 项**、后端 **224 项**、构建/发布脚本 **9 场景**通过；vue-tsc、源码契约、生产构建、资源检查和 .NET 构建通过。
- 隔离 fixture：5 个页面 × 明暗主题 × 2 个宽度，共 **20 张截图**最终哈希一致；Core 中 **504 个设置元素**的计算样式和矩形无差异。fixture 不覆盖原生窗口、WebView2 profile、DPI、真实网络或核心。
- R5 在完整检查后新增 **1 个**延迟轮次测试；聚焦复跑为 **3 文件 / 7 项**，并通过 vue-tsc。未重跑完整套件，7 项不计入 145 项。
- R5 ZIP（早于下述源码残留清理，未重新打包）：`artifacts/releases/Dashboard-R5-20260923-171509.zip`，**4,436,486 字节**，SHA256 **`A4FD035768F88AE3F1AB69E51CF1CF1F524A3DB9EABE68A6C84EF08D94D3EAEA`**。166 个条目与 Release publish 集合及逐文件哈希一致；无设置、核心、日志、WebView profile、PDB、依赖目录或 service worker。该包未启动、部署或签署发布结论。
- 历史 P10 记录只证明当时的隔离完整检查和发布链路通过、实机未验收；其旧计数和候选包不作为当前证据。

清理前的调查、阶段计划和完整历史报告已备份于项目外`D:/Project/Dashboard-docs-backup-20260923-173525`；它们不是当前维护入口。

## 后续源码残留清理（2026-09-23）

删除无消费者的UpgradeCoreModal、UpdateConfigModal、DataLine、SignalStrength、ProxyNodeGrid及public/icon.svg；同时清除ProxyNodeGrid失效构建断言、旧弹窗独占helper/API导出和四语文案。删除未接通的.lintstagedrc.yaml、prepare/husky、lint-staged及sort-package-json依赖，锁文件仅删除相关依赖项，未升级保留依赖。移除tsconfig中不存在的Cypress/Nightwatch/Playwright配置匹配项。

使用Node24.21.0和pnpm11.20.0，在系统临时目录隔离副本执行frozen-lockfile安装、前端全套**55文件146项通过**、vue-tsc、桌面Vite生产构建、源码契约及**9个脚本场景通过**。后端未改，不重复运行旧224项后端测试，也不将此次前端验证称为重跑完整check.ps1。原候选ZIP未更新。没有启动实际Dashboard或执行系统操作。

## 构建入口整理（2026-09-23）

根目录build.ps1、check.ps1、create-release.ps1移至tools/，不保留转发副本。三个脚本从自身目录的上一级定位仓库根，发布脚本使用明确的同目录build入口；CI、文档及脚本测试均更新。脚本回归额外核对外部工作目录调用和子脚本定位。

在系统临时隔离副本中，从仓库外目录运行完整`tools/check.ps1 -Configuration Release`（无跳过参数）与完整`tools/create-release.ps1`：**前端55文件146项、后端224项、9个脚本场景、类型检查、前端构建、资源检查、.NET Release构建及ZIP生成全部通过**。Node24.21.0/pnpm11.20.0；未运行Dashboard或实机核心。迁移不改变应用逻辑，现有交付ZIP未覆盖。

## 必须保留的行为语义

- `DashboardSettings = null` 表示保留旧偏好，空对象表示明确清空；增量导入、启动替换和用户重置不得混同。
- 非宿主浏览器保留 `/setup`、凭证保存和后端更新事件；桌面连接由宿主管理。smart 仅对 404 回退旧端点；网络错误、5xx 和分组部分失败不发布部分结果。
- 旧 native 列字段、隐藏设置偏好和无效旧存储键继续兼容；旧会话或旧 ACK 不得覆盖新状态。
- 宿主写盘失败回滚宿主管理状态，但不覆盖并发保存的 Dashboard 偏好；失败 ACK 可重试。
- 核心替换失败保留旧版本；恢复也失败则保留备份并报告。DPAPI 解密/迁移失败保留原设置或密文，不写空 Secret、不建明文备份。
- 正常升级保留 WebView profile，只清理可重建缓存；清理成功后才提交版本标记。显式重置和不可恢复损坏必须告知用户。
- `Executed` 仅表示命令进入执行路径，不代表核心、升级或提权成功；busy/closing 是明确拒绝。自启系统操作失败时，允许核心命令按已保存配置继续执行，保留“部分成功”而不整体回滚。

## F01–F18 覆盖映射

| ID | 自动化／隔离覆盖 | 手工待验收 |
| --- | --- | --- |
| F01 启动恢复 | startup、ACK、宿主快照/router | reload、失败恢复、导入 |
| F02 窗口托盘 | MainForm、context、router、trust | 托盘、窗口、轻量释放、DPI |
| F03 单核心事务 | lifecycle、gate、snapshot、拒绝组合 | 双核心与系统操作 |
| F04 双草稿 | Core 真组件、切换 payload | 真实双核心切换 |
| F05 401 | Core/HTTP、route/session | 真实 401 交互 |
| F06 凭证 | DPAPI、迁移、回滚、显式替换、前端存储 | 合成旧明文/不可解密密文交互 |
| F07 版本 | API/fallback、PID/session、updater | 双核心版本与更新 |
| F08 操作栏 | 组件顺序、GEO 排除、升级入口 | 原生布局与实际操作 |
| F09 图标 | cache/bridge/ProxyIcon、DOMPurify fixture | 真实核心缓存生成与显示 |
| F10 延迟 | 四目标十轮、统计、重测、tooltip、chart 生命周期 | 真实 API 与布局 |
| F11 测速通知 | provider、smart、手动反馈、HTTP/session | 真实测速和错误表现 |
| F12 会话 | backend、Home、WebSocket、host message | 切核/PID/凭证实流 |
| F13 表卡 | 旧字段、分组、runtime、fixture 截图 | 真实数据与窗口布局 |
| F14 视觉 | 20 截图、504 元素、断点组件 | WebView2、DPI、窗口状态 |
| F15 偏好 | observer、ACK、导入、可见性、Storage | reload、真实 IndexedDB/历史 |
| F16 数据升级 | profile sentinel、固定 origin/server、回滚 | 数据保留与真实升级 |
| F17 快捷键恢复 | wiring、keyboard、Home、chart lifecycle | 输入抑制、托盘、睡眠恢复 |
| F18 产品排除 | desktop controls 与禁用项构建契约 | 实机确认未暴露排除项 |

## 剩余风险

固定端口可能与现有 Dashboard 冲突，自启任务也不因目录不同而隔离。真实核心、UAC、自启、升级中退出、睡眠恢复、托盘/轻量模式、文件交互、DPI、WebView2 数据保留及迁移尚无实机证据；fixture 和单元测试不能覆盖驱动、权限、时序、网络与真实数据组合。

实际凭证恢复、快捷键输入抑制及首次设置拒绝/重试也尚未运行，统一按[手工验收](manual-acceptance.md)记录为 **NOT RUN**。冷启动、热恢复和释放后恢复没有实测毫秒结果，测量边界见[性能](performance.md)。
