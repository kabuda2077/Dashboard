# 性能测量与取舍

本记录保留P9阶段的实测方法与取舍；以下环境、计数与计时属于当时快照。当前架构见[docs/architecture.md](architecture.md)，发布验证见[validation.md](validation.md)。调用次数与耗时分开报告，不把测试或 Vite 构建耗时当作用户可见延迟。

## 环境

- Windows 11 Pro 10.0.26300；AMD Ryzen 9 7945HX，16 核/32 线程。
- Node 26.7.0，pnpm 12.4.1，.NET SDK 9.0.318。
- 已安装的 Vitest 4.1.9 / Happy DOM；真实 Vue 组件或模块，传输和 ECharts 渲染器按场景替身。
- 当前环境与项目声明 Node/pnpm 版本不同；实机 WebView 与标准发布环境仍需 P10。

## 设置保存与宿主状态

固定输入：同一连接身份，100 条逐 tick 的完整宿主状态更新，仅 latestCoreVersion 改变；另以 100 次逐 tick 设置修改构造防抖保存突发，再推进 3 秒空闲轮询。

| 指标 | 修改前 | 修改后 |
| --- | ---: | ---: |
| 100 次编辑后的防抖保存命令 | 1 | 1 |
| 随后轮询结束的累计保存命令 | 2 | 1 |
| 100 条相同会话状态新增 /version 请求 | 0 | 0 |
| 100 条相同会话状态新增 /configs 请求 | 0 | 0 |
| 100 条相同会话状态新增设置保存命令 | 0 | 0 |

保存计数是在宿主消息边界测得的命令数，不是磁盘系统调用数；生产 SaveDashboardSettings 每次有效命令调用一次 Settings.Save，因此减少一条重复命令也省下一次重复设置文件提交。没有推算毫秒收益。优化后独立重复三次均为 1 次保存，日志为 `.tmp/p9-writes-1.log` 至 `.tmp/p9-writes-3.log`。

采用的优化：只缓存宿主成功确认的规范化快照，在串行队列真正执行时比较；失败不更新快照，同内容仍可重试。显式刷新前保存也只有已确认同内容才省略。P9当时保留轮询以兼容直接localStorage写入；后续R4完成写入口迁移后已移除轮询，保留成功ACK去重。

复现：

```powershell
cd dashboard-src
./node_modules/.bin/vitest.cmd run src/__tests__/settings-write-measurement.test.ts src/__tests__/settings-save-ack.test.ts src/__tests__/home-session.test.ts src/__tests__/version-session.test.ts --reporter=verbose
```

## Overview 图表生命周期

输入包含单点时间序列 `[1000,1]`，前台变为 `[2000,2]`，停用后变为 `[3000,3]`；控制一次元素尺寸变化，等待 130ms 覆盖 100ms resize 防抖，再激活。另有直接响应式数据 chart 场景。真实组件挂载在 KeepAlive 中，mock ECharts 只计数，不能证明真实绘制耗时或视觉一致性。

| 场景 | 停用期间新增 setOption：前 → 后 | 停用期间新增 resize：前 → 后 | 再激活行为 |
| --- | --- | --- | --- |
| 直接响应式 chart | 1 → 0 | 1 → 0 | 1 次最新数据 setOption，1 次 resize |
| Overview MiniSparkline | 0 → 0 | 1 → 0 | 1 次最新 props setOption，1 次 resize |
| TimeSeriesChart | 0 → 0 | 1 → 0 | 未暂停时刷新；用户暂停则不 setOption，仍 resize |

注意 MiniSparkline/TimeSeriesChart 的前台更新已经各增加一次 setOption，不能将它算作停用工作。采用共享 useEChart 的局部 active 标志，停用时取消 resize 和触摸监听；重新激活不重复初始化、不改变用户 pause。代理两次与主会话一次复跑计数一致。真实视觉验收留 P10。

```powershell
cd dashboard-src
./node_modules/.bin/vitest.cmd run src/__tests__/overview-chart-lifecycle.test.ts --reporter=verbose
```

## 启动与恢复的测量边界

本轮未启动真实桌面宿主，以下三项没有实测毫秒结果，不宣称已提速；现有诊断埋点可供 P10 分别采集：

| 场景 | 分类证据与计量端点 |
| --- | --- |
| 冷启动 | 新进程；host:applicationContextCreated、webview:initializationStarted/initialized、navigationCompleted、frontend WS firstMessage |
| 托盘热恢复 | tray:showRequested dashboardReady=true；不应出现新 WebView 初始化；showDispatched 仅代表显示命令派发，不是首帧 |
| 轻量释放后恢复 | 先有 webview:disposed reason=lightweight-timeout，再有 dashboardReady=false 和新初始化 ID；分别记录初始化、导航、首个数据包 |

P10 在同机同数据下每类独立重复至少 5 次，报告中位数与范围，记录 WebView Runtime、窗口状态、核心数据量与缓存状态。导航完成/首包不等同真实首帧，若要报告可见首帧还需实机捕获。暂不修改启动路径。

## 代理延迟查找

详见 下节“固定代理延迟基准详情”。4 组各 128 项，另有 provider 128 项；64 个共用节点（每组约 50%），321 个唯一名称，3 个固定测速 URL。30 次预热，5 对交替顺序样本，每样本 100 次遍历，覆盖 mihomo 独立/共享与 sing-box 独立模式。

主会话独立复跑结果（每 100 次遍历的中位数，基线 → 复用，毫秒）：mihomo 独立 200.406 → 100.899；mihomo 共享 134.030 → 67.527；sing-box 独立 137.716 → 69.603。每次模型遍历生产 getLatency 调用从 1,280 变为 640；三种模式结果一致性检查通过，原始结果 `.tmp/p9-latency.log`。

**决定：本轮跳过生产 latency map 改造。** 模型明确存在重复计算，但这次只计量查找热路径，并未得到浏览器真实重算频率、列表虚拟化影响和帧耗时证据。name-only 全局缓存不适用，不同组测试 URL 必须隔离；在没有实机收益证据时不增加缓存失效状态。既有 sing-box/provider 测速路径不变。

## 收口

采用两项有直接计数证据的小改动：成功确认后的设置快照去重、图表停用期间停止渲染/resize 并在返回时恢复。latency map 和启动性能改造均跳过。前端 47 文件/119 项与类型检查通过；opt-in 基准额外 3 项通过；生产构建、源码契约及资源检查通过。未改变后端代码，本轮没有重复运行既有 215 项后端测试。实机视觉和三类启动/恢复计时仍明确交给 P10，本记录不将这些项目标为已实测。


## 固定代理延迟基准详情（P9历史测量）

Scope: measurement and reproducible benchmark only. No production optimization, user-data operation, commit, or P10 work. Run from `dashboard-src`: `node node_modules/vitest/vitest.mjs run --config bench/vitest.config.ts --reporter verbose --silent=false`. Benchmark is outside the normal `src/**/__tests__/**/*.test.ts` include. Requires installed dependencies. `pnpm exec` in this checkout attempted a dependency purge/reinstall without TTY, so the installed binary was used directly.

Environment reported by parent: Ryzen 9 7945HX (16C/32T), Windows 11 10.0.26300, Node 26.7.0, pnpm 12.4.1, .NET SDK 9.0.318. `node --version` directly returned v26.7.0. Times below were directly observed in this workspace; other environment values are supplied by parent.

Fixture: four groups of 128 entries and one provider list of 128 entries; 64 shared node names; 321 unique entry names including group names, with group selections resolving to shared nodes. Three deterministic URLs (default and two group overrides); provider uses undefined group context, as `ProxyProvider` does. Node histories contain deterministic zero/nonzero delays, plus URL-specific histories. Modes: mihomo independent, mihomo shared, sing-box independent. No network requests. The independent mihomo path reads URL-scoped `extra`; shared mihomo and sing-box read selected-node `history`. URL-specific provider test behavior is not simulated as a per-provider group lookup because the provider render consumer supplies no groupName. This fixture models render latency-map population followed by `proxiesCount` for each entry. Baseline calls production `getLatencyByName` twice per entry; candidate reads the first lookup's value from a per-group map on the second pass. Values and observed production `useRenderProxyList` counts are checked for consistency. No elapsed-time assertions.

| Mode | Production lookups/pass | Candidate lookups/pass | Baseline median / 100 passes | Reuse median / 100 passes |
| --- | ---: | ---: | ---: | ---: |
| mihomo independent | 1,280 | 640 | 192.306 ms | 97.219 ms |
| mihomo shared | 1,280 | 640 | 130.212 ms | 65.536 ms |
| sing-box independent | 1,280 | 640 | 135.468 ms | 68.831 ms |

Five paired, alternating-order samples per mode, with 30 warmup pairs. Observed baseline/reuse samples in ms (100 passes each): independent `199.384/96.633, 192.306/97.468, 191.910/96.770, 192.253/97.219, 192.396/97.219`; shared `129.234/66.082, 130.212/65.314, 129.654/65.536, 130.545/65.448, 132.886/66.190`; sing-box `135.540/69.947, 135.590/68.498, 135.195/68.831, 135.468/68.526, 135.450/69.857`. Three benchmark tests passed. This is a deterministic workload but timing remains machine/load dependent; no claim of frame-time or real-user speedup.

Design for parent decision: `renderProxies.ts` already computes a local `latencyMap` for filtering/sorting, but `proxiesCount` recomputes per-node latency through `getLatencyByName`. If optimizing, share a single *group-scoped reactive computed snapshot* of latency values between render and count; do not share a global name-only cache because the same node may have different group test URLs in mihomo independent mode. Preserve independent/sing-box dispatch in `getHistoryByName`, provider's absent group context, and reactivity to history, URL, mode and group changes. Validate reactive invalidation and output parity before considering production change. Other consumers (`LatencyTag`, `ProxyPreview`, `ProxyChainPath`, clash latency testing) remain separate and were not optimized here. The modeled duplicate lookups have a strong synthetic benefit, but additional Vue/browser profiling is needed to establish material UI benefit. Parent decides whether production optimization is warranted.

Note: the first version of the fixture accidentally made four synthetic group selections cyclic; the timed run stalled in the production chain traversal in shared mode. The fixture was corrected to terminate at shared nodes before the complete run above; this does not establish a production-cycle defect.
