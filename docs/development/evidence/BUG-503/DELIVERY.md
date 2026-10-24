# W97 / BUG-503 库存 MOVE 保管属性交付

COORD本聊天单人，Issue [503](https://github.com/GTX537/CP6/issues/503)，Main基线 `800691f802a87dbafc3aa9b504df1267e12e8b42`，任务分支 `codex/bug-503`，固定DEV-1；Handoff固定DEV-2 `codex/w97-handoff`。正式START2026-10-24T03:59:18.158173Z，原估25–40min＋独立前置约5min，最终据官方finish计，不叠加Handoff。

## 根因与改动

原MoveCore仅给双腿传数量和业务键，建目标行使用默认SELF/PENDING/null/未召回，导致来源货主、单位、价格、日期、卷号和质量状态丢失；已有目标也不检查其保管属性是否兼容。W96诊断只执行到首个货主失败断言，未把未执行属性当作已验证。本轮在修复前重现新原键命令和旧MoveAsync均CUSTOMER→SELF，两项真实失败保留。

同一既有PostgreSQL Owner权限根／业务表锁／事务中读取真实来源，元数据仅内部传递：新目标逐值继承OwnerType/OwnerCd、UnitCd、UnitPrice、ReceiveDate/ExpiryDate、PaperRollNo、QcStatus、RecallFlag；两腿流水保留已有货主／单位／单价／卷号列。当前可信actor仍控制审计身份，不能从库存货主推导登录身份。

已有目标属性必须逐值相同才可累加，既有目标对象ID、Creator和预留量保留；冲突在任何数量／采番／流水／原键／Outbox效果之前返回安全409，不覆盖零余额行、不加权计价、不泄露价格或冲突值。Stock旧键不能无损表示两组不同属性，未新增Owner维度或数据库迁移。

保持原数量精度、单位无隐式换算、AllowNegative规则。缺失来源在允许负库存时仍使用旧SELF/null/PENDING/非召回默认属性，不从目标推定货主；这是旧兼容输入保留，不声称已落实正式来源身份。DTO／v1规范编码／合同及原日志不变；已提交原键在当前query/准确原对象范围内恢复，不因后续属性改变再次移动或改写历史。MOVE复制真实价格不额外要求价格读写，公开结果仍只有引用。

## 实际验证和集中审查

70项去重相关本地验证通过（44项原生PG、26项既有逻辑回归），无跳过；修复前2项CUSTOMER→SELF失败保留。

- 新／已有兼容目标、九类属性分别不兼容、可信actor与数量精度、受保护价格无读取授权仍可移动；实际Program/HTTP成功与安全409。
- 第二腿失败完整回滚；提交确认丢失后恢复、撤销move权限仍可按query重放原结果、后续QC变化不被原键改回；原记录与同事务Outbox不重复。
- 真实PostgreSQL并发原始库存写者被Owner表锁阻塞，元数据读取和双腿提交期间不能插入改变；保留负库存的现有／缺失来源场景。
- 既有原操作CSRF、当前会话/租户/actor/准确对象/撤权、同键并发与close、盘点冻结和相关共享核心逻辑回归按最终验证索引的准确选择运行。

一次COORD完整SPEC/QUALITY：修复仅库存MOVE属性传递／目标兼容，其他Apply新行默认值等价；外部请求和序列化不变，权限/事务/重放/Outbox沿用原入口，新增记录为内部类型。公开HTTP与兼容MoveAsync均走修复；移动作业等旧调用只复用同一核心，不宣称其尚未采用的正式Owner已完成。

外部真实执行索引：`D:\CP6-local-test\wave97\foundation\final-validation.json`；全部命令／TRX／日志与失败原因按该索引保留，源码在本任务审查diff。既有成功结果不因commit/merge重跑，采用核对逐产品blob。

首轮修复验证54项通过、2项负库存用例因夹具缺失仓库行而失败；核对原夹具及原有默认拒绝行为后，只修正测试为显式仓库策略，补跑允许／不允许和有／无来源的4种组合。产品修复未变；复用未受影响54项，并定向验证旧移动／盘点冻结调用。失败没有抹除或计入通过。

## 剩余范围和生命周期

本地自有PG全部停止、无postmaster PID，测试数据／日志留存。普通Actions未触发，不改工作流、分支保护或生产门禁，无全仓回归、SQL Server验收、既有环境迁移或生产部署。正式Stock QuantityKernel/Root/Claim/容量、全库数据修复、Finance业务接收、真实业务验收及新增MOVE页面未在本修复内交付。

Issue503在相关测试、一次审查、原步骤复测和远端main采用核对完成后才关闭；此候选记录不提前声称关闭或席位已释放。Main/Handoff正常PR合并、准确任务分支定向删除与官方finish待完成，最终记录 `C:\Users\tt\CP6-planning-reports\20261017-foundation\wave97-final-verification.json`。24/375整卡、125总目标0完整、247正式运行NOT_RUN保持不变，七天ACTIVE。
