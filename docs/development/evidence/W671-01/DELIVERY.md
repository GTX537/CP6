# W671-01 WMS QuickPIN 授权与 CRM 原 Grant

DEV-2 `codex/w671-01` 来源为 W668 已采用 Main `a831e1df97c6052dc5dc2ba435fa4f1f67ec900f`，串行纳入 W670 已采用 Main `a16518e321c31e8cab3577d1a3b147d6c495721a`。测试专属提交 `a5ca67e05031f414a8e3d922820de40b2bd1e556` 只编译，正式运行采用 `cef15bf16dd9cc281e5a2dd58f3bd27cd592f8be`。一次集中审查覆盖相对已采用 Main 的五个范围文件，`git diff --check` 通过。

原 `POST /api/v2/admin/client-devices/quick-pin` 在 `wms-mobile:device-manage` 当前授权根内解析同租户原用户名，按目标 UUID 固定原件及凭据修订。现有用户改名走受同用户待确认约束的 Main Owner，Privacy 匿名化同样使用该约束。原名与 UUID 在最终 Main 根重新核对。原输入规则、Badge trim、修改人、修改时间和最终会话及租户期限检查保留。待确认原件保护 PIN 的带随机密钥验证值及固定 BCrypt salt。原 PIN 只在当前 HTTP 写入流程内核验。

已登记 Store 实际存在受影响 Grant 时，Main 在首次连接专用 CRM 封存身份前保存同用户原意图。该意图停止目标的新写签发，原读权限及独立主体按原策略继续。最终 Main 同根提交 Badge/PIN、原 Grant 退役、原 C02 token event、priority Outbox、回执和待确认清理。用户 C02 投影及其原版本保持，Main 登录会话仍按原凭据版本失效。前向迁移只扩原 `kind` 约束，保留 W668 的原 `completion_id` 约束及 W670 的 `privacy-disposition`。 [WMS Owner](https://github.com/GTX537/CP6/blob/cef15bf16dd9cc281e5a2dd58f3bd27cd592f8be/CP6.Core/Services/Wms/ClientDeviceService.Authority.cs#L104) · [原授权与回执](https://github.com/GTX537/CP6/blob/cef15bf16dd9cc281e5a2dd58f3bd27cd592f8be/CP6.Core/Services/Sys/IamAdministrationV1.CrmCredentialSeal.cs#L45)

## 实际本机验证

使用模拟用户与设备管理员、源绑定 Main Kestrel、两套隔离 PostgreSQL 18.6、真实 Main OIDC/签发及冻结 W649 CRM 受理夹具。四轮合计六个实际通过、零跳过。 [验收夹具](https://github.com/GTX537/CP6/blob/cef15bf16dd9cc281e5a2dd58f3bd27cd592f8be/CP6.Tests/Platform/Pgt017CrmQuickPinPostgreSqlTests.cs#L22) · [源码和程序集绑定运行器](D:/CP6-test-artifacts/W671-01/run-quick-pin.ps1)

| 场景 | 读回 | 证据 |
| --- | --- | --- |
| 专用 CRM 首连拒绝、Main 重启和原件恢复 | 路由真实触发一次 PostgreSQL `28000`，HTTP `503`。用户、原 PIN/Badge、Grant、C02、Outbox、CRM floor 原值，原待确认一条且尚未开始封存。重启后目标新写签发仍封闭、原读与独立写签发可用。原管理员异 PIN 请求 409，封存登录次数无增长。连接恢复后同一请求返回 `200`、`code=0`，原回执一条、待确认清除、原 token floor 为 `1`、新增一条 Outbox。 | [冷拒绝](D:/CP6-test-artifacts/W671-01/candidate-cold/coordination/quick-pin-cold.json) · [同原生 Main 根等待](D:/CP6-test-artifacts/W671-01/candidate-cold/coordination/quick-pin-main-root.json) · [恢复](D:/CP6-test-artifacts/W671-01/candidate-cold/coordination/quick-pin-recovered.json) · [TRX](D:/CP6-test-artifacts/W671-01/candidate-cold/main.trx) |
| CRM 原受理先提交 | 隔离 PostgreSQL 观察到 Main 专用封存连接实际等待，原 CRM 受理决定和对应 Outbox 各一次；其后原 QuickPIN 正常提交，旧 Grant 重试 401。 | [Main 结果](D:/CP6-test-artifacts/W671-01/candidate-owner-first/coordination/quick-pin-order.json) · [CRM TRX](D:/CP6-test-artifacts/W671-01/candidate-owner-first/crm.trx) |
| Main QuickPIN 封存先提交 | CRM 已收到合法旧签名，实际原地写在原生决策点 403，原行和 Outbox 未写。QuickPIN 原写集、token floor 和独立签发读回。 | [Main 结果](D:/CP6-test-artifacts/W671-01/candidate-pause-first/coordination/quick-pin-order.json) · [CRM TRX](D:/CP6-test-artifacts/W671-01/candidate-pause-first/crm.trx) |
| 原 `DeviceLifecycle` 的 `pin` 行 | 实际 Kestrel 和 Main PG 原自改路径 200、原凭据版本失效以及既有权限、期限与原 404 检查。 | [原回归 TRX](D:/CP6-test-artifacts/W671-01/related-device-pin/related.trx) |

QuickPIN 原 HTTP 没有客户端提供的操作键。恢复覆盖现有待确认原请求与原提交回执。整条成功 HTTP 响应丢失后再次发出的请求仍按原 HTTP 合同处理。本轮未运行未修复产品场景。两库丢失 COMMIT 确认的共享协议证据沿用 W666，本轮实际故障为专用连接首拒和 Main 重启。 [W666 同一封存 Owner 协议](D:/CP6-test-artifacts/W666-01/DELIVERY.md)

## 本地发布与交接

发布内容绑定 `cef15bf16dd9cc281e5a2dd58f3bd27cd592f8be`，310 项，清单 SHA-256 `324E22D25089EF64C42CB721EB16A5096274D3B162D08C02338168964AFD05D4`。新库初始化退出 0，实际 PostgreSQL 查到新允许项及原 2FA/Privacy 项，发布 Main `/health/live` 200、匿名 QuickPIN 401。W668 既有发布清单哈希复核相同。 [发布运行结果](D:/CP6-test-artifacts/W671-01/publish/result.json) · [文件清单](D:/CP6-test-artifacts/W671-01/publish/package-sha256.json)

范围进程均已停止，57260–57279 无监听。DEV-2 干净，分支 `codex/w671-01` 保留，席位由协调方完成采用后释放。本轮没有 Actions、环境推广或远端写入。
