# W239-01 补货实际移动与停止交付

W239补货实际直接移动与停止：Stock准确Root/Slice及既有Claim Binding、共享容量、Transfer原SourceDemand/完整Stage、独立批准和设备、REP共同事务及同cut未到转实存，原键恢复与页面。

生产业务验收按用户已批准的模拟数据口径执行。实际环境为隔离本地 PostgreSQL 18、Main Owner/API 与 Vue；客户/规则、期初Root、现有Claim Binding及RSV移动控制、技术/质量条件、身份与设备输入明确模拟。数量效果、容量、回执、原操作及HTTP请求实际执行，没有用模拟成功响应替代产品效果。部署状态单列为未部署。

## 交付行为

- W238原prepare登记接入实际Transfer Request/SourceDemand和一个完整Stage，Root半开范围与Request/Demand范围独立保存；不把StockId伪装成Root。
- 独立批准、当前设备/执行人、派发、整单完成；Stock两腿、Root分区、共享容量、原决定/事件与REP完成关联同一事务。当前批准撤销或两端状态不满足则无新效果。
- MOVE保留货主/技术/原用途/原Site和现有Claim Binding，不释放或重建销售承诺。已到量与未到保护同一观察更新；CurrentIntent引用当前订单版本和Stock观察，旧命令仍返回原结果。
- 未发送的草稿/登记可关闭源门；已有Transfer与Stock永久无效果决定共同关闭，晚执行不得穿透；已POSTED停止保留事实。现场结果未知进入RECONCILIATION/null并保留预算，不自动补剩余或反向搬运。
- [Issue 665](https://github.com/GTX537/CP6/issues/665)：人工单只核其真实源与目标，非相关库位无权不再阻断合法单；实际来源撤权仍拒绝。部分context只返回有权人工选项并标PARTIAL。
- 原生T_Stock精确范围由数据库触发器拒绝旧写入；原有PostgreSQL REP/Mobile写入口拒绝路径直接复用，不新造Mobile任务。已有库存自动迁移被拒绝。

合同与端点见[直接移动v1](../../replenish-direct-move-v1.md)和[建议/草稿](../../replenish-planning-v1.md)。

## 实际验证与复用

20 distinct PostgreSQL18/actual HTTP scenarios PASS;18 distinct Vue scenarios PASS (10 manual/execution,8 planning); app vue-tsc PASS. Simulated opening/source/MASTER/RSV control/IAM/device inputs, actual Stock/Transfer/REP transactions and HTTP. Unchanged successful evidence reused; original failures retained.

20项不同PG/HTTP场景包括：两腿和Root守恒/Claim Binding保留/容量/原结果；中途写失败回滚；停止胜出或返回已POSTED；容量/Claim/库位最终检查；原生raw writer围栏及租户/环境隔离；13整数/8小数极限坐标的1e-8移动；并发停止与执行一个决定；不同Root竞争同容量；REP完整链与同cut到货；未发/已移停止；未知现场结果保护及晚完成拒绝；丢提交响应后撤权原键恢复；REP最终写失败整组回滚；当前独立审批人撤权；未发送登记关闭；实际HTTP独立批准/派发/移动/原回执；Issue665原步骤；三项受影响的既有登记/预算/恢复回归。

后端实际记录位于 `D:/CP6-local-test/wave239/foundation/pg-*.json`，按每个场景最后实际结果选择，不把重复运行累计为用例数。汇总为 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave239-validation.json`。页面初次18项及修改部分10项验证保存在 `wave239-web-tests.log`、`wave239-web-review-tests.log`；app类型检查终态日志 `wave239-web-typecheck-delivery.log`。页面API响应在组件测试中模拟，HTTP场景使用实际宿主与真实登录账号。

保留失败：Stock首轮原生writer触发器排序规则冲突，显式C排序后原步骤通过；执行首轮REP事件错误引用新随机ID，改为真实版本文档ID后5项通过；Issue665旧权限范围复现失败后修复通过；HTTP测试助手固定登录创建者而被独立审批正确拒绝，改为另一真实登录账号后通过；Vue联合类型缩窄错误已修复。没有降低权限、吞错或删除失败日志。

未运行全仓回归、浏览器布局或正式发布构建；未冒充完整Stock EFFECT-2/H31、通用Transfer/RSV、真实外部消费者采用。已有成功的相同代码/输入证据按原来源复用，SHA变化不触发重跑。

## 集中审查与剩余范围

One author task SPEC/QUALITY review: full opaque references and distinct Root/Request/Demand coordinates; Stock-only slices/capacity/receipts, Claim Binding preservation and exact legacy-writer fence; shared final transaction/rollback, independent current approver and performer, original replay, stop tombstone and unknown protection; current source/target permission (#665), same-cut onHand/openIncoming, current intent order versions; UI durable exact commands and current action permissions. Full protocol limits explicitly retained.

本轮为全新显式模拟期初下的整单DIRECT_MOVE。完整TRANSIT、ActualFact/Case认账、通用RSV Use与Claim创建/释放、全部MASTER/质量宿主接线、既有库存迁移、列表/导出、历史任务族及外部消费者未完成；原D2退货商业/财务衔接仍保留，不关闭完整WP10-10/WP10-31或D2。未部署生产或迁移既有数据库。

原七天目标继续ACTIVE，D1/D4已闭环，D2/D3/D5/D6/D7仍部分完成；2/7只是阶段数，不是工程量百分比。正式375任务状态、125目标与247验收未因本窗口自动关闭。下一步：推进原D3计划写入；继续D2 ActualFact/Case有权处置、通用RSV/MASTER/质量接线、退货商业/财务衔接、历史任务族与原D5/D7集成。验收输入缺失直接模拟，不等待企业数据。

## 生命周期快照

COORD本聊天单人，W239从Main `a6a87cbd0a1ddc9836a4711d22dee88733f82eca` 开始；复用DEV-1 Main和DEV-2 Handoff，原估实际120–240分钟。此文件为合并前快照，Main/Handoff正常合并、精确分支删除及官方finish在同轮最终proof核对；固定目录和缓存保留。没有触发或取消Actions，没有变更工作流触发器、分支保护或生产环境。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/215)已完整采用并finish。Main快照实际44.8分钟；最终Main采用与finish由同轮最终核对记录确认。
