# W139-01 无订单工单可信创建与原操作恢复

部门用户可在真实工单录入入口明确选择健康部门；实际新UUID、工序/材料全图、序号、归属和不可变原结果同Owner事务提交。关联订单继续使用原订单权限来源，不允许通过新部门赋值替代。原键查询/关闭与页面重载恢复已采用，当前权限和最终门保留。

本聊天COORD单人推进原D2；Main基线872c9ebf01cd66f57b2766ad3966f466913e3e2f。复用DEV-1/Main和DEV-2/Handoff，CRM/Platform只读；W138按实际finish归档一次，独立README PR554保留。预计100–160分钟，最终按Main START至finish一次计，权威实际处置见本机wave139-final-verification.json。

## 实际交付

- mes-work-order:add创建上下文、明确部门与权限版本；scope2/3/4无订单时显式选合法健康部门，scope1/5保留无绑定合法路径。原DTO、数量精度及全局MES编号语义不变，输入UUID不能绑定历史对象。
- 新建专属能力限定当前DbContext/原事务/租户/真人和本次新header。真实全图创建后，仅无订单新UUID可绑定；参数化单条映射插入前后接受自身版本变化，其余当前来源/注册表/组织/映射revision门保持。映射、完整工单/子表、序号、审计和原结果同事务。
- WebOrderNo非空时禁止独立部门赋值，按已有订单范围与有效关联；提交前再次检查订单活动性。旧CreateAsync、订单展开、正常编辑/发行保持。OrderNo1/2/3显示引用不构成部门事实。
- Main.Mes.WorkOrderCreation.v1保证相同原键原输入一次效果、异输入冲突；当前query、原真人、原合同和确切工单UUID控制恢复。同号新对象不能替代，UNKNOWN不证明未执行，close墓碑阻断迟到创建。首次add-only创建合法，撤销add后当前query仍可恢复。
- 实际三步录入页采用新命令、明确部门、原UUID持久恢复与跨标签互斥，禁止未知自动重发。仅存按租户/真人隔离的原UUID；不存工单草稿/部门，身份变化清显示且保留引用。恢复已提交收据后若当前明细读取失败，保留确认单号并阻止编辑/发行直至重新读取成功。

[合同增量](../../../design-handbook/remediation/main-standalone-department-binding-v1.md#w139-增量无订单工单创建合同-v12026-10-25)；[登记与估时](../../dispatch/20261025-wave139/README.md)。

## 实际验证与审查

- 初始真实HTTP创建上下文返回403，原单场景加入完整创建/工序/材料、重复、发行、原结果及撤权检查后通过。页面初始5项红记录保留。
- 最终37项唯一相关PostgreSQL/真实HTTP行为通过：16项本次创建边界加21项现有Operations Owner相关回归。14项成功边界证据复用自corrected-boundaries-20261025T150307Z；最后owner-regression-20261025T150526Z的23项均通过。没有把复用说成全部重跑。
- 原boundaries6/15、corrected-boundaries14/15失败记录保留。根因分别是测试误以为现有全局编号表为空、测试跟踪业务实体后重新进入Owner，以及夹具UTC表达式与原timestamp-without-time-zone列不匹配；按真实语义修正测试观察/输入，无生产门放宽。并发重复创建、失联恢复、额外映射前后变化、当前撤权、整图与编号回滚、关联来源最后变化和原真人/租户/确切UUID均实际验证。
- 审查中补证本地登录标记变化但事件未送达时旧草稿可能被提交；定点红复现后，以begin前后同一身份代次检查阻止该路径。最终页面/恢复及原工单/出库意图相关19项通过，vue-tsc -b退出0。实际HTTP进程、隔离PostgreSQL由夹具拥有并关闭；本机D:/CP6-local-test/wave139/foundation保留日志、JSON、TRX及final-validation.json，每项来源可追溯。没有为文档跑产品测试。
- 一次作者任务级SPEC/QUALITY集中覆盖完整差异、权限和组织版本转换、全图原子性、关联来源、原结果/页面身份与恢复、合同台账、工作流输入。最终审查证据保存于wave139-main-review.json，无额外代理。

## 剩余范围与真实边界

Stock可信新建、正式REP/RMA/IN及其SourceDemand/Transfer/终端效果、D3计划写共同事务、D5实际宿主资格/企业传输、D6真实企业政策/日历/身份/密钥/runtime/broker及D7最终集成继续原目标。D1/D4 DONE，其余阶段部分采用，七天ACTIVE；375卡24DONE/2IN_PROGRESS/349PLANNED、完整0/125、247NOT_RUN+41REFERENCE_ONLY不提升。

仅新增PostgreSQL实现，无迁移、真实角色授予、依赖或工作流变更；无Actions触发/取消、生产部署、既有环境迁移或保护绕过。不操作Docker/socket，真实broker仍NOT_RUN。本地实际证据不作为生产验收。

## 分支与席位

本报告保存时最终Main收尾待执行；Main/Handoff正常PR合并后核远端采用，准确删除codex/w139-01与codex/w139-handoff，官方finish释放。固定目录和缓存保留，根用户文件不动；实际结果以最终管理proof为准。
