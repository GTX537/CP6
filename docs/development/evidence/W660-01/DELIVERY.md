# W660-01 普通用户的 CRM 封存恢复

Main 基线 `65b0cd7ccef2f23822f26bdecfec6df1348963e6`，本地提交 `1b53220b8f4e73626ce86487409ab972b40a6ff0`。两库实测使用隔离 PostgreSQL 18.6、实际签名 Main Kestrel、真实 CRM 封存函数与有限登录。W546 两种顺序及 W658 角色对照使用 W649 已采用的 CRM 原生测试宿主。租户、用户、权限与询盘为模拟业务数据。 [登记](D:/CP6-test-artifacts/W660-01/main-start.json) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [W546](https://github.com/GTX537/CP6/blob/65b0cd7ccef2f23822f26bdecfec6df1348963e6/docs/development/evidence/W546-01/DELIVERY.md) · [W658 证据](D:/CP6-test-artifacts/W658-01/DELIVERY.md)

## 写入与恢复

- 版本化和旧用户更新、版本化与旧角色分配、版本化组织分配共用原 `IamUserAdministrationV1.ChangeAsync`、当前 `user:edit`、操作键、受保护输入指纹、用户和引用 CAS。仅注册用户实际需要新 C02 封存时，Main 回滚预备业务写集，提交原 actor、操作、StoreId、用户和实际 grant 过渡意图。新用户首版快照及无 C02 变化由原路径提交。 [用户写入入口](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Sys/IamUserAdministrationV1.cs:53) · [待确认所有者](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Sys/IamAdministrationV1.CrmUserSeal.cs:9)
- 首次打开 CRM 连接在 Main 根事务之外。冷等待期间仅限制该用户的新 CRM 写授权。原合法带签名上下文和只读继续，写路径的上下文省去新写 fence，可独立消费的旧签发方式返回 503。其他用户及服务沿各自当前授权。当前根内的竞争用户来源变更得到明确冲突，独立浏览器 token 退休沿其原路径。 [签发边界](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.WebApi/Services/InquiryPermissionIssuerV1.cs:62) · [竞争来源仲裁](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:206)
- 原 actor 恢复时先重核原操作和 CAS，再耐久登记可能已封存。最终原生写者核对前驱和稳定用户投影，逐项封存它实际拥有的当前 grant 过渡。挂起行保存受保护指纹和来源摘要。最终用户认证代次、改密时刻及派生期限由实际提交生成。确认封存后，用户、快照、Outbox、审核、原回执和清除意图在一个 Main 事务提交。未发送可由原 actor 关闭，可能已封存时保留 `UNKNOWN` 与同键恢复能力。 [源捕获](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs:180) · [原结果与关闭](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Core/Services/Sys/IamAdministrationV1.cs:89) · [前向迁移](D:/CP6-worktrees/wave17-dispatch-20261018/CP6.Persistence.PostgreSql/Migrations/Core/20261115213000_IamCrmUserSealIntentV1.cs)

## PostgreSQL 实证

**24 项通过，零失败、零跳过。** 另保留实际基线失败和测试夹具故障证据。

| 检查 | 实际结果 |
| --- | --- |
| 冷拒接基线 | 基线三个产品程序集夹入同一修正夹具。真实封存登录拒接后 Main 503，两库原写集及围栏不变，目标用户仍得到新写 fence。断言以 `Expected False / Actual True` 失败。 [基线程序集哈希](D:/CP6-test-artifacts/W660-01/baseline-assembly-hashes.json) · [RED TRX](D:/CP6-test-artifacts/W660-01/cold-baseline-verified/main.trx) |
| 最终用户禁用及两库真实 ACK | 冷拒接后，两个实际 OIDC grant 仍能取得验证通过的无新写 fence 上下文，独立用户仍有 fence。实际 Main 换 PID，待确认原件原样读取。未尝试操作可关闭。CRM 首次确认遗失后只有原用户最低版本已推进，Main 原写集保持，不能关闭。再次执行准确原键并在实际用户 UPDATE 等待时定位最终 Main COMMIT 应答遗失。GET 和两个 HTTP 别名均读回唯一 `COMMITTED` 回执，原 pending 清除。两条 grant 和一条用户 C02 事实使 Main 代次 17→20。 [最终 TRX](D:/CP6-test-artifacts/W660-01/native-final-user/main.trx) · [原键、Grant ID、PID 和写集](D:/CP6-test-artifacts/W660-01/native-final-user/coordination/user-seal.json) |
| 竞争来源 | 冷等待中的真实自助改密、CRM 应答遗失后的真实管理员重置 2FA 均返回 409。Main 用户、快照、回执、授权代次、审核、Outbox、CRM 最低版本及原意图相同。 [原生用例](D:/CP6-test-artifacts/W660-01/native-final-user/main.trx) |
| 其余四个实际入口 | 旧改密、版本化角色、旧角色和版本化组织均执行冷拒接、重启、同 actor 和原键恢复。旧改密时实际刷新令牌吊销、最终密码时间与 C02 投影一致。角色及组织变化推进一条用户事实，原 grant 保留。四项沿前一候选的相同用户路径，最终新增守卫只仲裁竞争的独立来源写者。 [旧改密](D:/CP6-test-artifacts/W660-01/cold-legacy-password/main.trx) · [版本化角色](D:/CP6-test-artifacts/W660-01/cold-roles/main.trx) · [旧角色](D:/CP6-test-artifacts/W660-01/cold-legacy-roles/main.trx) · [组织](D:/CP6-test-artifacts/W660-01/cold-organization/main.trx) |
| W546 原直接顺序 | 最终源码与原 CRM Owner 各两例通过。CRM 先持门时原业务合法提交保留，Main 等待原生锁。Main 先封存时旧许可无法写新效果。同键返回原 IAM 结果。 [Owner 先](D:/CP6-test-artifacts/W660-01/w546-final-owner-first) · [封存先](D:/CP6-test-artifacts/W660-01/w546-final-pause-first) |
| 定向兼容 | 一组 W658 实际冷角色对照，Main 和 CRM 各一项。最终用户注册、独立写者、C02/审核/回执故障、密码和旧输入定向回归 13 项。 [W658 单组复验](D:/CP6-test-artifacts/W660-01/role-control) · [13 项 TRX](D:/CP6-test-artifacts/W660-01/user-regression/main.trx) |

前两轮基线夹具分别碰到已提前创建的 PG 所有者目录，以及历史 CRM 时间线安装 SQL 需要原生安装事务。校正后取得上表 RED。首次 W546 Owner 先抵达 `paused`，但 Windows 读取遇到同时写入的文件。改用同目录原子发布后，两种顺序各自通过。CRM 等值确认现有技术修订语义沿原函数，正式证据记录实际逻辑 C02 代次。 [目录失败](D:/CP6-test-artifacts/W660-01/cold-baseline/main.trx) · [安装事务失败](D:/CP6-test-artifacts/W660-01/cold-baseline-2/main.trx) · [首次桥接失败](D:/CP6-test-artifacts/W660-01/w546-owner-first/crm.trx)

## 本地发布和交接

已测 13 份源码哈希绑定本地提交。发布的 WebApi、Core、PostgreSQL 迁移 DLL 与实际受测文件逐一匹配，310 文件清单 SHA-256 `cc13c47c33e089e2373bcbd4dace1f796ab4a9312788ba84c6029bd8b5e3b4bd`。隔离新库实际 `DatabaseInit` 退出 0，存在新待确认表的 Main 根触发器。发布 API `/health/live` 返回 200，匿名用户修改返回 401。端口 57240–57259 无监听，任务所有的测试、API、PG 与 relay 已停止。 [源码哈希](D:/CP6-test-artifacts/W660-01/tested-source-file-sha256.json) · [发布及启动](D:/CP6-test-artifacts/W660-01/main-publish-smoke/result.json) · [文件清单](D:/CP6-test-artifacts/W660-01/main-publish-smoke/publish-sha256.json) · [停机证明](D:/CP6-test-artifacts/W660-01/process-verification.json)

`codex/w660-01` 保留本地一份提交，DEV-1 干净，集成及释放由协调方完成。登录与到期、OIDC 族退出和独立凭证命令各沿既有业务范围。此次交付为隔离本机验证。

协调核对后以远端 W659 `d1694c06a7947c139791971e93f3bc0ffb24d0bb` 为集成基线。W659 合入只新增其自身的租户期限、专用测试及状态记录，13 个 PGT-017 用户封存产品及测试文件的已测内容逐一保持，沿用上述 PostgreSQL 和发布启动结果。此批落地的入口仍属 PGT-017，正式矩阵维持 26 PASS / 1 NOT_RUN。已在验证组合之外继续登记 OIDC 浏览器会话退出、部门及全局菜单授权来源。
