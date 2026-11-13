# W540-01 · Main 租户暂停与 CRM 询盘配置

2026-11-13 UTC。Main 实测源码 `f4abf8cbc860c90d08a8f403d943cf325ed09d97`，基线 `5b7174b4b5f83e105b3a8360ab499995919fce83`。CRM 实测源码 `01e96d07c659dda7b203f2ffb2b128032f9fd9d0`，基线 `a4890974b3dce2fa9e978a62d3cbcfeb1958bf91`。[CRM #208](https://github.com/GTX537/CP6.CRM/pull/208) · [跨库问题 #1012](https://github.com/GTX537/CP6/issues/1012) · [PGT-017](../../acceptance/postgresql-tenancy.json#L646)

## 提交边界

Main 版本化与旧租户暂停共用 PostgreSQL 认证根。已登记租户先在 CRM 同一数据库、七个 C02 事务锁下封存当前租户版本，取得该库提交确认后，Main 才提交租户状态、身份快照、授权代次、原回执、审计和 Outbox。CRM 保存、预览、激活持同一组 CRM 租户锁直到各自 Owner 提交。新效果核对 Main 当次在线许可的存储和版本、CRM 发布版本、封存行以及原业务许可。REST 与 BFF 共用 Owner，已登记 REST 仍验证原签名投影。安装、数据库角色和两仓登记见 [CRM 交付说明](https://github.com/GTX537/CP6.CRM/blob/01e96d07c659dda7b203f2ffb2b128032f9fd9d0/docs/development/evidence/W540-01/DELIVERY.md)。

Main 与 CRM 分别提交。CRM 已封存而 Main 回滚时，旧版本的新效果关闭，原件仍按既有当前读取授权回放。相同暂停操作可重试同一封存。重新激活使用更高租户版本，原撤销会话保持失效。快照回退、跨主备切换、混合版本宿主以及两端提交回包丢失尚需后续恢复验证。

## 实测

使用两套独立 PostgreSQL 18.6、实际 Main Kestrel 租户管理和 OIDC、实际 CRM 配置 HTTP Owner，以及模拟租户、平台管理员、业务用户、站点和表单。

- **业务先持锁**。CRM 新配置在真实业务锁上等待，Main 封存连接在 CRM 中等待同一事务。期间 Main 写集不变。释放 CRM 后，新业务与原件、审计、Outbox、日历、草稿和受理绑定一起提交，随后 Main 暂停生效。
- **暂停先持锁**。保存、预览和激活都先取得真实 Main 许可。Main 在 CRM 封存后暂停于自身业务更新，三笔迟到请求均返回 403，CRM 写集一致。另一笔写入在 Main 认证根上等待，暂停提交后返回 401。Main 只增加一次暂停回执和审计，租户快照及现有授权的撤销快照分别产生优先 Outbox。
- **提交中断**。在 CRM 已封存时取消隔离 Main 的暂停更新，Main 状态、版本、Outbox、回执和审计均未变。CRM 新写 403，已有配置按原键返回原件。重试同一 Main 操作成功，重复读取结果一致。重新激活推进租户版本 2 → 3，发布正常队列事件，旧会话仍返回 401。
- 两仓联验共 6 通过，Main 相关回归 27 通过，CRM 相关回归 49 通过，均 0 失败、0 跳过。Main 回归执行在最后一次补加 C02 启用前提检查之前，该检查由三组最终两库联验覆盖。原始失败记录保留。修复前业务持锁时 Main 暂停先返回。候选曾因 GUID 配置绑定遗漏、测试错误计算授权代次及错误查询 Outbox 而失败，均以最终 TRX 复核。

本地证据位于 `D:/CP6-test-artifacts/W540-01`。最终记录为 `adopt-owner-first`、`adopt-pause-first`、`adopt-recovery` 的两个 TRX，以及 `regression-main`、`regression-crm`。Main 310 个、CRM 255 个发布文件分别有 SHA-256 清单，两套发布 API 摘要与实测程序相同。发布 Main 完成隔离建库，存活 200、匿名管理 401。发布 CRM 原生宿主存活 200、未获受信传输资格的配置请求 403。首次 CRM 冒烟的模拟合同摘要错误，改用制品实际摘要后通过。启动记录及源码和文件摘要见 `publish-smoke-02/result.json` 和 `publish-source.json`。隔离进程均停止。未启动、重跑或取消 Actions。

## 仍需验收

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。PGT-017 保持 NOT_RUN。Main 未核销的写者包括空间 Connector、模板、分析及独立 Space Context，旧 OA 批办与剩余工作流写入，银行对账、关账和预算，采购收货与 RFQ，以及 Stock 普通角色的直接写入。CRM 还需覆盖公开 attempt 与提交、受理决定、同意撤回、导出和下载、保留及复核过期。Main 角色、用户、会话、询盘范围和服务授权的跨库撤销、直接 SQL、租户到期，以及跨库恢复仍待采用与实测。已提交的技术投递、可信事实和历史回读按各自授权合同判断。[完整问题](https://github.com/GTX537/CP6/issues/1012)

固定 DEV-1、DEV-2 均由 W540-01 复用。采用结果、工作区状态与席位回收见两仓 PR 和 `main-finish.json`、`crm-finish.json`。
