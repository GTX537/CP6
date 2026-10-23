# W71 / 七天一期 D2：补充指示独立Owner、双端范围及可信部门绑定

COORD本聊天单人，真实开工2026-10-23T17:07:06.021972+00:00。Main `736d902d86440ee178334c80210abc4ed58dcb1f`；Handoff `8a83015c13b7abc3305928f9ed3200ddc17551a1`。W70 Main446/447、Handoff46全部采用，Issue445关闭，20独立.NET/7Vue/类型检查通过；实际61.49min＋前置6min，两分支删除、固定树干净detached、席位全部释放、Actions0，W37–W70不重复。

## 前置及任务

AGENTS/POLICY哈希未变；status/capacity无容量告警，原非allowlist目录不动；开放Issue核查后登记[Issue448](https://github.com/GTX537/CP6/issues/448)。读了最新台账/七天计划、REP目标入口、接受身份及R2正文开头到SPEC01–04和权限/迁移/采用门相关段落、当前ReplenishService/HTTP/实体/接口、IAM显式对象映射和机器扩展迁移。采用正文为9d108fba82cd4683__WMS-REP-01_库内补货_完整设计_RECOVERY_v1.1_R2.md；其完整NEW Transfer/Root/Claim合同未在本轮宣称实现。

源码现状：GET仅登录，独立服务无OperationsOwner；列表/详情只按目标Location范围，源位置和数量可能向仅有目标权限者披露。批量只按PIK/RES选库存，源查询不套仓区权限；公开命令信任调用方userName，没有最终当前Owner。对象无订单父链，也无可信部门绑定。旧Execute只是派MobileTaskV2并预占，不等物理完成。正式REP-G01..06依赖仍UNPROVEN，NEW不得回退旧V2链。

1. 所有PostgreSQL ReplenishService公共入口接当前原会话Owner；query权限与最终身份/字段/组织版本复核。查询在分页前核tenant、自身或显式对象UUID部门绑定、来源与目标实际Location两端仓区范围。Role1不跳过动作/对象/字段；缺真实位置不猜前缀。冻结查询参数，保状态0和准确已有数量，不把旧Status=Executed当新物理回执。
2. 复用IAM现有dept-standalone-mapping、操作原结果/CAS/审计，增加replenish-order对象类型。原生前向迁移验证同租户真实存活对象及部门，保身份不可变，不回填、不授角色，不用现有库存/调用人/仓库猜历史归属。同步现有对象选择器，不创建第二个权威映射源。
3. 原记录OperatorCd/Remarks按当前注册字段投影；普通列表隐藏自由文本和Creator/Modifier。目录缺失/权限不可用拒绝，不报假空成功。PG旧Create/Update/GenerateBatch/Execute/Cancel在实际当前动作/既有对象（有对象时）核验后REPLENISH_OWNER_ADOPTION_UNPROVEN拒绝，不调用编号/任务/库存/容量/审计/通知。旧非PG历史服务测试保留，不新增双库开发。
4. query目录和能力结果、实际HTTP/page接线；默认只读，能力/读取失败清数据、身份/乱序响应不回填；操作默认不可用，清晰说明正式生成/派发功能未开通。不把500行旧接口页面截断声明为完整仓库，新正式cursor/完整cut另属REP。

仅扩展本资源ReplenishOrder原生图锁和脏跟踪拒绝，复用其余Owner基础。公共IAM/目录/迁移/页面在本单人窗口串行修改。允许路径：Replenish服务/接口、OperationsOwner/Access、IAM standalone mapping、对应PG新迁移、Program/WmsPermissionSeed/语言seed、ReplenishController、Tests/Erp/相关既有测试、ReplenishView/api logistics、IAM类型/绑定选择器及其相关测试、本任务文档。

## 明确保留与验证

本轮只修ReplenishService及其HTTP/page入口。独立MobileTaskV1/V2当前Owner、旧在途任务家族/现场恢复、正式Transfer/SourceDemand/Root/Claim/规则cut/新Draft/完整分页均未采用；不伪称已封住这些独立入口。旧库存或W42部门基础不重复实现。完整REP、WP02-03整卡和24/375、125目标0交付、247 NOT_RUN保持。

相关新隔离PG先复现无Owner读/目标单端披露或取消，再核双端/tenant/self/dept/当前撤权/字段，IAM原生绑定与CAS，五命令无效果、实际Program登录HTTP和页面身份。一次作者完整SPEC/QUALITY；已有成功相关证据直接引用。结束停自有PG保数据/失败，正常两仓PR采用核对、准确分支删除及finish释放。

DEV-1 `codex/bug-448-replenish-owner`，DEV-2 `codex/w71-handoff`，均固定路径复用。预计实际90–130min：Owner/双端/字段25–35，可信绑定20–30，页面/命令/验证/合并收尾45–65；独立前置约4min，W70合并等待期间的只读核查不重复计时。无新树/代理/Actions/既有库迁移/生产部署，七天目标ACTIVE。W51指定停止data删除曾被自动审批以blocked by policy拒绝，保留不重试。

## W71验证完成

25项独立.NET（18真实PG、7既有InMemory）及16项Vue（6补货、10现有绑定页）通过，无跳过；Vue类型检查和相关构建通过。三个PG停止保留，原始PG/页面红灯保留。进入两仓采用及同任务最后收尾；完整REP和独立移动入口不提升，七天目标ACTIVE。
