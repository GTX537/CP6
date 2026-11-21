# Lead 维护授权 v1

调用 Main 的 `POST /connect/lead-maintenance-context-v1`，使用 HTTPS、`application/json` 和已登记的服务 Basic 凭证。配置项为 `LeadMaintenanceContext`，启用时要求现行 `InquiryServicePrincipals` 的服务身份发布及 `InquiryPermissionSigning`。实例对照当前 PostgreSQL IAM 来源、租户、服务、角色、部门与 `crm-privacy:retention-run`，再查用途和对象授予。仅有询盘保留、询盘表单或普通隐私读取权限均不能取得这份授权。

## 对象与用途

| 对象 | 请求绑定 | 期限 |
| --- | --- | --- |
| `INDEPENDENT_INBOUND` | 租户、可信 SourceAuthority、InboundId、原 `sourceMessageId` | 由认证来源和数据库接收时刻选择 `H01_QUARANTINED_RAW` / `CRM_LEAD_INBOUND_RETENTION`。按已冻结 `retainSeconds` 加 UTC 秒。 |
| `MANUAL_LEAD` | 租户、可信 Lead 权威、LeadId，`sourceMessageId` 为 JSON null | 验证当前 `MANUAL_LEAD_PII` / `CRM_LEAD_RETENTION` 政策。原数据库接收时刻加 24 个 UTC 日历月。 |

`authority` 是源对象的权威命名，`objectId` 是上表所指 UUID。入站对象可在持久化任何 raw 和 key 前由 Owner 建立原业务身份。实际接收时刻由 Owner 的数据库确定并签入原件。请求只有范围选择器，必填 `requestNonce` 由调用方为本次请求生成，编码为 32 随机字节的无填充 Base64url。两类对象的源权威、租户及对象均须与原件逐字段一致。入站还须匹配原消息，原消息 ID 使用无个人信息的业务标识。

| 用途 | 签回证据组件 | 判定 |
| --- | --- | --- |
| `privacy:policy` | SOURCE、POLICY | 给出已证的保留期限 `purgeAtUtc`，`holdActive` 为 null。 |
| `privacy:hold` | SOURCE、HOLD | 校验 HOLD 与适用政策的绑定，`purgeAtUtc` 为 null。返回按 Main 数据库 UTC 时刻确定的 `holdActive`。 |
| `privacy:maintain` | SOURCE、POLICY、HOLD | 仅在已到期且无活动 hold 时签发，返回期限和 `holdActive=false`。 |

`read`、`download`、`export`、`legal` 等用途走各自授权。三种维护用途各有精确授权行。缺行、跨租户或跨对象、撤权及授予过期为 403 `insufficient_scope`。认证失败为 401 `invalid_client`。无效请求或不支持用途为 400。活动 hold 或未到期分别为 409 `active_hold` 和 `retention_not_due`。来源、政策、hold 或当前身份无法证明为 503 `lead_maintenance_authority_unavailable`。缺少可信来源为不可用，不能解释为空 hold。结果设为 `no-store`。

## 持久发布边界

Main 宿主以 `LeadMaintenanceContext:ProviderKeys` 配置可信公开 Ed25519 公钥。每项按 `TenantId`、`TargetKind`、`Component`、`Authority` 绑定唯一 `KeyId` 和 `PublicKeyBase64`，公钥是标准 Base64 编码的 32 原始字节。由 Source 或 Lead 权威签发 SOURCE，绑定政策权威。相应政策权威签 POLICY，绑定 SOURCE 的 SHA-256、适用期间、期限规则、hold 权威及 `keyPolicyVersion`。HOLD 权威签确切目标、SOURCE 与 POLICY 原件 SHA-256、实际观察时间、有效期和最多 64 段完整区间。观察有效期最多 5 分钟，区间按 `fromUtc <= now < toUtc`，空结束时间表示开放。留存在数据库中的材料不会扩展宿主信任表，任何缺证或签名不符均失败关闭。密钥轮换沿宿主排空与受审配置替换，保持原 SOURCE 和入站 POLICY 字节，切换签名；恢复出流量前核对现用登记及新签名。

数据库迁移创建 `cp6_lead_maintenance_v1.scope_grants` 和 `cp6_lead_maintenance_v1.evidence`，空库不自动授予对象权限。授予由 Main 的可信 IAM/PRIV 发布者写入，证据由可信发布者持久化各 Owner 签名材料。运行实例对两表仅需 SELECT，另需 schema USAGE 和 `lock_target(uuid,text,uuid)` EXECUTE。应由数据库 Owner 为实际已批准的发布身份授予必要写权限。终端 OAuth 服务凭据只用于请求签发，发布者另经数据库授权鉴别。两个写入入口均以租户、类型、对象为事务锁范围。发布者若在同事务写多项，应先取本对象锁，IAM 写入在对象锁之前，整组提交才对签发可见。

`scope_grants` 需要精确对象、权威、服务主体、当前认证 epoch、用途、opaque 授予版本、有效区间和 `enabled`。`evidence` 的 `payload` 是原始 UTF-8 JSON 字节，`signature` 是 64 签名字节，`sequence` 是对象与组件下的正 Int64。SOURCE 行另存精确 `object_authority`，入站还存 `source_message_id` 供唯一性约束，两项均对照签名内容。其余组件的两列为 SQL NULL。SOURCE 与冻结入站 POLICY 不可替换。当前手工 Lead POLICY 和 HOLD 使用更大的已签序号替换，不可回退，同序号仅能更新同字节的签名。手工 Lead 政策变化后须一并发布匹配的新 HOLD，否则失败关闭。

## 签字节与消费

所有外层 `payload` 与 `signature` 采用无填充 Base64url，先按闭合 [schema.json](schema.json) 验结构，再解码原件。字符 LF 是单字节 0x0a。签名算法均为 Ed25519。函数形式如下。

```text
Main 签名输入      UTF8("cp6-lead-maintenance-context-v1" + LF + issuer + LF + keyId + LF) || 原 payload
Owner 签名输入     UTF8("cp6-lead-maintenance-evidence-v1" + LF + component + LF) || 原组件 payload
Owner sha256      lowercaseHex(SHA256(原组件 payload))
Main scopeDigest  lowercaseHex(SHA256(UTF8(
                  "cp6-lead-maintenance-scope-v1" + LF + tenantId + LF + targetKind + LF
                  + authority + LF + objectId + LF + (sourceMessageId 或空串) + LF + purpose)))
```

摘要中的 UUID 用小写连字符标准形式。验证端以信任配置固定 Main issuer、公钥和各 Owner 公钥，拒绝域、钥、签名、版本、调用目的、原对象、原消息或 nonce 不符的凭据。原件 bytes 可直接用于审计和核验，对 JSON 键重排、格式化或重新编码的结果做验签会失败。opaque 版本精确保留，正 Int64 序号与服务修订在 JSON 表达为十进制字符串。政策 `contentDigest` 是政策权威签名声明的政策内容摘要，组件 `sha256` 由 Main 对收到的证据原件计算，两者用途分别保留。

按数据库时刻签发，正常最长 60 秒，并受授权截止、租户截止、签名钥截止及适用的当前政策、hold 快照、下一 hold 边界约束。CRM 拒绝超期、跨请求、跨对象及不支持用途，核实签名组件、前置摘要及版本。丢回包或新实例启动后按原对象、原消息、原 purpose 及新 nonce 再取，原范围摘要稳定，每次重新核当前授权。签发不建立新的 CRM 业务槽或清除事实。WP03-16b 用同一原业务键执行并在 CRM PostgreSQL 最终事务锁内复核当前 hold、期限、政策版本及完整保护图。独立入站缺有效政策或密钥时的零 raw/key、`NEVER_RETAINED`、擦除审计、合法导出和恢复由该卡留证。

[实际 TLS 正反样例](examples.json) 是隔离运行的原回包和公钥。签名可用下列依据复验，历史样例的有效时间应取其签署时点。真实在线重新执行报告所列的测试，使用新凭据及当前时间。

```js
import { createPublicKey, verify } from 'node:crypto';

function ed25519(publicKeyBase64) {
  return createPublicKey({
    key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), Buffer.from(publicKeyBase64, 'base64')]),
    type: 'spki',
    format: 'der',
  });
}

function verifyOriginal(publicKey, prefix, envelope) {
  const original = Buffer.from(envelope.payload, 'base64url');
  const input = Buffer.concat([Buffer.from(prefix, 'utf8'), original]);
  if (!verify(null, input, publicKey, Buffer.from(envelope.signature, 'base64url'))) {
    throw new Error('签名不符');
  }
  return JSON.parse(original);
}

const mainPayload = verifyOriginal(ed25519(mainPublicKeyBase64),
  [response.schemaVersion, response.issuer, response.keyId, ''].join('\n'), response);
```
