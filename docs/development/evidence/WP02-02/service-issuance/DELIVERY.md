# WP02-02：真实 service issuance 租户事务有界采用

日期：2026-10-17。分支 `codex/wp02-02-service-issuance-20261016`。产品执行基线 `f7f160f350667d1bb7ba0b887341fe0a12e8310b`；后快进至 `20662e3db55db81bcc4a36d49bad1f4ecb8ce12d`，期间仅8份协调文档改变，没有因文档SHA重跑产品验证。COORD批准补充 `CrmOidcCrypto.cs` 的共享签名器窗口。本交付不关闭整个WP02-02、全Main或真实业务验收。

## 实现与合同

- 实际 `POST /connect/token` client_credentials 路径把原Basic交给专用singleton host再验证，冻结注册形成无自然人Owner的Service actor。签发使用独立purpose，核对client Enabled、cp6.services scope、CrmEnabled organization。自撤销使用独立AllowSelfRevoke，停用签发的client仍可用当前正确凭据撤销自己的记录。
- 新 `CrmIdentity:ServiceTokenTenancy` 显式选择 `service-token-v1` / `service-token-tenant-rls-v1`。同时启用旧ServiceRevocationTenancy或提供未知版本字段会拒绝。新链路不调用默认directory/RecordAsync连接，也不因布局、映射、权限或身份错误fallback。DefaultConnection endpoint/database仍作配置一致性检查，但该身份没有从新安装器获得跨租户写权限。
- 普通tenant login的一个物理PG事务完成真实目录读取、真实ServiceToken记录与最终核验。沿用真实Record写入内核、受管CP6Context/EF guard、TenantStorageTransaction；没有复制实体、额外业务连接、嵌套事务或自动重试。签名成功且确认commit后才返回token。最终检查tenant、物理库/安装/seal、Basic/actor/registration/source/配置期限及token exp；提交返回后已到exp也不返回成功。
- `Sys_Tenants` 保持共享权威表，不增加全局RLS。无tenant参数的固定SECURITY DEFINER helper经session_user和既有可信mapping定位唯一Id，只返回Enable/ExpireDate，并FOR SHARE持锁至commit。runtime无直接目录SELECT/UPDATE/枚举权限；helper由安装control owner拥有，固定search_path、全限定引用、无动态SQL，runtime只有EXECUTE。
- 停用UPDATE先持锁时，签发等待并读取更新后状态；签发先持锁时，停用等待其提交。取得锁后及最终fence重新取应用时钟。ExpireDate仍是原服务器本地wall-time / timestamp without time zone，null不限期、相等即到期；不改为UTC字段，也不改用tenant TimeZoneId。
- 新profile在**DI注册时**通过私有字段与防御性复制捕获issuer、active kid和key ring。新host与共享CrmOidcCrypto首次解析都使用该快照；原options/Keys对象和集合后续变化不能影响签名。未启用新profile时可选快照为空，原Crypto构造与Sign/Validate/Jwks行为保持。没有改Program或依赖配置。
- 保留原300秒、CP6.Services audience、at+jwt、service sub/client_id/tenant_id/jti/scope与错误分类。签发没有请求幂等键：commit后丢响应留下真实记录，重试签发新jti，不能称为恢复同token；自撤销继续原持久幂等结果及Snapshot/generation/priority Outbox原子性。
- 配置/密钥/凭据更新仍要求所有实例停止接入、排空、安静控制窗口更新、重启；不承诺热更新、跨实例即时撤权或任意并发管理员DDL fencing。

## 安装与权限边界

新CLI `install-service-token-v1 --stdin` 采用原显式安装字段与Tenants，但不接受IssuerLogin。只在新空隔离库安装，记录 `cp6_service_token_v1.installation`。复用安装/恢复骨架，旧service-revocation-v1、erp-identity-messaging-v1、旧seal SQL常量与恢复语义不变。

五表从冻结PG baseline operations精确选择：原撤销四表加真实 `public.Sys_Tenants` 的全部13列、PK、租户编码唯一索引及6项容量约束。没有实体/EF模型/历史迁移修改或虚假的完整EF history。安装不根据mapping创建/启用租户；测试由显式control在真实Sys_Tenants写入合成A/B权威行，不冒充生产目录或租户创建生命周期验收。

runtime新增受TenantId RLS约束的ServiceToken INSERT，其余保持原撤销最小权限。Sys_Tenants显式owner-only ACL，避免NULL ACL与授权撤回后的owner ACL表示差异，并未授予runtime目录读权。新profile初装核对普通角色资格、成员关系、所有权、role settings、目标库预带表/列/函数/schema/default ACL。其他自有component数据库的普通权限不是本目标库权限；目标库与全局角色资格继续受控。新seal在旧结构类别之外记录pg_catalog / pg_*表、列、函数、schema、type的PUBLIC及runtime有效ACL项，按权限和grant option规范化，忽略grantor和NULL/显式owner ACL表示差异。初装以pg_init_privs的initdb记录或acldefault为原生权限基线，拒绝额外PUBLIC权限；合法默认SELECT/EXECUTE/USAGE保留，恢复时有效权限增加或减少均拒绝。information_schema不在此新seal补充范围。旧seal常量原样保留。未知布局、profile/物理身份/目录/权限漂移拒绝。没有旧库overlay、既有数据迁移、增租户、恢复到新物理库或全Main启用。

## 实际验证及失败记录

使用.NET8、PG18.6、私有NuGet cache；专属loopback 55436，每次PG测试全新tenant-connections根。夹具核对端口/ownership/postmaster，结束后停止并保留数据。Kestrel自签HTTPS，client仅信任本次证书。真实controller、DI、Basic、authority、签名、目录、store、安装及撤销运行；新profile无FixtureActiveTenants。仅隔离无关后台服务及非服务端点依赖。

原始证据根均为 `D:/CP6-local-test/WP02-02/`，未复制日志/哈希清单到仓库。

| 证据 | 实际结果与适用范围 |
| --- | --- |
| `service-issuance-build-1.log` | 相关项目构建通过，原包漏洞/analyzer警告保留，无依赖改变 |
| `service-issuance-results/service-issuance-1.trx`、`service-issuance-test-1.log` | 94项配置/既有service-token相关通过；8项新PG因同一初装拒绝失败。误用了独立issuer“不得有其他DB权限”的验证器；改为新profile目标库ACL检查，不放宽本库隔离 |
| `service-issuance-test-2.log` | 插值条件缺括号造成编译失败，修复后继续；该次无PG启动、无TRX |
| `service-issuance-results/service-issuance-3.trx`、`service-issuance-test-3.log` | 1通过、7失败。首个漂移测试撤回临时SELECT后把Sys_Tenants原NULL ACL变成显式owner ACL，seal正确拒绝后续流量；后续场景不算通过。新profile初装现显式owner-only ACL |
| `service-issuance-results/service-issuance-4.trx`、`service-issuance-test-4.log` | 9项中7通过（6个PG场景和1冻结配置）；2失败：首次请求前改原密钥暴露了共享legacy lazy signer仍读取可变输入的**真实冻结缺口**；另一项是夹具finally对已commit事务再次Rollback |
| `service-issuance-results/service-issuance-5.trx`、`service-issuance-test-5.log` | 仅复测2项，通过。并发夹具改用Dispose收尾；HTTP当时先warmup再改options，虽验签/iss/kid/记录exp一致，但只证明已解析后的冻结，**未关闭首次解析缺口**，后由COORD核实并批准真实修复 |
| `service-issuance-results/service-issuance-6.trx`、`service-issuance-test-6.log` | 最终快照修复后14/14通过，0 fail/skip：首次HTTP前直接修改原issuer、active kid、key对象并清空Keys集合，仍由注册时快照签名；返回JWKS实际验签、JWT与记录exp一致；冻结资格测试及12项既有无新profile的Crypto/key-rotation构造行为通过。此证据关闭首次解析缺口 |
| `service-issuance-cli-build.log`、`service-issuance-cli-replay.log` | CLI构建通过；第5轮新profile同请求private stdin恢复成功并拒绝旧IssuerLogin输入；同一新CLI对上轮保留旧profile原请求恢复成功，原baseline SHA256 `04A077A0AB15863C63801F9FE8C100C18237708B534C2C32EF771EF020729B26` 与原seal兼容 |

每轮root在 `service-issuance-run-N-root.txt`。第4→5轮产品代码未变，只有两处测试调整，因此其余7项按当时真实范围复用。第1轮未受影响的旧行为成功证据复用；最终签名快照修复仅影响WebApi构造与DI，已用第6轮定向验证；Core/安装/并发代码没有变化，第4轮其余成功及第5轮并发成功按原范围复用，不把warmup结果扩大为首次解析证明。

CLI辅助脚本首次使用Start-Process -Wait等待了整个postgres进程树；核对本次ownership/postmaster后停止该自有实例，保留 `service-issuance-cli-replay-attempt1.log`。改为仅WaitForExit(pg_ctl)后完成两profile恢复。PG日志在对应cluster的 `issuance-cli-postgres.log`；密码仅从私有文件读入内存并通过private stdin/psql子进程环境提供，无凭据参数/日志。旧cluster仅同请求恢复，没有修改安装结构或业务数据。

限定复用上轮[撤销报告](../service-revocation/DELIVERY.md)内未变的池、共享事务guard、目录锁及撤销故障/事件证据。真实HTTP新profile还复用旧断言代码实际执行A/B、跨client/tenant、repeat/concurrent revoke、disabled client及普通登录权限；未把调用的子断言数量当交付目标。

复现环境：SDK `C:/Users/tt/.dotnet/dotnet.exe`；`NUGET_PACKAGES=D:/CP6-local-test/WP02-02/adopt-a1/n`；`CP6_TENANCY_PG_BIN=C:/Program Files/PostgreSQL/18/bin`；每次独立新的 `CP6_TENANCY_PG_ROOT=D:/CP6-local-test/WP02-02/tenant-connections-<GUID>`。

```powershell
& $dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter 'FullyQualifiedName~ServiceTokenPostgresTests|FullyQualifiedName~ServiceTokenConfigurationTests' --logger 'trx;LogFileName=service-issuance.trx' --results-directory $evidence -v:q
& $dotnet build tools/CP6.TenantStorage/CP6.TenantStorage.csproj --no-restore -v:q
# Only with private redirected stdin from an approved caller; no secret arguments:
& $dotnet tools/CP6.TenantStorage/bin/Debug/net8.0/CP6.TenantStorage.dll install-service-token-v1 --stdin
```

实际分轮过滤器见TRX对应stdout和上述范围。CLI实际重放脚本 `service-issuance-cli-replay.ps1` 位于私有证据根，使用ProcessStartInfo/redirected stdin/隐藏窗口，读取原canonical request后调用明确版本命令。

## 同次集中审查修正

固定候选 `127abecbebeb5ee2ae67ecc7d7184dc8d9b22bc9` 的集中SPEC/QUALITY审查发现两项P2；先保留真实红测，再在原准入范围修正，没有启动第二次全任务审查或全量回归。

1. 新seal只筛runtime role，漏掉PUBLIC=0，且初装未检查额外系统PUBLIC权限。原候选给pg_authid增加PUBLIC SELECT后，普通tenant的has_table_privilege为true，fresh安装、同请求恢复均被错误接受，seal也未变化。修复仅覆盖pg_catalog / pg_*，按PG原生初始ACL和默认ACL核验fresh额外权限，并将PUBLIC和runtime权限规范化加入新seal；pg_roles默认SELECT、普通函数EXECUTE和schema USAGE继续可用。表/列/函数额外权限，以及移除合法默认权限都能触发恢复拒绝；撤权或还原后seal恢复相等，同一个原请求成功。错误必须是ConfigurationRejected，不将任意异常当安全通过。
2. 共享安装器曾在包含用户输入的完整SQL上Replace旧schema字串，会把合法数据库`cp6_service_revocation_v1`改成`cp6_service_token_v1`。现仅在固定schema位置插值内部profile常量，用户数据库/login继续使用原QuoteIdentifier值。旧profile取原schema字串，所有旧模板的展开SQL及旧seal常量保持；最终CLI再对旧保存库的原请求恢复验证兼容，不重跑旧整套。

| 原始证据（同一私有证据根） | 结果与范围 |
| --- | --- |
| `service-issuance-results/service-issuance-review-public-red.trx`、`service-issuance-review-public-red.log` | 真实1项失败；输出freshRejected=False、sealChanged=False、replayRejected=False、ordinaryTenantRead=true。撤权后SELECT count(*)被42501拒绝，未读取/打印角色或密码字段 |
| `service-issuance-results/service-issuance-review-identifier-red.trx`、`service-issuance-review-identifier-red.log` | 真实1项失败：3D000 database cp6_service_token_v1 does not exist，确证完整SQL替换误改合法输入 |
| `service-issuance-results/service-issuance-review-public-green.trx`、`service-issuance-review-public-green.log`、`service-issuance-public-diagnose.log` | 首次修复尝试仍失败，不能算通过。我主动扩大到information_schema，而PG的这些原生SELECT/USAGE没有pg_init_privs行，误拒clean安装；只读对象/权限诊断后由COORD决定收敛至原声明pg_catalog / pg_*范围，未把现有ACL当可信基线 |
| `service-issuance-results/service-issuance-review-public-green-2.trx`、`service-issuance-review-public-green-2.log` | 1/1通过、0 skip。fresh表和函数额外PUBLIC拒绝；replay表/列/函数额外权限及pg_roles合法默认SELECT移除均拒绝；每次恢复权限后规范化seal相等、同原请求成功 |
| `service-issuance-results/service-issuance-review-identifier-green.trx`、`service-issuance-review-identifier-green.log` | 1/1通过、0 skip。含旧schema子串的数据库和login原名安装、恢复、普通连接与可信tenant绑定均正确 |
| `service-issuance-results/service-issuance-review-http-green.trx`、`service-issuance-review-http-green.log` | 最终新seal下原真实HTTP链1/1通过、0 skip：普通租户真实签发/记录/自撤销、跨租户及client隔离、disabled client撤销、最小权限、首次解析前修改legacy options后的签名/JWKS/记录exp一致 |
| `service-issuance-review-cli-build.log`、`service-issuance-review-cli-replay.log` | 相关CLI构建通过；最终HTTP新root的新profile同请求恢复成功且拒绝IssuerLogin；原旧profile保留库同请求恢复成功、原baseline digest不变，与模板逐处diff核对共同证明旧profile兼容（旧分支仍插入原固定schema，旧seal SQL不变）。新profile恢复证据使用最终新seal库，并非第5轮旧候选库 |

各次新cluster路径保留于对应`service-issuance-review-*-root.txt`，结束核对ownership/postmaster后停止；失败、诊断和成功数据均保留。上述修正影响安装与新seal，旧签名/到期/配置/并发行为仍按前表真实范围复用；最终只补原真实HTTP链的受影响集成验证，不用早期候选库声称新seal恢复成功。

## 剩余范围

Full17PGT、全Main、普通用户/部门登录与snapshot、真实租户创建/维护生命周期、bootstrap/dispatcher、ERP/ordinary outbox、其他上下文/跨仓宿主、真实业务数据验收、既有数据迁移、环境切换、生产候选、DEV/UAT/PROD均 **NOT_RUN**。这是五表fresh-only service endpoints profile，不能据此开启整个Main。

本执行者未触发/重跑Actions、未取消他人运行、未改workflow/分支保护/依赖，未发包、推送、建PR或部署。完成任务分支完整diff自查与 `git diff --check`，未发现范围外文件、凭据、机器配置或调试输出。旧seal常量、实体、EF模型/快照及历史迁移无改动；55436无监听。COORD已完成一次集中SPEC/QUALITY审查，发现的两项P2已按上述审查修正节处理；协调负责修正差异复查、公共状态同步和最终远端集成，结果由实际发生后登记。
