# W661-01 CRM OIDC 浏览器族注销恢复

Main 候选 `8e418614ba0bfe35f758353778ff3b7429fc7fef` 从已采用 W660 的 `94b6ef5159bc619de68b5eda7fc5c657bacb0ef7` 集成。差异为 9 个产品文件与 1 个原生测试文件。候选 10 个 Git blob 与最终测试输入逐一相同。冻结 CRM 应用源码 `8f5064db86bee2f0660fcb163a94d3496d7a39c2`，实际加载的 `CP6.CRM.Api.dll` SHA-256 为 `987A6F98555F7896AB0F8BA6CAF07ECF89604F9FC37D6BC3AC6FB78BEAAE928D`。全部身份、租户、凭据和表单均为本机模拟数据，两端运行真实 PostgreSQL 18。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012) · [受测源码与程序集](D:/CP6-test-artifacts/W661-01/tested-source.json)

## 源端与恢复合同

Main 在 PostgreSQL 原认证根与浏览器族锁下先接受 `/connect/end-session`。原 client、签名 `sid`、主体、租户、已存 nonce 摘要、登记 StoreId、原请求指纹和每枚原 grant 的 C02 前后继都持久固定。Main 释放该事务后才打开专用 CRM 封存连接。原 Web 族的新授权码和新 CRM 写 fence 随待确认意图停发。当前只读签发及原配置精确结果读回沿用既有授权合同。同一账号的独立浏览器族可继续取得新码和新写 fence。

确认阶段复用 W660 `CaptureCrmUserSeal` 与原 C02 快照和优先 Outbox 写者。在一个受限 CRM 事务内同步提交全族精确版本，再在一个原 Main 事务内提交浏览器族、所有关联 refresh、原 grants、C02 快照和优先 Outbox，以及已完成的原意图。Main hosted worker 仅恢复已接受的原族，无须保留 CRM 的签名 hint 或原浏览器请求秘密。原客户端随后通过有效的 `/connect/end-session` 才能取得 continuation。原 live 票据按原请求精确重读，保留一分钟、一次消费和 cookie 同源族核对。

## 实际验证

| 场景 | 观察结果 |
| --- | --- |
| 旧基线冷连接 | PostgreSQL 实际拒绝专用登录 `28000`。Main 退出失败，原族、refresh、授予及 C02 保持旧状态。旧产品仍为原族签出新写 fence，命中预期 RED。 [原基线 TRX](D:/CP6-test-artifacts/W661-01/baseline-cold/main.trx) |
| CRM BFF 与 hosted 恢复 | 冻结 CRM `/crm/logout` 经真实 HMAC 与 CSRF，立即删除受保护 `crm_auth.AuthStatesV1` 会话并清 cookie，返回原 HTML 失败页。Main 停发原族写 fence 并保留唯一意图。重新允许专用登录后，重启后的 Main Program 通过实际 worker 恢复，无浏览器重试，无 worker 票据。两个已兑换 grant 与一个始终未兑换的原码全部撤销，后续兑换返回 `invalid_grant`。原族产生三个精确 CRM fence、三个 Main 快照、三个优先 Outbox 项。另一族正常写许可仍可签发。 [hosted TRX](D:/CP6-test-artifacts/W661-01/final-worker/main.trx) · [实际 CRM BFF 宿主](D:/CP6-test-artifacts/W661-01/final-worker/permission-consumption-main/actual-crm-bff/logout.log) |
| 两库真实 COMMIT 确认丢失 | CRM 真 COMMIT 后断开回包，三个最低版本已生效，接收端技术 Revision 增加 3，Main 仍为原待确认且原业务写集保持。原操作重复时 CRM 同一版本确认使技术 Revision 再增 3。Main 真 COMMIT 后断开回包，精确重试读回唯一已完成原族，每个 grant 的业务快照及 Outbox 各一次。 [双 ACK TRX](D:/CP6-test-artifacts/W661-01/final-ack/main.trx) |
| 原授权负例与浏览器票据 | 真实签名但 nonce 错配返回 400。待确认时换原 grant/输入返回 503，保存的指纹与计划保持。相同有效请求重读相同 live 票据，原生剩余期至多 60 秒。不同浏览器族消费会照常跳转且保留其 cookie，同一票据再用返回 400。后续新票据只清匹配原族的 cookie。 [最终 worker 与票据 TRX](D:/CP6-test-artifacts/W661-01/final-worker/main.trx) · [最终 ACK 与票据 TRX](D:/CP6-test-artifacts/W661-01/final-ack/main.trx) |
| CRM 实际业务两序 | 沿 W546 真 `intake:reject`。业务先时旧许可下的原 CRM 制品先提交，注销随后成功。注销先时 CRM 拒绝迟到的原许可。两序各 1 Main + 1 CRM，均通过。Main 与两库端口由 W661 固定，原冻结 CRM Kestrel 独占一个由 OS 分配的回环地址。 [业务先 Main](D:/CP6-test-artifacts/W661-01/session-owner-first-native/main.trx) · [业务先 CRM](D:/CP6-test-artifacts/W661-01/session-owner-first-native/crm.trx) · [注销先 Main](D:/CP6-test-artifacts/W661-01/session-pause-first-native/main.trx) · [注销先 CRM](D:/CP6-test-artifacts/W661-01/session-pause-first-native/crm.trx) |
| 原合同回归 | `CrmOidcBridgeTests` 29 项及未登记 CRM 的原 Main 业务/注销双序 2 项均通过。两序及 31 回归与最终 2 项使用相同产品代码，后续增量为测试夹具。 [31 项 TRX](D:/CP6-test-artifacts/W661-01/regression-native/main.trx) |

新执行的功能与回归合计 37 通过、零跳过。冷拒和 ACK 使用受限 CRM 专用角色，CRM fixture 按冻版契约模拟本机信任的 TLS 终止，Main 与 CRM 均由真实 HTTP 宿主处理。首次夹具尝试中发现冻版 .NET 依赖解析、额外 Kestrel 监听和测试用户空纪元，均发生于目标业务前。ACK 首次尝试已实测 CRM 确认丢失，后续 Main witness 曾缺隔离密码，最终以原受控连接复测通过。 [首次 BFF 夹具日志](D:/CP6-test-artifacts/W661-01/candidate-native/permission-consumption-main/actual-crm-bff/logout.log) · [原双序夹具日志](D:/CP6-test-artifacts/W661-01/session-owner-first/crm.trx) · [原 ACK witness 日志](D:/CP6-test-artifacts/W661-01/ack-epoch/main.trx)

## 本地发布与交接

用已释放的 W659 构建缓存原地执行候选 `dotnet publish`。三份入口程序集的 ProductVersion 全部绑定候选 SHA，375 个文件的 SHA-256 清单哈希为 `FB31E8587B3FA0654E62AED71AB54F2722ED6F3145F7C80109900241196A3415`。发布程序在新隔离 PostgreSQL 执行 `DatabaseInit`，实际读到新原生族意图表和续票回读列。同包启动后的 `/health/live`、OIDC discovery、无客户端认证的 `/connect/end-session` 分别返回 200、200、401。 [本地发布结果与程序集哈希](D:/CP6-test-artifacts/W661-01/publish-smoke/result.json) · [发布文件清单](D:/CP6-test-artifacts/W661-01/publish-smoke/package-sha256.json)

`codex/w661-01` 在 Main DEV-2 保留本地候选提交供协调方采用，工作区干净。本任务所有进程已退出，57260–57279 全部无监听。席位仍由协调方持有，待 PR 和 `finish`。 [分支、进程与端口核对](D:/CP6-test-artifacts/W661-01/process-verification.json)

W661 以 Main 已持久接受原意图为恢复边界。Main 整体在首次接受前不可达、原生 `/api/auth/logout`、其他 IAM 变更、实际 Kafka/Dapr 投递及部署采用属于独立范围。生产环境与 GitHub Actions 均无操作。

协调复核范围为相对 W660 已采用主线的 10 个产品及测试文件。本地发布使用 W659 原构建输出目录，已保存整包相对路径 SHA-256 清单及真实启动结果，后续构建使用其他缓存以保留本次已启动的同一组文件。正式签名发布及线上环境采用单独记录。PGT-017 的正式矩阵维持 26 PASS / 1 NOT_RUN，下一批覆盖部门撤权的冷拒接和原委派操作恢复。
