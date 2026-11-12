# W507-01 询盘错误合同与浏览器恢复

原询盘最后一项 AC-INQ-034 实测通过。原始清单由 W497 结束时的 44/60 增至 60/60。本轮补齐 002、005、019、027、029、031、032、033、034、035、036、040、047、054、057、060。 [44/60 基线](../W497-01/DELIVERY.md) · [前一窗口](../W506-01/DELIVERY.md)

| 范围 | 现行证据 |
| --- | --- |
| FX02 | 签名 GET 和实际 Node BFF 核对不可重试的 `404 crm.form_unavailable`，无签发或 Cookie。三种带正确源摘要的合成历史政策分别实测 `409 crm.policy_binding_invalid`，无签发和持久差异。 |
| FX06、FX07 | 同意拒绝实测 `422 crm.public_schema_invalid` 和空明细，直接签名 Owner 上的 Origin、CSRF 或浏览器绑定统一为 `404 crm.public_not_found`。实际 PostgreSQL 到期后签名提交返回不可重试的 `410 crm.public_attempt_expired`，持久零写。签发前时钟沿同一现行校验及既有组件证据。Chromium 通过对应 410 响应验证保留草稿、显式重开和重新同意。 |
| FX10、FX13、FX20、FX21 | 复用实际同键异体 `409 crm.public_idempotency_conflict` 及零新增写入、回执不可枚举、当前授权和签名拒绝。回执采用 V15 主文的 `404 crm.receipt_not_found`，旧 fixture 使用 `crm.public_receipt_not_found`。公开授权失败用 `403 crm.permission_denied`，内部签名和范围诊断由权威边界保留。BFF 的 Origin 拒绝沿用自身外部边界。 [回执](../W117-01/DELIVERY.md) · [授权](../W503-01/DELIVERY.md) · [同键冲突](../W504-01/DELIVERY.md) |
| FX18、FX19 | 两个审核赢家后的反向旧 ETag 和初始陈旧 ETag 实测 `412 crm.version_conflict`，按 V15 主文为可重试，持久零新增效果。实际浏览器经 BFF 收到 412，读取当前版本并保留原因，用户显式确认后才以新键和 ETag 写入。配置的独立 412 语义另有回归。 [审核原结果](../W121-01/DELIVERY.md) |
| FX26 | 现行签名保存、证明状态、哈希、CAS、范围及相邻区间均按精确响应核对。另一操作者的凭证必须通过持久签发者哈希和当前同资源授权，才返回不可重试的 `403 crm.configuration_proof_actor`。伪造证明为同形哈希拒绝。历史 TTL=0 的已授权激活实测 `422 crm.configuration_policy`、空明细和零持久差异。浏览器清除旧证明，锁定该配置动作，显式重新读取。无关来源、规范内容和渲染摘要损坏仍按现行拒绝语义通过。 [配置采用](../W485-01/DELIVERY.md) |
| FX27 | 按已采用的 S01 有界夹具，合法键和超长联系人名、需求分别返回不可重试的 `422 crm.validation_failed`，各有唯一的 `max_length` 明细。旧短键返回 400。同一 Attempt 可按原键提交合法 Unicode 边界。带 JSON 的签名错误 GET 返回 405。拒绝路径零新增业务效果。 [原 Unicode 边界](../W484-01/DELIVERY.md) |

审核浏览器复测首次在发出请求前失败。隐私导出面板重新挂载时的旧异步操作引用使父页面一直锁定。修正卸载清理后，实际审核陈旧版本恢复与隐私拒绝浏览器两项均通过，原失败保留。

环境为本地 .NET 8、Kestrel、隔离 PostgreSQL 18.6、Node BFF 和 Chromium，业务及 IAM 模拟。公开过期和配置政策的浏览器分支通过夹具提供对应错误响应，签名 Owner 的实际 410、422 分别在真实 PostgreSQL 上执行。审核浏览器 412 由实际 Owner 经 BFF 返回。基线记录 16 项后端预期失败、2 项浏览器预期失败及 1 项 BFF Node 预期失败。最终 `contract-green-01.trx` 37 通过，`browser-and-lifecycle-green-01.trx` 8 个有效成功结果，另有 1 个已修复的审核浏览器失败，`intake-privacy-browser-green-01.trx` 2 通过。共 47 个不同用例成功、0 跳过，另有 53 个不同的定向 Node 检查及相关页面类型和浏览器构建通过。命令、TRX 和失败日志位于 `D:/CP6-test-artifacts/W507-01`。五个自有 PostgreSQL 实例已核验停止。

CRM #198 采用源 `b2e4224aa87ca147756f386eece15867bb2d77cf`，核对远端主线 `09d316a7c25a1f0148d98e90fbfff06cd5f4beb3` 及验证树。集中审查覆盖错误边界、权限、持久零写和显式恢复。准确任务分支已在本地及远端清理，W507 的 DEV-2 已释放。原始询盘验收 60 PASS / 0 NOT_RUN。共用服务采用、正式 PostgreSQL 与生产发布依各自台账。Actions 运行、取消和生产部署均为 0。 [CRM #198](https://github.com/GTX537/CP6.CRM/pull/198) · [CRM #196](https://github.com/GTX537/CP6.CRM/issues/196)
