# W542-01 · Main 暂停与 CRM 公开询盘

2026-11-13 UTC。Main 基线 `60030a93ce24085dd381b03be879ca4c3fe73f0d`，实测源码 `fbe33930cb236b59659fe0458922305acc6fe97f`。CRM 基线 `974cb61b2ed630b9ef39eb363f27d1b76d1dc708`，实测源码 `e417ac50ce450eef7fe17be084170a9c9f2d3717`，远端采用 `39df2fd32c1ff0b12328d5eb71f07ec5d521221d`。[CRM #209](https://github.com/GTX537/CP6.CRM/pull/209) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646) · [跨库问题 #1012](https://github.com/GTX537/CP6/issues/1012)

在 W540 已登记租户上，Main 的 `intake:release` 与 `public:receive` 服务上下文沿用 TLS、原签名 JSON 和现有认证根，在同一个 Main 事务读出当次租户版本并放入无缓存响应。CRM 公开表单签发持有已有的七个 C02 事务锁，并核对持久封存及健康的当前租户快照。公开提交在原 Owner 内通过同一组锁比对许可和开放版本，再消耗 attempt、扣限额及保存原图。启用登记后，同租户签发也进入 C02 事务锁，更新既有身份栅栏行。已提交的同键回放沿用浏览器与当前回执合同。登记和数据库角色沿用 [W540](../W540-01/DELIVERY.md)。

## 隔离 PostgreSQL 18.6 联验

使用两套 PostgreSQL、实际 Main Kestrel 租户管理、实际 Main TLS 服务授权和 CRM HTTP Owner。租户、访客、平台管理员、表单和输入均为模拟。四组联验共 8 项通过。

| 顺序 | 实际结果 |
| --- | --- |
| 签发先持锁 | 真实 attempt 写在数据库表锁上等待，Main 暂停在 CRM 栅栏等待。放行签发后，两个已签发 attempt 保留，新签发返回 404，业务写集与 Outbox 未变。 |
| 自动建线索先持锁 | CRM 已获授权并在限流表等待，Main 暂停等 CRM 提交。原回执、提交、线索、业务审计、Outbox 与限流账本保留，同键返回原 JSON 与 ETag。 |
| 暂停先提交，旧授权自动建线索 | 先拿到真实 TLS 许可，再暂停并在实际 Main 数据库核对提交，迟到的 CRM 请求返回 403。业务、审计、Outbox、消耗和限流账本一致。 |
| 暂停先提交，旧授权进入人工复核 | 同样强制顺序覆盖 `public:receive`，返回 403，无新增受理业务或配套写入。 |

四组均核对 Main 租户状态、管理版本、身份版本、权限代次、优先 Outbox、审计和回执。暂停的同操作重放返回原回执，暂停后 Main 服务许可返回 403。CRM 拒绝新签发。签发及业务先获门时，用 PostgreSQL 的阻塞 PID 验证 Main 连接确实在等待。无登记原有真实 TLS 路径 2 项通过，Main 定向 10 项通过、CRM 定向 20 项通过，均 0 失败、0 跳过。

修复前两项 CRM 回归确认了缺口，封存后仍签发或提交。联验首次运行把协调目录建在 Main 隔离夹具的新目录内，违反已有夹具约束。调整后，暂停先提交实际返回既有 HTTP 403 问题码 `crm.permission_denied`，同步修正测试原先对内部拒绝码的断言。原始记录保留。

`D:/CP6-test-artifacts/W542-01` 保存两仓 TRX、源码、SHA-256 和进程日志。四组通过记录分别位于 `accepted-attempt-first`、`verified-owner-first`、`accepted-pause-first` 和 `accepted-pause-first-review`。后续只增补签发先持锁的测试分支，复用已执行的其他用例，实测和发布 API 摘要一致。Main 本地发布 310 文件，隔离建库退出 0、存活 200、匿名管理 401。CRM 本地发布 255 文件，原生宿主存活 200、匿名配置传输 403。见 `publish-source.json` 和 `publish-smoke/result.json`。隔离进程已停止。

## 仍需验收

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。Main 尚需覆盖角色、用户、会话、询盘范围和服务撤权的跨库封存，租户自然到期、直接 SQL 与两库恢复。CRM 尚需覆盖受理、同意撤回、导出请求和生成、一次性下载、保留及复核过期。Main 独立 Space Design、定时 ABC、旧 OA 批办、剩余工作流、财务、采购和 Stock 普通数据库角色继续核对。Actions 启动、重跑和取消为零。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

CRM DEV-2 处置与 Main DEV-1 收尾证据见两仓 PR 和本地 `crm-finish.json`、`main-finish.json`。
