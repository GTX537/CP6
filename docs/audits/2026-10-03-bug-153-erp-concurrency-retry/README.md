# BUG153：ERP 并发测试按真实投递协议完成验收

[Issue #153](https://github.com/GTX537/CP6/issues/153)，P2。两库本地相关验证与一次集中审查已完成，正常交付待完成。

WP6 的真实 PostgreSQL ERP 回归首次出现 93/95 通过、2 失败、零跳过。失败用例同时投递同一命令或同一机会的不同版本，却使用只接受首次 `Applied` / `Duplicate` 的辅助函数。增加既有 `SqlFailureProbe` 后，实际诊断记录 `40001 / SerializationFailure`；ERP 处理器已返回 `RetryScheduled`，生产入口按既有协议返回 `RETRY`，测试却立即停止。

修复仅影响这两个测试。第一批投递仍并发执行；如果需要重投，必须已经观察到真实数据库事务冲突，且所有 native 错误只能是允许的死锁/序列化失败（SQL Server 另允许其伴随回滚码 3903）。最多五轮重投原 `MessageId` 和原始 bytes，按已有测试时钟推进超过八秒最大退避；未知原生数据库错误、不可接受的终态或重试耗尽仍失败。现有诊断器不覆盖所有托管异常，不能据此声称识别了全部 unavailable 原因。最后逐条验证 Inbox 为 Processed、持久化 payload 未变，并保留一张订单、一条明细、唯一 bridge、正确终态 journal 等全部原断言。

这不是把 `RetryScheduled` 当成功，也不是在失败事务中重试 SQL。生产处理器、业务逻辑、表结构、迁移和公共接口均未改变。持久化后台 worker 已有独立用例；本修复证明的是消息投递与最终业务结果，不冒称本次实际等待了操作系统九秒或运行了外部 broker。

| 执行 | 当前结果 |
| --- | --- |
| 原 WP6 ERP 矩阵 | 93 通过 / 2 失败 / 0 skip，原失败保留 |
| 诊断原两项 | 1 通过 / 1 失败 / 0 skip；失败观察到 native 40001 |
| 修复后 PG 原两项 | 2/2、0 skip；两项都实际观察到 40001 后完成原业务断言 |
| 修复后 PG 原 ERP 全套 | 95/95、0 skip，实际独立执行 |
| 修复后 SQL Server 原 ERP 全套 | 95/95、0 skip，实际独立执行 |

独立分支基于远端 main `cbbb7fc8e99290f6aba7589830a98a98726280e9`。首次 locked restore/build 成功；构建保留原 `BridgeWorkerSqlTests.cs:66` 的 xUnit2029 风格警告，没有扩大修复范围。只使用单独创建并留有 receipt 的本地两库；WP6 原失败数据库与证据未覆盖。

一次集中审查无未解决实质问题；31 份原始失败、通过、构建和审查材料见 [证据清单](native/manifest.json)。初版两份通过报告的 XML Counters 被序列化为空数组，原件保持，准确计数从清单关联的原 TRX 推导，不改写原执行。交付仍需正常 PR、远端包含性与必要冒烟。所有验证本地执行；没有启动 Actions、修改工作流/分支保护或部署生产。
