# W27-03 / #339：SSO 首次身份映射与原操作恢复

2026-10-19，COORD 单聊天。基线 `cac31d82117885dd3a265159b3d30eb698d9faf9`；原工作目录，分支 `codex/wave27-03-sso-identity-admission-20261019`。任务级作者 SPEC/QUALITY 完成，局部复查无剩余阻断，Ready to merge: Yes。#339 待正常合并及远端回读后关闭；完整 WP02-03 和真实业务验收未提升。

## 行为与合同

PostgreSQL 原 ExternalProvider/Subject 使用历史不区分大小写 bpchar 域，原查询会把大小写或尾空格不同的 sub 认成同一账号，已真实复现并登记 P0 #339。新精确分支以原始 UTF-8 字节匹配 tenant/issuer/sub，检查唯一性；已有精确绑定不依赖新准入政策，也不跟随新邮箱改绑。签名/iss/aud/有效期/nonce 与一次性 state 仍先验证，重复关键 claim 拒绝。

新身份必须使用 W27-02 的明确批准：当前 Authority、精确 issuer、client、EmailClaim、verified-email 语义和显式 email_verified=true。唯一同租户邮箱仅在允许初绑、原 subject 为空、原 provider 为空或完全相同且用户/租户当前可用时绑定，保留原角色、平台标志和认证整改要求。多邮箱、另一绑定或残缺冲突拒绝，不转 JIT 或本地密码。零邮箱匹配的 JIT 必须 AutoProvision、allowJit、当前同租户启用且批准的默认角色和用户名业务唯一性；服务端赋身份/角色，平台管理员与 fallback 默认 false，随机高熵占位密码，MustChangePassword=false，不默认 Role1。

只沿既有 Main 身份权威、认证根和配置，不另建身份 Owner。首次写要求实际 CP6Context 已采用该租户的 CrmIdentityRuntime；没有则 SSO_IDENTITY_WRITER_UNAVAILABLE，不伪造一个独立 Outbox。新实体先保存不可变原操作 intent，再重新取实际 writer 共用根，复核原 state/客户端绑定、配置政策、当前用户/角色及防 ABA revision。身份写、Main IdentityChange/version/Outbox、强制安全审计和 IDENTITY_BOUND/USER_PROVISIONED 回执在同一 PostgreSQL 事务提交。初绑推进既有 AuthenticationEpoch，使不包含联邦字段的身份投影也产生新版本并失效旧认证；不改投影合同。提交前再核时效与租户资格，避免在所需写入期间到期仍提交。

映射提交不等于 ACTIVE；MFA、整改或认证输出失败不会撤销已提交绑定。新 SSO 对准确绑定直接复用，不重复建号。提交确认丢失只回查原 receipt；相同 tenant/精确 issuer/sub 存在未确认 intent 时，新 state 也不能绕过。确定回滚后才以强审计持久关闭原操作；关闭失败或提交确认未知保持 UNKNOWN。没有重放已消费 code/state、删除重建或超时猜测成功。

浏览器沿 W27-01 原秘密能力查询/关闭，同时返回不含账号候选或凭据的 identityState/operationRef；关闭可阻止尚未到达的映射，已提交只保留并报告真实结果。多原操作不会任取一个宣称成功。支持入口 `GET /api/sys/sso-config/identity-operations/{ref}` 与 `POST .../close` 在根内复核当前原会话、租户和 sys-sso-config:edit，关闭保留 CSRF。实际 Vue 管理页只允许关闭刚查询为 UNKNOWN 的同一编号；失败保留原编号。Web/Native 回调保留安全编号，Native SDK 仅显示支持线索，不据深链接消费 PKCE、交换 grant、自动关闭或激活会话。

## PostgreSQL 升级与并发

前向私有迁移 `20261019155500_SsoIdentityMappingV1`：原始联邦字节键由 Sys_Users 实际 writer 事务维护，唯一约束落在物化 bytea 键，未把 STABLE bpcharsend 伪标为 IMMUTABLE；升级先回填，已有精确重复绑定会使迁移整体失败，账号不被改写。大小写/尾空格不同的旧键可独立保留。新的或改名的用户名遵 Main 既有租户/大小写域唯一性；原有重复用户名不被迁移删除，也不阻止与名字无关的修改。邮箱不加全局唯一约束，多邮箱继续按合同作为映射冲突处理。

user、membership、role、action、data-scope、field-permission、role-menu、menu 的实际 DML 共用原根并推进引用 revision，防止准备后改动又恢复相同值。用户/角色删除重建仍改变 revision。不可变 intent/receipt 保留关闭屏障，不随意 TTL 删除。原合法存储范围维持 issuer 300、sub 200、email/username 100 个 .NET 字符；越界拒绝，不截断或归一化，不修改旧数据库的精度/Owner。

## 验证与集中审查

原始日志、JSON、TRX 和浏览器截图在 `D:/CP6-local-test/wave27/foundation`。实际 PostgreSQL18、自有 PG55435 与迁移；OIDC 为受控密钥真实签名/验签与实际服务/控制器，支持恢复使用真实 Program/HTTP。浏览器为本机 Chrome 的实际 Vue/Element 页面和受控 HTTP，不冒真实企业 IdP/DEV。

| 执行证据 | 真实结果与范围 |
|---|---|
| sso-identity-exact-red-20261019T154358Z | 2 个真实失败：不同大小写/尾空格 sub 被映射到原账号 |
| sso-identity-exact-green-20261019T154929Z | 12 通过：原失败及相关 Web/Native SSO 合同 |
| sso-identity-mapping-compiled-20261019T160609Z | 23 通过：初绑/JIT、角色/平台保留、Main版本/Outbox/审计/回执、冲突拒绝、旧认证合同 |
| sso-identity-http-and-rollback-20261019T161321Z | 11 通过：实际 HTTP 当前租户/权限/会话/CSRF，强审计/Outbox 故障全回滚，普通 SQL 并发唯一性，以及受影响原浏览器恢复 |
| sso-identity-adoption-upgrade-20261019T161738Z | 4 通过：旧填充 PG 前向升级、重复键拒绝不改账号、首次绑定后实际 Web 控制器仍进入 MFA |
| sso-identity-final-review-20261019T162138Z | 8 通过：最终 EF 强审计关闭、提交回执丢失的原操作恢复、用户/角色 ABA/关闭竞争、审计与Outbox回滚、实际 HTTP |
| sso-identity-expiry-review-20261019T162253Z | 5 通过：身份写入期间租户到期回滚、最终提交前检查及原初绑/JIT/Web MFA |
| sso-identity-concurrent-callback-20261019T162410Z | 2 通过：不同已验签 sub 竞争同一邮箱仅一个绑定，同 sub 新原操作不能绕过未确认 intent |
| sso-identity-native-20261019T162027Z | 9 通过：Windows/Android 支持编号及原 PKCE/回调合同，无设备运行验收 |
| sso-identity-components / components-fixed | 26 个有效组件/协调器测试通过；前者 23 通过且配置页 mock 失败，补齐隔离 mock 后后者原 3 项通过 |
| sso-identity-typecheck-fixed | vue-tsc --build 退出 0 |
| sso-identity-browser-ready | 1 个真实 Chrome 管理恢复场景通过，已查看 identity-recovery.png；成功账号保留，仅同一未知操作可关闭 |

保留全部失败：最初的表达式索引被 PostgreSQL 42P17 拒绝，改为实际 writer 物化键；首次构建缺测试 using；关闭审计原 SQL 参数的时间类型失败，集中审查确认 StampTenant 保留显式非空 TenantId，最终改为既有 EF 强审计路径。回滚测试曾错误查询所有夹具租户的投影，限定当前 tenant 后通过；前端 mock 和测试参数类型问题已修正。初次浏览器冷启动在页面尚未挂载时超时，实际诊断确认组件及可访问输入后局部场景通过。未把失败改写为成功或相加为用例目标。

一次任务级作者 SPEC/QUALITY 覆盖五分支、可信邮件及准确协议键、当前角色/租户/权限、实际 DML/升级/锁序、原操作 UNKNOWN、Main 同事务/回滚、后续 MFA、前端身份和原生回调边界。审查发现的时效窗口与审计适配只作局部修正/复查；未新增代理或全仓回归。有效未变证据直接复用，提交/SHA改变不重跑。既有 NU190x/静态告警保留，未宣称漏洞扫描通过。

## 边界和资源

真实 tenant 的邮箱权威、issuer、角色批准仍未代为填值；应用采用须安装前向迁移并配置原 Main IdentityChange/Outbox。新策略无批准就不执行初绑/JIT。真实 IdP、DEV/UAT/PROD、原生设备与业务验收 NOT_RUN；完整组织/引用 CAS、CRM 消费和正式发布仍属后续，不提升完整 IAM 或 WP02-03。

复用原唯一物理目录、node_modules junction、wave18/.NET/NuGet缓存；Native Tests 仅复用缓存 restore，没有依赖版本或项目配置改变。全部本窗隔离 PG 已停止，实际 HTTP 由夹具释放；Web58427 PID15964 已核归属停止。保留失败与证据，没有清理用户材料、创建工作树/代理、运行或取消 Actions、修改触发配置或部署。14包/125目标/375任务/247运行NOT_RUN不变。正常集成后继续总台账/Handoff/资源与检查点收尾，goal 此时仍 active。
