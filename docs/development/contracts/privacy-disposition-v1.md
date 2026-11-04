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

用户要求完成本小任务后暂停，额度重置后再开工。W313 正常采用、分支处置及官方 finish 后暂停 24 小时目标；W314 实际 Owner 执行、W315 恢复及其他原卡余项尚未开始。
