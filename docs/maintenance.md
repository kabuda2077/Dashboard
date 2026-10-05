# 维护约定

本文只保留当前维护边界，不记录实施流水账、历史测试结果或未经重新确认的待办。运行、构建与测试命令见 [README](../README.md)，上游流程见 [upstream-merge.md](upstream-merge.md)，视觉约定见 [style.md](style.md)。

## 配置与数据

- 当前使用 `schemaVersion=2` 和独立的 `resources/webview-data-v2/`。不迁移旧 Dashboard 设置、凭证、历史、图片或启动项，不自动删除旧数据。
- 无设置文件时创建完整默认文档；已有文件不符合当前格式时明确报错，保留原件，不静默覆盖。字段定义和校验以 [AppSettings.cs](../src/AppSettings.cs)、[SettingsStore.cs](../src/SettingsStore.cs) 为准，不在文档中复制字段清单。
- 磁盘 profile 键为 `singBox`，桥接键为 `sing-box`，不可混用。宿主与前端应整包更新。
- Secret 使用当前用户 DPAPI；无法解密时，使用该凭证前要求显式替换，普通设置保存不得覆盖原密文。实现见 [SecretProtector.cs](../src/SecretProtector.cs) 和 SettingsStore。
- 当前格式的数据应在窗口重建、应用重启和覆盖更新后保留；升级应用不得删除用户设置、核心、配置或 WebView 数据目录。
- 无摘要升级的确认请求（`ConfirmUnverified`）必须携带 `expectedRevision` 和 `expectedRuntimeEpoch`。普通升级请求允许省略，携带时也必须匹配当前状态。校验见 [CoreLifecycleController.cs](../src/CoreLifecycleController.cs)。

## 验证与产物

- 日常修改先跑相关测试；文档/工具修改运行 `pwsh -NoProfile -File .\tools\check-maintenance.ps1`。最终交付直接运行 Release 入口，由它验证本次输入，不先重复完整 Check。
- `.tmp/` 放临时复现、日志、截图和性能报告；`artifacts/releases/` 只放 ZIP，`artifacts/verification/<包名>/` 放对应输入清单和验证摘要。不把运行产物复制进 `docs/`。
- 测试代码存在不代表本次已经执行；成功结果只覆盖实际输入、环境和执行范围，不代表当前分支没有其他回归。
- 集成测试按需显式启用，参数见 README；实际筛选规则见 [pipeline.ps1](../tools/internal/pipeline.ps1)。在线升级测试 `OnlineUpgradeIntegration` 被常规 Check/Release 排除，需要单独选择并准备隔离核心；`alreadyLatest` 不证明实际下载并替换了新版。
- 性能比较使用同环境、同负载，并验证窗口真正恢复可用。测试宿主内存、离屏渲染和短时压力测试不能替代应用内存、物理显示器帧率或长期泄漏验证。

## 系统场景的验证边界

以下是按改动范围选择的发布检查项，不是当前缺陷或未完成任务清单。已有自动化测试只能覆盖其实际断言，是否完成现场验收需以对应候选的执行结果确认。

| 涉及改动 | 需要关注的现场场景 |
| --- | --- |
| 启动、提权、自启、凭证 | 实际 UAC 同意/取消、登录任务生命周期、跨账户 DPAPI、缺 Runtime/损坏包、生产程序单实例与提权接力 |
| 窗口、布局、输入 | 不同 DPI、跨物理显示器、触摸与原生焦点；自动布局检查不替代设备验收 |
| 持久化、发布、资源更新 | 完全退出生产 EXE、覆盖应用文件再启动，验证当前格式数据保留；宿主/UI 资源替换测试不等于完整 EXE 更新体验 |
| 核心升级、网络、剪贴板 | 实际新版本下载与失败恢复、目标网络联通性、系统剪贴板成功路径；受控接口与候选测试不替代现场条件 |
| 睡眠与网络恢复 | 专用 TUN/物理网络上的睡眠唤醒，以及期间切核、重启 |

涉及系统任务、UAC、TUN、电源或用户数据的验证使用专用账户/VM、隔离核心与数据，并获得相应授权，不接管用户现有实例。当前提权路径会重启整个桌面程序；可信桥与 DPAPI 不构成宿主和 WebView 之间的强权限隔离。

## 文档维护方式

更新现有约定，不追加实施流水账、阶段审计或验收总表。问题优先形成可复现步骤和正式回归测试；只有需要长期遵守、且代码和测试无法清楚表达的约定才进入文档。上游基线记录仅保留下次合并所需的版本与取舍。
