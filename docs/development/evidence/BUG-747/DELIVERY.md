# BUG #747 — 退回审批的合法撤回运输

[Issue #747](https://github.com/GTX537/CP6/issues/747)，基线`58dd37ebbd3f1244ff41ec31dd8765c04ba6a1cc`，固定DEV1、`codex/bug-747-returned-withdrawal`，本对话COORD单人。

DOC、需求集和迭代的撤回运输接收合法RETURNED状态，仍在外部响应边界拒绝未知状态；原body、原命令、原人员和当前工作流版本保持不变。

根因：`ApprovalWithdrawalOwnerV2.ContextAsync`合法映射Status5为RETURNED且允许当前有权撤回，三个旧消费者的闭合枚举遗漏该值，导致DELIVERY_CONTEXT_INVALID/运输CONFLICT。现在沿原合同接收该状态，最终权限和业务状态仍由权威工作流裁决。

3项实际PostgreSQL18场景修复前均在合法RETURNED上下文处失败，修复后通过：实际任务退回发起人、实际Owner冻结撤回事实、运输提交后丢响应、原键查询恢复且只POST一次、重复运行不重投、业务原命令准确回读、不可变outbox字节不变。每项同时验证未知状态明确拒绝。

模拟业务输入，实际PostgreSQL18和Main内部业务/工作流Owner；测试运输适配器调用实际Owner并注入已提交响应丢失，未运行新的浏览器或HTTP验收。未变HTTP/页面继续引用各原任务实际证据，本次不把其写为新运行。证据索引`D:/CP6-local-test/bug747/foundation/final-validation.json`，失败日志保留。

历史CONFLICT恢复范围：三个既有delivery.validate触发器均禁止改写CONFLICT/WITHDRAWN/TERMINAL_CONFLICT，Claim仅接收未见或到期OUTCOME_UNKNOWN/BLOCKED。本修复不会自动恢复已落CONFLICT的记录。若既有环境存在此类记录，须先按准确环境/租户/Owner/原命令和error_code=DELIVERY_CONTEXT_INVALID定向盘点，核对当前接收端原命令与可用上下文，再由独立受控前向恢复方案追加可审计恢复证据；不得重置/删除delivery、改原body/actor或新造业务撤回。此次未连接、盘点或修改既有环境，不声称历史冲突已恢复。

集中审查完整diff：生产代码仅三个运输边界各补一个已被接收端合同定义的RETURNED值；主体/政策/版本和权限校验不变，运输结果分类与数据库终态不变。回归由实际审批SendBackTaskAsync产生RETURNED，未直接改工作流Status或模拟内部Owner。未改工作流、依赖、HTTP协议、UI或数据库迁移。

现有终态约束来源：`20261030002000_IterationWithdrawalDeliveryV1`、`20261104012000_DocumentWithdrawalDeliveryV1`、`20261104022000_RequirementSetWithdrawalDeliveryV1`。本次无数据库迁移、Actions或生产部署；push/PR核对内容和事件未变，复用既有工作流审查。未验证范围为既有环境数据恢复及部署。

原375保持30DONE/2IN_PROGRESS/343PLANNED，原59余53，125/247不变，24h目标继续。Issue在远端main准确采用后关闭；分支处置、工作区状态和官方finish席位释放以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug747-final-verification.json`核销。无需新Handoff席位，下一W300总控窗口统一同步结果。
