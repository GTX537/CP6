# 项目 4 · 把 Kafka 日志消费改成可恢复处理

## 目标

解决当前操作日志消费者的三个边界：重复消费会重复插日志，数据库成功而 SignalR 失败会整体重试，`Clients.All` 可能跨租户推送。实现稳定 EventId 去重、通知 outbox/dead-letter 和租户 group。

## 1. 事件契约

不要直接把数据库实体当消息。定义版本化 envelope：

```json
{
  "eventId": "uuid",
  "eventType": "OperLogCaptured",
  "schemaVersion": 1,
  "tenantId": "uuid",
  "occurredAtUtc": "2026-07-22T12:00:00Z",
  "correlationId": "...",
  "payload": {
    "userName": "...",
    "httpMethod": "POST",
    "requestUrl": "/api/...",
    "statusCode": 200,
    "elapsedMs": 42
  }
}
```

限制 payload 大小，敏感字段不发送或脱敏。

## 2. 数据模型

```text
Sys_OperLog.EventId
UNIQUE(TenantId, EventId)

Sys_NotificationOutbox
Id, TenantId, EventId, Type, Payload, Status,
AttemptCount, NextAttemptAtUtc, LastError, CreatedAt, SentAt

Sys_DeadLetter
Topic, Partition, Offset, EventId?, TenantId?, PayloadHash,
ErrorCode, ErrorSummary, AttemptCount, CreatedAt
```

DeadLetter 是否存原 payload取决于敏感性与加密/权限，默认只存安全摘要和受控引用。

## 3. 消费事务

```text
deserialize + schema validate
→ validate tenant
→ create tenant scope
→ begin transaction
→ insert OperLog (EventId unique)
→ insert NotificationOutbox
→ commit DB
→ commit Kafka offset
```

重复 EventId 唯一冲突视为已处理，确认 outbox 是否存在后可 commit offset。不要把所有 unique violation 都当重复；检查约束名/错误上下文。

## 4. 通知 worker

独立 worker 扫 Pending 且到期记录，按 TenantId 发 `Clients.Group("tenant:{id}")`。成功标 Sent；失败 Attempt+1、指数退避；超过上限告警/Dead。

并发 worker 要 claim 行，避免重复同时发送。即使仍重复推送，前端可按 EventId 去重。

## 5. offset 与数据库提交窗口

DB commit 后、Kafka commit 前崩溃：重放，但 EventId 去重，不重复业务效果。

Kafka commit 后才是消费进度推进。不要先 commit 再写库。

## 6. 毒消息

分类：

- JSON/schema invalid：不可重试，dead-letter 后 commit。
- unknown tenant：按政策 dead-letter/延迟，不能默认租户。
- SQL timeout：可重试退避，不 commit。
- duplicate EventId：成功语义，commit。
- SignalR down：不影响 Kafka consumer，outbox worker重试。

## 7. SignalR group

Hub OnConnected 从已认证 claim 得到 TenantId，加入 group。客户端不能通过 query 参数选择任意 tenant group。平台跨租户监控使用独立授权 group/endpoint。

## 8. 测试

- 同 EventId 两次，日志/Outbox 各一。
- DB commit 后崩溃再处理，仍各一。
- SignalR 连续失败，consumer offset 已推进，outbox 重试。
- A 事件只有 A group 收到。
- invalid JSON 不阻塞后续消息。
- unknown tenant 不落 default。
- 两 outbox worker 竞争不重复 claim。
- 取消时完成/回滚边界明确。

## 9. 指标

consumer lag、duplicate count、dead-letter、outbox pending age、send failure、每租户失败、处理延迟。告警关注最老 pending，不只队列长度。

## 10. 部署顺序

1. 增加 nullable EventId/outbox 表。
2. Producer 发 v1 eventId，consumer 兼容旧消息。
3. 新 consumer 双写 outbox但保留旧通知观察。
4. 启用 outbox worker。
5. 切除同步 `Clients.All`。
6. EventId 完整后收紧 NOT NULL/唯一。

## 11. 自我评审

- 消息身份跨重试稳定吗？
- 去重约束带 TenantId 吗？
- DB 与待通知同事务吗？
- 永久错误会卡 partition 吗？
- Clients.Group 的 tenant 来自可信 claim 吗？
- 日志/DeadLetter 泄露 payload 吗？
- 有恢复工具和指标吗？

## 12. 面试口述

按“at-least-once 重复窗口—稳定 EventId—DB 唯一幂等—通知 outbox—dead-letter—tenant group—故障测试”讲。不要说实现 exactly-once；这是可重复投递下的幂等效果。

