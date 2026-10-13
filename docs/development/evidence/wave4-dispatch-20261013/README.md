# 第四轮派工证据

[准入记录](admission.json)、[工作树建立](setup.json)和[执行包](../../dispatch/20261013-wave4/README.md)记录本轮分配。四份只读来源评估：

- [S01-11 ERP-CREDIT](S01-11-admission.md) / [机器记录](S01-11-admission.json)
- [S01-10 PostgreSQL存储](S01-10-admission.md) / [机器记录](S01-10-admission.json)
- [WP02-01可信上下文](WP02-01-admission.md) / [机器记录](WP02-01-admission.json)
- [S01-07质量合同](S01-07-admission.md) / [机器记录](S01-07-admission.json)

本次仅检查依赖、固定来源、当前源码、独占范围、工作树和文档/台账；未执行四项未来实现的编译、运行测试或数据库操作。上轮已交付证据见[第三轮交付](../wave3-integration-20261012/README.md)，本次不把旧测试改称新执行。PostgreSQL二进制/空闲端口可用性只作资源评估，不代表已建库或存储通过。

原始评估按字节复制并固定hash；其中候选写入窗口/建议只作依据，正式允许范围以本轮任务卡及dispatch.json为准。时间为本机UTC；后续GitHub回执的服务器时间单列，不能直接跨时钟推断操作顺序。准入不是业务PASS，READY不是START。

协调检查：[来源/92条向量/互斥窗口](dispatch-check.json)、[台账与生成视图](ledger-check.json)、[新增链接](added-links.json)。[检查器历史](check-history.json)保留首次索引字段识别失败及修正原因；固定原件、向量预期和业务数据未变。
