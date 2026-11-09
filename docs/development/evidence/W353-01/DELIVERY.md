# W353-01 · 全 Core RMA 租户存储 commissioning

官方 DEV-1 基线 4631874fdcfbf9fac6db5dc7c1e5397dd75f0542，2026-11-09 07:58:01 UTC 开工。PostgreSQL 18.6 上的真实发布宿主初始化与 CLI 验收均通过。所有测试库、登录和业务数据均在本任务模拟。

## 改动

- 增加仅适用于新隔离全 Core 的 rma-application-core-tenant-v1。空库先用 prepare 登记准确物理目标、普通 LOGIN、安装与操作键，再运行原发布宿主的 DatabaseInit。commission 以相同输入进入，核对数据库身份和当前 packaged Core EF 迁移历史。这样已有全 Core 库即使 RMA 为空也无法直接登记。[Core commissioning](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.CoreCommission.cs) · [CLI 操作说明](../../../../tools/CP6.TenantStorage/README.md)
- 单次 commissioning 按旧 Owner 顺序先取认证栅栏，再锁原 heads、versions 与 journal。只在两张原表及 Main.Wms.RmaApplication.v1 的 journal slice 都空时继续。两个原表的全部写入和该 Owner 的旧 journal INSERT 在数据库原生拒绝，包括独立的 close 路径。其他 Owner 保留原 journal 规则。原历史 RMA migration blob 037ea53344edf378d630c032198908f6b9e61f5b、WebApi Program 与 shared 默认路径保持原样。[原 RMA Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs) · [原历史迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261030022000_RmaApplicationV1.cs)
- 同一事务内复用 W350 的目录、复合键、延迟 FK、不可变版本、本地历史和 Ready。新普通登录能读取自己的租户表，业务写入及共享 journal 权限仍关闭。整窗原件在数据库保存，同键恢复检查原写栅栏与本地历史后返回原字节。W350 的独立空库 profile 与原模板脚本哈希保留。[共用原模板](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.cs)

## 实际验证

正式运行 D:/CP6-local-test/CP6-72H-20261109/W353-01/rma-core-20261109T081047-6430601e/commission，验收脚本 exit 0。真实 W351 发布入口 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 的 DLL SHA-256 为 3123dd9fa784d6c31bc674cc5bb015fd44e2431a8d825fcceb86c2e164af2be7。将 c5642cd4 与本任务基线的 PostgreSQL 迁移、Program、数据库迁移 profile 和认证栅栏文件定向比较，差异为空。三个独立 PostgreSQL 数据库均实际执行发布的 DatabaseInit，PID 21724、43740、52692 均 exit 0。独立查询的 Core/Space/IdentityPriority/ERP 四套迁移历史逐项匹配源，分别为 224/1/1/1。正向目标 OID 16386，system identifier 7706081207924235544，实际 Core 历史 SHA-256 为 3f541cc4a5aabfb4681ceb53b734c2ccfff1293278c3dbfc51d66c33b34d3451。

| 场景 | 实际结果 | 原始记录 |
| --- | --- | --- |
| 精确准入 | 已准备但未初始化、错 OID、换安装键、不同 OID 的真实全 Core 模板克隆、未准备及非空库补登记均拒绝 | rma_core_fresh-no-history.stderr，fresh-wrong-oid.stderr，fresh-clone.stderr，fresh-unprepared.stderr，fresh-nonempty-prepare.stderr |
| 旧数据保护 | 两个独立真实全 Core 库分别存入原 heads/versions 或只有原 RMA CLOSED journal。commission 都返回 shared-rma-not-empty，前后状态相同 | rma_core_business_nonempty-preserved.json，rma_core_journal_nonempty-preserved.json |
| 整窗失败 | 在第二个租户 CREATE SCHEMA 的私有测试 event trigger 报 P0001。第一租户先前已运行 DDL、历史及 Ready SQL，最后两租户 schema、目录、控制表和三个旧栅栏都回滚。失败后原旧业务及原 close SQL 又在原认证栅栏下成功执行并回滚 | postgres.log，fresh-interrupted.stderr，fresh-after-abort.json，fresh-old-authority-after-abort.stdout |
| 原写者在途 | 旧后台 18000 持有认证锁并已查原 journal，commission CLI 38724、后台 53828 在 transactionid 上等待它。旧事务可继续写原两表与原 journal 并回滚，commission 随后用原键成功 | fresh-inflight-locks.json，fresh-inflight-legacy.stdout，fresh-commission.stdout |
| 单一写权威 | 旧 native 业务 INSERT、TRUNCATE 和同旧键的 journal-only close 都到达原生 RMA_SHARED_AUTHORITY_RETIRED，SQLSTATE 42501。另一个真实 Owner 的原 journal SQL 在原认证栅栏下提交成功。最终两张旧表、RMA journal 仍为 0，其他 Owner journal 为 1，原生旧栅栏为 3 | fresh-old-business-rejected.stderr，fresh-old-truncate-rejected.stderr，fresh-old-close-rejected.stderr，fresh-unrelated-owner-committed.stdout，fresh-final.json |
| 原件与最小 ACL | 两个普通 LOGIN 各能用受保护映射查本 schema，并被拒绝业务 INSERT 与共享 journal 查询。两个租户的原本地历史与 Ready 匹配。prepare 和 commission 同键回原字节，换操作键拒绝。commission 原件 SHA-256 为 bc3941823d6889fb8c98d3fa7190db6999f7e1193553b0d8b39ffdc82823f1f0 | fresh-runtime-*.stderr，fresh-commission.stdout，fresh-original-replay.stdout，fresh-changed-operation.stderr |
| W350 回归 | 独立 PostgreSQL 用已保存的 W350 Release DLL 安装并迁 A，W353 CLI 在同库逐字节重放原 bootstrap 与 A，随后迁 B 成功 | W353-01/w350-before-after-7436e85a/before-after.json |

真实 RED 为 rma-core-20261109T080238-4bf6ac6f，W350 原 CLI 对新 prepare 返回 exit 2。第一次候选验收 rma-core-20261109T080758-a1caf10f 完成三次真实 Core 初始化，后续全 Core 模板复制超过 W350 夹具的 6 秒查询限额，实例已停，原日志保留。夹具只为模板复制使用 45 秒限额后正式重跑通过。

## 复现与范围

本地 locked restore 复用 W346 离线源，在 W353 私有缓存还原。工具单节点 Release build 为 0 错误、23 条既有其他源文件告警。候选 CP6.TenantStorage.dll SHA-256 为 53807c439aba8c48960a1ce1bc926d2ff02de302b66469884c9032dbedd3791c，CP6.Core.dll 为 35d2603b37813186d90f3a1f36e411b21f2e7e1908e82128eddf11cfd85116c2，CP6.Persistence.PostgreSql.dll 为 cb98bf1b1ea85b1287d0f2e7861d9936c851b36ff856364bf0fc01ed31b3d9d9。原记录在 W353-01/restore-cli.log、build-cli.log、test-full-core-2.log 和 test-w350-before-after.log。[复现脚本](../../../../eng/wp02-02/rma-tenant-migration/verify_commission.py)

    & 'C:/Program Files/python/python.exe' 'eng/wp02-02/rma-tenant-migration/verify_commission.py' --root 'D:/CP6-local-test/CP6-72H-20261109/W353-01' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55562 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli 'D:/CP6-local-test/CP6-72H-20261109/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll' --webapi 'D:/CP6-local-test/CP6-72H-20261109/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll' --webapi-source 'c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40'

旧写拒绝证明来自真实数据库执行原写与 close SQL，HTTP 入口、当前业务权限、共享 source、读模型与新 writer 的 journal 权限另窗验收。现有共享 RMA 业务迁移、正式推广与部署也分别验收。

分支 codex/w353-01，DEV-1。相对最新确认的 origin/main 310b99df 的 #808 变化无文件重叠。正式及 W350 回归 postmaster 17616、56508 已停止，专属 PG 55562 无监听。COORD 接手审查、合并和官方 finish。
