# W241-01 自动排列及完整Root批次交付

W241自动排列与完整Root批次：稳定优先级/交期/路线/ID排序、完整500项清单、明确时长来源与未知约束、每Root原子组、跨RootDAG/部分完成/原键恢复及页面。

生产业务验收按用户授权使用明确模拟数据。实际环境为隔离本地 PostgreSQL18、Main Owner/API和Vue组件；模拟准确工单/操作期初及Snapshot/Assignment/Claim、来源/执行观察、时长/交期/日历/维护输入，实际执行排列、五域CAS、Root/操作写入、回滚、批次/原结果和HTTP。业务验收结果为本自动排列范围通过；部署状态未部署，不等待真实企业数据。

## 交付行为

- 显式Site同cut完整捕获及30分钟有效期；500项完整manifest与20/50/100预览分页。无权工单只提供当前有权资源的不含身份占用，隐藏私有资源不披露，coverage标PARTIAL。
- 固定优先级/交期/路线/RootID/操作ID排序，准确技术微秒优先；1.3天LeadTime按旧截整10小时，缺省时长由用户显式给定。无机器UNASSIGNED、已开始锁定排除、缺约束UNKNOWN_CONSTRAINT，保留完整解释，不隐含8点、不假称APS或执行许可。
- 每Root一组，包括完整未改成员和根保留/扩展包络；日期未改变的操作保留版本。跨Root旧占用腾让形成准确提交依赖；最终围栏再检查当前其他Root的计划/执行占用，同资源版本变化不变成另一机器。
- 先持久原批次和每组发送意图，然后逐Root共同事务；组内Root/全部改期/application/audit/原组结果全成或全回滚。跨Root失败/未知前驱让后继等待，独立组继续，完整计数不能显示为全部成功。
- 同preview/Root跨命令和并发批次共用一个原组application；同键异意图拒绝，原单次改期与批次不能复用命令ID。撤权后按当前读权恢复；NOT_OBSERVED不证明无效果。停止只取消未发送组，UNKNOWN不释放、不另key重做。
- 页面要求显式范围/起点/时长/理由、完整清单确认，分页不改变所选原子组；保存原命令身份而不保存理由，跨页和未知恢复不重建候选，身份变化清除旧私有内容。

合同见[自动排列v1](../../scheduling-arrangement-v1.md)。W240原共同事务直接复用，新增行为符合Trust Internal Contracts/Fail-Fast；没有新增资格回执链或内部序列化往返门禁。

## 实际验证、复用与审查

28 distinct local backend scenarios PASS:11 arrangement PG18/actual HTTP,11 affected W240 scheduling PG/HTTP,6 permission-seed behaviors;23 Vue scenarios PASS (8 arrangement,7 time-only,8 existing planning); app vue-tsc PASS. Simulated business/source inputs, actual PostgreSQL effects and HTTP; unchanged successful evidence reused, initial compile failure retained.

11项新排程场景实际覆盖稳定manifest/旧估计和根一次版本/原结果，技术时长与未知/无机/锁定解释，整Root准备后Start导致整组拒绝和DAG等待，写入中途故障整组回滚后UNKNOWN及原组恢复、独立组不重复，500项分页仍一次应用全成员、超过500项Site仍可选择限额内完整Root，跨Root最终资源冲突，实际HTTP捕获/预览/提交/查询及候选篡改拒绝，精确旧占用前驱腾让和未改操作版本保持，并发不同命令同组结果/规划来源变更/过期，部分可读范围和私有资源不披露。原W240的11项受影响事务/权限/微秒/旧写围栏场景及6项种子行为在本轮实际执行通过；后续算法/隐私修正只重跑相关10项，选择范围修正后验证相关11项。

记录在 `D:/CP6-local-test/wave241/foundation/pg-*.json`及TRX；每场景取最后实际结果，不累计重复运行。汇总 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave241-validation.json`。页面 `D:/CP6-local-test/wave241-web-selection.log` 23项通过，类型检查 `wave241-web-selection-types.json` exit0。组件HTTP响应模拟，实际HTTP场景运行真实宿主/登录/PostgreSQL。

保留失败：首次编译缺少泛型返回类型推断，明确ArrangementPageV1后通过；没有运行失败测试被改称通过。集中审查修正了隐藏资源引用披露及未变时间也递增操作版本的问题，再以真实行为回归通过。收尾还修正了将500候选限额误加在整个Site捕获的问题：600项范围可选择其中300项，未选Root仍参与占用。没有触发Actions、没有取消远端运行，没有修改工作流事件/依赖/分支保护；未执行全仓回归、浏览器布局、正式发布构建或部署。

One author task SPEC/QUALITY review: full manifest and stable sort, technical/legacy/default duration, explicit unknown/unassigned/locked exclusion, unchanged operation versions, one atomic Root and current five-domain/source/physical-resource checks, immutable preview plus unique original group, durable SEND_INTENT/UNKNOWN, DAG wait/partial independent completion, current permission/revocation and tenant/privacy, stop only unsent, page-safe full selection and durable browser identity. Corrected private-resource disclosure, unchanged version increments and the distinction between a 500-selected-candidate limit and total Site size before final affected verification; no redundant internal qualification/roundtrip gates.

## 剩余范围与生命周期

原D3仍有资源更换六阶段、资源队列/EXEC事件和统一BoardCapture筛选KPI分页等未完成，不关闭完整WP08-10/WP08-11或D3。本轮启发式不声称最优APS或产能/物料齐套证明。工程/资源/执行产品宿主接线尚有剩余，业务数据可模拟。未部署生产或迁移既有数据库。

原七天目标ACTIVE，D1/D4已闭环，D2/D3/D5/D6/D7部分完成。2/7不是工程量百分比，375正式任务/125目标/247验收未由本窗口自动关闭。下一步：继续原D3资源队列、资源更换六阶段与统一看板接线；D2通用Transfer/RSV/ActualFact和退货商业财务、D5/D6/D7原剩余范围保留。缺少生产验收输入直接模拟，不等待企业数据或新增资格证明链。

COORD本聊天单人，Main基线`d65d48e72e96b27786ce96bb1385c677dbb7d952`，复用DEV-1/DEV-2；预计实际90–150分钟。W240已按最终proof归档一次，没有重复其实现/合并/清理。此报告为合并前快照；本轮正常合并、远端采用、准确分支删除及官方finish由最终proof核销，固定目录与可复用缓存保留。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/217)已完整采用并finish；Main待最终核对。本快照距开工33.97分钟。
