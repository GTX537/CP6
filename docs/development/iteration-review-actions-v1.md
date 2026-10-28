# 原生迭代评审处理与撤回 v1

本实现接续完整候选评审包，采用 `ITER-OA-APPLICATION/1` 和原 OA `oa.app/2.0`、`oa.app.withdraw/2.0`。本地 PostgreSQL 验证不代替真实企业来源、正式验收或生产推广。

## 业务入口

入口根为 `/api/engineering-iterations/owner-v1`，均要求当前登录、可信租户、实际项目/对象/字段及来源读权。写入继续要求 CSRF 和该命令的当前动作权；客户端不能指定 Owner、服务身份、撤回事实或实际处理人。

| 路由 | 行为与前置 |
| --- | --- |
| `GET /{id}/review-issues` | 返回全部问题的最新不可变版本和上下文 ETag |
| `POST /{id}/review-issues` | `CREATE/RESPOND/VERIFY/DOWNGRADE/NOTE`；准确 If-Match；动作分别使用 `review-issue`、`review-issue-verify`、`review-issue-degrade` |
| `GET /{id}/review-submissions/{submission}/application` | 本轮原决定、原应用回执、当前业务阶段、真实控制行版本及撤回通知状态 |
| `POST /{id}/review-application` | `operationId/submissionId/expectedReceiptId/reason`，使用准确上下文 If-Match 和 `review-apply` |
| `POST /{id}/review-submissions/{submission}/withdraw` | 仅 `commandId/reason`，If-Match 为实际业务控制行的 8 字节 Base64，使用 `review-withdraw` |

`review-issue-operations`、`review-application-operations`、`review-withdrawal-operations` 各有 `GET /{operationId}` 及 `POST /{operationId}/close`。原人撤销写权限后可按当前读权限恢复原结果；关闭未知键后不可迟到执行。原命令不同输入不当作重放。原结果查询不会重新触发业务。

问题定位冻结包中的准确候选/版本及实际 JSON Pointer，或明确的流程问题。新问题默认 BLOCKING，必须指定实际项目成员和截止日期。问题版本和 expectedRevision 用十进制字符串传输，保留完整 Int64；原答复、证据及作者不改写。责任人答复；独立评审人按当前专业角色及验收动作权关闭，作者、责任人及答复人不得自行验收。NOTE 只追加说明，不改变答复人或关闭事实。降级要求独立动作权与原因。

当前轮次允许处理其冻结包携带的原问题；原问题的 SubmissionId 不改为新轮次。已完成轮次只追加说明。下一轮冻结所有问题的实际最新版本，不能用重新送审丢弃阻塞项。

专业决定采用原生 `/api/oa/tasks` 通道，`GET /iteration-review/{submissionId}` 返回本人当前实际待办。保持实际 task/instance 行版本、原分配/代理授权、SOD、冻结评审人和当前专业角色；要求意见。OA APPROVED 与业务 APPLIED 分别展示。

## 事务与准确身份

SubjectId 始终为准确 Round UUID；Subject.businessRevision 为不可变 RoundBinding UUID；应用单元和 action 固定 `PLM_ITER_REVIEW_DECISION`。Package、Aggregate、Policy 和 Snapshot 摘要保持独立，既有原件的时间精度及字节不改变。

APPROVED 应用要求准确当前轮次、全部阻塞问题 VerifiedClosed、当前来源/专业规则及最终授权有效。RETURN→IN_DESIGN 并清当前评审链接，REJECT→AWAITING_EVALUATION 并保原链接；两者均不自动关闭迭代。业务版本、真实控制 CAS、应用原件、双原操作、回执、Outbox 和审计同 PostgreSQL commit。缺少任一必需事实会回滚。BLOCKED 只由原应用人以新操作及准确前驱回执恢复；终态原回执不改写。

业务撤回与应用争用同一控制行和迭代头。应用先提交，撤回返回 409 CONFLICT 及原 APPLIED 回执，不生成撤回 Outbox；撤回先提交，业务回到 IN_DESIGN、完整冻结实际人/前后控制版本/原命令/事实/Outbox，迟到决定只记 STALE，不能覆盖后继轮次。原包、问题和原 OA 实例全部保留。

本轮撤回生产端仅支持非代理的真实请求人：原 Starter，或有当前撤回权且明确承担项目/迭代责任的人。未实现委托撤回和 BLOCKED 后换人/换 Grant 的重新授权入口，不能由 worker 自动替换原人；这些范围保留在完整 WP12-19。

## 持久通道与当前权限

- 决定接收：`/internal/plm/iteration-review-results/v1`。保存准确原 bytes、事件去重、逻辑决定去重、异体冲突及序列 GAP；原生 OA 源证据独立核验。接收 ACK 不等于业务应用。
- 回执查询：`/internal/plm/iteration-review-application-streams/v1/{streamId}/result` 与 `/receipts`。游标绑定当前服务/主体、准确快照和页位置；原修订/前驱连续后 OA 才更新展示。
- 撤回服务：原 OA `/internal/oa/app/v2/submissions/{id}/withdraw-context`、`/withdraw` 和 `/withdraw-commands/{commandId}`。服务先取得真实 Workflow Submission 行版本；body/传输命令/Outbox ID 永不重算。
- Workflow Tx W 原子取消尚未完成任务、Token 和 FormTo，保存实际业务请求人的 WITHDRAWN 决定、DecisionOutbox、命令原回执及审计。若已有批准/拒绝，保存 TERMINAL_CONFLICT 及原决定；原人权限撤销则 BLOCKED，权限恢复后只重试原事实。终态重放只需当前读权。
- 每次网络交换都在数据库事务和业务锁外。发送端先查询原命令；403/503 不当作未观察。成功提交但响应丢失后，查询恢复，禁止换 ID 重发。状态 412 重新读取 Workflow 当前版本，原 body 不变。

各服务使用已登记 C02 客户端、环境/租户/Owner/准确 Submission 授权及来源 REAL/DEMO 模式。新迁移不自动授权服务或角色。`IterationReviewDecisionDelivery`、`IterationReviewReceiptDelivery`、`IterationReviewReceiptRecovery`、`IterationReviewWithdrawalDelivery` 均沿现有注册配置，默认禁用；生产模式不接受 DEMO。不能从 payload 提供的 URL 转发令牌。

独立页面按当前身份/租户隔离未知操作键，只保存操作和对象标识，不缓存问题正文；Web Locks 协调同身份多页写入，身份变化丢弃晚到响应。受控证据须重新读取所选准确版本，撤回结果、通知状态和业务应用分别显示。

完整验收、外域身份/注册和其他 Owner 采用仍待，后续工程冻结/REL/VER、WO/SCHED 不由本实现冒充完成。实际证据见 [W200 交付](evidence/W200-01/DELIVERY.md)。
