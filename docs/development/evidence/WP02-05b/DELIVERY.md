# WP02-05b：身份来源批准与完整后继发布

结论：**卡 DONE**。父卡 WP02-05 的已验证部分保持，余卡两项 scope/正反 acceptance 全部核销；原卡达到完整 DONE。本文为本卡唯一交付报告，包含实现、消费接缝、实际验证及预算。CRM 实际采用仍归 WP02-06。

开工基线 Main `674a7ed23acbb25061ecec0c401595e69a645c62`，产品/测试源码 `effeac4f80c6576f82d92343b18e49ba335b9f8b`；固定 DEV-1、`codex/wp02-05b`。严格前置 WP02-05 原号及其原前置已核对 DONE；当前余卡不以自己未完成阻塞自己。基线包含目标核对 PR1246；本卡未改任何原 AC、卡定义、依赖、覆盖集合或375张原卡范围。

## 本卡剩余出口与结果

按 [任务定义](../../task-definitions.json) 与 [父卡拆分报告](../WP02-05/card-closure/DELIVERY.md)：

| 原出口/验收 | 本次实际结果 |
|---|---|
| 经批准的 tenant/slug/issuer/region/client 映射变更；准确原件及前后来源/版本 | 原生当前平台管理员与会话/CSRF 门批准；相同 TenantId 保持历史业务归属。前版 revision/hash 必须精确命中，保存完整原输入、批准人/租户、批准引用和时间窗；新来源与原结果同事务提交 |
| Main 完整后继目录、水位、发件原结果和旧身份处置 | 冻结所有现存 tenant/user/department/permission/service-principal/撤销快照及历史 tombstone，校验原 hash/schema 和必需原生对象集合；保存每项原来源、版本、原 UTF-8 字节，完整数量、水位、这次原 Outbox 消息及旧 browser family |
| 正向：合法切换、原键重传和可追溯旧事实 | 实际 Main 登录/批准/新配置重启、服务令牌签发、旧 cookie CRM 授权拒绝与新登录授权通过；准确版本取回与原命令逐字节一致，后续目录改变不改原件；尾部连字符 slug 按原配置合同可加载 |
| 反向：错源/漏项/未知版本/同版异内容/旧版冒充 | 未批准 issuer/region/client 被拒绝；错 revision/hash、缺快照、非法/冲突客户端、过期批准均无效果；未知版本404，不回退 latest；同操作异内容409；原件删改和无原件推进来源被数据库拒绝 |
| 完整处置前不能复活旧身份 | 旧服务 token/browser grant 撤销与完整发布同事务；旧 browser family 永久退出 CRM 新授权，保留 Main 原生控制会话供当前权限下原件恢复。新宿主不能把旧 family 重新换成 CRM grant，旧 issuer 撤销事件按已批准历史原来源验证 |

映射原 AC：**AC-ID01-01/04/05/06、AC-ID05-01/02/03** 的 Main 来源责任；**SYS-TENANT-01::AC-TN04-04** 的 Main 批准后继及旧身份事实；**PLT-CTX-01::CTX02-A5** 的稳定租户 GUID、批准 slug/来源变更与保留物理数据责任。本卡仅核销其 Main 部分；不把它标成 CRM 三方运行、Inbox 冲突隔离、Ready 或最终业务授权全部通过。原暂停/恢复、完整分页与原写者证据继续精确复用父卡链接；没有改写历史执行结论。

## 已交付入口与准确取版

1. 原生当前平台管理员通过 `GET /api/platform/tenant/{id}/identity-source-v1` 取得准确 revision/hash/binding；历史完整目录尚未登记时明确返回 `unregistered`，不按启动配置自动继承历史身份。
2. `POST .../commands` 使用 `IdentitySourceCommandV1`：不透明 OperationRef、ExpectedRevision/ExpectedSourceSha256、完整 Next、OwnerApprovalRef、EffectiveAfterUtc/ApprovalValidUntilUtc。v1 明确 `CompatibilityWindowSeconds=0`、`SessionDisposition=REAUTHENTICATE_ALL`（CRM身份）、`RegionDataDisposition=KEEP_EXISTING_STORAGE`。不自动更新部署配置或迁移物理数据。issuer 为既有宿主级配置；每个涉及的租户都须独立批准，不能以 A 的批准认领 B。
3. 返回 `identity-source-publication-v1` 原字节，包括 Previous/Current 完整来源、两端摘要、准确版本、Boundary/ExpectedCount、Items 原 payload/base64、Outbox 原消息及 RetiredBrowserFamilies。普通展示名改变不是来源迁移。原 C02 事件 schema/bundle 不变，历史撤销保留旧 issuer 与消息字节，不伪造为新来源。
4. 实际服务 reader 使用 `GET /internal/crm/identity/source/v1/publications/{revision}` 读取指定版本。租户只取自实际服务 token/可信目录映射；当前 reader 权和新来源登记必须通过；禁止 tenant 查询覆盖。普通 PostgreSQL 登录只能调用自己租户的 `read_hash/read_publication`，不能直接读来源表或用外租户参数。
5. 原操作者用 `POST .../commands/read` 按 OperationRef 恢复原件；当前会话/当前平台管理员资格仍必需。提交响应异常返回 OUTCOME_UNKNOWN，沿原键读取，不换新键推断无效果。本卡实际执行了回滚与已提交原件恢复，未宣称新注入 wire-level COMMIT 丢包。

历史库先执行前向 Core 迁移 `20261117110000_IdentitySourcePublicationV1`；测试从此前 FinancePolicyOriginalV1 实际升级，旧快照原字节/摘要/版本不变。已有完整 Bootstrap 的旧来源必须由当前管理员明确批准登记；已有身份事实但 Bootstrap 不完整时拒绝自动认领，缺完整目录不能发布成功。真正全新租户在第一条 C02 事实前可登记，事务回滚同时取消登记。原独立 service-token/revocation 布局保持其冻结合同，不能在未做 Core 迁移的布局冒称新来源发布。

## PostgreSQL 与相关验证

环境：Windows，.NET SDK8.0.424，PostgreSQL18.6，独占可丢弃模拟业务库；本轮各 `permission-consumption-*` 根和端口55617/55435，service-token 使用 `tenant-connections-service-token`、55436。真实 Main 子进程、真实 Kestrel/JWT/SQL；账号、业务、RSA key、服务秘密、Owner 批准均为明确模拟。服务目录 HTTP 组合宿主只用显式测试公钥替代不可访问的 `.example` 网络 JWKS；没有改产品认证规则，不宣称网络 JWKS、TLS、Dapr broker 或 CRM 消费运行通过。

产品最终构建 `build-final.log`：0错误，172条编译/依赖告警保留；随后仅调整测试夹具的 `build-fixture-final.log` 构建0错误、143条告警。构建命令：

```powershell
dotnet build CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-test-artifacts/WP02-05b/build -m:1 -p:UseSharedCompilation=false
```

| 运行/TRX（根 `D:/CP6-test-artifacts/WP02-05b`） | 实际结果 | 适用范围 |
|---|---|---|
| `final-core/final-core.trx` | 127 / 127，零跳过 | 初次核心与受影响回归，后续改变的来源场景由 final-main 替代 |
| `configured-directory-final/configured-directory-final.trx` | 8 / 8，零跳过 | 最终实际配置目录宿主及 A/B/A 矩阵 |
| `service-token/service-token.trx` | 8 / 8，零跳过 | 独立 service-token PostgreSQL 配置兼容 |
| `final-main/final-main.trx` | 238 / 238，零跳过 | 最终来源正反/前向迁移/原字节/并发/回滚/原生 IAM HTTP 与受影响写者 |
| `catalog/catalog.trx` | 2 / 2，零跳过 | 完整迁移目录指纹、未知或改写函数/公开权限拒绝 |

合计 **343 项唯一相关用例通过、零跳过**。相同用例去重；改变后的行为以最终运行替代初次结果，其他代码/依赖/输入未变的结果复用，不因提交SHA变化重复测试。

各组使用 `dotnet vstest build/bin/CP6.Tests/debug/CP6.Tests.dll --TestCaseFilter:... --logger:trx;LogFileName=...`；`CP6_PERMISSION_PG_BIN=C:/Program Files/PostgreSQL/18/bin`、ROOT 为新的独占目录、REQUIRED=1。准确过滤器为：final-core：来源/目录/写者及 CrmIdentityConfigurationTests/CrmOidcServiceTokenTests；final-main：`final-main-filter.txt` 精确列出的131个方法/238项 Main 场景，包含来源/迁移/写者/目录与受影响原生IAM；configured-directory-final：IdentityDirectoryConfiguredHttpPostgresTests/IdentityDirectoryConfiguredHttpMatrixTests；service-token：ServiceTokenPostgresTests；catalog：IdentityDirectoryBusinessFunctionsPostgresTests。完整实际测试名称/时间/结果在各 TRX，owned 根保留停止记录和故障历史。

初始红测与失败均保留：`red-executed` 三项配置改源被错误接受；`source-guard` 记录批准入口尚未实现；`boundaries/publication` 保留测试TLS代理、非法测试correlation及旧catalog pin问题；`review-red-current` 复现非法客户端被接受/合法尾部连字符被拒；`enrollment-red` 复现旧快照被 Bootstrap 认领。`review-final` 暴露模拟JWKS不可达及旧IAM夹具缺完整登记；`enrollment-red` 同时暴露单独运行缺备用管理员的夹具依赖，均按真实原因修正。首次 `review-red` 在构建完成前启动、只跑到旧8项，明确排除其证据，不冒充新行为红测。扩大回归 `final-acceptance` 保留204通过/37失败/32跳过：通用IAM夹具误固定来源造成34项不适用/失败，已撤回该改动并按实际Main宿主配置显式准备；另3项需要独占CRM项目的环境失败及32项未运行场景，不计本卡Main验证通过。最终Main选择238项逐一复测，CRM实际采用仍在WP02-06完整保留。没有将失败、未运行或环境准备错误改写为通过。

## 集中审查与专项边界

单对话一次任务级 SPEC/QUALITY 集中审查及上述发现的定向复查。来源登记只认无任何历史事实的新租户；批准在原 AuthenticationCompletionGate、当前会话/actor 和 PostgreSQL 来源行锁下完成，批准有效期在最终决定点复核。两个批准并发一胜一冲突；封存失败回滚 token撤销/快照/水位/来源；普通登录外租户拒绝且无来源表直接权限；新source writer数据库触发器阻断旧宿主继续发身份事实，完整目录catalog pin更新后仍拒绝未知函数/改写/公开EXECUTE。旧会话/新授权、当前权限撤销及CSRF已实际验证。

TenantId、Owner和历史业务归属不变；金额/数量未改，身份 Boundary/revision 仍作为精确字符串，原 payload/base64 不经有损数值转换。迁移为独立新版本、仅前向、仅在本地独占库执行；本卡没有操作已存在业务环境、生产数据库或部署。冻结pub的来源与每项原来源分开，消费者不能把旧撤销的issuer改写为新issuer。

CRM 按 WP02-06 实际采用本准确版本/目录与旧身份处置，仍须执行其原内容门、重建、旧会话不复活及最终授权场景；该卡 DONE PR 再核对 SYS-TENANT-01 全23条原AC后关闭。PLT-CTX-01 此 Main 缺口解决后只剩 WP02-29 和 WP02-24；整目标及 INT-ID-01 均不在本卡提前关闭。

## PR 与工作区

候选及受保护 origin/main 工作流树均为 `3355235636fd45116f5445fca32fc65e49f13003`，沿用已核对的普通手动入口及版本tag生产入口；本分支非tag，无workflow/依赖配置改动，任务Actions列表为空，启动/重跑/取消均0。无新生产发布。

本卡在同一分支/PR提交产品、这份报告及DONE台账。台账的准入远端SHA是以上已核实基线，不虚构尚未产生的merge SHA；新源码的实际远端采用由本PR merged状态和官方 `D:/CP6-test-artifacts/WP02-05b/finish.json` 的 proof.commit 核销。只有核对新源码为远端main祖先后才对外宣布完成。按官方finish定向清理本任务本地/远端分支、停止自有进程，固定DEV-1干净detached后释放；根工作区原有用户文件保留。

## 本卡核销、预算与下一卡

本卡 [PR #1247](https://github.com/GTX537/CP6/pull/1247)，本窗关闭1张余卡，新增拆卡0。原号 DONE / 完整 DONE = **47 / 44**；登记卡48/379、目标2/125（含1个TEST_ONLY支撑）。

目标核对已按官方finish `2026-11-17T14:03:03.512026+00:00` 补齐总有效 **1726.921秒（0.479700h）**，低于3h，从125h目标池扣除，剩余 **124.520300h**。本卡从该finish至 `2026-11-17T16:22:25.185112+00:00` 连续有效 **8361.673秒（2.322687h）**，含开工准备59.597秒；上限14276.798秒（3.965777h），未超限，未触发二次拆分。原336卡上限总和2,052h保持，缓冲消费0h，剩326.55h。计划累计 **4.377623h**，未到24h检查点；本候选之后的提交/合并/官方finish差额在下一卡同一交付PR接续计入本卡，不计用户等待。

严格前置完整展开后：WP02-13b与WP02-06下游最长链均43条边，按原序列先 **WP02-13b**；WP02-14b为41，WP02-16b为36。只登记下一卡，不在本卡实现其业务。原号预算及历史拆分记录不改，完整DONE由原号与全部余卡计算。

本次台账与生成页一致性检查 `manage_roadmap.py --write --report D:/CP6-test-artifacts/WP02-05b/ledger-final.json` 通过、错误0；工具代码未变，复用 PR1246 的116项唯一工具测试。此检查仅验证台账结构和来源追溯，不充当业务测试。最终集中审查同时核对原AC、修订场景、覆盖集合和严格依赖保持不变。
