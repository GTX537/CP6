# 原生审批任务决定事务

W181 延续 OA-APP 已接受合同，在 `oa.app/2.0` 实例上采用现有 FlowEngine 会签、串签、并行算法。入口是既有 `POST /api/oa/tasks/{taskId}/decision`；旧实例继续原办理服务。

原生输入为 `commandId`、`decision`（approve/reject）、`comment`、`taskRowVersion`、`instanceRowVersion`。两个版本都是服务端实际 8 字节令牌的规范 Base64。`If-Match` 必须与任务版本一致；缺失返回 428，过期返回 412。驳回意见必填，意见上限 500 Unicode 标量。身份只来自受控登录，代理使用原 `X-Acting-As`；原生输入不接受客户端身份、业务字段补丁或可信决定证据。

`GET /api/oa/tasks/{taskId}/decision-context` 返回当前有权办理任务的双版本及状态，ETag 对应任务版本。它不是业务详情或完整 SubjectView。`GET /api/oa/tasks/decision-commands/{commandId}` 只查询实际原命令；未知为 404，不发起新票决。重复命令在当前原办理人及业务/字段读权通过后返回原结果，不再要求已撤销的审批动作或代理授权，也不改用新版本重做旧动作。同命令异体返回 409。成功沿统一 `{commandId,state,result,queryRef}`，`result` 内含 `voteId`、`decisionId` 和本次任务结果 `task`；错误含标准 messageKey、correlationId、retryable 及原命令 resultQueryRef。

实际事务先持 Main 授权根与 Scope fence，核当前命令权、原任务职责、委派范围/时效和双方职责分离，读取实际业务 Owner 的冻结政策与当前对象/字段读权。引擎只暂存原算法产生的任务、履历、token、实例变化。投票、原命令结果、审计和终态决定/Outbox 与这些变更共同提交；没有同步业务回调、外部发送或独立内层提交。

数据库命令登记绑定本次 PostgreSQL 事务身份及准确实例。原生运行时写入必须由同事务尚未完成的命令承载；延迟约束核投票、实际任务/责任人/履历及终态 Outbox 完整性。完成后的命令、票据、决定和消息原件不可改写。任务新增的 `ApprovalRowVersion` 是真实写入时生成的并发令牌，不是对历史审批票的推断；实例继续使用已有 RowVersion。没有重建旧实例的历史职责或人票。

## 职责和决定证据

`wf.pinned-routing-duty/1` 定义当前职责映射：RoleKey=`wf.duty/` 加准确固定流程版本、节点、档号/档代码和完整原审批人规则的 canonical SHA-256。它标识真实流程任务职责，保留原规则中的实际 IAM RoleId、指定人、主管或组合来源，不把哈希说成一个新增 IAM 角色，也不从最后点击人反推职责。每票引用 W180 的不可变原任务分配，独立保留 Principal、Actual、OnBehalf、准确 Grant 原件/版本及当时权限、Scope、SOD 证据。

RequirementId=`wf.req/` 加实际实例、token、节点、档号与轮次的 canonical SHA-256；因此会签同组不会冒充多个不同流程职责，串签和不同分支也不会混票。决定 EvidenceSet 保留所有实际人票、准确业务政策及满足集合，末次办理人仅记录真实终态执行者。

当前实际业务政策适配来自 `Main.Wms.ToolDispositionPolicy.v1` 原不可变政策。批准要求实际不同 Principal 人数达到其冻结 RequiredHumanApprovals；驳回沿既有内核投票规则，不能伪造批准人数。缺少人票或完整职责证据时回滚本次操作，保留此前已提交的票。其他业务政策需各自实际 Owner 适配，不用通用默认政策替代。

决定时间和投票时间由本库生成，原时间不覆盖。终态原消息采用 OA-APP DecisionEnvelope，`payloadDigest` 排除自身；存储另核完整传输 bytes 的摘要。消息有固定事件/决定身份，当前只持久保存，后续可靠交付必须复用这些原件。

## 后续采用边界

决定已保存、消息已入队、Owner 已收事实、业务已应用是分别可证的状态。本轮没有把消息入队记为 APPLIED，也不修改 Tool 原送审意图的历史 ACK。业务当前投影、原生办理页面、决定/证据查询投影、实际发送/接收及 Owner ApplicationReceipt 与业务共同事务继续接入；原命令查询只能证明它返回的那次办理。服务节点和子流程仍按明确挂起边界等待实际 Owner 适配。原 D2/D3/D5/D6/D7 出口与正式运行验收保留。
