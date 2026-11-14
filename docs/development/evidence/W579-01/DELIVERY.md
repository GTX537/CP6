# W579-01 完整 Core 迁移后的 Identity Directory 鲜装

对应 [缺陷 #995](https://github.com/GTX537/CP6/issues/995) 和 PGT-017。Main 输入基线 `0e3709f16024bdf8b104f033dce76a20d2b5d6da`，已验源码 `f3afd4d531f4c638df1dde426a6a1eabf33fae8b`。CRM 运行源码 `1ca973ea2fb033b957f3544e827ea580e17b4774`，沿用 W578 本地编译产物。使用模拟租户和两套隔离 PostgreSQL 18.6，Main HTTP、CLI 和 CRM 子进程均实际运行。

完整 231 条 Core 迁移中有 564 个业务及存储例程，516 个在旧准入表之外。全量 layout 截止 `20261114030500_StockOrdinaryWriteAuthorityV1`。修复为按排序的迁移 ID、函数定义与有效非 Owner ACL，以及附属触发器定义与启用状态核对该 layout。初始 171 个 PostgreSQL 默认 PUBLIC EXECUTE 在安装事务中撤销。保留既有角色、数据库/schema/列授权、列与主键形状、RLS 和回执校验。对两个普通登录查得业务函数 EXECUTE 为零。较早受支持 layout 沿用既有检查。后续 Core 迁移改变实物 catalog 时需复核。 [安装说明](../../../../CP6.Core/Persistence/Tenancy/README.md)

| 本地验证 | 结果 |
| --- | --- |
| Core 全量迁移鲜装与最小权限，未知函数、函数体、设置、权限及禁用库存触发器 | 2/2；拒绝 16 项注入目录差异 |
| 两个运行登录的目录授权，跨租户读取和写入阻断 | 6/6 |
| Main 实际配置读取、真实 Kestrel A/B HTTP、显式 CLI 安装及回读、未知提交后只读核对 | 3/3 |
| CRM 子进程消费 Main 的全部多页目录，另一数据库保存并核对真实原值 | Main 和 CRM 各 1/1 |
| Main 实际 C2 服务令牌，CRM 后台为 A/B 完整发布；对账条数、租户、摘要和逐条原值，错误凭据拒绝 | Main 和 CRM 各 1/1 |

合计 15 项通过、零跳过。初始共享资源和默认端口对照产生的夹具失败，以及后台发布一次轮询超时，均保存在 `D:/CP6-test-artifacts/W579-01`；最终每套使用独立资源。本次后台用例在父进程首先用真实 `/connect/token` 取令牌并收集同源预期，CRM 子进程独立取令牌。超时未建立产品故障归因。构建后的本地 PostgreSQL 发布保留 310 文件 SHA-256 清单，两份主体程序集与测试内容一致。Kestrel 存活端点 200，未登录写入口 401。目录 HTTP 以回环传输运行，TLS 终止由夹具模拟。未使用现有数据库、未部署，Actions 启动、重跑和取消均为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。已有真实目录后台发布和 W578 真实 Kafka 的分项证据。待在同一两库场景完成实际投影切换、恢复差量写集并安全开放，以及 Main 余下写入口。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
