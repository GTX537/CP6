# W2：组合事务与恢复设计补充

本章属于 `CP6-DESIGN-REVISION-20261011/R1`，效力遵循 [01-version-and-authority.md](01-version-and-authority.md)。新增决策只具有本次任务的设计效力；原件不改写，既有设计接受记录不转化为运行采用。全部 consumer 的真实采用仍为 **UNPROVEN**。主登记中的 W2 逐项证据、完整源 SHA、正反验收预期见 [owner-adoption.json](assets/finance-recovery/owner-adoption.json)，整合输入见 [finance-recovery.json](workparts/finance-recovery.json)。本章没有业务执行、数据库迁移或生产验证声明。

## 1. OPS-IF-003：CP6-COMPOSED-RESOURCE-PLAN/1

冲突是真实的设计组合冲突：RMA v1.0 §9.1（原稿 699–711 行及 DATA-DICTIONARY 314–339 行）先锁 Scope/source/claim，再进入 IN/Stock；Stock v1.1.1 1414–1426 行要求全部 Canonical→Scope→fine Ordinal。两段分别照抄会形成 ABBA。本补充显式替代 **RMA 与 Stock 组合事务中 §9.1 的取锁序列**，包含 Scope-first 与 source/claim/IN 等资源在共同全序中的位置；保留全部原资源、保护目的与持锁验证条件，不改变 RMA 原 source coordinate、范围占用、DTO、Stock WriteKind 或 stock posting manifest。单独读原稿不再足以实现该组合入口。

不能从 Canonical 一概开始：FUL v1.4 R3 7328–7332 行已有 F−1 SalesCatalogueMembershipFence，DEL v1.3 R3 15270–15322 行已有 F0 Movement 解释门。完整组合次序如下。

|阶段|资源及同阶段顺序|必需覆盖|
|---|---|---|
|F−1|`(Tenant, Mode, LegalEntity, Site)` catalogue；跨 site ordinal|空 site 预建门，全部目录成员增删、过滤属性、时间生效与权限 writer；S 读/X 改|
|F0|全部受影响原 Movement 的完整 Ref/解释 epoch，ordinal|DEL/OUT/Stock 更正、关闭、permit、late apply；不允许先 F0 再补 F−1|
|Canonical|全部原 occurrence/canonical、执行别名及 leaf closure 身份，原全序|Stock 十二 WriteKind 及所有新增 source adapter|
|Scope|所有实际站点/仓库及祖先，先父后子，再 ordinal|不能仅锁 RMA 表面 warehouse 或单 target location|
|DETAIL|全部 owner/resource-key 按既有 ordinal 全序，预合并为最强 S/X|原 source membership/head、E/A/P entitlement 范围、RMA 授权、IN Work/Manifest、Stock fine、QA/H30、location metricSet/capacity、claim、全部反向目录|
|LOCAL_TERMINAL|按注册关系的 command/work/checkpoint/receipt、audit/outbox|同一 outer native Uow；触发器、FK、唯一键、行锁、范围锁也计入图|

这是一个**资源注册协议**，不是给每个 owner 新建同名 applock。物理身份为 databaseBinding、database principal、原锁资源字节/原行键/原谓词；别名必须折叠到同一物理锁。原所需资源仍全保留。RMA 的 `sourceOwner + ShipmentLine(owner,id) + Invoice(owner,id)/NONE + Schedule(owner,id)/NONE + baseUom` 不加入 RmaId、decision 或 sourceVersion；原 E/A/P 条件与 ACTIVE_RESERVED/CONSUMED_BY_RECEIPT/UNKNOWN_HOLD 占用仍在 Serializable head+范围谓词内验证。

### 1.1 计划、获取及提交

封闭 DTO 为 [contracts.schema.json](assets/finance-recovery/contracts.schema.json) 的 `ResourcePlanBody/ResourcePlan/Resource/Edge/NativeUow/LockReceipt`。所有属性 required，显式 nullable，拒绝额外字段；新 DTO 不向原 Stock/RMA wire 添加字段。`resource-registry.json` 登记本次三个 profile，实际物理资源及 writer 清单必须由采用方补充受信证明，示例清单不能冒充运行注册。

1. 在无业务锁的预读阶段解析所有 source、全部 warehouse/location、metricSet（含 N/A）、route/alias、执行、范围、authority 和 closure catalogue。包括目前不存在的行对应的 key-range/predicate guard。原 authority 和经济键完整保留。
2. 用当前不可变 registry 版本折叠 physicalIdentity，同资源选择 X 优先；生成依赖 DAG、完整有序 resources 与 acquisitionOrder，摘要覆盖全部字段、scope、sourceSetDigest、writerSetDigest、databaseBinding。每个 resourceId 恰出现一次；所有 edge 指向已登记资源；alias 无交叉重复；同物理资源不允许两个 node。
3. 注册图必须包含表中强制边及原 DEL K1→S2→S3→E4→D5→L6→O7、OUTPUT E→source→Stock fine 等局部边。若这些边与原 DETAIL ordinal 实际相冲突，拒绝 `LOCK_PLAN_UNPROVEN`，先完成双方新版本采用，不能临场选择一边。BP/EST/QTN 与 Finance 使用自己的完整已登记图，不把仓储 ordinal 强加给它们。
4. 在实际同 connection、database、principal 的 Serializable outer native transaction 获取全部锁。S→X 升级禁止；native transaction 再入只接受已持有同 physicalIdentity 且 mode 足够，不可借外层名称开启独立连接/独立 commit。
5. 获取后重核目录 generation、head、权限、plan digest 和所有 CAS。如果预读集合改变，整笔回滚并在无锁阶段重新收集；不得持后序锁补前序锁。负 applock 返回、超时、死锁受害者按原事务失败处理，不据此推导业务无效果。
6. RMA receipt 路径在同一次 commit 写入 Stock posting、IN Receipt、RMA Physical/H30 与 claim transition；任一失败全回滚。返回失败或确认丢失时查询原 command/Stock 原操作，不产生第二自然键。只有真实 transaction handle 证明才可出具 LockReceipt；序列化字符串或事后 proof 注册不证明原子性。

schema 中 Seq/Counter 的数值上界语义为 signed Int64；NativeScope 是**新计划的受信适配描述**，不是原业务 Scope 的替代品。原 RMA Guid/整数 revision、Stock 字符串 Int64、MATCH Key160、财务 Scope 均保持原 wire，不能因接入资源计划统一改型。所有 nullable scope 轴必须与具体 profile 的完整原 scope proof 对应；省略一个实际参与的维度拒绝。

### 1.2 全 writer 门禁

Stock 的 POSTING、CLAIM_CREATE/BACKING/ALLOCATE/PICK/RELEASE/REBIND、HOLD_PLACE/RELEASE、CORRECTION_APPLY、RECONCILIATION_APPLY、SOURCE_APPLICATION 逐一采用。还包括 RMA authorize/revoke/source change/entitlement/receipt/seal/correction/H30，IN 全 source adapter，OUT/RSV/DEL，MAT/EXEC/OUTPUT，QA/PLM source/control，MASTER location/metric membership/capacity、legacy、import、manual、worker、migration/cutover 和 repair。目录注册本身必须使用 X membership/control gate，防止“已验证全路径”后又添加可写别名。

每个 writer 证据须绑定 profile/version、部署入口与关闭的旧入口集合、物理锁名/原数据库身份、S/X 模式、native Uow provenance、CAS、失败与恢复路径；只有接收 receipt 的证据不足。一个历史写入口未纳管就不能声明对应 profile 已采用。跨库、未知 trigger、未知隐式锁次序、无完整 writer coverage：`NATIVE_UOW_UNPROVEN` / `LOCK_PLAN_UNPROVEN`，零新效果，继续 REQUIRED_FENCED。

测试必须覆盖 RMA 与 Stock 普通过账/更正/claim/legacy import 的双向竞争、RMA 多仓库、N/A→CONTROLLED metricSet 变更、新 alias 注册、先取 F0 后请求 F−1、锁模式升级、实际同名不同物理锁、IN 独立连接及 commit confirmation lost。静态样例只能验证资源计划结构和规定的排序；不能证明数据库不会死锁或所有 writer 已采用。

## 2. PLAN 的精确补充

[source-patches.json](assets/finance-recovery/source-patches.json) 是原 SHA + 唯一 before 原文 + after 原文的**数据补丁**。按source_path与原SHA分组，一次核对原件，再预检同组所有before并在同一内存副本顺序替换；不能拿中间已变更副本与原SHA反复比较。同组修改后的版本只可另存新版本设计，原档案不改。仅静态内存应用核对；原 SQL/C#/TS 不编译、不执行。应用方必须拒绝 SHA 不同、匹配次数不是 1、不同换行/BOM 或二次重复应用，不能模糊寻找近似文本。三个 finding 的完整源 SHA 与上下文见登记。

### PLAN-IF-005：ATP Replay→Evaluation

R11 DATA-CONSTRAINTS 明确 ReplayRecord→Evaluation→PolicyIdentity→Carrier→Receipt→children→audit/outbox。R11 repair evidence 只证明 bind_snapshot 的 helper；没有覆盖 `_complete_evaluation_internal` 2215–2217 行的反向锁。进一步实际展开调用链发现：`recover_by_id`2286 行先调用 `recover_integrity`，后者1350 行已拿Evaluation KEY SHARE；`calculate_projection_stage`2172 行经recoverStage先拿Evaluation，2193行才更新Replay；intake2022–2029行一次join `FOR UPDATE OF r,e`也不能作为明确顺序证明。

本修订对同一原SHA提供五个补丁片段：终结入口先按完整自然键C ordinal查锁Replay再锁Evaluation；calculate和A04在任何recover validator前按相同次序预取Replay UPDATE、Evaluation UPDATE；binder的原精确Replay槽从KEY SHARE改成预取UPDATE，避免同outer Uow后续计算/终结升级；intake旧结果分支把join锁拆成先Replay再Evaluation。无replay行抛 `REPLAY_BINDING_MISSING`，原authorization ticket、CAS、cold validator、terminal+replay atomic write保留。新建intake保留原自然键advisory transaction锁→本事务不可见的新Evaluation/Replay行创建；外部事务不得先取得尚未发布的Evaluation再加入该Replay槽。

不得把新查锁本身当作改变 replay 所属关系的许可：BOUND 后 replay/evaluation 关联不可变；所有可创建/重关联路径必须采用同一原 replay 自然键协议及组织隔离。若运行实现允许一个 evaluation 在完成阶段增加关联 replay，这违反此采用前提，必须关闭该写入口或先补共同 membership gate，不能上线这段局部补丁后宣称全局闭合。补丁排序支持既有多个相关 replay 行，不擅自增加“一个 evaluation 只可一个 replay”的数据库唯一约束。

正验收：intake replay、binder、calculate、A02/A04 terminal并发相同evaluation，锁顺序都Replay→Evaluation，后续不升级先前共享Replay锁，原CAS只允许合法一次结果。反验收：无replay拒绝；任何Evaluation-first入口、公开可写路径绕过helper、关联集合可变writer或read后在同事务转为write却未预收完整资源仍存在，则adoption不通过。独立只读cold validator不因只需要Evaluation而强制锁Replay，但其调用者后续要写Replay时必须先执行共同prelude。R11历史PASS_STATIC_DESIGN_DATABASE_NOT_RUN保持其原覆盖范围。

### PLAN-IF-010：replay 与新成功响应的当前授权和有效期

CP18 permission catalogue 128/137/167 行要求 replay 当前对象/action/field-mask 授权、原 receipt 不因撤销消失，以及每次输出前有效期。原 C# PlanningAction22 行与 TS106 行 old 分支仅初始授权，新成功分支也仅在 commit 前校验。本补丁在 replay 的原 canonical Parse 后、outer commit 前调用既有 `Authorized` / `authenticated`；replay 和新成功两个分支均在 confirmed commit 后、输出原 body 前再次调用同一校验以覆盖等待过期。该最终校验立即读取当前时间，之后到响应选择间没有异步等待；复用已有对象/action/scope 参数，不新增会计权限。

这是原 current Core/provider 门的补齐，不冒称一个已取得的 authority 对任意未来撤销有分布式原子效力。Core provider、current local authorityRef/head 与原 scope gate 仍必须真正采用。过期/撤销按原 permission catalogue 返回当前可见性对应 404 或 action 403，不能返回旧或新敏感 body，不能删除原 receipt 或重执业务。commit UNKNOWN 仍 503 + query 原命令，不以等待期间的授权过期替代 UNKNOWN，也不返回候选 body；confirmed 后拒绝响应不撤销已经提交的业务，后续在新鲜授权下查询原命令。两分支在 await commit 返回后立即记录共同的 `commitConfirmed`，再执行最终授权检查；catch 仅在未确认提交时调用 rollback，避免二次异常掩盖提交后的授权拒绝。原 `replayed` 字节不更改。[静态时序向量](assets/finance-recovery/authorization-timing-vectors.json)覆盖两语言两分支的 capture 等待过期、commit 等待过期、恰好到期、有效原文输出和 UNKNOWN；验证器还拒绝缺少最终检查、先检查后记录提交、已确认后仍 rollback 及 UNKNOWN 也执行最终检查的错误补丁。它不执行原业务代码，真实 provider/field-mask/撤销与并发验收仍为 NOT_RUN。

### PLAN-IF-011：TerminalCanonical 结果集

`ApplyOwnerIntermediateCp18` 75 行已经将经过校验的 `@LocalTerminalCanonical` 写入原 CommandSlot；新成功分支 146 行只有 COMMIT，既有重放35 行则返回 `TerminalCanonical, Replayed`。新增成功返回相同一结果集一行两列：原 canonical string + `Replayed=0`；重放保留 `Replayed=1`。consumer 只要求并取 TerminalCanonical；不能把辅助列加到已封闭 CommandOutcome JSON。

原存储过程内 COMMIT 在存在外层 native transaction 时不等于 outer commit confirmed；C#/TS 继续等待 `CommitConfirmed/commit` 才响应成功。失去确认返回原 503、原 commandId/query，不重跑外部 owner。Enqueue 子过程及驱动不得泄露额外结果集；多/零结果集必须 `SQL_RESULT_SHAPE`。正验收新结果和重放 body 字节相同；反验收缺列、多结果集、外层确认丢失。

## 3. 已有设计的采用与边界

以下不是新设计缺口。每项完整 evidence、consumer 清单、正反期望在机器登记中；本节列出必须保留的区别。

|登记|原版本与实际 consumer 应采用的规则|
|---|---|
|COM-OWNER-LOCK-ORDER|BP R3/EST/QTN 完整 dependency DAG；IAM→BP→EST，保留各 owner 内 command/mapping/reverseClaim/root 子序；反向调用、漏闭包/循环拒绝。CRUD/import/legacy 与消费共享门。|
|COM-IC-PROTECTION-ADOPTION|INT-COM1.0.2 仅两 purpose；durable grant 无超时释放；source Control→ActiveGrant 与本地 Execution 原 COMMITTED/ABORTED 对账。即时敏感权限撤销独立，需要即时撤销在途效果的 purpose 禁用协议。|
|COM-IC-BOUNDED-REBUILD|INT-COM 完整原 H/V、handoff/attribution/group/conflict、Intake/Quote/Commercial/Resolution 目录；代际分页+最终CAS；UNCHANGED只checkedAt；向量cut不冒充全局now。|
|COM-FUL-CATALOGUE-AND-CURRENT-FENCE|FUL1.4 R3 F−1空site母目录和F0外层门；ORD所有schedule与过滤/生效/权限writer纳管；严格current仅native同库，跨库UNKNOWN。|
|COM-S6-PRECHECKPOINT-FINALAUTH|S6 v0.2重复/旧/等版本也先真事务precheckpoint，双摘要冲突DRIFT quarantine；OwnerFinalAuthorizationGateV1与权限撤销同native gate；读历史/可信登记/机器重投/新业务分别授权。|
|OPS-IF-004|RMA五处置adapter逐个 REQUIRED_FENCED；每个 owner/route/capability/query/seal/correction 完整采用才单独开启；QA-NCR接受或IN收货不替代处置授权。|
|OPS-IF-005|DEL1.3 R3原F0→K1→S2→S3→E4→D5→L6→O7；全部Movement与Stock discovery cut核对，漏通知WAITING_SOURCE；close和commit同门且原提交优先恢复。|
|OPS-IF-009|MAT pending basis succession 与 EXEC X44 CURRENT 后继已经设计闭合；PENDING_BASIS_SUCCESSION独立purpose，同EXEC-BASIS-DOMAIN无gap转移义务；superseded-before-effect非物理无效证明。|
|OPS-IF-010|OUTPUT真实E IdentityDomain/measurement budget早于Basis/work，完整report/correction/complete/late/migration writer纳管；E B writers不能反取Stock/WO；受控Facade不等于已有运行端点。|
|OPS-IF-011|OUTPUT缩量关闭同allowance下全部Stock执行/aliases，禁新SP01/SP02；100已消40改90只剩50，旧消费不改；单grant seal不证明全路径关闭。|
|OPS-IF-017|QA-NCR R12 root decision与proof addendum2窄范围替代旧缺口；实际Uow、完整proof JCS与current generation保留。Finance H30仍只有REACCEPT/SCRAP/REWORK/HOLD，RETURN_TO_SOURCE保持PENDING语义未映射，不能假作HOLD。|

## 4. 验证范围

本任务只运行自建静态资产验证器：逐原件 SHA、补丁唯一原文匹配、schema 已用关键字与结构、正反合同样例、登记全覆盖、状态枚举及文档链接。源候选 SQL/C#/TS 与业务测试均 **NOT_RUN**；权限、库存/财务、真实锁竞争、真实 source/owner/adoption 全部仍需实现阶段专项验收。静态结果见 [static-report.json](assets/finance-recovery/static-report.json)。
