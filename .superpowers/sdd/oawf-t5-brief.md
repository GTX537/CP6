# M-OA/WF T5 任务简报：IAuditable 贴点(Wf_FlowDef/Wf_ApprovalBinding 族)

## 背景与位置
M-OA/WF 横切接线波末任务(计划原文: 「T5 IAuditable: Wf_FlowDef/Wf_ApprovalBinding 贴点」)。先例=M-MES T5(01b953d,一次过审): **豁免裁决逐实体字段级实查**,禁按类别套先例;豁免须源码注释+负测试。拦截器: CP6Context ChangeTracker.Entries<IAuditable>() 纯标记零迁移。

## 需求
1. 圈定贴点实体(CP6.Entity 中 Wf_*/Oa 域实体目录**全量**实查):
   - **Wf_FlowDef**(流程定义——本波高危键 oa-designer:* 的落库对象,定义变更影响所有在途流程)与 **Wf_ApprovalBinding**(审批绑定单源——计划点名)必贴。
   - 其余 Wf_/Oa 域实体(FormDef/Instance/Task/Token/委托/ApproverMap/收藏/偏好/通知等)按「**不可逆治理配置或权限授予类即纳入;高频运行时流转/追加型即评估豁免**」口径逐实体裁决——运行时实例/任务/令牌类大概率豁免(高频写+状态机流转由引擎测试锁),但**委托授权(Delegate 类实体)与 ApproverMap 是权限授予面,应纳入**。
   - 逐实体裁决表(实体|字段级/语义证据|裁决)是交付物核心;全量对账(目录 N=纳入+豁免,零漏)。
2. 每实体单行 IAuditable,零其他改动零迁移。
3. 测试: OawfAuditTests 照 MesAuditTests 形状(Field/Old/New 真值+负测试)。
4. 若贴点引发迁移,停手报 BLOCKED。

## 验证与提交
- `dotnet test --filter OawfAuditTests` 全绿;全量(基线 1775)不许跌;git status 无 Migrations。
- 单 commit 即 push。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\oawf-t5-report.md`(全量裁决表+TDD 证据)。回复只返回: 状态、commit、一行测试结论、裁决计数一行(纳入N/豁免M/目录全量对账)、concerns(15 行内)。