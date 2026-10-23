# W71-01 / Issue448 补充指示当前Owner与双端范围

COORD本聊天单人，2026-10-23T17:29:26.149207+00:00。基线Main `736d902d86440ee178334c80210abc4ed58dcb1f`，DEV-1任务`BUG-448-REPLENISH-OWNER`、分支`codex/bug-448-replenish-owner`；DEV-2仅Handoff。唯一产品交付报告；[Issue448](https://github.com/GTX537/CP6/issues/448)。

## 缺陷、实施与合同边界

原ReplenishService未要求当前Owner，查询仅套目标位置授权，未授权来源位置及数量可能被读出，取消还信任调用方用户名；GET仅检查登录。新隔离PostgreSQL两项失败已实际复现。

所有PG公共服务入口现要求同一DbContext的当前原会话OperationsOwner，原生事务中检查query/命令动作、租户、当前角色/组织/字段并最终复核。读取失败保留401/403/503，不返回假空成功。仅本资源扩展ReplenishOrder图锁和预跟踪拒绝；现有Owner脏工作、外层事务和错上下文防护保留。

查询在分页前限定租户和对象范围：本人核实际Creator，部门/子部门/自定义使用显式同租户对象UUID绑定，全域仍受仓区规则。来源和目标都必须关联同仓真实存活Location，两端分别套当前仓库/仓区授权，可以联合不同区域授权；不猜PIK/RES前缀。保留Role1仓区语义，但不跳过当前动作、对象和字段。参数冻结并限制合法页码，状态0与12.12345678数量不损失。

IAM现有standalone mapping增加replenish-order，复用当前Owner、原操作结果、CAS及审计。对象选择器只暴露当前租户实际存活ReplenishOrder，原生前向迁移扩展既有对象kind约束和验证触发器，保留所有旧kind及映射身份不可变，验证真实同租户对象/部门。不回填、不猜归属、不新建第二权威源。query目录只补缺，不授角色。

列表固定隐藏OperatorCd/Remarks/Creator/Modifier，详情前两字段按当前MIN规则投影，源行不修改；不宣称已接完整REP字段/cut。PG五个旧命令Create/Update/GenerateBatch/Execute/Cancel先核当前动作及既有对象（如有），然后503 REPLENISH_OWNER_ADOPTION_UNPROVEN，先于编号、移动任务、库存、容量、审计或通知。接受的REP R2要求NEW走正式Transfer及SourceDemand/Root/Claim/规则cut，缺这些门不能回退旧V2。历史非PG路径保留且用原有InMemory检查，不代表双库验收。

实际Program注册scope/字段；HTTP GET和能力接口要求query，Owner异常保原状态/no-store。页面默认只读，能力或数据失败清旧数据并明确错误，身份变化和乱序结果丢弃；直接命令处理函数也核权限/意图，等待确认后再次检查，冻结请求体，重复及未知结果不重发。旧Status=Executed显示“旧记录报告完成”，TaskIssued为“已派发任务”，不冒充物理完成。旧页面仍最多500行，明确提示缩小范围；正式完整cursor/cut另属后续合同。IAM下拉和五语文案沿现有入口更新。

本轮仅ReplenishService及其HTTP/page和既有IAM扩展。独立MobileTaskV1/V2入口Owner、旧在途任务恢复、正式Transfer/SourceDemand/Root/Claim/新Draft/完整分页未采用；不声称封住独立移动入口。完整REP/WP02-03、24/375整卡、125目标0交付、247运行NOT_RUN保持。

## 验证与集中审查

- `D:\CP6-local-test\wave71\foundation/replenish-red-20261023T171029Z.json`：两项真实PG红灯，无Owner读取及伪造调用人取消均未抛异常，原失败保留。
- `D:\CP6-local-test\wave71\foundation/replenish-native-20261023T171603Z.json`：15项PG通过，覆盖双端实际Location授权与授权联合、租户/本人/部门健康祖先、字段及精确数量、现有IAM绑定/CAS/原结果/原生跨租户拒绝、当前撤权和最终字段来源失败、脏/错上下文/外层事务拒绝、五命令严格零依赖调用及零效果、目录不授权、实际Program/login/HTTP。
- 原生并发用例第二连接修改Creator遇55P03，直到当前Owner读事务结束；随后可改且下一查询隐藏对象。锁异常按Npgsql实际包装核验，无重新运行粉饰。
- `D:\CP6-local-test\wave71\foundation/replenish-regression-20261023T172109Z.json`：10项通过，包括RMA原生并发fence、IAM native管理及Machine绑定3项PG，加7项既有Replenish InMemory服务用例。和前15无重复，共25独立.NET，18真实PG、7InMemory。相关项目及必要依赖构建通过；保留历史警告。
- `D:\CP6-local-test\wave71\foundation/replenish-ui-red.json`：6项旧行为真实失败，涉及默认可写、能力/缺数据失败、身份变化、乱序、缺query动作和重复/未知提交。`replenish-ui-first.json`最终16项通过（6补货、10既有绑定页），无跳过。实际SFC配API替身，不是生产E2E。
- `replenish-typecheck.log`为空且进程exit0，Vue类型检查通过。一轮任务级作者SPEC/QUALITY检查已覆盖实际原REP规则/采用门、完整产品diff、当前身份/范围/字段/事务锁、原生绑定与旧kind保持、无效果拒绝、目录不授权及页面失效结果；无剩余阻断项。无依赖或工作流变更。

三个本轮自有PG簇均ownership/stopped存在且无postmaster.pid，数据、日志与失败记录保留。没有Actions、既有库迁移、生产部署、真实完整REP/扫码/移动任务/库存业务验收或全仓回归；迁移只在新隔离测试库验证。CRM Issue109及其他未完合同保持。

## 收尾

原估90–130min＋独立前置4min，最终以Main start/finish计时，Handoff不重复累计。候选通过后正常两仓PR采用、准确分支清理与finish释放，未提前声称席位释放。七天目标ACTIVE继续。W51指定停止data删除曾被自动审批拒绝，原因blocked by policy；原目录保留，不重试或绕过。

## 实际采用与最后收尾

Main [PR449](https://github.com/GTX537/CP6/pull/449) source `338c61c084e4e1a73c2e91c12807c307272c580f`采用远端main `f35ad97a42fdc4686b143a54c0c3b0f6c3ae106c`，27任务路径核对。Handoff [PR47](https://github.com/GTX537/CP6.AI-Handoff/pull/47)采用 `3eedbd17b1910905b133cabb1a10ee4250b01b43`，准确任务分支删除、固定树干净detached、finish释放。17个产品路径与已测提交逐blob相同，复用有效成功结果；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue448在最终远端和生命周期核对后关闭。当前约27.09min，原估90–130min，最终以finish为准，独立前置约4min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave71-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续独立Mobile入口前置及D5/D6公共基础，完整REP保持未采用。
