# WP02-18 · 工作台客户端数据状态局部交付

对应第十轮 D 的已登记客户端窗口。源码基线为 `8823c31421c1346d2750faee8da16d016ff5fc56`，Handoff 分配为 `573934e2a264ee3cff4a01e8519f0d5a123a6ebc`，START 为 `3d3e94af28f41451539c194ae8f56fc7538a6c15`。实现分支为 `codex/wp02-18-dashboard-states-20261016`；候选 SHA、草稿 PR 和交付消息的固定链接由 Handoff DELIVERY 记录，避免报告自引用。

## 改动及行为

summary、未出货和库存滞留分别持有 loading / ready / forbidden / unavailable 状态。初始与刷新期间不展示业务零；只有通过既有响应结构校验的成功结果才显示实际零或空集合。HTTP 403 清除旧结果并使用已有译文展示“403 · 越权拒绝”；网络错误、500、非成功信封及必需字段缺失展示已有“请求失败”文案。业务信封的数字 code 不被猜测为 HTTP 授权结论。

保留 summary 的原始对象合同，以及两个 widget 的 `{code:0,data:...}` 合同；未出货兼容 `items` / `rows`，可空日期、客户及数量字段保持原合同。只检查数字的有限数值类型，不改变数量/金额表示或引入数据库约束。出货样本统计、最近受注、制造环图都受 summary 状态控制，不在失败时伪装成空数据。既有路由权限与布局保留，新增 summary 独立刷新入口，两个 widget 原刷新入口不变。

三个请求组分别使用代次保护。旧成功、旧失败及已卸载组件的响应不能提交；这是丢弃响应，不是取消网络请求。初次加载三组与 SignalR 启动独立触发；BusinessNotification / NewOperLog 仍仅刷新 summary，避免 widget 读请求再次形成操作日志刷新循环。卸载清理通知计时器与自身事件处理器，不停止共享连接或移除其他使用者的处理器。

观察已有 `cp6_authed`、`userName`、`menus`、平台管理员信号及 platform store 的 impersonation 状态：storage / focus / 既有菜单更新事件、响应提交前检查以及响应式 watch 会清空三组与通知，并使旧请求失效。观察到身份变化后，本组件保守停用旧共享连接消息直至重新挂载，手动 HTTP 刷新仍可用。未引入权限版本、租户标识或服务器授权推断；没有可观察信号的服务端变化仍需后续工作处理。

## 实际验证及审查

2026-10-16 在本地 Windows、现有锁文件和已安装依赖上执行；依赖输入与前项安装时相同，没有更新 package / lock 文件。

- `npm test -- src/views/dashboard/dashboardLoadState.spec.ts src/views/dashboard/DashboardView.spec.ts`：最终 **16 通过、0 失败、0 跳过，退出码 0**。覆盖待加载不闪零、有效零/空、403 清旧值、网络/500/非成功/缺字段、旧成功与旧失败竞态、新请求仍在加载、组间独立、身份清理、卸载与延迟连接启动。首轮亦通过；在移除会将首次权限加载误作身份变化的额外观察后执行最终相关测试。日志：`D:/CP6-local-test/WP02-18/tests-01.log`、`tests-final.log`。
- `npm run type-check`：通过。原始日志：`D:/CP6-local-test/WP02-18/type-check-01.log`、最终 `type-check-final.log`。不声称运行了前端全量测试或生产构建。
- [check-page.mjs](check-page.mjs)：真实 Vue 页面、Chrome、API 夹具检查通过，最终端口为 `127.0.0.1:60338`，pageErrors 为 0。核对初始占位、真实零/空、403 清旧值、畸形响应不可用、未出货筛选请求的旧响应丢弃、390px 手机布局与身份变化清理；人工查看桌面/手机截图。脚本只使用临时本地服务、拦截所有 API 并阻断外部网络，finally 关闭自有浏览器和服务，没有连接业务数据库。SignalR 网络连接在浏览器夹具中返回 503，生命周期与推送分组行为由组件测试覆盖。
- 浏览器前三次未通过记录保留：`page-01.log` 为冷启动导航超时，改为 DOM 就绪并单独等待页面；`page-02.log` 暴露夹具未登记 dashboard 菜单，补齐现有动态路由所需菜单；`page-03.log` 点击 Element Plus 隐藏 switch input 超时，改为点击用户可见开关。`page-04.log` 为最终通过结果。以上为检查脚本修正，没有掩盖或跳过失败断言。目录均为 `D:/CP6-local-test/WP02-18/`；截图为 `dashboard-states-desktop.png`、`dashboard-states-mobile.png`。夹具语言包和未登记其他业务菜单带来控制台提示，不存在未捕获页面异常。
- 完成一次集中 SPEC/QUALITY 审查，审查者只读核对完整候选与实际 DTO / HTTP / store / SignalR 实现，**无 P1/P2 阻塞项**。其建议明确身份变化后旧连接停用行为已纳入本报告；不将审查者未执行的测试记为其通过证据。

发布前核对候选及远端 main 的 `.github/workflows` tree 均为 `3355235636fd45116f5445fca32fc65e49f13003`，复用既有相同内容分析：12 个仅 workflow_dispatch 的工作流，1 个仅匹配 `v*.*.*` Tag 的 R2 工作流，无 PR 或任务分支自动编译/测试触发。本次没有工作流及相关依赖修改；任务分支运行列表为空，无需取消运行。未触发、重跑 Actions 或改变工作流/分支保护。

## 交付边界

仅 WS03 的现有客户端加载状态与响应竞态部分交付。DashboardController 仍是 legacy KPI / 全局缓存路径；scoped KPI、服务端授权/撤权与租户隔离、Owner 指标定义、菜单 CAS、数据库与真实业务验收均未在本窗口实施或验证。浏览器数据及身份为夹具，不是生产数据、真实权限或业务端到端验收。

完整 WP02-18 保持 `PLANNED / NOT_CHECKED`，WP02-03 前置及设计原 20 条业务 AC 的 NOT_RUN 范围不提升。未运行后端/数据库测试、跨仓验收、正式制品发布或部署。COORD 负责串行合并、总台账、生成页及四份项目状态；候选发布不代表 main 已合入或完整业务完成。
