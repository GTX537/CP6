# W136-01 新设备可信部门创建与原操作恢复

新设备创建已接入明确部门选择、同事务新UUID映射及Main不可变原操作；当前权限/组织最终门保留，未知结果按原编号查询或关闭。页面不保存设备草稿，不自动重发；旧合法本人/全租户与add-only创建保留。

范围为原七天D2可信创建的首个实际Machine采用及D3现有设备入口；本聊天COORD单人。基线Main `970afcaced8ad00982f9af27b1819a3f398456ed`，CRM/Platform只读，Handoff固定DEV-2。原估100–160分钟，实际按Main START至最终finish一次计，最终记录在本机wave136-final-verification.json；准备/最终核对另计。W135只依据最终proof归档一次，不重复其工作。

## 已实现

- creation-context提供当前原会话add范围内健康部门；部门/子树/自定义范围必须显式选择，本人/全租户合法无绑定创建保留。当前版本不符412，越界/禁用/异租户选择拒绝，没有当前部门默认。
- Owner生成真实新设备UUID，调用者输入ID不成为历史对象绑定能力。一次性创建句柄限定当前上下文/事务/租户/真人。设备保存后仅插入它的映射，再写原结果和私有审计，共同提交；已有对象归属维护仍走IAM。无新数据库迁移/角色授予。
- 受控映射版本转换只接纳该参数化插入；前后仍核组织/全部参与者权限、确切映射revision和对象身份。没有修改通用ExecuteAsync最终门。额外映射写、同事务动作撤权、最终权限来源失败整体回滚；并发原生撤权等当前事务完成后生效。
- Main.Mes.MachineCreation/v1复用不可变原操作账。同键同输入返回原结果，不同输入409；丢失提交确认后按原键查询，原真人/当前query/精确对象UUID必须有效。业务代码重用不能冒充原对象。UNKNOWN不表示未执行；明确close与创建同锁，未提交则持久CLOSED，后续不能补写。
- 页面使用明确部门选择与原结果恢复；同标签页/跨标签页互斥，身份变更与迟到响应不覆盖新身份，重载仅恢复原操作引用。只持久化身份范围下操作ID，不存设备/部门草稿。初次创建保持add权限，恢复独立要求query。

完整接口与版本语义见[绑定合同增量](../../../design-handbook/remediation/main-standalone-department-binding-v1.md#w136-增量新设备创建合同-v12026-10-25)，[实施登记](../../dispatch/20261025-wave136/README.md)。

## 实际验证与审查

实际隔离PostgreSQL/HTTP验证覆盖明确部门/跨租户/过期、同事务额外映射与撤权回滚、并发重复、确认丢失原回执、当前query/原真人/精确UUID及原路径；相关96项通过，页面16项通过，Vue/TS检查通过。成功范围按真实输入复用，不计为正式生产验收。

- HTTP先红：`red-native-20261025T111226Z.json`实际执行1项，现有GET按设备代码处理creation-context，返回Forbidden，证明入口缺失。实现后`green-http-20261025T111411Z.json`同一实际HTTP流程通过；创建/重复/恢复与撤query均经真实API。
- `boundaries-20261025T111652Z.json`15项真实PostgreSQL定向验证通过；`owner-regression-20261025T112139Z.json`96项相关回归全通过，包含本轮创建、原Machine/Standalone/OEE/Planning及Stock原操作兼容性，零跳过。夹具自行拥有隔离集群、迁移和测试API，结束停止，不复用用户数据库。
- `web-tests-reviewed.json`16项页面/恢复行为通过；`web-typecheck-reviewed.log`退出0且无诊断。测试权限mock参数标为string的类型诊断已修复；集中审查后新增两项恢复交互验证，最终16项重新执行通过。Vue模板与本轮接口经过真实类型检查。
- 失败记录保留：首次run脚本错误使用native-report参数，测试NotExecuted，未当通过；该独立55464实例验证归属后已停止。随后改用既有组件夹具的CP6_PERMISSION_PG_ROOT/BIN。前端先红为未调用creationContext；最终类型检查曾发现测试mock无参签名，已定点修复。所有本地日志/JSON/TRX在 `D:/CP6-local-test/wave136/foundation`；原UI红结果保留于工具执行记录。
- 一次作者任务级SPEC/QUALITY覆盖完整候选diff、当前数据/身份边界、输入/原键语义、受控组织转换、错误回滚、页面身份竞争及依赖/Actions范围。集中审查明确修复add-only初次创建不应额外要求query；并修复其他旧写入UNKNOWN不应拦截新建原回执查询，以及创建中列表403不能误设不可解除的旧写入未知状态。对应两项页面竞争回归通过，旧写入未知门仍保留。最终审查与源码树对应本机wave136-main-review.json。没有启动额外代理或按小步骤重复全仓审查。

## 明确剩余范围

这是Machine真实创建采用，不是所有独立对象创建通用授权。库存、盘点、无订单工单创建与本次StockTransaction精确来源继承继续；历史归属不从业务键/调用人猜测。REP正式规则/cut/SourceDemand/Transfer-Stock共同门、RMA正式IN适配与五终端效果、D3计划写的Snapshot/Occurrence/Root/WO同UoW、D5真实宿主/企业传输、D6真实企业政策/日历/身份/密钥/runtime/broker与D7最终集成仍未证明。真实broker仍NOT_RUN，本轮未触碰旧拒绝socket或启动Docker。

PostgreSQL唯一新增数据库；没有Actions运行/触发修改、生产部署、既有环境迁移或保护绕过。375卡24DONE/2IN_PROGRESS/349PLANNED、完整0/125、247NOT_RUN+41REFERENCE_ONLY不变。D1/D4 DONE，D2/D3/D5/D6/D7 PARTIALLY_ADOPTED；七天ACTIVE，2/7不能当工时完成率。

## 生命周期

复用DEV-1/Main与DEV-2/Handoff，未增树/目录连接，缓存和主目录用户文件保留。两仓分别正常PR后核远端完整采用，定向清理codex/w136-01与codex/w136-handoff并官方finish；此报告提交时Main最终收尾尚待，最终proof核销。没有冒称尚未完成的合并或席位释放。

## 本轮采用快照

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/112)：`3264aecfc477fb50d37bdc5d5e2fe7a26d7a9a32` → `bcdce4fb2c172673c39bfc9119519afa775332a3`；远端完整树采用，codex/w136-handoff本地/远端已删除，DEV-2已finish释放且干净。Main正常PR/finish最后执行；截至准备登记39.96分钟，最终按Main finish。

## 用户询问的七天进度口径

2026-10-25按截至W135已合并交付评估约65%，保守区间60–70%；当时W136待Main合并，不提前计入。采用原七阶段等权、阶段内已交付功能的人工估值：D1 100、D2 30、D3 60、D4 100、D5 80、D6 85、D7最终收口0，平均65。它不是可机器验证的验收百分比，也不是剩余工时权重；不按轮次、测试数量或累计耗时推算。依据W129出口矩阵与W130–135新增采用，必需真实外部输入仍算未完成。D2/D3写协议和D7最终集成是主要余项。正式阶段关闭仍仅D1/D4，不改变台账整卡或正式运行验收。
