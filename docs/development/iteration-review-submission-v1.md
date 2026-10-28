# 原生迭代评审包与 OA 送审

W199 接续 [候选方案 Owner](iteration-candidates-v1.md)，实现 WP05-19 的原生首次送审。规范来源为 [共享消费映射](../design-handbook/remediation/07-shared-consumer-mappings.md) 的 `ITER-OA-APPLICATION/1` 和既有 `oa.app/2.0`。本轮不签发业务批准、工程冻结或用途许可；决定接包、问题关闭、批准应用、撤回竞争和后继轮次由 WP05-20 接续。

## 准入与冻结

服务从已认证身份、可信租户和 Scope 环境读取上下文。浏览器只提交准确迭代 Ref/ETag、输入版本、候选 Ref 全集、政策 Ref、项目评审人、评审依据和原因。客户端不能指定 Owner、Round、Aggregate、Binding、Submission、Outbox、服务身份或回调地址。

首次送审要求当前 `IN_DESIGN`、非暂停、项目有效、批准需求输入可用、产品/场地未漂移，且每个候选均为当前版本、当前技术 PASS、无未解决发现并有原技术 Manifest。完整集合直接读取候选存储，不复用每页 20 项的查询；缺项、多项、重复身份或同身份异版拒绝。持有相同原件不代表当前仍有权读取，文档、质量、需求、字段、项目成员及最终权限均参与原事务核对。

规则具有自己的不可变身份、专业角色/最少人数、有效期和 `RETURN|REJECT` 处置。最后登记的有效启用规则才可用于新送审；撤销不会抹掉旧规则。评审人须为有效项目成员、具有当前评审动作和专业角色，并独立于迭代编制、负责人、候选编制/修改、技术预检与本次提交者。本入口尚不执行其专业签署。

评审包保存送审前准确迭代原文、输入、全部候选原文/版本/差异/原技术来源、评审人及依据。首次原生轮次尚无独立评审问题，因此本版本保存空问题集；不能用它承接已有问题的外域或后继轮次。

## 持久映射和字节

新 `ITER-CANDIDATE-AGGREGATE/1` 按原生 UUID 小写文本的 UTF-8 字节排序完整成员。`aggregateDigest` 是 schema、换行与不含自身 digest 的 canonical body 的 SHA-256。每个成员保留候选版本、原摘要和完整原技术检查中的技术/文档引用，不将候选版本替换为技术修订版本。

映射中四个本地 envelope 合同为 `Main.Plm.IterationReview.native-reference/1`、`technical-reference/1`、`original-digest/1`、`registration/1`。其精确封闭 JSON Schema 定义在 `IterationReviewMappingV1`，每个 envelope 保存 schema SHA-256、准确 canonical 原文 Base64、原字节 SHA-256 与长度。它们明确属于本地映射；不冒充 ENG 签出的新引用。technical-reference 保存原 SourceCheck Ref 和完整原 JSON，原候选摘要与原 Manifest 摘要分别保留。

Package SHA-256、Policy 原摘要、各 Technical Manifest 原摘要、Aggregate domain 摘要彼此独立。单候选时保留其准确技术 Manifest；多候选无唯一原技术 Manifest，映射中使用合同允许的 null，各项原 Manifest 仍保存在完整评审包。

Round、Aggregate、Binding、Submission、Outbox 均在服务端一次生成。`businessRevision` 为 Binding UUID；`subjectId`、Submit `roundId` 和 Engineering `roundId` 为本轮 UUID。`engineering.candidateId/version` 为集合身份；Engineering digest/reviewPackageDigest 均来自完整评审包。`rejectDisposition` 只在本地 Binding，未扩展 OA Engineering 或决定枚举。

Snapshot 仅含 `{schema,aggregate,binding}`，新 canonical 使用 Unicode codepoint 键序、原 Unicode UTF-8、不做 Unicode 归一化、不转义合法非 ASCII，拒绝重复键及孤立 surrogate。原来源 JSON 字符串完整保留。Snapshot 与 Subject digest 都绑定这些相同准确字节。超过 256 KiB Snapshot、1 MiB OA body、1000 项 OA 身份列表或 16 MiB 本地评审包即拒绝，不截断集合、不改写原 wire。

## 事务、查询与投递

`cp6_iteration_review_v1` 的不可变 policy/submission/outbox 记录与原生 ITER `IN_REVIEW` 后继版本、原生 ITER 操作记录、评审操作记录/审计处于同一 PostgreSQL 事务。数据库延迟外键与 provenance/stage 约束要求原件、阶段、两个操作记录和 Outbox 同时存在；评审包逐项核对真实候选版本。`ready()` 关闭缺失约束触发器的路径。设计计划、任务及候选编辑在评审阶段关闭，项目权限变化继续影响披露和未接收送审的准入。

入口共用 `/api/engineering-iterations/owner-v1`：

| 路径 | 行为 |
|---|---|
| `/{id}/review-context` | 完整当前候选、政策、送审历史与上下文 ETag |
| `/review-roles`、`/{id}/reviewers` | 当前有效角色及项目成员 |
| `/{id}/review-policies` | 登记规则；独立 `review-policy` 动作 |
| `/{id}/review-submissions` | 首次送审；独立 `review-submit` 动作及 If-Match |
| `/{id}/review-submissions/{submission}` | 当前权限下读取冻结包、准确 Snapshot/Submit 原文 |
| `/review-operations/{id}`、`/review-operations/{id}/close` | 查询原命令或原子关闭未提交命令；历史结果不改写 |

失联后只查原键；相同命令相同 body 返回原回执，异体冲突，已关闭未知命令不能迟到提交。浏览器恢复记录按环境、REAL/DEMO、租户和用户隔离，只保存必要身份，不保存业务正文，并用浏览器锁避免跨页重复发起。

`IterationReviewSubmissionDelivery` 使用既有受控服务身份/TLS 原包传输；固定 Owner profile 选择本业务原件和投递表。默认无已启用目的注册，不主动向现有环境发包。worker 先持久化尝试、释放业务锁，再查询原服务命令；只有明确不存在原命令时才 POST 原字节，丢失应答后查询原件恢复。尝试、路由版本和 ACK 独立持久化。

接收端依据完整本地原件复核 Scope/Owner、原包、完整当前来源、当前政策、评审人和准确部署注册，并沿原生 OA TxS 写入 AppSubmission/StartOutbox，TxE 以 Submission 身份启动一次 FLOW。`RECEIVED` 或 `RUNNING` 均不等于 ITER 业务批准：页面保留 `IN_REVIEW / NOT_RECEIVED`（业务应用未收到）。本轮未启用决定消费者、业务应用或应用回执发送。

## 验证边界

本轮的隔离 PostgreSQL、本地 HTTP/TLS、Vue 与构建证据见 [W199 交付报告](evidence/W199-01/DELIVERY.md)。原生文档/质量/需求批准链使用真实本地 Owner 与持久流程；隔离文件存储/扫描替身的范围单独记录。这不是企业生产验收、环境部署或 125 总目标/247 验收的完成证明。没有触发 GitHub Actions。
