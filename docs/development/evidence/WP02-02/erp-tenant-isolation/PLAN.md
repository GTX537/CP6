# WP02-02：ERP 共享存储行隔离与 EF 事务接入

基线 Main `f6cc17b936d05b0336d319cbfa5097a77511d658`，分支 `codex/wp02-02-erp-tenant-isolation-20261015`。
用户继续推进既有 WP02-02；采用已接受 `cp6-pg-tenancy-v1` 第 3、5、6、7 节。沿用 PostgreSQL 唯一目标、一次集中审查与相关本地验证。

本轮把已交付的租户连接组件接到一个真实 EF 模型：`ErpIntegrationContext` 的 8 张表。
按已接受设计，全部继续位于固定 `erp_integration`，不复制到租户 schema，不改主键、MessageId 全局唯一域、字段/精度/审计规则。
本上下文没有动态 schema，复用与租户无关的现有 EF 模型；不为它虚构租户模型缓存。其他业务上下文的 schema/cache 采用仍后续处理。

实施内容：

- 新布局专用、显式调用的 PostgreSQL 受保护登录→租户目录及无参数 SECURITY DEFINER helper。helper Owner 为专用 NOLOGIN 最小权限角色；固定受控 search_path、完全限定对象、无动态 SQL、无 PUBLIC EXECUTE；运行账号不能读写目录或取得 helper Owner。
- ERP 8 表 ENABLE/FORCE RLS，旧行 USING 与新行 WITH CHECK 俱全。依据真实 session_user 对应的受保护绑定，不使用可改写 GUC；拒绝未知/失效映射和身份冲突。现有全局唯一索引保留，错误不披露他租户内容。
- 仅具名表/角色、按用途授予必要 DML；两类 replay audit 保留 append-only 语义。安装由受控调用者在明确事务中执行，不挂到应用启动、不修改既有迁移历史、不接管已有不明对象或扩大既有 runtime 权限。
- 内部 EF 接入器消费上一轮 TenantStorageTransaction，在同一实际 NpgsqlConnection/DbTransaction 上构造真实 ErpIntegrationContext。保持调用方显式提交与当前 scope/binding 检查；不接受客户端 tenant/schema/连接串，不启用自动重试。DBContext 生命周期局限回调，实际每条 EF 命令执行前核当前受管边界，禁止外借/缓存上下文。
- 同次集中审查确认 `ReadCommitted` 下核验与业务 SQL 的目录快照可能变化：helper 读取前取得事务级共享 advisory lock，目录 INSERT/UPDATE/DELETE 触发器取得同域排他锁，把绑定固定到事务结束；目录 TRUNCATE 直接拒绝，避免表锁与 advisory lock 顺序反转。此锁只解决目录变更与读写之间的竞态，不替代真实宿主授权或 Owner 最终门；增加两连接的定向并发验证。
- 针对该 8 表记录实际模型/唯一域/writer/raw SQL/保留恢复依赖的局部清单。它不冒充所有 328 表的完整清单。

代码范围为 `CP6.Core/Persistence/Tenancy/` 的新资产及必要租约扩展；测试为 `CP6.Tests/Persistence/Tenancy/`，必要时允许新布局安装代码位于 `CP6.Persistence.PostgreSql/`。
实施中确认：`PostgreSqlTextQuerySqlGenerator` 的业务 collation 原为未限定名称，在新租约的 `pg_catalog` search_path 下不可解析。本轮同时将该引用明确限定到原有 `public`，保留排序/文本语义，并做实际字符串查询验证；不扩展为其他 provider 重构。
不修改当前 Program、认证/Owner 最终门、旧工厂或默认业务路由；不改变实际 ErpIntegrationContext 的合法输入和 SQL Server 历史路径，不新增包/项目依赖。

本地验证复用已恢复 0.10.3 依赖。只在新的自有 PG18 集群及合成 A/B 数据上安装，复用已修正的夹具生命周期；独占端口 55436，启动前核空闲，结束确认自有路径/PID/端口并停止，保留数据与失败。
验证覆盖普通账号直接 SQL/EF 的 SELECT/INSERT/常量 UPDATE/DELETE 隔离、GUC/SET ROLE/目录攻击、原全局唯一冲突、跨 schema+共享表同事务回滚/提交，以及失效 scope/绑定后 EF 命令拒绝。实际使用真实 EF 模型及受影响既有 PG 兼容初始化，不能只建简化表冒充完整模型。
只给当前行为写有意义的回归；未改连接池等原证据复用，不重复全仓测试。允许实现中按实际 API 收敛内部细节，不新增逐步骤批准。

COORD 独占台账/状态/Handoff 和 Main:tenant-storage / Main:ef-model 编辑窗口。一份 DELIVERY 报告，正常 PR 合并、远端核对和本地 main 同步。
完整 WP02-02 保持 IN_PROGRESS；真实宿主当前授权、Owner 最终门、其他上下文映射、完整迁移/恢复及原 17 PGT 不由本轮局部验证自动关闭。无 Actions/正式发包/既有业务迁库/部署授权。
