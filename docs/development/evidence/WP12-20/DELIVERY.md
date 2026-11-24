# WP12-20 通知偏好和已读水位修订候选

Main 为 f0739befada77bcd98b56d448a8f89ed6564be7e，修订前 Draft 为 8901c7bcf31d68a2f4e8b05af0677bece45eb5c3，本轮受验源码为 829c4472f6a5354009b4a5ff7af6a842868b1f69，源码树为 3c7d2fa51bf882c5628afb879c4b94f2c12962b6。原 DEV-1 的 WP12-20 交同一 Draft #1320 独立复验。 [正式 REVISE #428](https://github.com/GTX537/CP6.AI-Handoff/blob/b41d8574a7716c45cb9bef3eabccd3deddc1b4a4/handoffs/CP6-DEV-20261012/tasks/WP12-20/replies/20261123T214640Z-coord3-candidate-review-r2/MESSAGE.md) · [第二版原证据](https://github.com/GTX537/CP6/blob/8901c7bcf31d68a2f4e8b05af0677bece45eb5c3/docs/development/evidence/WP12-20/DELIVERY.md)

## 共用原投递路径

- 两个 API 版本共用 Main 已有 worker、Wf_Notification 状态与 SignalR 协议，三份入口源码对象与精确 Main 相同。候选不含第二个 worker 或通道账本。两版仍以原 ID、原 EventKey 和原状态查询领取，通知偏好仍在原生产者同事务留版本证据。
- PostgreSQL 前向迁移在同一写锁和事务内安装原领取保护，并将既有在途及尝试后未确认的旧记录保存为状态 3。此后两版原领取 UPDATE 在获胜的同一行上直接持久为状态 3，原调用仍获得领取回执。状态 3 表示已有尝试、结果待核对；两版查询都只领取 0 或 2。迟到的原成功可以写 1，原失败留状态 3。保留原键、尝试数、已有完成事实、已读时间和原错误摘要。未尝试的状态 0 仍由原入口处理。
- 本机从精确 Main 的末条迁移开始，用实际原 worker 先完成并读取一条、模拟一次失败、再暂停一条原在途刷新。原 worker 已领取时前滚数据库，并发两实例处理迁移前未尝试待派和迁移后写入。迟到的原成功落账。另在迁移后分别模拟失败和领取后无回执中断，按旧退避到期后重新启动兼容实例并处理新待派。八条测试原键各一次，五条确认，三条未知均只一次尝试，额外投递、邮件尝试和原 ReadAt 改写均为零。
- 保留两个合成租户的真实期限重算、原命令重放和既有引擎写入重放。原站内路径按各自接收人共发送五次合成刷新，并发与复扫不新增提醒，邮件外送替身为零。期限列表仍从日历 Owner 已提交凭证确定最小摘要和任务入口。其余旧通知保留现有投递范围，通用历史摘要继续按本人读取。

## 已读和客户端版本配套

旧列表保留 data 数组，外层带该次观察的受保护 watermark。旧 read-all 以发起页面持有的水位为准，缺水位返回 428。旧 Web 资源不提交此字段，连到新版 API 时原一键已读按钮可能没有效果，应刷新加载配套新版 Web，或由调用方先读列表、持有该次 watermark 再提交。新版客户端只提交明确水位。两个观察点和晚到事实、重放、原首次 ReadAt、真实 FORM/APP 深链与失权清理均重新覆盖。

## 验证和余项

本机隔离 PostgreSQL 18.6、Kestrel 和 Chrome 合成资料 65/65。Vue 应用类型检查通过，Vitest 23/23，台账结构和生成视图检查通过。SQL Server 未运行。SQLite 两项旧排序规则错误留精确 Main 对照。两代实际发布镜像及生产切换未运行。原测试通过本机同源 worker 实例和真实前向迁移覆盖顺序与重启，全部传输替身留在本机。

| 原验收维度 | 可复验范围及待办 |
|---|---|
| AC-T06-01/02/03 | 偏好兼容、矩阵、remind、本人 WFC CAS 和原回执定向实测。 |
| AC-T06-04 | FORM/APP 同事务与期限重算，原出站队列迁移前、中、后及兼容重启的同原键证据。 |
| AC-T06-05/06 | 共用原站内路径在此次切换不重复领取。逐通道账本、迁移至逐通道 worker、当前收件资格及待派偏好复核仍待独立采用和重新核验。真实邮件、Required 与正式运维原键重送未获 Owner 采用。 |
| AC-T06-07 | 新版实测两次观察、晚到通知、重放和明确 428。旧 Web 需配套更新。 |
| AC-T06-08 | FORM/APP Chrome 授权、失权清理、实际代理、本人及租户收件范围。原 worker 的路由边界保留 Main 合约，逐通道的新路由未交付。 |

原件在 C:/Users/tt/CP6-test-artifacts/WP12-20/20261124-wp1220-exec3-r3。source-bind.json 绑定技术输入、原 worker 与传输合同的 Main Git 对象和测试原件。raw-focus-test-output.json 保留合成原键和计数，隔离 PostgreSQL 已停止。 [原投递余项和正式 QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/330504be45ba928527adc0dc503dfc698d3c18b2/handoffs/CP6-DEV-20261012/tasks/WP12-20/messages/20261123T202654Z-cp6-exec-3-question/MESSAGE.md)

| 原件 | SHA-256 |
|---|---|
| wp1220-r3-final.trx | 0e9ae10a6678d2ba1c5e6fda01e0f7d3f4dee5bee75328aed635ece8dde0ea8d |
| permission-consumption-wp1220-r3-final/workflow-notification-browser/result.json | 5ea9f291b45c48c966c3af4743d045a9eb0b9042b2610b181d96e0ec264b118d |
| roadmap-structure.json | d887ac74bc59034d4c9c72b9e036ead7bfafc655b022293ba890f683a2324f5c |

Main 为 PLANNED，原八项 AC 均为 NOT_RUN，OA-TASK-01 25/46。WP12-15 的 714.019 秒仍按原完整 DELIVERY 和准确路径查重后，等待下一合资格 Main 台账单次结转。 [原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/37c1eb25397baa8f8fe51cf75e870f23f3c045be/handoffs/CP6-DEV-20261012/tasks/WP12-15/messages/20261123T194905Z-cp6-exec-3-delivery-closeout/MESSAGE.md)

快照 2026-11-24T00:52:15.7897612+00:00，本轮有效 1538.090 秒，累计 5760.944 秒。上两轮最终有效 4222.854 秒计一次。两段等待 916.548 和 10081.885 秒、首次领取前核对 316.775 秒单列。DEV-1 保留本卡。两项指定技能在本机未找到，按授权执行 CP6 规程。

## #429 正式拆分候选

本次只更新台账、任务定义、计划、生成视图和本报告。受验源码 829c4472f6a5354009b4a5ff7af6a842868b1f69 与第三版精确候选 4d47c967fa478f587b3eefb282b793276358b8db 的所有产品、迁移和测试源码完全相同。独立裁决 #429 据实际 PostgreSQL 18.6 与 Kestrel/HTTP复跑 64/64，另回读执行 65/65 和 FORM/APP Chrome 原件。独立 TRX SHA-256 ea43b39d342db2c3d77fda721086746059a7265cc6b4a9803744b2211533c7f7。原件 C:/Users/tt/CP6-test-artifacts/WP12-20/20261124-wp1220-coord3-r14/wp1220-coord3-r14.trx。本轮只做台账和视图验证，复用同源码证据。 [#429 裁决](https://github.com/GTX537/CP6.AI-Handoff/blob/16954d7291e2f375c127792ff176398b2b5773dd/handoffs/CP6-DEV-20261012/tasks/WP12-20/replies/20261124T010152Z-coord3-candidate-review-r3/MESSAGE.md)

| 原验收 | 候选状态 | 已验及余项 |
|---|---|---|
| AC-T06-01 | PASS | 旧扁平与矩阵兼容及明确恢复默认。 |
| AC-T06-02 | PASS | timeout 保持 unsupported，原生提醒走 TodoCreated。 |
| AC-T06-03 | PASS | 本人 CAS、冲突、原回执与非法写入边界。 |
| AC-T06-04 | NOT_RUN | FORM/APP 同事务及原路径迁移已有证据，逐通道迁移和原键恢复待 WP12-20b。 |
| AC-T06-05 | NOT_RUN | 原路径共用领取及未知保留已证，逐通道状态、真实邮件及正式运维原键重送待 WP12-20b。 |
| AC-T06-06 | NOT_RUN | 正式 Required、真实派送前资格和偏好复核待 WP12-20b。 |
| AC-T06-07 | PASS | 每页持有明确观察点，晚到未读、重放保留 ReadAt，旧 Web 配套限制为 428。 |
| AC-T06-08 | NOT_RUN | FORM/APP 深链、清理和代理隔离已证，新的逐通道接收及披露边界待 WP12-20b。 |

候选原号 WP12-20 为 DONE/SPLIT，fully_done=false。WP12-20b 初始 BLOCKED，父卡及八项原 AC 均保留，后继 WP14-07、WP14-08、WP14-09 沿原前置等待完整交付。OA-TASK-01 候选 29/46 且目标开放；当前 Main 仍为 PLANNED、原八项 NOT_RUN、目标 25/46。候选台账等待独立核对精确 HEAD。CP6 当前使用 PostgreSQL。WP12-20 不补做 SQL Server。后续仅有明确旧库兼容或迁移依赖的卡验证 SQL Server，旧代码与数据库保留。

快照 2026-11-24T01:14:49.410334+00:00。此前有效 5860.205 秒，新增台账工时 430.208 秒，合计 6290.413 秒。三段等待 916.548、10081.885、824.151 秒与首次预检 316.775 秒单列。余卡 8109.587 秒，强制拆分线 12164.3805 秒。WP12-15 尾差 714.019 秒只按原完整提交及路径候选结转一次，该卡累计 5826.402 秒、余卡 8573.598 秒。计划累计 207761.235 → 208475.254 → 214765.667 秒。WP12-14 尾数沿用已采用登记。 [WP12-15 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/37c1eb25397baa8f8fe51cf75e870f23f3c045be/handoffs/CP6-DEV-20261012/tasks/WP12-15/messages/20261123T194905Z-cp6-exec-3-delivery-closeout/MESSAGE.md) · [签收 #425](https://github.com/GTX537/CP6.AI-Handoff/blob/c9ff21ce30e355262b9d8251d30130e96fd42c29/handoffs/CP6-DEV-20261012/tasks/WP12-15/replies/20261123T195253Z-coord3-split-closeout-ack/MESSAGE.md)

旧工作卡余项与现有数据保持原责任。完整发布镜像切换、真实邮件、Required 与正式运维重送需后续正式采用。DEV-1 保留现有卡，后续由 COORD 独立复核。两项指定技能在本机未找到，本轮按 CP6 规程执行。
