# W53 / 七天一期 D5：CRM PostgreSQL 当前投影与依赖图

COORD本聊天单人，2026-10-23T02:30:45.433557+00:00。Main `14895c15b339d17c1d7887dff2cdee1ee70bac1d`、CRM `248e179cc1b1e9bc31a0f3c09e36f7004a270f6d`、Handoff `791c53a8c42ea54583802802aa53ee1e997af931`。W52 CRM97/Handoff28/Main421与独立BUG419 PR420已采用、准确任务分支删除、固定树干净detached、全部席位finish；登记实际48.35min（含BUG419，不累计子任务重叠），前置另约6min。12个CRM用例和一个Main驱动通过，驱动/子用例同一场景，另复用6项BUG419验证。七天目标ACTIVE，不重复W37–W52。

## 前置、准入与边界

已核最新AGENTS/POLICY、台账WP02-06/09直接前置、INT-ID-01当前合同、W51本地PG会话fence、W52完整Main原始基线与封存证据及CRM旧Published源码。旧编译器依赖8字节SQL RowVersion，不给PG伪造该版本。全WP02-05/06/09及真实验收仍未完成。capacity无告警；主目录用户文件保留；只复用两个开发席位。

本窗实现CRM PG当前投影引用和独立依赖图读取：引用W52不可变原始Items，明确原版本、摘要、删除和source lineage；较低版不回退，同版不同内容留冲突并关闭图读取，缺少完整目录项不误判完成。显式调用方Serializable事务中完成引用替换、冲突/状态、原始证据和已支持token撤权；失败全组保存点回滚，不自行commit/ACK。

对账begin返回本轮原生revision/attempt/有界lease；complete在共同W51行fence下重核source、租约、CAS，失去租约/迟到完成不能覆盖新状态。依赖图保留完整合法v2角色、字段/部门范围/时刻精度，缺祖先/role不贡献可用用户；读取明确是PendingInspection，不能产生Allow。持久当前引用与只读编译的typed图分开，不宣称完整持久Published或生产授权已经采用。

PreCheckpoint全早退、Platform0.10.3七阶段锁序、真实事件来源/AuthoritySelection冲突恢复、所有writer共同最终锁、Owner事务、宿主Program与部署仍在后继窗口。W51行fence不冒充Platform全锁序。本窗不改旧SQL历史迁移、不切换既有环境、不启用第二消费者、不设Ready/Allow；不能因基础可用而提升125目标或247真实验收。

## 实际预算与固定席位

预计实际80–130min：PG引用/租约/CAS与防回退30–45；依赖图和定向故障/并发验证30–50；集中作者SPEC/QUALITY、三仓合并与finish20–35。按实际记录，不等待日历。

DEV-1 Main `codex/w53-coord`只维护台账/派工/七天计划/四状态；DEV-2 Handoff `codex/w53-handoff` START保存后finish deferred，bindCRM `codex/w53-01`实施/合并/finish，再bind回Handoff resume交付/合并/finish，Main最后收尾。集成席位无实现，不新增物理目录。

允许CRM Infrastructure/Identity/PostgreSql与独立PG依赖图模型、Baselines必要内部source复用、项目嵌入资源、相关PG测试和文档/四状态；独占CRM身份/PGschema资源。只跑本地相关行为，复用W52未变实际Main协议证据，不再重复跨仓联通。PG隔离55437；一次任务级作者审查，失败记录保留。

W51准确data删除曾被自动审批拒绝，唯一理由blocked by policy；其已停止数据保持保留，不重试绕过。W52三个自有PG也已停止，保留证据。

## W53实际交付

CRM [PR98](https://github.com/GTX537/CP6.CRM/pull/98)已采用，source `796ef79442eb718ad311256b8f0e2598f2c3e104`，远端main `7c74f9c9471415df4f4f6429671b8665fafbb4cb`，13任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/7c74f9c9471415df4f4f6429671b8665fafbb4cb/docs/development/evidence/W53-01/DELIVERY.md)。22个不同相关本地用例通过：7个原生PG投影/事务、4个依赖图、9个W52基线、2个会话回归；W52未变Main协议证据复用；无Actions、非完整Owner验收。

PG当前引用指向W52不可变原始Items，防降版/同版覆盖；冲突同事务记录并关闭图读取，缺目录项为Unavailable。调用方Serializable事务内保存原始证据/已支持token撤权/引用/状态，savepoint错误撤回半套效果；2分钟原生租约及attempt/revision CAS阻止迟到完成。原生引用写入也使旧ticket失效。独立typed依赖图不伪造SQL RowVersion，保留完整合法角色/字段/时刻精度；图只供PendingInspection，CanAuthorize=false。

一次作者SPEC/QUALITY审查通过，最后补充实际中途数据库异常且上层catch后commit的回滚用例；所有失败日志保留。自有PG已核身份和零外部会话后停止，数据/证据保留，不重试W51被自动审批拒绝的准确删除。CRM准确本地/远端分支删除，固定工作区干净detached，manage finish释放后DEV-2复用Handoff。

正式Platform0.10.3 Inbox/PreCheckpoint全早退、历史一致性与AuthoritySelection、完整持久Published/当前授权、所有writer最终门、真实Owner共同事务和Program采用仍待后续。W51行fence不冒充七阶段锁协议；ObservedAtUtc只是本地应用时间。无现有环境切换/生产部署，不提升24/375整卡、125目标0交付或247运行NOT_RUN。七天目标ACTIVE，继续D5。

Handoff [PR29](https://github.com/GTX537/CP6.AI-Handoff/pull/29)已采用 `80737129714840a079cde5ea7a015ff2b4f4a016`，准确本地/远端分支删除、工作区干净detached、finish释放。Main仅剩当前协调分支正常PR及finish，不提前声明释放。登记开工至此约25.90min，原预算80–130min，实际最终时长以finish为准，不重复累计子任务。此前只读核对约2min。
