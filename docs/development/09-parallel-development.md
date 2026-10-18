# 多对话任务分配与交付

> 当前接续：[第十七轮](dispatch/20261018-wave17/README.md) A/B/C/D真实开工已收；D固定补证候选3bc3d3c8已到，A接续原审查；B修正836de524已到待C局部复查，C自身修正中；[工作目录与存储计划](planning/20261018-workspace-storage.md)启用已完成目录复用，新任务仍独立分支，本轮工作树/资源不搬动。下方“尚无开工”为历史。

> 当前分配：[第十四轮原四对话执行包](dispatch/20261017-wave14/README.md)。A修复#38；B/C交叉只读审查现有C/B候选；D验收准备并在A交付后审查。窗口已登记，尚无真实START；旧候选普通集成仍P0暂停，#245已关闭。

> 最新收口：A/#245 已通过[正常PR](https://github.com/GTX537/CP6/pull/248)合并并核对远端，限定窗口CLOSED、Issue已关闭。#38是下一P0准备项，B/C/D普通集成继续暂停；完整任务和业务验收未提升。

> 当前集成：A/#245 权限候选已完成审查修正及局部复查，准备正常串行合并；见[单份报告](evidence/WP02-03/current-permission-consumption/DELIVERY.md)。原A工作树保留，完整任务与业务未关闭；#38仍待独立开窗，B/C/D普通工作继续暂停。

> 优先顺序已批准：[公共核心基础收口计划](planning/20261017-foundation-priority.md)。四队列职责是后续方向，不自动重派；当前先P0，原候选和窗口状态仍以台账为准。

> 最新协调：[全项目盘点与 P0 优先安排](planning/20261017-project-inventory.md)。wave13 四份真实 START 及四份候选 DELIVERY 已接收、尚未审查集成；A 优先处理 P0 #245，B/C/D 普通窗口暂停指令待执行者回执。以下“尚无 START”等为历史快照，完整任务与业务验收状态仍以台账为准。

> 当前分配：[第十三轮四个新对话执行包](dispatch/20261017-wave13/README.md)。A当前授权读取、B服务租户资格control、C实际publish启动身份、D菜单刷新恢复；四个限定窗口CONFIRMED，尚无真实START。第十二轮CLOSED，完整任务与业务验收未提升，COORD统一共享状态与串行合并。

> 当前收口：[第十二轮真实服务签发交付](evidence/WP02-02/service-issuance/DELIVERY.md)已正常合并并核对远端，B限定窗口CLOSED；完整WP02-02及业务验收未完成，A/C/D无新窗口。

> 当前集成：第十二轮B / WP02-02真实服务签发候选已完成相关验证及一次集中审查，正在同步状态并正常串行合并；见[报告](evidence/WP02-02/service-issuance/DELIVERY.md)。完整任务/业务验收未提升，A/C/D无新窗口。

> 当前分配：[第十二轮真实服务签发租户事务](dispatch/20261016-wave12/README.md)。仅B/WP02-02窗口CONFIRMED，唯一COORD代理已开始只读准备，准入发布后实现。第十一轮CLOSED，A/C/D无新窗口；完整任务与业务验收未提升。

> 当前收口：第十一轮B / WP02-02自撤销局部交付已正常合并并核对远端，窗口CLOSED；见[单份报告](evidence/WP02-02/service-revocation/DELIVERY.md)。完整WP02-02及真实业务验收仍未完成，未自动分配下一窗口。

> 第十一轮集成时点：B / WP02-02服务令牌自撤销候选已完成相关验证与一次集中审查，COORD正在同步共享状态并正常串行合并；见[单份报告](evidence/WP02-02/service-revocation/DELIVERY.md)。完整任务/业务验收未提升，A/C/D没有新窗口。

> 第十一轮原开工登记：B / WP02-02由当前COORD对话内唯一实现代理接续，实际资料读取已开始；执行身份登记发布后进入代码实施。旧B对话请读最新台账，不重复领取或并写工作树。范围仍以[原执行包](dispatch/20261016-wave11/README.md)为准，完整任务与业务验收未提升。

> 第十一轮原派工登记：[第十一轮服务令牌自撤销接线](dispatch/20261016-wave11/README.md)。仅B / WP02-02限定窗口CONFIRMED，真实source/binding、有限写集隔离及同事务入口由一个Owner连续实施；尚未收到START。A/C/D无新窗口；第十轮三个窗口继续CLOSED，完整任务和业务验收状态不提升。

> 当前交付：[第十轮三份局部实现已集成](evidence/wave10-integration-20261016/README.md)。PR231/233/232正常合并并核对远端，真实START与交付已登记，A/B/D窗口CLOSED。完整A/D仍PLANNED，B仍IN_PROGRESS；真实业务接线和验收未提升。本次未派下一轮。

> 历史集成：已收到第十轮三份候选，按任务串行同步状态与合并；本窗口接收 WP02-18，见[交付报告](evidence/WP02-18/dashboard-states/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 历史集成：已收到第十轮三份候选，按任务串行同步状态与合并；本窗口接收 WP02-02，见[交付报告](evidence/WP02-02/provisioning/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 历史集成：已收到第十轮三份候选，按任务串行同步状态与合并；本窗口接收 WP02-03，见[交付报告](evidence/WP02-03/field-decisions/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 第十轮原分配：[第十轮限定实现](dispatch/20261016-wave10/README.md)。A→WP02-03 字段权限决策，B→WP02-02 PostgreSQL 初始化与恢复，D→WP02-18 工作台数据状态；C 本轮不新增任务。三窗口已登记，尚未收到实际 START；完整依赖和业务 NOT_RUN 保留。

> 上轮交付：[第九轮四份局部实现已集成](evidence/wave9-integration-20261016/README.md)。PR225/227/226/228 已正常合并并核对远端，真实 START 与局部交付已登记，四窗口关闭。完整任务 A/D 仍 PLANNED，B/C IN_PROGRESS；真实独立信任、Owner/宿主业务接线及正向回退兼容仍未验收。本次没有分配下一轮。

> 历史集成窗口：已收到第九轮四份候选，按任务串行同步状态与合并；本窗口接收 WP02-28，见[交付报告](evidence/WP02-28/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 历史集成窗口：已收到第九轮四份候选，按任务串行同步状态与合并；本窗口接收 WP02-16，见[交付报告](evidence/WP02-16/codegen-preview/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 历史集成窗口：已收到第九轮四份候选，按任务串行同步状态与合并；本窗口接收 WP02-02，见[交付报告](evidence/WP02-02/identity-priority-tenant-isolation/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 历史集成窗口：已收到第九轮四份候选，按任务串行同步状态与合并；本窗口接收 WP01-01，见[交付报告](evidence/WP01-01/trust-stages/DELIVERY.md)。其余任务以最新台账和各自报告为准，历史分配中的“未收到 START”已被本轮真实 START/DELIVERY 替代。局部交付不等于整项 DONE 或业务验收。

> 第九轮原分配：[第九轮四对话执行包](dispatch/20261015-wave9/README.md)。A→WP01-01 分阶段信任配置，B→WP02-02 Identity 四表与 ERP 同事务隔离，C→WP02-28 实际宿主身份，D→WP02-16 安全即时预览。四工作树与写入/端口范围已登记，尚未收到本轮 START；已确认窗口发布一次真实 START 即可继续，不重复等待 ACK。COORD 只保留共享台账与串行合并。完整状态与局部窗口分别记录，不提升业务验收。

> 历史已交付窗口由 COORD 独占 WP02-02 存储/EF：ERP 8 张共享表的受保护目录、RLS 与同事务 EF 接入器已实现并经自有 PG 验证，见[本步骤交付](evidence/WP02-02/erp-tenant-isolation/DELIVERY.md)。既有 handler/worker 尚未切换；后续继续真实宿主/Owner、其他上下文映射和生命周期。完整任务 IN_PROGRESS，原 17 PGT 与业务验收 NOT_RUN；其他对话未新增派工。

> 前序窗口由 COORD 独占 WP02-02 租户存储窗口：内部绑定、专用池及事务租约已实现并经自有 PostgreSQL 定向验证，见[本步骤交付](evidence/WP02-02/tenant-connections/DELIVERY.md)。四模型 328 表清单准备下一步映射；真实宿主/Owner、RLS/helper、EF 及原 17 PGT 仍待完成，完整任务 IN_PROGRESS。其他对话未新增派工。

> 前序窗口由 COORD 独占 WP02-02 Main 正式包采用窗口：四处引用与 16 份锁升级到正式 0.10.3，相关本地检查通过，见[本步骤交付](evidence/WP02-02/main-package-adoption/DELIVERY.md)。完整任务 IN_PROGRESS，随后进入 PostgreSQL 租户存储；原 17 PGT 仍 NOT_RUN。A/B 第八轮局部窗口已关闭，[原授权及一次发布](evidence/authorized-adoption-release-20261015/README.md)和下方分配记录保留为历史。

本轮把14个工作包展开为可分配任务，覆盖125个原设计目标。任务是有界派工范围；接手对话先按准确来源和当前源码细化执行步骤，再实施。卡片中的历史源码入口不是永久不变的补丁位置，`PLANNED`不等于已开工或已验收。

唯一状态仍为 [roadmap.json](roadmap.json)。任务表见[07](07-detailed-tasks.md)，各工作包的任务卡与目标完成清单见[10](10-work-package-tasks.md)。本次生成交接材料，没有自动创建用户侧对话或启动业务实现。

## 当前执行节奏（2026-10-15 用户决定）

优先完成可运行的功能。每项任务只保留受影响行为的验证和一次集中审查，SPEC/QUALITY合并；文档只查diff和必要链接，执行者自查即可。复用有效结果和既有报告，不以测试或证据数量作为目标，不为每次提交重跑。已批准的实施窗口发布一次真实START即可继续，不再等待同范围的运输ACK。具体按[仓库规则](../../AGENTS.md)执行。

第八轮A保留adapter/manager关键正反回归，不逐项制造579个测试或重放旧准备检查。B只验证本次Release/Capabilities/版本脚本及实际包内schema；架构检查仅在架构输入受影响时执行，Main三入口/私有宿主兼容验证留到实际消费任务。未执行范围保持未验收；权限、租户隔离、金额/库存/并发一致性、已接受合同和正式发布要求仍按实际风险保留。

## 如何分配

建议先开4个执行对话，协调对话负责总依赖、公共合同和合并。对话名表示责任范围，不表示整个领域使用一个永久分支；每交付一个任务，再从最新main开下一个任务分支。

| 首轮对话 | 先接任务 | 可独立做的结果 | 必须守住的范围 |
|---|---|---|---|
| A 精确合同基础 | S01-01 | 核准确来源/组合hash，交付各profile共用向量 | 不改真实Owner，不把静态结果写作业务PASS |
| B PostgreSQL多租户 | S02-00 | 选定共享/租户存储布局，写权限/连接池/迁移/恢复设计与场景 | 优先评估schema；不自动创建schema、迁库或删除TenantId |
| C 工程历史验收补订 | [WP05-01](task-plans/WP05.md#wp05-01) | 有版本替代依据、旧→新覆盖与不可覆盖记录 | 不重新索要不可找回原件，不伪造旧30/48清单 |
| D 全链验收准备 | WP14-01 | 跨域用例责任、独占测试数据需求与证据矩阵 | 不写其他三个任务的文件，不以fixture成功冒充业务验收 |

任务精确ID和初始候选清单由台账生成；没有前置任务也仍要核对所需资料、代码和互斥资源。候选不是自动READY。首轮交付后，协调对话可按依赖依次开放平台/Main/CRM基础，再同时推进有前置能力的商业、工程、库存与财务任务。

后续按业务责任保留8类工作队列：公共平台/租户、CRM/商业、工程生命周期、计划/采购、制造/质量、仓储物流、财务、OA/SPACE。队列可以轮换到现有对话，不必一次创建大量对话，也不把工作包层面的接口关系当成必须整包完成的依赖。

## 对话间直接沟通

用户指定 [CP6.AI-Handoff](https://github.com/GTX537/CP6.AI-Handoff) 为直接沟通通道。[本轮开发交接入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)约定首轮四任务的资料分支、消息目录、固定提交回执和读取时机。执行对话将 START、QUESTION、PROGRESS、DELIVERY 发布在自己的资料分支；总协调直接读取固定提交并在协调分支答复。用户无需复制普通启动报告、问题正文或交付包。

Handoff 的资料分支与业务实现分支不同；当前仓库没有 main，本轮协调分支从已核对的实际默认分支创建，独立发布通信资料，不更改其根 CURRENT 或历史设计指针。各对话首次写入前必须读取上述入口；不要以历史 PD01 Lane 或旧设计百分比替代本轮任务卡。

Handoff 记录通信与证据定位，唯一任务状态仍为本仓 roadmap.json。START 是领取申请/启动回报；协调对话核对后在本仓登记 assignment、entry_gate 和资源窗口，再回传准确台账提交。上传、ACK 和自检均不代表任务准入、用户批准或 DONE。业务提交/检查/远端包含性仍按所属仓库规则验证。

在每轮协调开始及任务启动、依赖变化、交付、恢复工作时读取相关远端新消息，以完整提交 SHA 和路径识别已处理记录。发布消息不会自动唤醒休眠对话；本轮没有新增后台轮询或通知服务。无法访问私有仓或推送失败时如实标记 LOCAL_ONLY / PUBLICATION_BLOCKED 并保留内容，不声称已送达；只有需要用户决策或授权的事项再交用户。

## 领取与开工

1. 协调对话选择一个准确任务ID，核对它的`dependencies`均已真实交付、`entry_conditions`及准确输入可满足。跨仓读取远端main和实际发布包，不能引用别人未合并的候选分支作为已交付依赖。
2. 在唯一台账填写本任务`assignment`（对话标签、仓库、分支、worktree和领取时间），确认本轮资源窗口，将`entry_gate`置为`CONFIRMED`。拆分交付时所有任务均未领取、门状态为`NOT_CHECKED`；当前值读取最新台账，不将这份历史快照当作实时状态。
3. 执行对话读任务卡的设计/验收来源及当前AGENTS，按最新代码写出本任务执行步骤。其后在独立工作树实施，只触及已分配范围；若发现新依赖或设计冲突先把证据发回协调对话，禁止私自改共同合同。
4. 任务状态按PLANNED→READY→IN_PROGRESS→VERIFYING→DONE记录；资料不足或等待外部决策才登记具体BLOCKED。准备阶段可以阅读/形成任务私有计划，但不得绕过依赖开始业务写入接线。

登记工具检查任务ID、依赖环、目标主体责任、状态/证据和活动任务资源冲突；它不创建Git分支、不建立分布式锁、不查询其他对话实时状态，也不认证外部执行证据。必须只有一个协调对话处理领取记录，不能各分支同时修改台账后声称已经取得互斥锁。

## 文件与资源边界

每个任务的`exclusive_resources`列出其主要共享写入区域；重叠资源不能同时处于IN_PROGRESS/VERIFYING。它是派工约束而非生产数据库锁。路径相同、生成来源相同、公共接口或迁移历史相同，即使资源名不同，也必须补登记冲突；字符串检查不能代替diff审查。多张卡共用的源码入口已保守登记`file:仓库:路径`资源；源码入口可能只读，领取时可依据实际写入清单缩小预约，但必须由协调对话记录依据，不能为了并行自行删锁。

共同文件按交付窗口串行整合：

- `docs/development/roadmap.json`、生成视图、`docs/project-memory/PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`。
- 同一仓库的解决方案/项目引用、依赖锁、生成器输入与公共Schema/TypeRegistry。
- Main `CP6.Core/EFDbContext/CP6Context.cs`、PostgreSQL模型/迁移与其他仓库各自DbContext/迁移历史。各域可以先在任务内独立实体/配置文件实施；注册共同模型时由协调对话安排一个明确编辑窗口，不能同时生成相互不知情的迁移。
- 共同事务、库存mutation、财务过账或身份授权的同一入口。业务服务拆在不同文件不等于它们的数据写权可以独立改变。

每个实际任务的私有计划、测试输出和交付报告分别用任务ID命名。执行对话不要编辑其他任务的报告；失败和旧证据保留。协调对话在该任务分支交付时统一合入状态变化，避免以`ours/theirs`整体覆盖JSON或生成文件。

## 跨仓与合并

Main、CRM和Platform任务分开提交/PR。生产者先交付准确Schema/包/行为，消费者基于已交付版本采用；契约向量可提前独立准备，但不能把模拟Owner当真实消费通过。各任务有能力边界：例如Main同事务原语不会自动证明所有财务/库存Owner已接入。

合并前核对当前主线差异、新冲突和旧验证可复用范围；只重跑受影响检查。必须在每次push/PR/merge前检查自动工作流触发，未经新授权不启动Actions。普通任务不改发布权威、生产门禁或既有环境，后续数据库目标统一PostgreSQL。模型迁移/真实数据切换/部署须按各自独立任务和授权办理。

合并后核对所属仓库远端main包含任务提交，必要冒烟完成后登记DONE，再清理该任务自己的worktree；保留其他对话工作区。只完成一张任务卡不自动关闭目标；目标需其主体任务、原验收、修订场景和远端交付证据均满足。

## 可复制的交接消息

将下面消息中的任务ID替换成已领取的准确编号，并附台账中的分支/worktree，不需要复制整份归档：

```text
请接手 CP6 任务【已领取的任务ID】，仅完成该任务卡定义的范围。
入口：D:\CP6\docs\development\07-detailed-tasks.md 和 10-work-package-tasks.md。
先读 D:\CP6\AGENTS.md、任务对应卡片、原设计/修订输入与验收来源；
直接沟通入口：https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md。
读取本轮通信约定，在本人资料分支追加启动回报/问题/交付；从协调分支读取本人回复，不让用户转述普通报告。
再核对任务所属仓库的最新远端main及已交付前置，读取协调对话登记的assignment和entry_gate。
后续唯一数据库PostgreSQL；schema租户方案以S02-00的已交付决定为准，不删除既有TenantId/授权。
从已确认main在分配的独立分支/worktree细化执行步骤并实施，禁止修改其他对话工作区。
只改本任务分配文件。若涉及公共合同、同一EF模型/迁移或共享写入入口，先取得协调编辑窗口。
按任务卡正反场景做相关本地验证，保留失败及源码/产物证据，未执行保持NOT_RUN。
将结果写入本任务独有的报告，通过Handoff固定提交交付给协调对话，同步台账及四份状态页。
按仓库保护流程交付；未经新授权不运行Actions、不迁移既有业务数据、不部署生产。
最后报告：实际改变、检查结果、准确各仓SHA/PR、剩余范围与下一任务可消费的合同版本。
```

台账读取命令使用Python 3.10+：`python docs/development/tools/manage_roadmap.py`。本机已核对解释器位置见[上轮检查](evidence/s01-planning/README.md)，裸`python`可能指向旧版本；不要因此改用付费远程运行器。
