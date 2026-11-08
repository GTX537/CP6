# Main 主体资料处置计划 v1

W313 是 WP02-15 / SYS-PRIV-01 的局部交付：版本化政策、精确预览、冻结计划、独立审批、保留限制（hold）及原操作恢复。批准不会自动修改用户资料。本窗没有实际擦除执行、跨 Owner 销毁或备份恢复能力，不代表整张隐私卡或法律合规验收完成。

## 权限与精确范围

入口 `/api/platform/privacy-dispositions-v1`。所有调用使用独立 PostgreSQL 工作单元，持有现有 IAM 共同授权门，核验真实宿主会话、启用平台用户、当前租户、数据库已绑定环境和当前 `privacy-disposition:read`。新变更另外需要 `policy / prepare / approve / revoke / hold` 对应权限。环境、操作者、申请人和审批人由可信宿主取得；客户端不能指定身份。仅登记权限定义，不自动给任何角色授权。

政策在操作者租户与环境内按 PolicyId 追加版本，绑定目标租户、依据引用 UUID 和启用状态；更新须携当前 ExpectedVersion，首次为显式 null。依据引用指向业务登记材料，本实现不代替法律判断，不产生默认法律保留年限。hold 的版本、依据、管理人及管理租户、复核期限均保留；其作用域是环境加目标租户和主体，不因操作者租户不同而绕开。

预览读取目标稳定 ID、当前平台身份标志及现有 `identity_admission_revisions` 用户版本，不读取姓名、联系方式等原值。现有 IAM 用户/角色变更、删除重建及 ABA 版本语义直接复用，不再包一层资格登记。冻结范围包含环境、目标租户、主体、目标版本、政策 ID/版本/依据、hold 版本、动作、字段、效果及未覆盖 Owner，scopeDigest 绑定整个范围。请求须带实际预览的 digest；目标、政策或 hold 变化立即 412，不自动替换预览。

拟执行动作 `ANONYMIZE_IDENTITY_PROFILE`：登录名去标识化并保留稳定 ID；清理 NickName、Email、LastLoginIp、BadgeNo、ExternalSubject、ExternalProvider；停用登录并撤销认证材料。这是未来执行器的精确拟议效果，当前仅计划，字段集合同时列出 UserName。保留未覆盖项：安全日志、授权/组织引用、OA/PLM 历史、CRM 业务及历史、文件/先前导出副本、备份/外部副本、未接入 Owner。不把空结果解释为全域清除。

平台租户目标受保护；仍具有平台权限的主体须先通过独立身份管理流程撤权。政策停用或活动 hold 阻止申请/批准。active hold 必须有未来复核期限；到期仅提示复核，仍保持限制，不能自动失效。解除或续期要求当前 hold 版本和新的依据记录；解除后的新版本不会恢复旧计划批准。

## 审批与恢复

计划和决策均不可变追加。`REQUESTED → APPROVED / REJECTED / REVOKED`，`APPROVED → REVOKED`；终态不得再批准。批准和拒绝必须由申请人以外、当前具有 approve 的平台用户决定；撤销须有 revoke。每项决策带计划当前版本及依据引用；批准时重新核对整个冻结范围和当前限制。查看已批准计划会重新显示目标/政策/hold 版本及审批人当前平台身份、启用状态和 approve 权限。历史 APPROVED 与 `ApprovalCurrent` 分开；后者只是当前计划有效性，不是已执行证明。

原操作复用 Main Owner journal，同事务保存政策/hold/计划/决策、原回执及最小审计。键按环境、操作者租户、操作者 ID、OperationId 隔离。相同输入回原结果，不同输入 409；当前 read 可以恢复撤掉旧写入权限后的原结果。`NOT_FOUND` 不等于无效果；显式 close 原子保存 CLOSED，阻止迟到变更。尝试提交后应答未知返回 `PRIVACY_DISPOSITION_OUTCOME_UNKNOWN`，查询原键，不能换键盲重试。最终会话检查失败回滚整个变更和 journal。

页面在浏览器持久存储中只保留按环境/租户/操作者隔离的操作 UUID。确认取消不发请求，跨标签通过浏览器锁核对原操作；未知结果查询失败仍保留 pending，刷新只读恢复，不自动重发。切换身份清空当前资料并拒收迟到响应。当前页面支持读政策版本及 CAS 配置、精确预览和计划、独立批准/拒绝/撤销、hold 设置/续期/解除；不提供实际擦除按钮。

旧 `DELETE /api/platform/gdpr/erase/subject/{id}` 和 `/erase/tenant/{id}` 无论 confirm 值均为 410 `PRIVACY_APPROVED_PLAN_REQUIRED`，防止绕过审批和 hold。旧整租户导出及内部历史擦除服务保留原历史范围，未宣称其已采用本合同；当前无其他公共擦除调用入口。

## 本次交付和暂停

参见 [W313 交付](../evidence/W313-01/DELIVERY.md)。仅在本地新建 PostgreSQL 18 隔离夹具应用迁移；模拟用户、政策依据、申请和审批资料用于实际业务流程验证。没有迁移既有数据库或生产部署。

W313 正常采用、分支处置及官方 finish 后曾按用户要求暂停。用户于2026-11-08明确继续目标；W314下述执行属于本次新交付，W315恢复及其他原卡余项继续按台账登记。

## W314：已批准 Main 身份资料的实际执行

`POST /plans/{id}/execute` 接受 OperationId、ExpectedVersion（准确批准事件）、ExpectedScopeDigest 和 ConfirmationRef。当前 read 可恢复原操作；首次执行另核独立 execute 能力，不以平台身份或 approve 能力代替。精确计划必须仍为 APPROVED 且当前有效，目标、政策、hold、审批人启用/平台身份与 approve 权限在共同 IAM 门内核对，保持到效果提交。旧版本、旧范围或已撤销计划不能执行。

真实 Owner 为 `Main.Identity.Profile.v1`，仅对冻结目标租户/主体执行该计划的 `ANONYMIZE_IDENTITY_PROFILE`。UserName 使用完整稳定用户 UUID 去标识化并保留原用户 ID；列出个人字段清空，账户停用，密码替换为随机不可取得材料的合法 BCrypt 哈希，清理 TOTP 和快速切换 PIN、关闭密码回退并推进 AuthenticationEpoch；同租户主体的刷新令牌和浏览器会话撤销。原角色/组织引用和原历史不删除。没有借其他 Owner 的 APPLIED 冒称全域成功。

执行必须具备当前数据上下文针对目标租户的实际身份快照/Outbox配置。复用已有 IdentitySnapshotWriter 生成停用用户投影并撤销 CRM 浏览器授权及其令牌，必要事件参加同一 PostgreSQL 事务。未配置时在写入前明确503，不降级成功。源用户字段通过精确原生写入修改，避免 EF 字段审计复制旧姓名/邮箱等原值；只为身份快照/消息执行 SaveChanges。此限制不删除已存在的字段、操作、安全审计历史；这些仍位于明确未接入的历史 Owner 范围。

同事务保存不可变 execution、安全回执和 Main Owner journal；回执只含稳定标识、计划/申请人/审批人/执行人、确认依据、政策与前后版本、字段分类和未覆盖清单。`OwnerOutcome=APPLIED` 表示此真实身份 Owner 已完成；整体 `State=PARTIAL`，其他业务/历史/文件/外副本仍未覆盖。相同原操作只回原结果，撤掉 execute 后仍可在当前 read 下恢复。提交应答丢失继续 UNKNOWN 原键协议；最终会话失败回滚用户、会话/授权、身份事件、execution 和 journal。

`GET /plans/{id}/execution` 在当前 read 下读取准确原回执。不同 actor 的原操作隔离；另一 actor 可在相同操作者租户且具 read 时查看该计划的安全处置回执，不因此取得新执行权。另换操作键重复执行已完成计划返回409，不能把新操作伪装成第二次成功。已有 execution 的计划不能再批准、拒绝或撤销，后来 hold 或撤权不抹去已提交的事实。已处置主体阻止再申请同类身份匿名化；W315另行接入恢复及后续写入墓碑，不据本窗宣称备份恢复已安全。

页面通过独立执行确认列明主体、字段、不可逆影响、停用认证和缺项。浏览器仍只保存分区后的操作 UUID；取消不发送，未知只查原键，刷新不自动重发，显示历史真实处置与整体PARTIAL。无既有数据库迁移或真实环境擦除。本次验证和剩余见 [W314交付](../evidence/W314-01/DELIVERY.md)。
