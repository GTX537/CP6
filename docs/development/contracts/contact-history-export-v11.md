# Main PRIV/FILE 联系人历史导出与受控下载

| 项 | 固定值 |
|---|---|
| 业务合同 | `CONTACT-HISTORY-EXPORT/1.1` |
| 提供方 | `Main.PRIV.FILE.ContactHistory` |
| 证明权威 | `Main.PRIV.FILE.ContactHistory/1` |
| 数据集、意图、格式 | `CONTACT_HISTORY`、`CONTACT-HISTORY-SCHEMA/1`、`CONTACT-HISTORY-INTENT/1`、`CSV_VIEW` |
| 字段 | `contact.displayName`、`contact.lifecycle`、`relationships.periods`、`activities.summary` |
| 制品上限 | 2097152 字节 |
| 正式路径前缀 | `/internal/main/v1/contact-history-exports` |

## 调用者和授权事实

入口由 Main Kestrel 提供 TLS。可信 CRM 适配器按租户登记 `client_id`，使用已登记、当前有效的 Main C01 `at+jwt`，受众为 `CP6.Services`、scope 为 `cp6.services`，`sub` 为 `service:<client_id>`。每次调用提交一个与签名租户一致的小写 D 格式 `tenantid` 和一个 `correlationid`。后者接受 1 至 128 个 ASCII 字母、数字、点、下划线或减号。JWT 验签、Main 机构激活、服务令牌记录和撤销状态各自检查。仅许可签发时所用的同一服务令牌可使用该许可。

Main 通过 `ContactHistoryExport` 节显式启用入口。配置 `Clients[].TenantId`、`Clients[].ClientId`、`Epoch`、`KeyId`、由密钥库提供的 `PrivateKeyPem` 和 `DownloadWindowSeconds`（1 至 60，默认 30）。原请求的已登记服务调用恢复操作还需要 `Clients[].CanReconcile`。部署方为消费者独立登记租户、TLS 端点、提供方、权威、epoch 和签名公钥。服务令牌记录使用与 Main 授权投影同库的 `crm_identity.ServiceToken`，供每批字节在同一事务锁定当前凭据。已发原件只在原登记 epoch 与 key 下查证。换代需要先协调原件回读与消费者采用。

Main PostgreSQL 中受限的 `cp6_contact_history_export_v11` schema 保存以下权威事实。业务调用者通过 HTTPS 入口使用裁决，源投影由取得相应权限的 Owner 写入者发布。

| 表 | 作用 |
|---|---|
| `sources` | 按租户和 Contact 记录当前保留状态与隐私修订。匿名化或保留状态变化推进隐私修订。 |
| `access` | 按租户、Contact、主体记录对象读权、申请权、DL/180、字段、用途、有效期、已覆盖登记截点和源权限水位。每次更新推进本地授权版。 |
| `history` | 按同一租户、Contact、主体、权限水位、用途、视图存可读事实。每个事实的登记修订、发生时间、字段值独立。 |

同一源出版事务更新相关 `sources`、`access` 与历史行。改变源授权时同步发布新的权限水位，范围不足时把可证明的截点或覆盖显式收窄。Main 对 `sourcePermissionWatermark`、`currentAccessEvidence` 按可信投影中的完整 opaque 值核验，并复核当前 Main 主体和 Contact 的字段及用途授权。旧权限水位或隐私修订的文件需要获得相应版本的新原件才能重新下载。原 Job 历史及字节证据仍保留。

## 请求和独立结果流

请求报文包括 `contractVersion`、`eventId`、`requestId`、`sourceOwner=CRM_CONTACT`、`canonicalization=CONTACT-EXPORT-CANON-1`、`intentDigest`、`intent`、`sourcePermissionWatermark`。进入协议的 JSON 对象验证字段集合及重复键。意图绑定租户、Contact、`requestedBy=readerSubjectId`、数据集与 schema、`AS_RECORDED` 或 `CORRECTED_INTERPRETATION`、用户确认顺序的 `fieldIds`、用途、`recordedThrough`、可空发生时段和格式。

Main 计算 `SHA-256(UTF-8("CONTACT-EXPORT-CANON-1\n") + 规范 JSON)`。计划内动态文本用途按 NFC，UTC 为毫秒，紧凑 JSON 对象键按 Ordinal 排序，显式 null 和数组次序保留。CRM 自行验证其命令 HMAC `RequestDigest`。Main 为实际制品字节计算另一份内容 SHA-256，并由消费者对下载内容复验。已有四字段意图向量的摘要为 `d2bb0c2ca661031e987b9890ccb8b8249cabc58e94c5cbaa1b936b81189ebc14`。

源历史先选每个事实在 `recordedThrough` 时已登记的最新修订，再应用发生时段 `[occurrenceFromUtc, occurrenceToUtc)`。按确认顺序生成 UTF-8 CSV，使用 CRLF 和 CSV 引号规则，表格公式前缀转成文本。任一所需字段、用途、申请能力或隐私条件不通过时，整项拒绝。截点超出目前可证明的覆盖范围时返回 503，保留同键恢复。可终结的前置拒绝原件保存 `finalityDomain=BEFORE_JOB_ALLOCATION` 和可认证的 `evidenceRef`。

每个租户的原 RequestId 永久绑定一个完整意图和唯一 Job，或绑定一个分配前终拒。源事件键也在租户内唯一。Main 在一个 PostgreSQL 事务发布分配决定、Job/Artifact 原件和准确制品，同键读取已存原决定。同键异意图返回 409。授权有限的原读回只披露原决定以及当前允许读取的证据。

Job 轴依次发布 `ACCEPTED` 1、`RUNNING` 2、`COMPLETED` 3，Artifact 版本轴的首次 `READY` 为 1 并附 `requiredJobRevision=3`。`INITIAL_COMPLETE` 的前驱为空，`CONTIGUOUS` 指向同轴的精确上一版。`CHECKPOINT` 带完整状态、覆盖版本、精确流身份和选定的 `resolvesConflictIds`。源签名查证可按任意次序读取，应用端按原 Job、所需 Job 修订和本流前驱决定何时应用。独立脱敏版使用同一 ArtifactId 和新的 opaque ArtifactVersion、摘要及版本轴，保留列序，只填写生命周期并标记 `PARTIAL`。

结果包的 `body` 是原 UTF-8 字节的 Base64，`bodySha256` 是原字节摘要。Job 的流摘要为下列对象的规范 JSON SHA-256，Artifact 带上自己的 id、version，Job 对应两项为 null。

```text
{tenantId,producer,epoch,requestId,jobId,artifactId,artifactVersion}
```

结果签名为 ES256 P1363 的标准 Base64。校验输入由 `CONTACT-HISTORY-RESULT-PROOF/1\n` 与 `{tenantId,producer,authority,epoch,eventId,requestId,kind,streamIdentityDigest,revision,bodySha256,keyId}` 的规范 JSON 拼接。公钥为 SPKI DER 的标准 Base64。Checkpoint 内层签名域为 `CONTACT-HISTORY-CHECKPOINT/1\n`，对象为 `{tenantId,producer,epoch,authority,reference,revision,streamIdentityDigest,coversThrough,resolvesConflictIds}`，并与该事件的已签原件逐字段比对。

## TLS 路径和原有消费操作

所有路径相对于前缀，JSON 字段使用 camelCase，响应采用 `Cache-Control: no-store`。

| 操作 | 方法及路径 | 绑定 |
|---|---|---|
| `Describe` | GET `/dataset` | 数据集、schema、格式、字段、上限和发行方。 |
| 签名登记回读 | GET `/authority` | 已登记发行方、权威、epoch、keyId、算法、公钥。 |
| `SubmitOrReadSameAsync` | POST `/requests`，GET `/requests/{requestId}` | 响应 `{body,trust,declined}`。受理或终拒二选一，原决定可查。 |
| 原件发布和原字节回读 | GET `/requests/{requestId}/results` | `{originals,nextAfter}`。每页至多 16 项，按持久发布序从 `after` 标记之后读取。非空页返回尾标记，空页保留旧标记供后续查询。包包含 kind、eventId、bodySha256、streamIdentityDigest、revision、keyId、signature、body 和 trust。 |
| `AuthenticateResultAsync` | POST `/proofs/authenticate` | `{eventId,bodySha256}` 对上已存原件时回 `trust`。 |
| `VerifyReplayAsync` | POST `/proofs/replay` | `{original,eventId,bodySha256}` 回布尔验证结果。 |
| `VerifyCheckpointAsync` | POST `/proofs/checkpoint` | `{original,checkpoint}` 回布尔验证结果。 |
| `AuthorizeReadAsync` | POST `/permits`，GET `/downloads/{permitId}` | 完整 `{subject,artifact}` 绑定对应受控窗口及流。 |

恢复身份可向 `POST /requests/{requestId}/conflicts` 提交 `{conflictId,eventId,streamIdentityDigest,conflictingBodySha256}`，证明已登记原事件发生冲突。推进 checkpoint 时向 `POST /requests/{requestId}/checkpoints` 提交 `{streamIdentityDigest,expectedRevision,resolvesConflictIds}`，只关闭指名且仍未闭合的同流冲突。撤销或派生脱敏制品分别向 `POST /requests/{requestId}/artifacts/revoke` 或 `POST /requests/{requestId}/artifacts/redact` 提交 `{artifactId,artifactVersion,expectedRevision}`。

## 许可与原字节

`subject` 为 `{tenantId,readerSubjectId,contactId,readPurpose,fieldIds,currentAccessEvidence}`。`artifact` 为 `{requestId,jobId,artifactId,artifactVersion,intentDigest,contentSha256,bytes,format}`。Main 精确核对全部字段、旧状态和当前原件冲突，成功返回 `{producer,binding,validFromUtc,validUntilUtc,permitId}`。许可截止取配置窗口、服务令牌及当前授权截止的最早值。`validFromUtc` 最多兼容调用前两秒的 CRM 初次时钟观察，且受源授权起点限制。实际下载始于许可持久化之后。

下载路径要求原服务凭据。成功响应的 `Content-Length`、`X-Content-SHA256`、`Content-Type: text/csv; charset=utf-8` 和 attachment 作用于确切制品。入口在首字节前复算完整内容摘要。每批至多 16 KiB，持当前服务、主体、Contact 权限和隐私条件的共享锁检查后写出。批间释放锁使撤权事务能提交。前置失效返回零字节，已开始的流在下一批当前检查失败时中断，先前已发批次可在网络中完成传输。接收端以长度和摘要确认完整文件。恢复 ACL 后须申请新许可。许可 ID 只在服务凭据和绑定有效期内使用。

HTTP 400 表示输入不合合同，401 表示服务认证失败，403 表示当前条件未满足，404 表示原件不可见，409 表示已绑定或前沿冲突，503 表示当前结果无法证明。签名以已登记身份和实际证据为准。消费者接入、原 AC 出口和可复验文件在唯一交付报告中登记。[WP02-31 交付报告](../evidence/WP02-31/DELIVERY.md) · [CRM 已采用消费合同](https://github.com/GTX537/CP6.CRM/blob/512303e9f6c530dbd398991d96a69ffe9990cf23/src/CP6.CRM.Api/Contacts/ContactHistoryExportV11.cs#L38)
