# PostgreSQL 后续开发基线

决策日期：2026-10-11。来源：用户明确指定后续使用 pgsql，并说明希望利用 schema 更好地服务多租户。本决定适用于后续开发、任务拆分和本地验收；历史记录保留原时点。

## 已决定的事项

- PostgreSQL 是后续新增开发的唯一数据库目标。类型、SQL、索引、参数绑定、锁、事务和恢复采用 PostgreSQL 语义；新增任务不再要求 SQL Server 或双库实现/验收。
- 保持已接受合同的精度、合法输入、身份、授权和原子性。选择既有设计中的 PostgreSQL 部分，不能把 Provider 切换当成修改业务合同的授权。
- 保留既有 SQL Server 代码、原始数据、备份和历史证据。此决定不执行迁移、删除或环境切换，也不改变正式发布权威及门禁。

## schema 多租户的方向与边界

优先评估“平台共享 schema + 租户业务 schema”的方案，最终物理布局由 S02-00 形成有版本的设计结论。可用下例理解方向，名称仅示意，不是建库脚本：

```text
CP6 PostgreSQL database
  platform_shared     租户目录、平台级配置（准确清单由设计确认）
  tenant_a            租户 A 的业务表
  tenant_b            租户 B 的业务表
```

schema 是命名空间，并非天然的安全屏障：有相应权限的角色可以访问同库其他 schema；`search_path` 中不可信可写 schema 还会引入对象解析风险。[PostgreSQL schema 官方说明](https://www.postgresql.org/docs/18/ddl-schemas.html)。因此，本次只确定数据库和优先评估方向，不宣称现有系统已经实现上述布局。

Main 源码基线 `2df45097c8ba18728d30bc85f12cc874921c25e4` 已有租户上下文与 TenantId 机制，入口为 `CP6.Core/Services/Common/ITenantContext.cs`、`TenantScope.cs`、`CP6.Core/EFDbContext/CP6Context.cs`。现有行级应用过滤、租户复合键和授权不能因增加 schema 自动删除。若共享表使用数据库 RLS，需另行设计；超级用户、BYPASSRLS 角色和通常的表 Owner 可绕过 RLS，不能用这些身份的成功查询证明普通租户隔离。[PostgreSQL RLS 官方说明](https://www.postgresql.org/docs/18/ddl-rowsecurity.html)。

## S02-00 必须给出的设计结果

1. 分类现有共享数据、租户数据和跨域 Owner；对比 schema 布局与现有 TenantId 方案，给出选定方案、成本依据、版本和兼容阶段。schema 数量、租户规模与升级时间需有可复现测量或明确未测范围，不承诺无限扩展。
2. 从可信认证/租户目录解析 schema；客户端不得直接提供数据库对象名。记录 PostgreSQL 标识符长度、规范化、大小写和碰撞规则，保留稳定 TenantId 与审计/消息身份的对应。
3. 明确连接池复用、事务内上下文设置/清除、失败/取消/重试、后台任务和管理员跨租户操作。不能将会话级 `search_path` 残留给下个租户；不能把 `search_path` 当成拒绝显式跨 schema SQL 的权限门。
4. 明确运行角色、迁移角色、schema/table/sequence 权限，以及共享表是否采用 RLS。若共用运行角色有全部租户权限，必须如实说明隔离依赖应用授权，不宣称数据库权限已隔离各租户。
5. 定义 EF 模型/缓存和动态 schema 绑定、迁移历史归属、逐租户升级锁与版本、部分失败恢复、添加/停用租户、单租户导出/恢复及全局升级策略。保留同一业务事务中 Owner 与 Inbox/Outbox 的原子性。
6. 形成 A/B 租户交替复用连接、伪造租户、显式跨 schema 读写、缺失租户上下文、后台重试、共享表越权、迁移中断恢复和备份恢复的正反验收矩阵；设计阶段全部 `NOT_RUN`。

S02-00 在新增真实租户存储布局、迁移和业务写入接线前完成；它不阻塞 S01 的纯类型、decoder 和隔离测试库中的精度映射。独立数据库/专用角色只是设计选项，不能未经评估作为既定架构写入所有模块。

## 与历史文档的关系

[原实现评估](05-implementation-assessment.md)和归档仍描述当时的双库事实。当前计划与冲突的旧默认要求以本决定及 [AGENTS.md](../../AGENTS.md) 为准；历史成功结果只按其真实 PostgreSQL 范围复用，SQL Server 结果不能替代 PostgreSQL 验收。本次没有修改原设计资产、数据、业务代码或生产发布配置。
