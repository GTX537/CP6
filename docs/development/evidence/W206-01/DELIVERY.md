# W206 仓库草稿独立入口与旧业务隔离交付

COORD单人；CP6-7D-20261022 D2，WP10-01/WP10-10限定窗口。Main `codex/w206-01` 基线5a6aa0328d4dca81f15cd7453c7f5a08135410ca。W205完整收尾，归档一次，只关闭其两个Transfer environment限定生命周期；完整任务状态不变。

## 实际改动

- 新增 Main.Wms.WarehouseDraft.v1：真实 Space_Site 原件和当前查询权限、既有五类 WarehouseType 明确版本，Tenant内跨Site规范代码保留，DRAFT/BizRevision=1、实体、部门范围、审计和原结果共同提交。
- 当前 add/query/abandon、1–5部门范围、WmsRoleScope和字段权限；最终源版/权限撤销整笔回滚；同Actor当前读权恢复、UNKNOWN/close与迟到创建串行，原键结果不重复创建或泄漏旧私有名称。
- 新迁移安装实际原生 WarehouseCd/Code/Id 消费者写围栏，创建核实际列/触发器并锁当前消费者，避免旧悬空代码经新建被悄悄采用。原生库存/Location等不得使用新DRAFT。旧锚不改名迁移，首次正式采用后该Tenant后续新增不能绕过协议。
- 名称维护复用真实MASTER-NAME/1入口，当前ETag、同值NO_CHANGE、不升版，实际变化业务版+1；新DRAFT无原操作的裸改名/换身份/软删/启用也被数据库拒绝。
- 独立 `/wms/warehouse-drafts` 页面及 `/api/wms/master/drafts-v1`，明确Site/类型/部门，安全回查；本地存储只有原操作UUID，Web Locks和新鲜权限，失权与迟到响应清理。已有仓库查询/名称维护保持独立入口。

[合同与范围](../../warehouse-draft-v1.md)。本地技术列false/100不作为有效库存或路由政策。BaseCode/ManagerCode可空合同及10/20位合法长度保留；非空缺真实关系Owner明确拒绝，未将M_Staff较短字段或Sys_User冒充负责人。新Location初始化及ACTIVE/DISABLED/RETIRED保护链未实现，不关闭全DEV-M01/MASTER/Transfer/Stock或七天目标。

## 实际验证与集中审查

33 PG18/Program HTTP; 45 Vue;types/build PASS;0skip。最终证据 `D:/CP6-local-test/wave206/foundation/final-validation.json` 逐用例保留真实执行来源。仓库草稿使用最终实现重跑；旧仓库读/名称/范围/实际HTTP使用native-fixed成功部分复用，之后仅新草稿reservation SQL及服务端子查询分页发生变化。集中审查另验证旧Stock INSERT真实等待创建锁，锁释放后读取已提交DRAFT并拒绝，库存仍为零；Handoff保留先前32项成功子集，Main补充此项。前端代码未改变，45项Vue与types/build复用本轮成功结果。

保留全部初始失败：测试辅助函数重载误选旧FlowDraft Owner（编译）；最终检查错误地再次登记回调（事务回滚）；information_schema.sql_identifier[]与text[]比较未转换（PG拒绝）。撤权夹具的reader-only hook未命中INSERT，改为真实非查询写钩子并断言实际注入。均在本任务内修正，无测试跳过或权限降级。一份作者完整SPEC/QUALITY审查覆盖权限、数据库、契约、旧路径、恢复和差异；不以测试数代业务验收。

没有Actions触发/取消项，没有工作流/依赖/分支保护变更。只运行隔离本地PG18及actual Program HTTP；既有环境迁移、真实生产/现场/企业验收均未执行。125目标、247正式验收、所有完整实施任务和原七天阶段出口保持。

## 运输与生命周期快照

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/182；源6a5c45939a9d198bfaf1a64ca4c27d9318c6c4f3；目标e44bd573dac9b09f46f8e9df0f6c61ade588b74e，全树采用核对后任务分支已精确删除、DEV-2已finish释放。Main本报告随最终代码提交，是自己合并/finish前快照；随后正常PR、远端采用核对、精确分支清理和官方finish，并在外部最终核对中记录结果。

本报告登记实际21.64分钟，预算120–200；最终实际时间按Main官方START→finish，不叠加Handoff。两固定目录复用，无新树或跨仓重绑，根未跟踪文件/依赖缓存保留。最终证据位置 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave206-final-verification.json`。

下一步：继续真实库位初始化/层级及规则、生命周期Owner保护，补据点/负责人关系来源，随后Transfer完整上下文/T04 Request/Draft与Stock/RSV共同事务。D3/D5/D6/D7原出口不变。
