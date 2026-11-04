# Main 主体持久导出 v1

原任务 WP02-15 / SYS-PRIV-01；W312 在 W311 三个实际来源上新增受控制品和原操作恢复。本合同不代表整张隐私卡、跨应用导出、法律保留政策或生产推广完成。

## 身份、用途与来源

入口 `/api/platform/privacy-exports-v1`。每次调用使用独立 PostgreSQL ReadCommitted 工作单元，先持有现有 IAM 共同授权门，再由真实宿主核验会话、当前平台身份及当前权限。模拟登录身份拒绝；最终检查外部注销和时钟。环境取可信 `ScopeAuthorization.EnvironmentId`，必须与数据库已绑定环境相同，不从客户端接受环境/申请人/操作者。

当前申请人与执行人为同一已认证 actor，分别记录字段；不提供代申请人输入。目标 `TargetTenantId + SubjectId` 独立于操作者租户。收件人为明确指定的、申请租户内启用的平台用户，可以与申请人不同；外部收件人和跨租户收件投递尚未采用。收件人将来下载必须独立通过当前下载权与来源权，不凭持有制品 ID 获权。

访问政策 `MAIN.SUBJECT_EXPORT.ACCESS.v1` 接受 `SUBJECT_ACCESS_REQUEST` 和 `ACCOUNT_SECURITY_REVIEW` 两种用途；该枚举约束本实现的授权用途，不构成任意国家法律依据、法律批准或法律保留期限。请求携带精确政策版本、申请引用 UUID、目标、收件人、来源数组及 1–1440 分钟技术下载窗口。需要扩大用途/来源时升级合同与政策，不能靠自由文本绕过。

来源为 `identity / security / roles`，分别对应 W311 明确分类的用户资料、安全事件与稳定角色关联。仅实际选择的来源进入载荷；未选来源 `EXCLUDED_WITH_REASON / OUTSIDE_REQUESTED_SCOPE`，数量和读取时间为空，不能冒称零行。未选 identity 时只读主体 ID 与租户完成归属绑定，不读取完整用户个人字段。其他 Owner 仍按 W311 逐项 UNKNOWN_SCOPE，未分类内容不进入载荷。

新制品使用 `scope.v3` 表达可选来源；`manifest.v1` 保持兼容。真实来源分别读取，保留独立起止时间与可变行版本依据，仍 `PARTIAL / complete=false`。保存生成时确切字节，不声称全域同一时点快照。

## 权限与当前读写

`privacy-export` 下定义 read、export、download、revoke 和 scope-identity / scope-security / scope-roles。迁移登记菜单及既有租户操作目录，不自动授予任何角色；新租户按既有 IAM 操作目录管理配置能力。平台身份本身不足以生成或下载。

- context / 原操作 / 制品元数据：当前 read；原操作只由原申请人按自己租户和环境查询，制品元数据仅申请人或收件人可见。
- 新生成：read + export + 每个请求来源能力，当前政策及目标/收件人绑定有效。
- 原请求重放：先按当前 read 返回原结果；不要求原 export 能力仍在，也不重新读业务来源。不同输入占用同键返回冲突。
- 下载：当前平台身份 + download + 制品全部来源能力，当前 actor 必须为原收件人。原申请人不因此自动拥有其他收件人的载荷。
- 撤销：当前 read + revoke，且为原申请人。幂等变更下载可用性；不能恢复、扩大范围、改变收件人，也不回收已下载的外部副本。

所有原生 IAM 变更和本制品写入共用已有认证门，最终决定与保存之间不通过短 TTL 代替权限保护。下载最后还检查当前主体租户绑定、访问政策、撤销/过期、实际载荷长度与 SHA-256；载荷缺失或损坏立即失败。审计记录 DOWNLOAD_AUTHORIZED 表示服务器已授权释放字节，不冒称客户端已收取成功。

## 原操作、原子性与 UNKNOWN

键为环境 + 申请租户 + 原申请人 + OperationId。请求正规化为内部 DTO 的确切序列化字节保存；不接受未定义字段。操作 COMMITTED 保存最小回执，CLOSED 保存未到达关闭标记。查询无行返回 NOT_FOUND，不能据此认定在途请求不会稍后提交。

`POST operations/{id}/close` 在同一门内：已提交则回原结果，未到达则保存 CLOSED；迟到生成看到 CLOSED 不产生效果。该关闭仅终结本 actor 的原操作，不是取消别人的任务。调用者可按原请求重放确认，但页面不保存完整请求，也不自动重发。

生成在一个 PostgreSQL 事务中保存不可变原操作、最小元数据回执、独立制品载荷和必要审计。必需来源失败、审计失败或最终会话失效都会回滚；尝试提交后的未知应答明确 `PRIVACY_OUTCOME_UNKNOWN`，使用原键查询，不把异常当无效果。同步生成不伪造 REQUESTED/GENERATING 异步任务状态。

`cp6_privacy_export_v1` 是实际 Main 隐私制品 Owner。artifacts/commands/audit 不保存原始个人资料载荷；payloads 单独保存 bytea，供后续明确处置任务管理。回执包含最小稳定标识、用途、来源清单、数量/时间与完整性元数据，不保存导出的姓名、联系方式或安全日志内容。现有工程 FILE 接口要求工程修订父对象，不能伪造该父对象接入本制品；此处不宣称已完成通用 FILE、跨 CRM 或备份生命周期接入。

不可变原操作与审计、制品身份不可修改；制品只可从未撤销变为已撤销。载荷删除尚无产品清理入口，不运行自动保留/清理任务。已缺失载荷明确不可下载；合法销毁、tombstone、备份恢复前再应用和 hold/引用冲突留后续实际处置窗口。

## HTTP 与实际页面

GET context / operations/{id} / artifacts/{id}；POST commands / operations/{id}/close / artifacts/{id}/revoke；GET artifacts/{id}/download。响应 no-store，下载仅当前验证后的附件，无永久公共链接、范围请求或浏览器缓存凭证。旧主体 GET `/api/platform/gdpr/export/subject/{id}` 返回 410 `PRIVACY_USE_DURABLE_EXPORT`，避免继续绕过新主体流程。旧整租户导出与擦除尚未转成此 Owner，是独立真实剩余范围，不能据本次声称所有旧隐私入口都已采用。

页面明确展示目标、用途、来源、收件人和下载窗口，确认取消不创建操作。生成与下载分开；刷新查询原操作，未知时只能查询或显式关闭。浏览器持久记录仅身份分区下的操作 UUID，不存导出内容或完整命令。跨标签页锁内检查未解决原操作；当前身份改变立即清空显示，迟到响应不能更新页面或触发下载。浏览器存储/协调不可用时拒绝发送新生成。

验证依据见 [W312 交付报告](../evidence/W312-01/DELIVERY.md)。真实 PostgreSQL/认证 HTTP/Chrome 使用明确模拟业务数据；不部署、不迁移既有数据库、不运行 Actions。
