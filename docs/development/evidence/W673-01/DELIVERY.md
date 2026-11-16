# W673-01 SSO 已有账号首次绑定与 CRM 原授权

候选 `e2e0e4ea8216596d01f639dcedaaa1f9148c7410`，采用的 Main 为 `98db305659c0303a859611aac0bb2ee9679ce4ad`（W672 #1193）。初始独立测试检查点 `1be0274ba2c25a780b4f7a4a5ec9aed3cf170b07` 只做静态编译。交付关联 [#1012](https://github.com/GTX537/CP6/issues/1012)。

## 行为

- 批准的已有用户 LINK 在原 IdP 验签和一次性 state 消费后保存原映射与同用户 CRM 待确认记录。首次连接实际登记 Store 前，原用户、批准版本、客户端绑定、绝对期限、epoch 和 C02 待封存写集已固定。待确认期间目标仍可按原规则读取，新 CRM 写授权没有有效 fence。独立主体继续正常。 [链接与封存 Owner](https://github.com/GTX537/CP6/blob/e2e0e4ea8216596d01f639dcedaaa1f9148c7410/CP6.Core/Services/Sys/SsoService.CrmIdentityLinkV1.cs#L36)
- 同一个有效客户端绑定经新的合法 IdP 回调续行原件。Main 根内复核批准、当前用户、租户及期限，用固定 epoch 更新原用户并封存相应 CRM 代次。用户绑定、C02、Outbox、审核、唯一原回执和待确认清理同一次 Main 提交。已绑定读取、JIT 和普通 MFA 继续采用各自原合同。零可封存来源的 LINK 在本地末段重验当前写集。 [映射入口](https://github.com/GTX537/CP6/blob/e2e0e4ea8216596d01f639dcedaaa1f9148c7410/CP6.Core/Services/Sys/SsoService.IdentityAdmissionV1.cs#L13)
- 原浏览器状态归并续行回调，未到达的 IdP 重验保留关闭屏障。可以明确关闭尚未发起封存的原件。封存可能已提交时保留原 `UNKNOWN` 与原待确认记录。Web 映射未确认页提供同一 capability 的重新验证与明确结束；普通回调错误保留原关闭处理。新的前向迁移只追加当前种类，保留 W672 的 2FA completion 检查。 [原回执](https://github.com/GTX537/CP6/blob/e2e0e4ea8216596d01f639dcedaaa1f9148c7410/CP6.Core/Services/Sys/SsoMappingRecoveryV1.cs#L30) · [Web 重验](https://github.com/GTX537/CP6/blob/e2e0e4ea8216596d01f639dcedaaa1f9148c7410/cp6.web/src/auth/browserAuthentication.ts#L199) · [PostgreSQL 迁移](https://github.com/GTX537/CP6/blob/e2e0e4ea8216596d01f639dcedaaa1f9148c7410/CP6.Persistence.PostgreSql/Migrations/Core/20261116020000_SsoIdentityLinkCrmSealIntentV1.cs)

## 实际验收

本机 PostgreSQL 18.6，两套隔离数据库，普通签名 Main Kestrel 登录、CRM grants 与授权上下文。IdP 用模拟用户、真实签名 JWT 和现有 SSO 验证路径。冷场景的同 origin 重验运行实际 `AuthController`、`authorize`、`callback` 和会话输出。两种 CRM 提交顺序使用冻结 W649 的真实 PostgreSQL CRM 受理夹具。 [已执行程序集](D:/CP6-test-artifacts/W673-01/candidate-cold-1/tested-assemblies.json) · [任务运行器](D:/CP6-test-artifacts/W673-01/run-sso-link.ps1)

| 场景 | 实际结果与证据 |
| --- | --- |
| 专用 LOGIN 首次拒接、原操作恢复 | PostgreSQL `28000`，既有用户、原 grant、C02、Outbox 与 CRM floors 无新业务效果。两张受影响的 grant 均失去新写 fence，原读和独立主体可用。同浏览器另发而未到达的 state 保持唯一原 `UNKNOWN`，明确关闭为两 state 各记录 `CLOSED`。另一绑定不能续行，原 code/state 再交零换码。 [TRX](D:/CP6-test-artifacts/W673-01/candidate-cold-1/main.trx) |
| 真实 CRM COMMIT 应答丢失 | 原 Main 业务与回执保持待确认，CRM floors 已为 `[5,1,1]`。独立实际授权支持会话执行 close，得到同原 `UNKNOWN`。同 origin 用新 state 和原浏览器 capability 完成原 LINK；原期限和 epoch 保留，用户版本 `4→5`、Outbox `4→7`、两 grant 撤销、原 LINK 审计及回执各一。只有后来成功重验产生一次正常会话。 [原结果](D:/CP6-test-artifacts/W673-01/candidate-cold-1/coordination/sso-link-cold.json) |
| CRM 真实业务先提交 | Main 专用封存连接 PID `8408` 在实际 CRM Owner 锁上等待。CRM 原受理拒绝决定及对应 Outbox 各一，之后封存完成，旧 grant 原请求重试 `401`，已提交结果保持。 [Main 结果](D:/CP6-test-artifacts/W673-01/candidate-crm-owner-first/coordination/sso-link-order.json) · [CRM TRX](D:/CP6-test-artifacts/W673-01/candidate-crm-owner-first/crm.trx) |
| LINK 封存先提交 | CRM 原请求已得到签名授权上下文，后在实际写入点返回 `403`。原决定和 Outbox 无新写。Main 同样保留一个原 LINK、两 grant 撤销、精确版本与 floors。 [Main 结果](D:/CP6-test-artifacts/W673-01/candidate-crm-seal-first/coordination/sso-link-order.json) · [CRM TRX](D:/CP6-test-artifacts/W673-01/candidate-crm-seal-first/crm.trx) |
| 原 SSO 合同与 Web | 18 项 PostgreSQL 回归通过，覆盖普通批准 LINK/JIT、原 Web MFA、Native PKCE、原 Main 应答未知、并发与租户期限。23 项定向 Web 测试通过，受影响入口类型检查退出 0。 [TRX](D:/CP6-test-artifacts/W673-01/related-2/related.trx) · [Vitest](D:/CP6-test-artifacts/W673-01/web-vitest.log) · [类型检查](D:/CP6-test-artifacts/W673-01/web-typecheck.log) |

候选和相关验证零跳过。首轮静态编译缺测试缓存命名空间，补充后无错误。首轮关联套件在应用启动前被测试服务器拒绝不合约的 `main-pg` 根名，更换任务根后仅重跑该组。离线 npm 缺原镜像 tarball，采用原锁文件完整性验证安装后运行 Web 测试。 [首编译](D:/CP6-test-artifacts/W673-01/checkpoint-compile/build.log) · [关联夹具失败](D:/CP6-test-artifacts/W673-01/related/related.trx) · [npm 离线失败](D:/CP6-test-artifacts/W673-01/npm-offline.log) · [实际安装](D:/CP6-test-artifacts/W673-01/npm-prefer-offline.log)

## 本地发布与交接

发布 310 项，源码 `e2e0e4ea8216596d01f639dcedaaa1f9148c7410`，清单 SHA-256 `CD0DCF59BCE6E41E4AF70CD46BA2A0D56EDCEC2FA8A9E888581865BF03A1CF90`。发布的 WebApi、Core、PostgreSQL 三程序集与 native 实测哈希逐项相同。新库初始化退出 0，查询确认 W672 completion 约束、新 kind 和续行字段，实际 `/health/live` 200，匿名原 SSO 配置恢复 401。 [发布结果](D:/CP6-test-artifacts/W673-01/publish/result.json) · [发布清单](D:/CP6-test-artifacts/W673-01/publish/package-sha256.json)

九个任务文件已集中复核，`git diff --check` 通过。测试与发布进程已停止，57260–57279 无监听。`codex/w673-01` 为本地保存的候选，DEV-2 工作树干净，席位由协调方核对采用后释放。验证限本机模拟输入和原生 PostgreSQL，生产部署和 GitHub Actions 未执行。
