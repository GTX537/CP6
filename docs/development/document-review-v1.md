# 文档专业评审与准确版本批准 v1

本入口采用 `Main.Plm.ControlledDocument.v1` 和 PostgreSQL 原生 FLOW。送审、流程决定、文档批准应用及回执接收是分别保存的事实；送审成功、运输回执或流程 APPROVED 均不能单独把文档变为已批准。

## 使用过程

1. 有 `review-policy` 权限的实际 Owner 显式登记专业规则：当前有效角色、每项最低人数、外部要求、作者/评审人分离和有效期。没有内置的企业批准规则或自动授予的专业身份。
2. 有 `review-submit` 权限的用户选择准确工作版本，提交评审依据、变更关系和评审轮次。不可变包保存本版/前版引用、原文差异摘要、原件和派生关系、来源与语言依据及准确政策版本。完整包留在文档 Owner，FLOW 快照携带准确引用及摘要。
3. 原生待办链接到 `/plm/controlled-document/{documentId}?submission={submissionId}`。页面展示该次送审的准确历史版本、前版差异、专业要求及原件下载；专业签署使用原生任务和实例版本，必须填写意见。当前实际处理人和被代理主体都须有相应权限及专业资格；一名处理人不能借多个代理名额满足多人要求。
4. FLOW 形成不可变决定，通过当前服务及准确提交授权送达文档 Owner。拥有独立 `review-apply` 权限的用户确认应用，APPROVED 还须通过当前政策和文件可用性检查。应用事实、业务结果、原操作、审计、回执及 Outbox 共同提交。REJECTED 保存准确拒绝事实，不授予文件使用权。
5. 本次应用结果只属于该文档版本。后来修改形成新工作版本，旧签署保留；新版本不继承批准。同版重提须明确替代一个已经实际应用 REJECTED 的轮次，不能派生重复活跃轮次。

语言权威和外部来源的专业认定由实际有权人员依真实资料完成。本地示例政策、角色或 DEMO 服务不能代表企业专业批准。受控文档批准也不替代制造用途许可、工程冻结或发布。

## 人员 API 与恢复

以下路径相对于 `/api/controlled-document/owner-v1`：

| 请求 | 作用 |
| --- | --- |
| `GET /review-roles` | 查询允许政策维护者选择的实际启用角色 |
| `GET /{id}/review-context` | 当前文档、政策、送审列表及并发令牌 |
| `POST /{id}/review-policies` | 显式政策版本；必须带 If-Match |
| `POST /{id}/review-submissions` | 准确版本送审；必须带 If-Match |
| `GET /{id}/review-submissions/{submission}` | 冻结包、前版、实际应用状态 |
| `POST /{id}/review-application` | 带 If-Match、原操作 ID、准确 submissionId、expectedReceiptId 和原因 |
| `GET /review-operations/{id}`；`POST /review-operations/{id}/close` | 政策/送审原结果或持久关闭 |
| `GET /review-application-operations/{id}`；`POST /review-application-operations/{id}/close` | 批准应用原结果或持久关闭 |

原生待办采用 `/api/oa/tasks/document-review/{documentId}`、`/{taskId}/decision-context` 和 `/{taskId}/decision`，保留 Task/Instance 双版本及原始签署命令。`GET /api/oa/tasks/decision-commands/{commandId}` 查询原结果；`POST /api/oa/tasks/{taskId}/decision-commands/{commandId}/close` 与签署共同受原生授权锁保护，关闭键不能迟到执行。原签署为 COMPLETED；文档原操作为 COMMITTED，不能混用。

页面只持久化租户/人员隔离的操作、对象、送审和任务 UUID。未知结果保留原键，不保存评审意见或原件，不自动重发签署；重新查询仍要求当前读取权限。身份变化清除私有资料并隔离迟到响应。

## 服务采用与配置

主机须显式配置 `ScopeAuthorization:EnvironmentId`，并完成该环境、租户的真实 Scope 登记。专业绑定使用 `PLM_DOCUMENT_REVIEW` / `DOCUMENT_REVISION`，业务 Owner 固定为 `Main.Plm.ControlledDocument.v1`。原生绑定、不可变发布、服务登记及逐提交权限独立生效；不存在根据请求体选择任意 SQL schema、接收者或凭据的入口。

以下发送配置默认关闭，复用现有 C02 当前服务登记、实际 token 记录、HTTPS 来源固定和禁止重定向规则：

| 配置节 | 功能 |
| --- | --- |
| `DocumentReviewSubmissionDelivery` | 送审原文送达原生 OA 受理入口 |
| `DocumentReviewDecisionDelivery` | 决定送达 `/internal/plm/document-review-results/v1` |
| `DocumentReviewReceiptDelivery` | 应用回执送回原生 OA 回执接收入口 |
| `DocumentReviewReceiptRecovery` | 通过准确文档回执链恢复已观察到的缺口 |

文档决定接收登记位于 `cp6_document_review_fact_v1`；OA 应用回执登记位于 `cp6_approval_receipt_v2`，Owner 必须准确匹配。回执链入口是 `/internal/plm/document-review-application-streams/v1/{stream}/receipts`；分页令牌绑定环境、租户、服务主体、流和快照，仍逐请求检查当前授权。只有真实出现的缺口才恢复，不制造额外应用回执或业务效果。

发送器先查询原结果，使用持久租约和 epoch；响应丢失保持 OUTCOME_UNKNOWN。撤销 receive 后保留 query 的主体可恢复已有结果，不因此取得再次提交权。文档批准效果与工具实物处置分属不同来源表；共享传输只接受封闭的 Owner 集合，并核对实际不可变来源。

代码、隔离本地 HTTP/PG/TLS 检查和实际企业验收分别记录。本轮没有启用企业凭据、迁移既有数据库、运行 Actions 或部署。
