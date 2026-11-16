# W664-01 全局 MenuKey 跨 CRM 库封存

## 基线

受测 Main 产品源码为 b396445131a330d1a8ddd4cc057d39dc4cb68b6c。本地测试检查点 fabc899c557b60531fd67af5dab96acc4ca379aa 只新增隔离 PostgreSQL 测试。用实际 Kestrel、签名登录 Cookie 和 CSRF 执行 PUT /api/menu。两枚实际 StoreId 绑定同一隔离 PostgreSQL 18.6 实例中的不同物理数据库。两租户分别通过实际 OIDC code exchange 获得受影响角色和独立角色的 bearer。租户、项目和授权输入为模拟数据。 [原问题 #1012](https://github.com/GTX537/CP6/issues/1012) · [测试源](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.Tests/Platform/Pgt017CrmPendingMenuFanoutPostgreSqlTests.cs) · [登记](D:/CP6-test-artifacts/W664-01/main-start.json)

| 原生边界 | 实际结果 |
| --- | --- |
| Main 原请求 | HTTP 500。目标菜单、两个租户的 RoleAction 与 RoleMenu、可见目录修订、两角色快照与 generation、普通和优先 Outbox 原样保留。两个受影响的 Main 角色来源仍为版本 4。 |
| 第一 CRM 库 | StoreId 749995c5-6e0b-4769-9272-71299ac9e08e，database OID 16385。专用 LOGIN 可用。角色最低版本 0→5，身份根 revision 1→2。 |
| 第二 CRM 库 | StoreId c668100f-9199-4f7e-bbc7-290084534cc0，database OID 16399。专用 LOGIN 实际拒接 28000。角色最低版本为 0，身份根 revision 为 1。 |
| 当前授权 | 两枚受影响角色的旧 bearer 仍收到新的写 fence。两枚独立角色仍可请求带 fence 的写许可，原合法读继续返回 200。 |

实际 RED 为 1 失败、0 跳过，断言同时记录 Main 回滚、第一库已经封存及受影响角色仍能取得新许可。 [真实 TRX](D:/CP6-test-artifacts/W664-01/baseline-verified/main.trx) · [原写集和 Store 身份](D:/CP6-test-artifacts/W664-01/baseline-verified/coordination/menu-cold.json) · [执行入口](D:/CP6-test-artifacts/W664-01/run-menu.ps1) · [受测产品与测试 DLL 哈希](D:/CP6-test-artifacts/W664-01/baseline-verified/result.json)

TRX SHA-256 BE31F5217106D59AE942D7A1E4A5BB16E67557EAD3ED625AD809687AD48732FE。原写集 JSON SHA-256 8E60556B20121A97940CC437AB8CBEDED5DC3774099B5818562249692F1C28C2。实际受测测试文件 SHA-256 E63DDEFAB6CAE8414E8B80119FB18F373772B7D44AD3F0D36A98596999084DB4。

首轮编译缺少测试类型的命名空间。前两次运行分别遇到夹具签名字段误用 tenantId 和 PostgreSQL 物理表名误用 Sys_RoleMenu。修正为原 organizationId 与 Sys_RoleMenus 后，编译 0 错误并取得上表业务 RED。 [编译失败](D:/CP6-test-artifacts/W664-01/build-baseline.log) · [签名夹具失败](D:/CP6-test-artifacts/W664-01/baseline/main.trx) · [物理关系夹具失败](D:/CP6-test-artifacts/W664-01/baseline-native/main.trx) · [最终编译](D:/CP6-test-artifacts/W664-01/build-baseline-final.log)

## 实施

基于已采用 W662 的 Main `70dd831ab58f75d66a67dc367e73bd09f83919c0`，原 `POST/PUT/DELETE /api/menu` 使用各自原权限 `menu:add`、`menu:edit`、`menu:delete` 和共同 Main PostgreSQL 写根。第一次事务以回滚保存点运行原 C02 写者，确定各租户原角色及精确原版本和后继哈希。父操作和现有按角色粒度的静默登记在同一次 Main 提交建立。两库原绑定连接均成功打开后，才记录可恢复的封存开始状态。普通角色的读取、关闭和原写请求不能处理菜单的子记录。 [原 API](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.WebApi/Controllers/Sys/MenuController.cs) · [菜单事务](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.Core/Services/Sys/IamAdministrationV1.CrmLegacyMenuSeal.cs) · [角色隔离](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.Core/Services/Sys/IamCrmRoleSealV1.cs)

最终事务使用原 Main 根复核当前调用人、原目标和全部 C02 计划，按各真实 Store 的原生事务封存。Main 提交一次原菜单写入、C02 后继、Outbox、字段审核、操作审核和精确响应，同时清理角色子记录。原 `PUT /api/menu/tree` 继续走旧入口。尚未封存的原请求按目标身份和 C02 计划恢复，菜单树世代只用于识别已提交响应。旧 HTTP 无客户端键，同一签名调用人的同一请求可在当前目标及菜单树世代仍匹配时回读已提交响应。之后的请求按当前菜单状态和原 API 行为处理。 [记录和回读](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.Core/Services/Sys/IamLegacyMenuSealV1.cs) · [前向迁移](https://github.com/GTX537/CP6/blob/d256b373577bab5ac1d0edad407b4933e124c875/CP6.Persistence.PostgreSql/Migrations/Core/20261115230000_IamLegacyMenuSealIntentV1.cs)

## 定向本地结果

运行使用模拟业务主体、真实签名 Main/Kestrel、PostgreSQL 18.6 和各自绑定的真实物理 CRM 库。两个原生先后顺序调用了冻结 CRM 产品的实际 `intake:reject` Owner，测试侧辅助程序只建立合法模拟业务及锁等待。

| 场景 | 实测结果 |
| --- | --- |
| 三种旧路由的第二库冷拒接及同请求恢复 | HTTP 503，响应 `{code:503,message:"AUTHORITY_FENCE_UNAVAILABLE"}`。两个库原角色最低版及身份根保持原值，两枚受影响的旧 bearer 无法得到新的写 fence，独立角色和合法读可用。无关 `PUT /api/menu/tree` 仍提交排序及原权限目录修订，待处理原件和角色状态不变。恢复后实际菜单和每库角色后继各提交一次，Main 有两条 C02 Outbox 及各一条字段和操作审核，原响应逐字回读。POST 覆盖原 RoleAction 等菜单登记。DELETE 原 count 3，重放仍为 3，重新登记同 ID 后两个新调用分别返回 1 和 0。 [PUT](D:/CP6-test-artifacts/W664-01/native-display/main.trx) · [POST](D:/CP6-test-artifacts/W664-01/native-post/main.trx) · [DELETE](D:/CP6-test-artifacts/W664-01/native-delete/main.trx) |
| 封存先提交 | 真实 CRM 已得到旧许可，等待期间 Main 提交精确的两角色封存和原菜单。CRM 恢复原请求后收到 403，原受理图与出站事件相同。 [Main](D:/CP6-test-artifacts/W664-01/order-seal-first/main.trx) · [CRM](D:/CP6-test-artifacts/W664-01/order-seal-first/crm.trx) |
| CRM 业务先提交 | Main 封存在 `pg_blocking_pids` 中等待旧 CRM Owner。CRM 以 200 提交原 `Rejected`、决策和一条 reject Outbox 后，Main 推进角色封存。CRM 原投影版本 5 对应原生最低版本 6。相同 CRM 请求稍后得到公开 `403 crm.permission_denied`。当时 Main 仍签发 200 当前 context，原业务、历史、决策计数及 Outbox 保持提交后的状态。 [Main](D:/CP6-test-artifacts/W664-01/verified-owner-first/main.trx) · [CRM](D:/CP6-test-artifacts/W664-01/verified-owner-first/crm.trx) · [CRM HTTP 和原生写集](D:/CP6-test-artifacts/W664-01/verified-owner-first/coordination/crm-owner-committed.json) · [等待](D:/CP6-test-artifacts/W664-01/verified-owner-first/coordination/owner-wait.json) |
| 两次真实 COMMIT 应答丢失 | 第一 CRM 库 native COMMIT 后丢应答，A 最低版本 5、B 仍为 0，Main 原写集不变且仍保存同一已开始的父子操作。随后真实恢复请求等待 B 根，B 成功后 Main 的最终 COMMIT 丢应答，Main API 当次返回 503。再送同一原 PUT 取得已提交的相同响应与原唯一 Main 写集。A 的原 `advance_role_v1` 等版本确认允许技术根修订再加 1，C02 角色代际只推进一次。 [TRX](D:/CP6-test-artifacts/W664-01/native-ack/main.trx) · [实际应答和等锁](D:/CP6-test-artifacts/W664-01/native-ack/coordination/menu-ack.json) · [最终状态](D:/CP6-test-artifacts/W664-01/native-ack/coordination/menu-committed.json) |
| 相关回归 | W532 原有等待后撤权、菜单树以及两租户同角色 C02 授权回归 11 通过，0 跳过。 [TRX](D:/CP6-test-artifacts/W664-01/scoped-regression/main.trx) |

保留早期夹具失败。PowerShell 未设置顺序时输出空值，导致两个扩展场景在建立测试前解析失败。修正后 ACK 夹具的第二库观测连接复用了打开后已去密码的字符串，换为原始隔离 builder 后真实双应答用例通过。CRM 业务先提交的冻结用例先依据旧整族退出断言 401，随后任务测试首次尝试断言内部错误名。冻结 CRM 的 `ProblemCode` 按旧合同把 403 转成公开 `crm.permission_denied`。最终 test-only overlay 精确断言公开合同并采集状态，实际运行的 9 个 CRM/Platform 产品 DLL 与冻结 W649 文件逐一匹配。显示排序首次夹具遗漏原目录修订触发器，最终验证明确要求其修订并比较其余范围。 [空顺序 PUT](D:/CP6-test-artifacts/W664-01/final-put/main.trx) · [空顺序 ACK](D:/CP6-test-artifacts/W664-01/final-ack/main.trx) · [连接夹具失败](D:/CP6-test-artifacts/W664-01/verified-ack/main.trx) · [原 401 断言](D:/CP6-test-artifacts/W664-01/order-owner-first/crm.trx) · [中间 403 名称断言](D:/CP6-test-artifacts/W664-01/native-owner-first/crm.trx) · [原目录修订断言](D:/CP6-test-artifacts/W664-01/verified-display/main.trx) · [任务测试 overlay](D:/CP6-test-artifacts/W664-01/Pgt017HumanMenuRoleOrder.cs) · [实际冻结产品哈希](D:/CP6-test-artifacts/W664-01/crm-product-sha256.json) · [执行入口](D:/CP6-test-artifacts/W664-01/run-menu.ps1)

## 发布检查和分支交接

本地候选 `d256b373577bab5ac1d0edad407b4933e124c875` 相对已采用 Main 仅七个任务文件。完整 diff 检查通过。最终定向测试 19 通过，0 跳过。各顺序及故障测试使用同一组 Main 产品 DLL，发布时逐个比对 `CP6.WebApi.dll`、`CP6.Core.dll`、`CP6.Persistence.PostgreSql.dll` 和七份实测源文件。隔离 PostgreSQL 57276 `DatabaseInit` 成功，原生共同根、父表、子列及触发器存在。实际 Kestrel 57277 `GET /health/live` 返回 200，匿名 `PUT /api/menu` 返回 401。310 个发布文件清单 SHA-256 为 `AEFCE2439C8B7C87D7E46A2B0D2C12F0869EB2414F468863FC260205A93D7CA0`。 [源码哈希](D:/CP6-test-artifacts/W664-01/tested-source-file-sha256.json) · [发布与启动结果](D:/CP6-test-artifacts/W664-01/main-publish-smoke/result.json) · [发布文件清单](D:/CP6-test-artifacts/W664-01/main-publish-smoke/publish-sha256.json)

本地 `codex/w664-01` 保留候选，DEV-2 工作树干净。57260–57279 无监听，任务 PostgreSQL 和宿主退出，临时签名桥接文件已移除。席位仍登记给 W664-01，由协调方集成后释放。本次验证仅在本地，Actions、生产和远端引用均无操作。 [停机及工作树证明](D:/CP6-test-artifacts/W664-01/process-verification.json)
