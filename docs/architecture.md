# Dashboard 当前架构与维护边界

本文描述P1–P10及R4整理后实际落地的结构。上游跟进步骤和产品排除项以[docs/upstream-merge.md](upstream-merge.md)为准，视觉规则以[docs/style.md](style.md)为准；本文不复制两份清单。最终实施摘要和验收证据见[验证记录](validation.md)，测量与优化取舍见[性能记录](performance.md)。

当前自动化和隔离Release审计通过，不等于Windows/WebView2/真实核心验收完成。[手工验收清单](manual-acceptance.md)的未执行项继续有效；测试数量、ZIP哈希和环境信息记录在验证记录中，不在这里维护另一份副本。

## 责任与入口

| 层 | 主要入口 | 拥有的职责 |
| --- | --- | --- |
| Windows应用生命周期 | `Program`、`DashboardApplicationContext` | 启动与恢复失败反馈、托盘、窗口存在性、退出和系统事件协调 |
| 桌面窗口与资源 | `MainForm`、`DashboardServer`、`WebViewDataMaintenance`、`WebViewTrustPolicy` | WebView创建/释放/导航、固定本地origin、内容更新缓存失效、可信消息和外部导航 |
| 宿主配置与核心 | `DashboardHost`、`CoreLifecycleController`、`AppSettings` | 单活动核心、双核心配置、配置事务、凭证、系统自启、更新与状态快照 |
| 消息协议 | `HostMessageRouter`、`HostBridgeMessages`、`DashboardStatePublisher`、`composables/hostBridge.ts` | 命令校验和分派、全量/增量消息、单一前端接收器、响应式宿主状态 |
| 前端启动与适配 | `dashboardStartup.ts`、`appEntry.ts`、`hostBootstrap.ts` | 导入store前恢复偏好、启动Vue、将宿主连接翻译为Clash后端、窗口操作接线 |
| API与会话数据 | `api/http.ts`、`helper/backendSession.ts`、`helper/backendRuntime.ts`、`assembly/*`、`HomePage.vue` | 请求/流所属会话、旧结果失效、运行数据重置、按路由启动数据任务 |
| UI与偏好 | 页面/组件、`hostDraft.ts`、`helper/storage.ts`、`dashboardSettingsSync.ts` | 展示、双核心编辑草稿、手动反馈、可观测偏好写入及宿主保存确认 |
| 视觉 | `assets/styles/components/app.css`、`dashboard-desktop.css`、共享控件 | 通用原语和有限桌面覆盖；具体尺寸、颜色与布局约束见STYLE |

不要求所有桌面定制移入同一目录。按调用链确认所有权，避免增加只有转发作用的平台层或命令框架。

## 启动与连接

前端执行顺序是`main.ts → dashboardStartup.ts → appEntry.ts → hostBootstrap.ts / Vue`。启动恢复模块有意不依赖会初始化store的模块：先向宿主请求带requestId的Dashboard设置快照，再导入应用。`null`保留旧浏览器偏好，`{}`明确清除`config/*`；失败显示重试入口，不带着错误默认值继续启动。

生产页面由`DashboardServer`在固定`http://127.0.0.1:33291/`提供。端口占用明确失败，不随机换origin。MainForm直接导航`#/core`，连接参数由宿主state提供；浏览器预览仍支持URL导入。两种核心在主界面均通过Clash-compatible API访问，Windows进程和系统操作由宿主负责。

`hostBridge.ts`维护唯一原生消息接收器，先应用共享state，再通知消费者。全量state、runtimeState、logAppend、iconCacheUpdated和windowState保留不同载荷；不因几个共有字段而将每次更新变成全量发送。图标缓存直接由响应式`hostIconCache`消费，版本fallback直接读取`hostState.coreVersion`。已删除旧回调出口、图标事件镜像和宿主版本全局镜像。浏览器后端更新事件保留为兼容入口，桌面生产路径不再派发它。

## 核心命令、保存与退出

Core提交`coreType`及完整的`mihomo*`、`singBox*`两套命名字段；活动配置的`corePath/configPath/apiUrl/secret`投影只作为出站state供连接消费者使用。不可解密凭证必须使用对应的显式替换标志，空Secret也需要明确确认。

配置保存与随后的启动/重启/切换/升级持有同一`CoreOperationGate`租约。gate不排队，忙碌或关闭时拒绝。普通落盘失败阻止后续核心动作；OS自启失败保持原有部分成功语义，不能等同磁盘保存失败。`HostSettingsTransaction`回滚宿主字段，同时保留独立保存的Dashboard偏好。

组合命令返回`Executed`或`Rejected`。前者表示执行了该路径，不承诺核心一定启动或升级成功；具体操作的结果仍由各自反馈负责。首次设置只有宿主确认保存后才关闭向导；自动提交限制为一次，拒绝后可显式重试，避免state回包循环提交。

`CoreOperationGate`和`ShutdownTaskTracker`保持独立：一个负责核心配置互斥，一个负责先登记后执行及退出等待。应用退出异步等待宿主关闭，预算为10秒，之后释放UI；这不保证不响应取消的所有外部工作都已结束。后台探测、进程身份和升级替换有各自的取消/归属/回滚边界，不用单一busy布尔值替代。

窗口关闭、托盘隐藏和应用退出不是同一操作。轻量模式延迟释放WebView，提前重开取消释放；实际WinForms/WebView/系统交互仍需手工清单验证。

## 会话与异步数据

`backendSession`由后端UUID、协议/地址/路径/凭证与宿主会话代数组成身份。宿主身份包含核心类型、运行状态、PID及凭证恢复状态，因此相同endpoint切换核心也会同步使旧会话失效。

HTTP、WebSocket、测速队列及跨await的数据拼装检查所捕获会话，旧结果不能写入新store；旧401不能清除当前连接。桌面当前会话401引导到Core修正宿主凭证，浏览器保留自身setup流程。手动请求经`requestError`统一处理反馈及已处理错误标记，后台普通失败不制造重复通知。

Home协调数据任务和重建，`backendRuntime`只重置运行数据，保留用户偏好、暂停选择和持久历史。版本/API配置的请求序号或reset代数与会话代数不同：前者还能区分同会话的新旧请求，不可一并删除。晚到的宿主可执行版本可以补充空fallback，不触发未变会话的额外版本探测。

## 数据持久化

| 数据 | 权威位置与处理 |
| --- | --- |
| 宿主双核心配置及Windows偏好 | `AppSettings`，原子保存及事务回滚 |
| 核心Secret | Windows用户DPAPI保护的持久字段；明文仅用于当前运行连接与编辑 |
| Dashboard `config/*` | 前端响应式偏好，宿主保存字符串快照；requestId匹配ACK后才确认成功 |
| `setup/*` | 后端连接身份；桌面serializer不持久化密码 |
| `cache/*` | 可重建缓存，不加入宿主Dashboard快照 |
| 页面sessionStorage | 页面交互状态，不加入宿主快照 |
| IndexedDB | 上传背景图片、连接历史等profile数据；图片选择标识与二进制载荷保持分离 |

偏好adapter保留原生Storage身份和原调用方的VueUse选项。普通写入及跨文档变化使用storage事件；初始化/延迟挂载/动态key的默认值写入补通知，安装同步时比对恢复快照与当前存储。不再每秒轮询所有设置。直接导入和重置显式通知；启动恢复与旧键迁移由初始化比对覆盖。

`writeDefaults:true`和`false`调用方继续保持各自行为：前者在初始化或其他文档删除键后可能物化默认值，后者保持未修改的缺省键不存在。不得为减少代码统一选项而改变导出内容。保存队列只在成功ACK后去重，失败快照允许重试。启动快照替换、导入增量合并和reset清空是不同语义。

旧明文凭证迁移必须验证保护结果后再原子提交；损坏配置或迁移失败不覆盖原件。不可解密密文在普通保存中保留，只有显式替换可以改变。用户迁移到不同Windows账户需要重新填写凭证。

内容更新定向失效HTTP缓存、Cache Storage和Service Worker，保留整个WebView profile及其用户数据。不能用删除profile作为常规升级修复。

## UI兼容与样式

共享设置原语在`components/app.css`定义；`dashboard-desktop.css`最后导入并保留实际桌面/Core例外。Core按钮与通用顶栏的尺寸差异、侧栏动画/窗口控件预留和截断规则按STYLE执行。变更共享样式应验证实际叠加结果、长文案、明暗主题和窗口宽度，源码selector存在不等于布局正确。

SettingsContent以实际容器宽度决定单双列，1000px为断点。旧双列偏好与排序键保留为无效数据；无用编辑、排序工具已删除。旧`hidden-settings-items`仍生效，保留其响应式可见性helper，不恢复设置可见性对话框，也不借代码清理悄悄改变用户可见项。

## 验证与后续维护

完整自动入口是`tools/check.ps1`，顺序覆盖源码契约、脚本回归、前端测试/类型/构建、资源验证和.NET构建/测试。`tools/create-release.ps1`负责Release发布及ZIP；包审计还应核对发布集合、文件hash和不应包含的用户数据。实际操作方法以UPSTREAM_MERGE为唯一入口。

行为测试优先覆盖真实消费者，例如Core拒绝后向导、按钮顺序、图标缓存和SettingsContent断点。必要构建边界继续保留文本检查，例如禁用native依赖、桌面入口和最后导入覆盖层；修改函数名不应迫使维护无行为意义的检查副本。

上游跟进以三个确定的提交点比较，再沿入口→依赖→消费者→测试追踪改动。历史文件清单不是自动覆盖白名单，旧调查的缺陷描述也不是当前源码事实。新增兼容层、缓存或抽象前先证明现有消费者和实际收益；保留未知边界，避免用小基准代替真实界面性能证据。
