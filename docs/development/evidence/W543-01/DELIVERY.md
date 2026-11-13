# W543-01 · Main 暂停与 CRM 人工受理和隐私写入

2026-11-13 UTC。Main 基线 `16e1287a88dd618a510b724f8937de66071f5eff`，实测差异 `c58b2abaafe64f2d3547726a1fe722695112a7dc`。CRM 基线 `39df2fd32c1ff0b12328d5eb71f07ec5d521221d`，实测产品差异 `b38d2e093389c9981d1e6eaf410df3f57b413388`，远端采用 `928b6ae82743bca6d9a2228c9c1bc2c876b6c231`，最终配置兼容夹具 `a4f3546efa324c90fbeb127209e3d9b82cd1aceb`。[CRM #210](https://github.com/GTX537/CP6.CRM/pull/210) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

Main 的人类上下文在原 PostgreSQL 事务中读出当前租户版本，作为受信响应头返回。CRM 在原 C02 锁下验证身份、签名和业务权限，在受理放行、拒绝、撤回同意、隐私导出请求、一次性下载的新效果前核对 Main 响应和持久封存。已有原件按既有授权与恢复合同处理。沿用 [W540](../W540-01/DELIVERY.md) 存储登记和数据库角色，保持原签名 JSON。

## PostgreSQL 18.6 实测

两套隔离 PostgreSQL，实际 Main 登录、OIDC、版本化暂停、在线人类许可和实际 CRM HTTP Owner。租户、访客、管理员和业务数据均为模拟，使用已有回环转发模拟 Main 的 TLS 终止。受测动作使用 Main 实际人类签名和 C02 快照，准备询盘和下载制品所需的服务授权由夹具签发。

- 5 个动作各强制两种顺序，共 10 组、20 项通过。业务先持锁时 Main 在 CRM 等待，业务先完成。回读提交状态、历史、原件、审计、Outbox 和一次性消费。暂停生效后原人类会话的新请求返回 401。
- 暂停先提交时，暂停前已经签发的人类授权在 CRM 返回 403。受理与同意状态、导出记录、审计、Outbox 和下载凭证状态未变。Main 的租户状态、权限代次、身份快照、授权撤销与优先 Outbox、回执和审计一致。
- 基线 `baseline-reject-04` 复现了实际 Main 暂停后，CRM 仍以迟到的授权将询盘置为 Rejected。保留先前夹具的角色发布、浏览器调用和基线边界失败记录。配置兼容首次因空快照清单被视为实际发布而失败，修正夹具后 `regression-configuration-02` 两仓均通过。Main 定向 8 项、CRM 定向 15 项通过，均 0 失败、0 跳过。

本地 `D:/CP6-test-artifacts/W543-01` 保存通过及失败 TRX、构建与发布证据。Main 发布 310 个文件，WebApi 摘要 `807e2fbafbaef07cc52bfac0b72a8edc6ac847df083ecb219b19f0ad9e7be15b`，建库退出 0、存活 200、匿名管理 401。CRM 发布 255 个文件，API 摘要 `89e1403ee303f1115f9c1a0857e32f1bf3a2a4cab0b926b411c8021f49a8f3dc`，存活 200、匿名配置传输 403。发布与本批联验使用相同的产品二进制。后续兼容夹具只改一处测试文件，CRM 重新构建包含不同的程序集版本元数据，具体摘要见 `publish-source.json`。隔离进程已停止。Actions 启动、重跑和取消为零。

## 仍需验收

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。PGT-017 尚有 CRM 导出生成、保留、复核过期。Main 角色、用户、会话、询盘范围、服务撤权、自然到期、直接 SQL 与两库恢复仍需采用和实测。Main 独立 Space、旧 OA 批办、工作流、财务、采购和 Stock 普通数据库角色的剩余提交边界继续核销。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

两仓分支处置、固定工作区状态和席位回收见两仓 PR 和本地 `main-finish.json`、`crm-finish.json`。
