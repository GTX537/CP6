# W532-01 · 旧菜单和管理员 2FA 的提交授权

2026-11-13 UTC，Main 基线 `6d62e4124c9641af834d7cdd32291df399c8d6ce`，实测源码 `f74aeb2d6d31b989d063b4a5de4d19ca92539dbe`。正式 PostgreSQL **26 PASS / 1 NOT_RUN**，余项 PGT-017，原询盘 60/60。 [原正式卡](../../acceptance/postgresql-tenancy.json#L646) · [Issue #1002](https://github.com/GTX537/CP6/issues/1002)

## 六个现行入口

旧菜单新增、修改、树排序和删除沿用 `menu:add/edit/delete`。管理员重置用户 2FA 和当前租户 2FA 策略沿用 `user:edit`。同一请求 `CP6Context` 通过 IAM 取得 PostgreSQL 共同根，读取当前会话和动作，再读取目标、变更和保存，根持至 commit。旧菜单原输入和返回契约继续适用。版本化菜单树保留自己的平台管理、CAS 和原结果路径。 [共同事务](../../../../CP6.WebApi/Filters/CurrentWriteAuthorityAttribute.cs) · [旧菜单](../../../../CP6.WebApi/Controllers/Sys/MenuController.cs)

PostgreSQL 重置复用既有凭据变更，同时在相同事务写原 `TwoFactorReset` 事件，保存目标和实际操作者。模式真实变化同步保存原值、新值和操作者的安全审计。两个动作可有意使当前会话失效。取得共同根后核对当时的完整会话，完成时复用已有的时钟、注销和取消校验，保留合法自重置和改策略的提交语义。旧 9 个工作流动作继续采用默认完整会话复核。 [重置入口](../../../../CP6.WebApi/Controllers/Sys/UserController.cs) · [策略入口](../../../../CP6.WebApi/Controllers/Sys/TwoFactorPolicyController.cs) · [完成检查](../../../../CP6.WebApi/Services/TenantAdministrationHttpV1.cs)

## 实际验证

- 旧产品实测 6 个入口均在真实 IAM 撤权先持根时返回 200。两个合法自失效场景本已能提交并拒绝后续旧会话，但缺少策略审计或重置操作者元数据。`baseline.trx` 共 8 个新断言失败。首轮测试编译的 tuple 类型错误保留于 `build-baseline.log`。
- 最终隔离 PostgreSQL 18.6、Main Kestrel、真实登录和 CSRF，22 项 PostgreSQL 和 16 项既有组件测试共 **38 通过、0 失败、0 跳过**。6 个新竞争均直接观测到业务连接等待 IAM 持有的同一根，撤权提交后 403，菜单及目标凭据/政策版本、审计写集不变。重新授予原动作后，4 个菜单写入口按原合同实际成功。自重置、策略更新分别成功提交凭据或政策代次及对应审计，旧会话后续返回 401。
- 定向重验覆盖 W531 的 9 个工作流入口、日历与改价的两种提交顺序、版本化菜单树真实 HTTP 及 2FA 既有行为。普通 Stock 与暂停的 W531 原生证据按现有调用链复用。 [新增实际回归](../../../../CP6.Tests/Erp/Pgt017LegacyIamAuthorityPostgreSqlTests.cs) · [W531](../W531-01/DELIVERY.md)
- `.NET 8.0.424` 构建零错误，保留原依赖警告。发布文件 SHA-256 清单、实际 TRX、等待后端与查询、冒烟及停止凭据位于 `D:/CP6-test-artifacts/W532-01`。本地发布 WebApi SHA-256 `2b7f1e071c041d1f21a271d22bbe31ca8bfec1d8fb555b2334f6365672acb27b` 与实际业务测试宿主一致。发布文件在独立本地 PostgreSQL/端口启动，HTTP 存活 200，匿名受保护路由 401。模拟宿主和数据库已停止。

一次集中审查覆盖动作键、既有状态、真实事务与完成时会话、审计原子性、回归和任务 diff。旧流程执行、财务/采购/计划、部分 WMS/Space、订单取号、仅缓存注销和普通 PostgreSQL 直接业务写入仍需逐项处理。整宿主写者覆盖尚未闭合，PGT-017 保持 NOT_RUN。Actions 启动与取消为 0。本次只执行隔离本地发布冒烟。分支处置、远端采用和席位释放以 PR 与 `D:/CP6-test-artifacts/W532-01/main-finish.json` 为准。 [已知写者边界](../W531-01/DELIVERY.md#仍待核销的正式前置)
