# W322：租户已分类来源的受控导出 v1

原SYS-PRIV-01/WP02-15要求用途、准确接收人、当前披露权限、原operation/不可变制品和逐Owner Manifest；现GdprController仍GET返回ExportTenantAsync直接流。GdprService旧tenant反射EF拓扑并StripSensitive，模块已明确此方式不能证明新增/嵌套内容已分类。现SubjectExportCatalogV1已分类Sys_User/Sys_SecurityLog/Sys_UserRole字段，SubjectExportReaderV1以stable user筛选；W312原制品始终有准确subject，不能把Guid.Empty/TenantId冒作主体。

## 取舍与连续授权

brainstorming按已批准的24h连续窗口完成本次具体设计。直接包装旧反射全量不能证明秘密/未知嵌套内容可披露；复用主体制品会混淆身份、hold和处置Owner。采用独立tenant receipt/schema、准确TenantId选源投影和现可信Owner事务/权限入口。COORD单人登记本共享窗口，复用当前Main内部合同；不重复请求通用模板确认或运输ACK。

## 准确读取与Manifest

首批显式identity/security/roles，准确target TenantId下全部对应行，无用户名反扫或伪subject。字段直接复用已经接受的三个Owner白名单；租户登记元数据单独只含Id、TenantCode、TenantName、Enable、ExpireDate、TimeZoneId，Remark及其他未分类/内部字段不带出。禁密码/token/2FA secret/key、自由ContextJson等未知内容。新源选择不把缺失行冒不可用，实际0记录清楚写0；真正来源失败立即失败，不用空清单掩盖。

每源真实数量、开始/完成UTC读取窗、字段INCLUDED/EXCLUDED_WITH_REASON、实际selector/版本依据；现EF未采用实体及明确OA/PLM/CRM/FILE/BACKUP等非EF Owner列UNKNOWN_SCOPE。提取SubjectExportCatalog公共目录枚举供两种Manifest使用，保持旧主体wire/目录顺序及含义。租户Manifest没有subject字段，完整性false、整体PARTIAL、逐源读取窗，不声称跨全部Owner一致快照或整租户全覆盖。

## Owner、存储和当前披露

PrivacyExportOwnerV1改partial，新租户分面直接复用其Transaction、当前platform actor/权限、环境/IAM根/最终会话与时钟；新内部类型沿已有保证直接采用。独立Main.Privacy.TenantExport.v1、cp6_privacy_tenant_export_v1制品/载荷/commands/最小audit，不写其他应用主数据库，也不更改全库租户schema方案。

新tenant-read/export/download/revoke与tenant-scope-identity/security/roles分别授权，不自动授角色。准确target tenant须存在（停用/过期目标仍可按现平台隐私能力导出）；请求人/实际人由会话确定。接收人须为operator tenant中准确且启用的platform actor；实际下载独立核对当前recipient、当前download/所选源能力、原scope/政策、目标binding、TTL/撤销与最终会话，不借平台角色绕权限，也不临时授明文。

封闭命令operationId/requestRef/targetTenantId/purpose/sources/recipientId/policyVersion/accessMinutes，拒伪Actor/subject/未知字段；用途TENANT_ACCESS_REQUEST/TENANT_SECURITY_REVIEW，TTL1–1440分钟，政策MAIN.TENANT_EXPORT.ACCESS.v1。读取与生成结果的真实bytes/hash/长度/Manifest、原命令和审计同commit；元数据与可后续处置载荷分离。相同UUID不同输入冲突，当前tenant-read可在撤掉export后取原结果；NOT_FOUND不等无效果，关闭原UUID阻迟到。真实COMMIT回包丢失保原UUID，按原结果恢复，不把再次生成当恢复。

新命令/制品/URL与主体模型分别定位，不互认。原主体导出、处置及恢复合同保留。新租户载荷的物理保留/处置和恢复Owner尚未采用时必须继续明确列剩余，不套用W320主体载荷Owner或伪称已删副本。下载可撤销与真实物理清除分开。

## 兼容、页面与验证

旧整租户HTTP直接散流退役410并指向受控入口；旧服务内部方法、历史数据/记录保留。页面明确新出口仅已分类来源及未知Owner余项，不将它命名成全域完成。独立tenant表单显示目标/用途/来源/准确接收人和TTL、真实Manifest/回执、当前受控下载及撤访问；只保身份分区UUID，navigator.locks协调标签，取消零发送、存储失败不先发、身份改变丢弃迟到结果。原响应丢失重载取原结果，不重复生成。二人浏览器验证请求人生成、准确接收人读取/下载，跨人/跨tenant与撤权拒绝。

先实际红测证明旧直接出口及新分面/范围缺失；实现PG前向DDL/显式投影与Owner，再受影响PG/实际Main认证HTTP/Chrome/Vue/类型，覆盖实际秘密和未知嵌套内容排除、准确target/0行、当前授权与收件人、TTL/撤销、原UUID冲突/关闭、COMMIT丢答、最终会话/审计原子回滚及主体分面不互认。一次COORD集中SPEC/QUALITY审查，修复后定向回归；单报告/roadmap/四状态同步，Main/Handoff正常采用、准确分支清理及官方finish。仅隔离PG18与模拟业务数据，无Actions/部署/既有数据库迁移或外部发送。

原37536DONE/2IN_PROGRESS/337PLANNED、原59余47、125/247不变。W321登记至finish实测5775秒（1.60h），低于2.5–4h；W320实测1.72h。本窗复用现事务但新增租户投影/收件披露边界，估1.75–3h低置信实际Codex执行。约20h预算仍继续，72000秒统一校准，较快则接相邻真实余项，W310预算末尾；不空等凑时、不新开周期。
