# W494-01 询盘 FX24 接收端投递

COORD 接续 W491 与 W490 的业务排期，复用 DEV-1 和 DEV-2。CRM 实现从 `e9e2097979580a53847f75525a381ea9b0b9cf1d` 开工，属于 WP03-02 的原 FX24 投递范围。已验证源码 `b7dfbcee0b2d45892ee358fd9191a29c42576860` 合入 CRM `b682af7234b44873ed8d04e451ec88c97525ca5b`。远端包含该源提交，树均为 `ab0355e9d39f642a86407ed40bfec5919bd6bd51`。 [W491](../W491-01/DELIVERY.md) · [W490](../W490-01/DELIVERY.md) · [CRM PR #183](https://github.com/GTX537/CP6.CRM/pull/183)

## 接收行为

原生 CRM API 提供 `POST /internal/crm-inq/outbox/dispatch`。租户服务凭据绑定宿主已登记的租户，正文接受 `eventId`。接收器从 PostgreSQL 只读视图取得提交或审核 Owner 的原事件。投递方提供事件标识和当前尝试序号，确定拒绝由接收器执行冻结事件 Schema 校验产生。

`crm_inquiry_projection_v1` 以 `(TenantId, ConsumerName, EventId)` 作为接收键，消费者为 `crm-inquiry-projection-v1`。事务锁以同一键串行处理，等待结束后读最新已提交结果。首次处理在同一事务写入 `ConsumerInbox` 和 `Dispatch`。`Dispatch` 持久表示原 FX24 的接收状态，生产者的发布、租约和回执仍由现行发送协议持有。接收角色读取原事件视图并操作自己的技术状态。

首投返回 `Dispatched`，重复返回 `AlreadyProcessed`。已提交拒绝返回 `RetryScheduled` 与 `Retry-After`，响应的 `X-CP6-Delivery-Attempt` 给出下一序号。前四次拒绝使用 5、10、20、40 秒的数据库时间窗口。重复同序号和提前送达只读回原结果，跳号返回 409。第五次确定拒绝写入 `Quarantined` 和 `consumer-schema-rejected`，以事件、租户与错误码记录告警。终态重放沿用已提交结果。数据库异常或未知回执以同一序号恢复。

## PostgreSQL 实测

| 原场景 | 实际执行与结果 |
|---|---|
| FX24-S01 | 真实公开提交与 H01 创建 Submission、Lead 及原事件。正式 0.10.3 发送器向回环模拟 sidecar 发布，从实收原文提取事件 ID 并核对 SHA-256。两次并发 HTTP 投递恰有一次返回 200 `Dispatched`，Inbox 与接收状态同事务提交，哈希和时间一致。 |
| FX24-S02 | 另一并发请求和后续重复投递返回 200 `AlreadyProcessed`，首次接收时间及两行 PostgreSQL `xmin` 一致。错误凭据和租户交换被拒。另一租户以自己凭据查询该事件时，与查询未知事件返回相同 404，未写接收状态。 |
| FX24-S03 | 首次插入时为一个模拟 LeadCreated 源构造不兼容的 2.0.0 Schema 版本，同一原业务事务提交事件、原文摘要和绑定。测试临时插入触发器随即撤除，产品约束全程执行。由接收端实际解码拒绝五次，逐次回读计数与状态，实际等待四段退避，验证跳号、提前请求及同序号重放零写。第五次返回 200 `Quarantined`，计数 5、状态 90、无 Inbox。 |
| 事务回滚与恢复 | 原生人工审核放行生成 LeadCreated。以隔离库约束令第二个写入 Inbox 失败，HTTP 返回 503，先写的 `Dispatch` 随事务回滚。移除测试约束后，以原序号成功处理，确定失败数为 0。 |

各场景回读原队列行、业务行计数及 H01 调用。测试中的生产者成功发送、接收者成功处理和第五次确定拒绝分别持久化。复用 W488 的真实租约到期及原请求 UNKNOWN 证据，已核对发送协议和相关用例的输入。 [W488](../W488-01/DELIVERY.md)

使用本地 .NET 8、实际 Kestrel HTTP 与 PostgreSQL 18，隔离端口 55573。业务资料、IAM 和 sidecar 输入为模拟。`projection-final.trx` 共 7 项通过，包含发送到接收联测、人工放行的故障恢复、两项现有发送行为及三项提交/H01 回归。第五次拒绝采用 `projection-green-02.trx` 的通过结果，接收实现和该用例有效输入相同。合计 8 个不同场景通过，0 跳过。`projection-red.trx` 保存原入口 404，`projection-green-01.log` 保存 .NET 参数名编译失败。日志、TRX、精确命令和受管数据库记录在 `D:/CP6-test-artifacts/W494-01`，各隔离 PostgreSQL 均已核验并停止。

任务级集中审查覆盖实际入口、认证/租户、原件视图权限、锁键和事务、未知回执、退避、终态与源输入。AC-INQ-023 依据 FX24 三个实测结果采纳，原询盘验收 40 PASS / 20 NOT_RUN，关联修订 2/2。正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。

## 启用与交付

`CrmInquiryNative:Projection:Enabled` 默认关闭。启用需要管理员在已经安装 submission 和 decision 存储的目标 PostgreSQL 调用 `InquiryProjectionReceiverV1.InstallAsync`。为同库独立角色授予 schema `USAGE`、`OriginalEvents` 的 `SELECT`、`ConsumerInbox` 的 `SELECT, INSERT` 及 `Dispatch` 的 `SELECT, INSERT, UPDATE`。应用启动阶段注册服务，`/health/ready` 检查权限。配置 `WriterConnectionString` 和 `TenantTokenSha256`，后者以登记租户 GUID 为键保存独立 32 字节随机值经 Base64URL 编码后所得凭据的 ASCII SHA-256 小写十六进制摘要。调用方以 HTTPS 或本机回环 HTTP 发送 `Authorization: Bearer` 和 `X-CP6-Tenant-Id`。首次默认尝试序号 1。此轮调用方由联测驱动，实际 Kafka 投递配置和生产告警接入留待相应环境采用。

主仓在同窗同步台账、生成进度与四份项目状态。两仓普通工作流仅手动运行，R2 候选只匹配版本 tag。Actions 运行和取消均为 0，环境迁移与部署均为 0。接续 WP03-04 的物理缓存索引、限流桶期限及四处置活体回执组合。W492 可独立执行，W493 仍以 W492 和正式包为前置。

CRM 官方 finish 已删除本地和远端 `codex/w494-01`，DEV-2 干净 detached 并释放，固定路径及可复用缓存保留。主仓最终采用和 DEV-1 释放由本窗口 PR 记录。
