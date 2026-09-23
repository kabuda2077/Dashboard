# Windows 手工验收清单

状态：**本轮全部手工用例均未运行（NOT RUN）**。自动化与隔离发布链路已经通过，但不能替代本文的 Windows 手工结果，P10 仍未全部完成。本轮未启动真实核心、未触发 UAC、未创建或删除自启任务、未休眠系统，也未操作用户现有安装或数据。

当前候选ZIP、SHA256、自动化和隔离浏览器证据统一见[验证记录](validation.md)。执行前核对候选产物，不沿用历史包哈希。

## 1. 使用边界与隔离数据

每次实际执行都必须先记录：构建提交、产物 SHA-256、Windows 版本、WebView2 Runtime 版本、显示缩放、测试目录和执行人。结果只能写 `PASS`、`FAIL（附证据）` 或 `BLOCKED（附原因）`，不能用自动化测试结果代替手工结果。

- 将待验收 ZIP 解压到新建的临时目录，例如 `%TEMP%\Dashboard-P10\<run-id>\app`；不得覆盖日常安装。
- 启动前确认隔离目录中没有从开发机复制的 `settings.json`、`logs/`、`resources/EBWebView/`、核心二进制、配置或下载缓存。应用生成的数据必须只留在该目录。
- 只使用合成名称、合成 Secret（例如 `p10-secret-not-real`）和专用回环端口；不得使用真实订阅、节点、令牌或个人浏览数据。
- 需要预置/升级数据的用例，从该用例自己的 `seed` 目录复制到新的用例目录；不同用例不得共用 `settings.json` 或 `resources/EBWebView/`。
- API 401 用例使用一次性回环 fixture，并记录端口与响应；不得连接日常核心。真实 mihomo/sing-box 用例必须使用可丢弃二进制、最小测试配置和独立端口，并由已获授权的专用环境执行。
- 正常版本替换不得整目录删除 `resources/EBWebView`。内容更新应由宿主定向清理 HTTP cache/service worker 并保留 IndexedDB/profile 数据。若要验证“全新 profile”，只能移动或复制隔离用例自己的 profile，并记录该动作。
- UAC、自启、系统休眠/恢复和真实核心用例只能在明确授权的专用 Windows 环境执行；当前均未执行。

每次执行前重新检查固定端口`127.0.0.1:33291`是否被占用；不要沿用历史PID。已有日常Dashboard实例时，不得擅自停止、重启、读取其配置/profile或覆盖安装。涉及现有实例、真实核心、UAC、自启、休眠的操作，需明确目标和动作范围；未获授权时保持NOT RUN。

## 2. 验证依据

生产职责见[当前架构](architecture.md)，自动化与特性覆盖见[验证记录](validation.md)。本文件只维护实机操作、期望结果和执行状态，不复制测试清单。

## 3. 可在无真实核心的隔离目录执行

下列每项当前状态均为 **NOT RUN**。

| ID | 状态 | 前置数据/动作 | 预期行为与证据 |
|---|---|---|---|
| M01 | NOT RUN | 全新隔离目录，无 `settings.json`、无 profile、无核心；启动 `Dashboard.exe` | 打开 Core 页；无用户数据被带入；退出后所有新数据只出现在隔离目录。保存首屏截图及目录清单。 |
| M02 | NOT RUN | 在 UI 中将主题和至少一项非默认 Dashboard 偏好改为可辨识值，等待保存完成，再执行同 WebView reload | reload 后先恢复宿主最新 snapshot 再初始化页面；所选值保持；不闪回 store 默认值；`settings.json` 只含允许的 `config/*` snapshot。 |
| M03 | NOT RUN | 基于 M02 执行“重置 Dashboard 设置”并允许页面 reload | 空 snapshot 明确清除旧 `config/*`；reload 后显示默认值；非 `config/*` 浏览器数据不被误删。 |
| M04 | NOT RUN | 仅在该用例副本中制造 `settings.json` 写入失败，再改一项会 reload 的设置 | 显示保存失败；页面不 reload；草稿仍可见；恢复写权限后重试可成功，旧有效文件在失败期间不损坏。记录提示和前后文件 hash。 |
| M05 | NOT RUN | 连续进行两次设置修改，使第二次发生在第一次 ACK 之前；随后 reload | requestId 只确认对应保存；最终 snapshot 是最后一次已确认值；旧 ACK 不触发错误 reload。若 UI 无法稳定制造乱序，记 `BLOCKED`，不得伪造 PASS。 |
| M06 | NOT RUN | 启用启动 URL 自动导入；使用只含合成 Dashboard 偏好的导入 URL | 每个 document 只询问/导入一次；确认后先收到落盘 ACK 再 reload；reload 后值一致。拒绝导入不改设置、不强制 reload。 |
| M07 | NOT RUN | 使用一次性回环 HTTP fixture 对 `/version`/业务请求返回 401；Secret 只用合成值 | 手动请求只产生一次归属正确的错误提示；旧会话 401 不清除新会话 endpoint；不会把 Secret 写入 `setup/api-list` 或其他 localStorage。保存 fixture 日志与 UI 截图。 |
| M08 | NOT RUN | 正常关闭到托盘，60 秒内重新打开；不启动核心 | WebView 未被释放或待释放被取消；窗口及时恢复；当前路由、未提交 UI 状态和宿主状态不被旧 suspend 结果覆盖。 |
| M09 | NOT RUN | 开启轻量模式，关闭到托盘并等待超过 60 秒，再打开；不启动核心 | WebView 被释放后可重建；重新请求最新 Dashboard snapshot 与 host state；设置一致，无白屏、重复 listener 或旧通知重放。记录隐藏时长和恢复耗时。 |
| M10 | NOT RUN | 用“旧内容”隔离副本先写入 Dashboard 偏好及 IndexedDB 哨兵，再以待验收内容正常覆盖程序文件后启动 | 宿主定向失效 HTTP cache/service worker，新页面资源生效；Dashboard 偏好和 IndexedDB 哨兵保留；不以删除整个 `EBWebView` 作为通过手段。 |
| M11 | NOT RUN | 在环境当前配置的 DPI 下，分别检查正常、最小允许尺寸和最大化；不修改系统 DPI | 窗口控制、拖动/缩放、侧栏、Core/Overview/Proxies/Rules/Connections/Logs 无遮挡；记录实际 DPI。其他 DPI 未有专用机器时标 `BLOCKED`。 |
| M12 | NOT RUN | 同一隔离产物各重复 5 次：冷启动、60 秒内托盘热恢复、轻量释放后恢复；先确认现有诊断输出中确实捕获到 `frontendMounted` 及其单位/起点 | 只有先观察并保存一条真实 `frontendMounted` 记录，才可用它分别记录三类前端挂载时间；未捕获则记 `BLOCKED`，不得推算。报告中位数和离散值；不得用 Vite build 时间或 `showDispatched` 冒充首帧。正确性失败时性能结果无效。 |

## 4. 仅限获授权专用环境

这些也是 P10 发布条件的一部分，但本轮按要求没有执行。每项状态均为 **NOT RUN**；没有对应结果前，不得写“核心/升级/UAC/自启/睡眠恢复已验证”。

| ID | 状态 | 隔离要求 | 预期行为 |
|---|---|---|---|
| S01 | NOT RUN | 可丢弃 mihomo、最小无真实代理配置、独立 API 端口 | 启动/停止/重启、reload config、flush DNS/Fake IP、GEO/升级入口按宿主契约工作；失败有单一明确反馈，无重叠操作。升级只有实际执行后才能单独判定。 |
| S02 | NOT RUN | 可丢弃 sing-box、Clash-compatible API、独立端口 | 启停/重启/升级经宿主；主页面只走 Clash-compatible API；版本优先 `/version`，不可达时显示 exe fallback；不出现 native Tools。 |
| S03 | NOT RUN | 两个测试核心刻意配置为同一 endpoint/Secret，进程互斥 | 切换前提交两个草稿；宿主确认后 core type/PID 使旧 HTTP、WS 和队列结果失效；页面、版本、流只显示新核心，endpoint 相同也不能复用旧 session。 |
| S04 | NOT RUN | 独立核心和日志目录，允许执行更新检查/替换的快照环境 | 检查/升级期间按钮状态与版本点一致；失败清除 stale availability；退出等待已登记任务；替换失败保留旧可运行文件。 |
| S05 | NOT RUN | 明确允许 UAC 的测试账户和快照/回滚点 | 需要提权的路径只在预期点提示；拒绝提权可恢复且无半写设置/孤儿进程。 |
| S06 | NOT RUN | 使用可回滚 VM 或无日常 Dashboard 自启配置的可丢弃 Windows 测试账户，并明确允许修改固定任务 `Dashboard\Autostart` | 生产实现不支持自定义唯一任务名。开启、注销/重启验证、关闭和清理固定任务；XML/可执行路径正确；失败状态回显且不谎报成功。不得在含用户现有同名任务的账户执行。 |
| S07 | NOT RUN | 可休眠/恢复的测试机、可丢弃 mihomo TUN 配置 | 休眠前状态被正确识别；恢复仅在仍属同一运行实例且条件满足时恢复；退出/切换后的旧任务不重启核心。 |

## 5. 补充交互验收

- **NOT RUN：凭证恢复**。在隔离副本使用合成旧明文和测试生成的不可解密密文，验证提示、原件保留、明确替换/留空及两核心隔离，不读取真实用户Secret。
- **NOT RUN：快捷键**。随M11核对页面快捷键、输入框编辑时的抑制及桌面禁用项。
- **NOT RUN：首次设置拒绝与重试**。验证忙碌时不提示完成、不收起向导、不循环提交；显式重试并经宿主确认后关闭。

## 6. 完成判定与交接

- 每个实际执行项必须有独立证据；未执行保持 `NOT RUN`，环境不具备则改为 `BLOCKED` 并说明，不得留空或推断通过。
- 自动化 gate 与干净发布/ZIP 检查已经通过，证据见 [validation.md](validation.md)；M01–M12 中与发布范围相关的项及获授权后要求执行的 S01–S07 尚未执行，因此 P10 仍为未完成。
- 最小应用接线集合没有待补自动化项。剩余证据是本文明确列出的 native/WebView/真实核心/系统手工验收；不为此新增第二套浏览器框架或要求一个全包式 E2E 用例。
