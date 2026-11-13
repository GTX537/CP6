# W530-01 · PGT-025 原生合同与精度保持

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。CRM 以 `eb35232c1ae819efaacac318781c78e4cf9d0d2b` 为基线，测试源码 `e2e8a23a32345fedde6b44cb2ab72b533c25e0f7`，远端采用 `a4890974b3dce2fa9e978a62d3cbcfeb1958bf91`。主仓基线 `3f773d3b65f2b0d3a8487cf0c3cbdc611f3d16e3`。 [CRM PR #207](https://github.com/GTX537/CP6.CRM/pull/207) · [正式矩阵](../../acceptance/postgresql-tenancy.json)

## 采用范围与字段

选用已运行的 CRM `ConfigurationSaveRequest` / `ConfigurationView` / `BundleIdentity`，调用实际 Kestrel BFF 和 `CrmInquiryConfigurationOwnerV2`。从受控新库安装入口按组织 GUID 建立 `t_<guid-N>."InquiryDraftSnapshots"`。现有父表按 `OrganizationId` 路由，保留原复合主键、Calendar 与 PreparedIntake 外键、分区不可变触发器和现行鉴权。共享 `ConfigurationSaveResults` 和含内容的 Bundle 使用 `session_user` 所绑定的专用普通 LOGIN 及强制行级策略。普通账号仅通过父表访问草稿。 [安装说明](https://github.com/GTX537/CP6.CRM/blob/a4890974b3dce2fa9e978a62d3cbcfeb1958bf91/docs/handbook/10-DATABASE-MIGRATIONS.md) · [PostgreSQL 分区](https://www.postgresql.org/docs/18/ddl-partitioning.html) · [PostgreSQL 行级策略](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)

| 合同事实 | PostgreSQL 映射与实际结果 |
| --- | --- |
| 源草稿原文、摘要 | `SourceUtf8` / `SourceHash` 为 `bytea`。保存后源草稿 `WorkingPeriod.day` 的数值 token 为 `-0`，类型化日历值为 0，原源字节的 SHA-256 回读一致。HTTP 请求、Owner 构造的草稿源和响应具有各自真实字节身份。 |
| 词法精度与原业务身份 | `IdentityJson` / `ViewJson` / `VersionToken` 为 `bytea`。保留 80 位原表单版本、19 位小数秒、`0000-02-29`、200 个码点含 NUL、原组织、表单、创建者和 Bundle 版本。日历投影和原版本来自现有 Owner。 |
| 共享原回执 | 原 `OrganizationId`、`ActorId`、`RequestKeyHash` 槽位不变。独立连接读回原规范请求摘要、受保护响应、原 ETag、审计及同一 Outbox。解保护后 UTF-8 字节与数据库 Bundle 原结果及 HTTP 回放相等。 |
| 执行与回滚 | 后段 PostgreSQL 故障确认已经暂存租户草稿。回滚后草稿、日历、Intake、Bundle、共享回执、审计及 Outbox 计数均回到前值。使用同一操作键完成一次合法提交。 |
| 实际断连与权限 | 后端 18512、xid 852 的真实 `CommandComplete COMMIT` 被环回代理截断，HTTP 为 503。独立读确认已提交。当前权限拒绝时 403，零 Owner 写连接。恢复权限后新后端 31512 返回同一原 201、UTF-8 正文和 ETag。无重复效果。 |
| 租户约束 | 物理证据为 `t_1f0c9e665dfb410d8ce9f0b92bca8089` 的实际 tableoid。其他已登记普通 LOGIN 对 A 的草稿、原回执和 Bundle 返回零行。A 直接查询 B 分区和尝试写入 B 均被 PostgreSQL 42501 拒绝。未建分区的组织被 PostgreSQL 23514 拒绝。 |

S01 profile 为 `crm-inq-r3-v15`。精确输入包为 `CP6-S01-CONTRACT-INPUTS/1.0.0`。采用的 [CRM profile 与向量](https://github.com/GTX537/CP6.CRM/blob/a4890974b3dce2fa9e978a62d3cbcfeb1958bf91/contracts/s01/generation-manifest.json) SHA-256 分别为 `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e` 和 `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。组合 OpenAPI SHA-256 为 `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349`，当前明确采用的终端 OpenAPI SHA-256 为 `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d`。源和回放的实际摘要见运行证据。 [S01 入口](../../../../eng/contracts/s01/README.md) · [前序原键恢复](../W510-01/DELIVERY.md)

## 验证与交付

本地 PostgreSQL 18.6、.NET SDK 8.0.424、CRM 锁定 Platform 0.10.3。业务数据和身份来源为模拟，CRM Owner、Kestrel 和 PostgreSQL 为实际运行。最终 `pgt025-owner-final.trx` 1 项通过，独立的新库 `pgt025-compatible-final.trx` 11 项通过，0 失败、0 跳过。旧布局回归包括锁、外键、精度、当前会话和原回执。两种安装使用独立测试库，因为原布局不能在同库被改成新布局。初次混跑及旧只读集合断言的失败结果保留。已在原规则下纠正集合断言并复测。

运行证据位于 `D:/CP6-test-artifacts/W530-01/pg/57368780aa3788e88da9acc1193dbf59/pgt025-owner-recovery.json`，两套 TRX 和 `terminal.json` 在 `D:/CP6-test-artifacts/W530-01/`。集中审查覆盖布局安装、原约束、普通 LOGIN 与共享结果、故障与重连、旧布局兼容。CRM 分支已定向清理且 DEV-2 已释放。主仓远端采用与 DEV-1 释放以本任务 PR 及官方 `main-finish.json` 核实。Actions 启动、取消及部署均为零。

剩余正式项 PGT-017。WP02-10 整卡、CRM 其余表的租户布局及既有数据库的迁移、回填和启用继续按各自任务处理。 [PGT-017 已有库存证据](../W435-01/DELIVERY.md)
