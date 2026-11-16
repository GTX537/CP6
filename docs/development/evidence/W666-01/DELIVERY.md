# W666-01 用户凭据变更与 CRM 写许可

## 范围与原行为

本轮覆盖原 `POST /api/auth/change-password` 与 `POST /api/user/reset-2fa`。验证使用 Windows 上的真实 Main Kestrel、PostgreSQL 18.6、模拟租户与账户、Main 正常登录和签发，以及原 CRM 受理 Owner 的 PostgreSQL 夹具。

领取基线为 `a3a4e3ca179cfc0430e5f65e4b5e7ed400ed12c1`。测试 checkpoint `d0bfc198b40ccf831a42f01b09e2a3e953d77e65` 只加入专属夹具。串行实施合入 W665 #1185 Main `39b52d7b906beb4ab191849af015906bd8827513`，工作分支合并点 `cbe77e49ebaef36b4cfd3d0fa0641553683d4514`。

| 原基线场景 | 实际结果 | 原始输出 |
| --- | --- | --- |
| 自助改密 | 真实专用 CRM 登录拒绝一次，SQLSTATE `28000`。HTTP 500 problem+json，0 条待确认记录，受影响 grant 仍得到新的 `intake:reject` 许可 | [TRX](D:/CP6-test-artifacts/W666-01/cold-password-pg/main.trx) · [事实](D:/CP6-test-artifacts/W666-01/cold-password-pg/coordination/credential-cold.json) |
| 管理员重置 2FA | 同一拒绝与原结果，受影响 grant 继续得到写许可 | [TRX](D:/CP6-test-artifacts/W666-01/cold-reset-pg/main.trx) · [事实](D:/CP6-test-artifacts/W666-01/cold-reset-pg/coordination/credential-cold.json) |
| 合法平台代入目标租户管理员重置 2FA | 同一拒绝与原结果，验证真实代入 claim、有效目标租户管理员和既有 CSRF Cookie | [TRX](D:/CP6-test-artifacts/W666-01/cold-impersonated-reset-cookie/main.trx) · [事实](D:/CP6-test-artifacts/W666-01/cold-impersonated-reset-cookie/coordination/credential-cold.json) |

三次独立读回均确认 Main 凭据状态、原 refresh、活跃 grant、C02 快照与两个 Outbox、成功审计和 CRM 两个最低版本保持原值。原 Main 访问、签名 `intake:list` 与独立主体签名写许可均继续。自助用例包含真实错误旧密码与历史密码复用两次 400。原基线两端在正常连通时的四个顺序均通过，每个顺序实际执行 Main 与 CRM 各一条。早期夹具编译、物理表名和代入后的浏览器 Cookie 保留问题修正前的输出留存，对应目录没有计入原行为。

## 产品改动

- 两个原路由复用 Main 原授权根与 W660 按租户和用户唯一的待确认表，在第一次连接 CRM 前持久接受原操作。只有当前自助用户可改自己的密码，并继续接受原强制改密会话。管理员重置复用原动作权限元数据、当前真实有效主体、允许的代入主体和原完成检查。
- 原件包含受保护的请求指纹、原凭据版本、密码 BCrypt salt 或重置 epoch、原时间与 C02 原前驱和完整后继哈希。原密码、新密码、2FA 密钥和 token 仅留在它们原有运行或存储边界，待确认原件不收录这些值。更换密码的重试仍要提供原凭据和原新输入。
- 连接恢复后先持久记录已开始，再在 Main 根下同一实际 Core 事务中重读原件、处理用户和当前 grant、提交精确 CRM 版本批次、落原操作回执并清除待确认状态。现有签发的待确认门只停止受影响用户的后续写签发。两个路由受控依赖故障返回数值 `503` 与 `AUTHORITY_FENCE_UNAVAILABLE`。
- Main 最后 COMMIT 回执丢失时，已通过授权的同一请求据不可变成功回执恢复既有 HTTP 成功格式。若回执也无法查询，保留 `IAM_OPERATION_UNKNOWN`。原无客户端操作键的接口仍无法区分已结束请求的完整 HTTP 响应丢失与后来一次全新的重置。成功改密的原 Cookie 清除和事后尽力审计、重置的原事务内安全审计均保留。

## 定向验证

业务源码提交为 `a2011cfbaac1a15d7cd9bc88b2c7099cfcec7e94`。九个提交文件与最终测试程序集记录中的源码 blob 逐字节相同。实际通过 19 条，零跳过。 [测试程序集与源码](D:/CP6-test-artifacts/W666-01/final-ack-impersonated-reset/tested-assemblies.json)

| 场景 | 实际结果 | 原始输出 |
| --- | --- | --- |
| 自助改密冷拒绝、重启与两库丢失 COMMIT 确认 | 首次实际 `28000` 后 HTTP `503`，原状态逐项相等，持久原件 1 条。只停受影响新签发。新密码不同的原重试返回 409。CRM 的 COMMIT 确认丢失后，CRM 两个最低版本已为 `[5,1]`，Main 原写集和无回执仍保持。最后 Main UPDATE 在真实原行锁上等待，确认再被丢弃后，原请求回读已提交回执并返回 200。 | [TRX](D:/CP6-test-artifacts/W666-01/final-ack-password/main.trx) · [冷拒绝](D:/CP6-test-artifacts/W666-01/final-ack-password/coordination/credential-cold.json) · [CRM 确认](D:/CP6-test-artifacts/W666-01/final-ack-password/coordination/credential-crm-ack.json) · [恢复](D:/CP6-test-artifacts/W666-01/final-ack-password/coordination/credential-recovery.json) |
| 合法代入管理员重置 2FA 冷拒绝、重启与两库丢失 COMMIT 确认 | 同样真实经历 `28000`、冷待确认、两库独立丢确认和原回执恢复。直接登入的同一有效管理员无法接管原代入请求，实际 403。原代入主体恢复成功，审计保存有效管理员和实际代入者。 | [TRX](D:/CP6-test-artifacts/W666-01/final-ack-impersonated-reset/main.trx) · [冷拒绝](D:/CP6-test-artifacts/W666-01/final-ack-impersonated-reset/coordination/credential-cold.json) · [CRM 确认](D:/CP6-test-artifacts/W666-01/final-ack-impersonated-reset/coordination/credential-crm-ack.json) · [恢复](D:/CP6-test-artifacts/W666-01/final-ack-impersonated-reset/coordination/credential-recovery.json) |
| 自助改密两种提交顺序 | 各调用 Main Kestrel 和实际 CRM 受理 Owner。CRM 先时 Main 连接器实际等待，原受理决定和对应 Outbox 各一次，凭据变化后 CRM 原重试 401。Main 先时，先取得旧签名的 CRM 写在原生决定点 403，原 CRM 行和 Outbox 保持。Main 宿主与 CRM 各 2/2。 | [CRM 先](D:/CP6-test-artifacts/W666-01/final-order-password-owner/result.json) · [Main 先](D:/CP6-test-artifacts/W666-01/final-order-password-seal/result.json) |
| 普通管理员重置 2FA 两种提交顺序 | 同一真实 CRM Writer 与 Main 路由交互，两种顺序已提交影响与拒绝效果如上。Main 先后检查原 scope。Main 与 CRM 各 2/2。 | [CRM 先](D:/CP6-test-artifacts/W666-01/final-order-reset-owner/result.json) · [Main 先](D:/CP6-test-artifacts/W666-01/final-order-reset-seal/result.json) |
| 既有凭据与权限合同 | 强制改密、无 `user:edit` 的用户，原 profile 拒绝，改密成功，旧会话失效，新密码登录通过。原未注册租户管理员自重置及旧动作撤回的实际锁序 2/2。原改密组件检查 6/6。组件回归首跑的两个 native 用例因夹具目录命名未启动 PG，保留原输出，改用原夹具指定目录名运行后 2/2。 | [强制改密](D:/CP6-test-artifacts/W666-01/final-restricted-password/main.trx) · [PostgreSQL 回归](D:/CP6-test-artifacts/W666-01/final-native-regression/regression.trx) · [原组件](D:/CP6-test-artifacts/W666-01/final-relevant-regression/regression.trx) |

两个恢复用例独立读回各一个原回执、一个审计和同一原用户及 grant C02 哈希，生成号 16→18、Outbox 4→6、最低版本 `[5,1]`，原会话随后被拒。测试记录不保留交换用 Cookie 或完整 token。按原夹具适用范围复用 CRM 已采用受理 Owner，W665 普通退出无业务源码改动。

## 本地发布与工作区

本地发布包包含 310 项，清单 SHA-256 为 `666CE0D45B653885E9A7C363620BD31ED63BA4A73839250EC37F2309D5CFC805`。三份 Main 程序集的版本元数据绑定业务提交 `a2011cfbaac1a15d7cd9bc88b2c7099cfcec7e94`。发布包对实际 PostgreSQL 18.6 执行 DatabaseInit，确认 W665 与 W666 的迁移对象。发布 Main Kestrel 的存活端点 200，两个匿名凭据改动请求各返回 401。 [运行结果](D:/CP6-test-artifacts/W666-01/publish/result.json) · [逐文件 SHA-256](D:/CP6-test-artifacts/W666-01/publish/package-sha256.json)

发布进程与本任务 PostgreSQL 已停止，57260–57279 无剩余监听。分支 `codex/w666-01` 保留本地提交，DEV-2 工作区干净，席位保留到协调方完成远端采用及释放。本轮验证均在隔离本地环境执行。 [发布停机](D:/CP6-test-artifacts/W666-01/publish/pg-stop.log) · [运行夹具](D:/CP6-test-artifacts/W666-01/run-credential.ps1) · [发布脚本](D:/CP6-test-artifacts/W666-01/publish-main.ps1)
