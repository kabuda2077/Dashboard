# 维护约定

本文只保留当前维护边界，不记录实施流水账、历史测试结果或未经重新确认的待办。运行、构建与测试命令见 [README](../README.md)，上游流程见 [upstream-merge.md](upstream-merge.md)，视觉约定见 [style.md](style.md)。

## 配置与数据

- 当前使用 `schemaVersion=2` 和独立的 `resources/webview-data-v2/`。不迁移或删除 1.x 的设置、凭证、历史、图片或启动项。v2 的浏览器更新重建规则见下文。
- 无设置文件时创建完整默认文档；已有文件不符合当前格式时明确报错，保留原件，不静默覆盖。字段定义和校验以 [AppSettings.cs](../src/AppSettings.cs)、[SettingsStore.cs](../src/SettingsStore.cs) 为准，不在文档中复制字段清单。
- 磁盘 profile 键为 `singBox`，桥接键为 `sing-box`，不可混用。宿主与前端应整包更新。
- Secret 使用当前用户 DPAPI；无法解密时，使用该凭证前要求显式替换，普通设置保存不得覆盖原密文。实现见 [SecretProtector.cs](../src/SecretProtector.cs) 和 SettingsStore。
- 窗口重建、同版本重启或同版本前端资源替换保留浏览器数据。应用版本变化或缺少有效版本标记时，在创建 WebView 前删除 `resources/webview-data-v2/EBWebView`；不扫描资源指纹。版本标记存于独立的 `resources/webview-data-v2/.webview-content-version`，清理成功后原子保存纯版本号；失败不标记完成、不继续启动。首次切换旧策略重置一次，不迁回旧浏览器数据。
- 浏览器重建会清空连接历史、本地背景等数据；宿主 settings.json、核心、配置、icon-cache、日志及 1.x 的 `resources/EBWebView` 不得删除。已提交的宿主界面偏好仍按 v2 启动协议恢复。
- 未解决的旧 TUN 睡眠唤醒自动重启逻辑已撤下；新的恢复方案需单独实现和验证。
- 无摘要升级的确认请求（`ConfirmUnverified`）必须携带 `expectedRevision` 和 `expectedRuntimeEpoch`。普通升级请求允许省略，携带时也必须匹配当前状态。校验见 [CoreLifecycleController.cs](../src/CoreLifecycleController.cs)。

## 验证与产物

- 日常修改先跑相关测试；文档/工具修改运行 `pwsh -NoProfile -File .\tools\check-maintenance.ps1`。最终交付直接运行 Release 入口，由它验证本次输入，不先重复完整 Check。
- `.tmp/tests/` 放带独立运行/用例标识的测试数据（下载的测试核心夹具在 `tests/fixtures/` 子目录）；`.tmp/experiments/` 放临时调查和验证副本；`.tmp/reports/` 放日志、截图、wire fixture 和性能报告。共享测试目录辅助类负责清理，WebView 测试先等待自己创建的浏览器退出；失败有限重试并记录路径，不静默遗留。`artifacts/releases/` 只放 ZIP，`artifacts/verification/<包名>/` 放对应输入清单和验证摘要，历史候选放 `artifacts/archive/`。不把运行产物复制进 `docs/`。应用只编译 `src/**/*.cs`，临时目录、归档和依赖缓存必须排除在默认项目项之外，不能仅依赖 .gitignore。
- pnpm store/cache 使用正常用户配置，脚本不注入 store 覆盖或固定机器路径。工具版本分别由 `.node-version` 与前端 `packageManager` 声明；Node runtime 由 pnpm 共享缓存管理，不维护 `.tmp/toolchain`。生产程序的系统临时目录、核心同盘暂存和备份不随测试目录调整。
- 测试代码存在不代表本次已经执行；成功结果只覆盖实际输入、环境和执行范围，不代表当前分支没有其他回归。
- 集成测试按需显式启用，参数见 README；实际筛选规则见 [pipeline.ps1](../tools/internal/pipeline.ps1)。在线升级测试 `OnlineUpgradeIntegration` 被常规 Check/Release 排除，需要单独选择并准备隔离核心；`alreadyLatest` 不证明实际下载并替换了新版。
- 性能比较使用同环境、同负载，并验证窗口真正恢复可用。测试宿主内存、离屏渲染和短时压力测试不能替代应用内存、物理显示器帧率或长期泄漏验证。

## 系统场景的验证边界

以下是按改动范围选择的发布检查项，不是当前缺陷或未完成任务清单。已有自动化测试只能覆盖其实际断言，是否完成现场验收需以对应候选的执行结果确认。

| 涉及改动 | 需要关注的现场场景 |
| --- | --- |
| 启动、提权、自启、凭证 | 实际 UAC 同意/取消、登录任务生命周期、跨账户 DPAPI、缺 Runtime/损坏包、生产程序单实例与提权接力 |
| 窗口、布局、输入 | 不同 DPI、跨物理显示器、触摸与原生焦点；自动布局检查不替代设备验收 |
| 持久化、发布、资源更新 | 完全退出生产 EXE、覆盖应用文件再启动；验证宿主数据保留、浏览器只按版本重建、同版本资源替换不重置，且不触碰 1.x 数据；宿主/UI 资源替换测试不等于完整 EXE 更新体验 |
| 核心升级、网络、剪贴板 | 实际新版本下载与失败恢复、目标网络联通性、系统剪贴板成功路径；受控接口与候选测试不替代现场条件 |
| 睡眠与网络恢复 | 专用 TUN/物理网络上的睡眠唤醒，以及期间切核、重启 |

涉及系统任务、UAC、TUN、电源或用户数据的验证使用专用账户/VM、隔离核心与数据，并获得相应授权，不接管用户现有实例。当前提权路径会重启整个桌面程序；可信桥与 DPAPI 不构成宿主和 WebView 之间的强权限隔离。

## 文档维护方式

更新现有约定，不追加实施流水账、阶段审计或验收总表。问题优先形成可复现步骤和正式回归测试；只有需要长期遵守、且代码和测试无法清楚表达的约定才进入文档。上游基线记录仅保留下次合并所需的版本与取舍。
