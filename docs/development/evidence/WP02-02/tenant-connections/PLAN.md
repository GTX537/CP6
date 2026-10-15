# WP02-02 租户连接组件实施

基线 Main 7837009aab983f38e068f0f3dd56745c12450449；分支 codex/wp02-02-tenant-storage-20261015。
用户“继续推进”授权后续常规实现，沿用 cp6-pg-tenancy-v1；仅本地验证，不授权 Actions/发包/业务迁库/部署。

本轮实现宿主内部的不可变 TenantStorageBindingV1、受控目录、专用 Npgsql DataSource 与租约。
入口只读当前可信 context，不接收客户端 tenant/schema/连接串；缺/legacy/已结束 scope、非 Ready/未知模型、错 host/数据库/角色拒绝。
目录和凭据为宿主私有配置；不构造新的 wire 字段，也不把一次 Evidence 当永久授权。
池按 host/database/tenant/principal/credential version 分离，所有存活及 draining 池计入进程连接预算，支持空闲回收和轮换排空。
固定 pg_catalog search_path，禁止 multiplexing/no-reset/环境事务旁路；打开后核 session_user/current_user/database，受管操作核当前 scope 与绑定。
显式本地事务由调用方控制；不自动重试、提交或声称拥有 Owner 最终门。

新增代码限 CP6.Core/Persistence/Tenancy/，相关单元及真实 PG 测试限 CP6.Tests/Persistence/Tenancy/。
真实 PG 测试若需独立入口，允许 eng/wp02-02/tenant-connections-tests/ 及其真实恢复锁、Core 对准确测试程序集的 InternalsVisibleTo；避免普通单元测试默认启动本机数据库。无需新增包版本或重跑无关项目。
不改现有 Program/认证/旧默认 tenant/public 工厂、EF 模型和 migrations，不开放新业务路由。
独占本地 PG18：新建 D:/CP6-local-test/WP02-02/tenant-connections-* 运行目录及 55436 端口，先核未占用；随机凭据保密。
测试用 A/B 普通 LOGIN 和明确合成表，覆盖复用/强制串租/跨 schema 拒绝/取消回滚/预算/轮换；收尾只停自有实例，保留数据和失败记录。
复用已采用0.10.3及相关依赖，不新增包版本；先关键失败回归，再集中实现、相关测试和一次任务级审查。

该组件是可独立集成的基础能力，不宣称完整 WP02-02 完成。真实对象 inventory、共享 RLS/目录 helper、EF 缓存与映射、宿主当前授权、Owner 原子写集、迁移恢复仍待后继；17 PGT 和业务状态不提升。
COORD 独占台账、生成视图、四状态和 Handoff；交付一份 DELIVERY.md，正常 PR 合并并核对远端 main。
