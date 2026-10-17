# 第十六轮：P1 Space库存精确范围传输

[#257](https://github.com/GTX537/CP6/issues/257)已通过[D最终审查](https://github.com/GTX537/CP6.AI-Handoff/blob/68f574242b847daeb1c898a33b97c78c3c071c4c/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-257/REVIEW.md)、[PR260](https://github.com/GTX537/CP6/pull/260)和用户指定PG真实DEV，现Closed；[单份DELIVERY](../../../quality/evidence/issue-257/DELIVERY.md)。P0暂停解除。按BUG管理P1下一项规则，当前是[#132](https://github.com/GTX537/CP6/issues/132)，不是再次重复登录/仪表盘验收。

| 原对话 | 本轮职责 | 当前准入 |
|---|---|---|
| A | [BUG132精确范围有界传输](A-BUG-132.md) | 唯一实现，必要的向后兼容库存查询API合同亦由A独占 |
| D | [准备与集中审查](D-REVIEW-BUG-132.md) | 只读准备；A固定交付后一次集中审查 |
| B/C | 保留原候选与#253/#251/#252修正准备 | 尚无新产品窗口；旧阻断未解除，不能自动集成 |

旧d2836f33补丁会在选择超过100个ID时省略过滤、变为整站查询，已经撤回，不得照搬。站点超过10,000库位时，合法请求子集仍须成功且精确。现主线GET按每ID拼参数；服务QueryInventoryAsync已在一次请求内校验SiteAccess、Current Published和最多10,000唯一选择，优先复用该单一边界。

验收仅源码、受影响前端/HTTP合同和必要现有内存服务测试，依Issue不操作既有数据库或替换本机服务。可用A自有loopback18232/15232测试fixture；无PG端口/迁移/DEV部署窗口，不新增SQL Server或双库验证。375任务/125业务目标不因BUG收尾提升。#132不要求复制#257的真实DB验收矩阵。

## 执行规则

先读根AGENTS、[本轮入口](README.md)、[资源与写集](dispatch.json)和roadmap。COORD在本准入合并后从最新main创建A产品工作树和A/D任务资料分支。Handoff仍为GTX537/CP6.AI-Handoff，协调分支codex/dev-coordination-20261012，本轮waves/20261017-wave16.md；各自只写tasks/BUG-132或tasks/REVIEW-BUG-132。真实开工发布一次START并安全推送/回读后连续推进，无运输ACK；D收到A固定DELIVERY后接续同一REVIEW，无第二START。派工不代表开工。

精简验证：相关行为、故障和边界加一次集中SPEC/QUALITY；修正只复查影响，未变证据直接引用，不跑全仓或为文档编译。每任务一份DELIVERY/REVIEW。COORD统一共享台账/四状态、正常合并和远端核对；执行者不改main或共享状态。push/PR检查候选及main工作流事件/路径，未变内容复用；Handoff私有/Actions disabled且默认/自身无workflow后推送，不带tag。无Actions、既有数据库/服务操作、部署或force-push授权。
