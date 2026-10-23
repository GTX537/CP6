# W86 / 七天一期D2：库位优化历史观察独立Owner与旧写入口围栏

COORD本聊天单人，开工2026-10-23T23:14:47.717649+00:00。Main `a32182de95bf392d3c9e4705b84720cc39f62909`，CRM `3e69fa188a43d261660fe871103bc2b7b400a674`，Handoff `7fdc5d18e71fc49a0c11b7406f1044727f721abf`。W85 Main475/476和Handoff61全部采用，Issue474关闭；39独立用例及类型检查通过，实际16.82min＋独立前置2min。准确任务分支删除、固定树干净detached、全部席位释放；2个PG停止保留，Actions0。W37–W85不重复。

## 最新前置与范围

最新AGENTS/POLICY哈希未变；官方status/capacity无告警，D盘约207GB可用。两个非allowlist历史目录及根用户未跟踪文件不动。无重复Slotting Issue，登记[Issue477](https://github.com/GTX537/CP6/issues/477)。已读设计手册WMS-SLOT-01、当前原件ec7dcc5179f0e584__WMS-SLOT-01_库位优化_完整设计_RECOVERY_v1.1_R2.md独有§1–7及15AC、共同A1–A7和WP10-28/29前置。S-G01–05均UNPROVEN，本窗不承接完整固定cut分析/Proposal批准/Transfer派发/停止。

旧服务整实体/建议JSON读取只有旧仓库provider，Analyze按Txn行计数及库存最大位置，Approve随机旧Mobile任务，Cancel只停Pending；没有独立Owner/当前来源字段/完整作业族，不能作为R2采用。

1. WP02-03有界窗口增加PostgreSQL独立wms-slotting:query当前原会话、本人/真实slotting-plan UUID部门2–4/可信租户、有效仓库和仓区许可，原生权限/业务围栏与最后重核。具名SLOTTING-OWNER/1历史观察、稳定服务端分页（50默认/200上限），旧状态只标历史，知识UNKNOWN、LEGACY_ROW_COUNT不等真实物理频次。
2. RecommendationsJson保原文，不通过浮点重新序列化、不把坏JSON或null当空数组。显式标原文有效/LEGACY_PARSE_ERROR/未记录及权限不可披露；不造新来源cut或结果。旧不透明整仓快照缺逐条来源映射：统计值及原文仅在本Owner RecommendationsJson可读，并且当前Stock/Outbound/Mobile query、各scope5、相关源全部字段可读及该仓全区范围可证时披露；否则只给当前有权计划头，来源状态不可披露、统计null和原文null，不用部分当前集合证明完整历史源。依赖不可达拒绝，不当空授权。ApproverCd/Remarks各自当前MIN。
3. 原生slotting-plan部门映射真实UUID、IAM类型/选择器、迁移及启动query目录（无自动角色授权）；公共注册串行。PG旧读409，Analyze/Approve/Cancel在各自当前动作/可见对象检查后SLOTTING_OWNER_ADOPTION_UNPROVEN、零序号/方案/任务/库存/状态效果。旧Approved不等MOVED、旧Cancelled不等完整族关闭，禁止包装旧Mobile链当NEW Transfer。非PG历史保留，既有原文/数据不迁移。
4. 真实HTTP/no-store；页面历史列表/详情，仅受权数据，原文以安全文本保精度，解析异常/未知可见。身份、源权限变化、乱序、失败、筛选/分页清旧结果，不发写操作、不自动重试未知。保持其他物流页面。

## 实际估时、门禁与收尾

预计35–55min：实现20–30、定向验证10–15、一次集中审查/合并收尾5–10；独立前置2min，与W85收尾重叠的只读不重复计。七天目标ACTIVE。

真实PG先复现无Owner读/分析/取消，验证tenant/self/部门UUID/仓区、完整源读取门和撤权/字段MIN、原文精确与坏JSON保留、超200分页、最终失败/原生并发、三旧写零效果、实际Program/login/HTTP；页面验证精确原文、当前身份/权限/失败/乱序/分页。只受影响构建/用例/类型检查，无全仓/Actions/既有环境迁移/部署。

DEV-1 `codex/bug-477-slotting-owner`产品/总控，DEV-2 `codex/w86-handoff`沟通，单人固定席位复用、无代理或新树；PG55435自有夹具串行启停保留。正常PR、远端采用核对、定向分支删除、finish释放。24/375整卡、125目标0完整交付、247正式运行NOT_RUN不提升；完整WP10-28/29及CRM真实Owner/公开受理保持后续。W51指定停止data删除被自动审批blocked by policy拒绝，原地保留不重试。

## W86验证完成

19独立.NET用例（15原生PG、3既有非PG库位优化行为、1两租户启动种子）及22Vue用例通过，无跳过；相关构建和vue-tsc通过。一次集中审查闭合，原文/来源门/旧写零效果、原生并发、真实HTTP及203条分页通过；3个实际PG停止保留，套接字环境失败单项补验通过、原失败保留。正常采用与同任务收尾后继续，七天ACTIVE。
