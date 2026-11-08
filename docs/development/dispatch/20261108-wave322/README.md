# W322 租户已分类来源受控导出

原WP02-15租户受控导出采用：准确target tenant的已分类identity/security/roles来源，显式用途、接收人、独立tenant能力、TTL和不可变制品/原操作；当前下载重判身份/范围/撤销/期限，真实Manifest含数量/读取窗/字段排除/未接Owner。独立租户模型无伪subject，整体仍PARTIAL，旧直接租户HTTP散流退役410；其他Owner全量导出与新租户制品物理保留/处置仍列真实余项。

Main `48420d71829daeb955198351696cc29aec33545a`，Handoff `bfaaf8edfc15f3085680fd7451d8a2ce9c616ca6`，COORD单人固定DEV1/DEV2；W321 Main776/Handoff297已核两仓采用、准确清理和官方释放，实测5775秒。本窗累计72040秒，剩余约3.99h；估1.75–3h低置信实际执行。原375/59及125/247不变。

[范围与实施](../../contracts/privacy-tenant-export-v1.md)。

## DELIVERY

W322完成Main.Privacy.TenantExport.v1：准确target tenant下已分类identity/security/roles及白名单租户元数据，明确用途/接收人、独立tenant能力、不可变制品/原操作/TTL；当前披露重查收件人/来源权限/绑定/期限/撤销和最终会话，逐源真实Manifest。没有伪subject，旧直接租户HTTP散流退役410；其他Owner全量导出和新租户载荷物理保留/处置及恢复仍为真实余项，整体PARTIAL。 11项新增实际PostgreSQL18场景、5项受影响主体导出回归（含新增两人Chrome实际下载与既有主体Chrome）、18项Vue行为及vue-tsc通过。 [报告](../../evidence/W322-01/DELIVERY.md)。

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM业务历史、FILE及其他来源副本、整租户全部Owner导出、新租户制品受控物理保留/处置及恢复、WAL/备份/外部副本和完整环境恢复。W306、W249及已闭环FORM卡不重复；24h继续相邻真实余项，W310仅预算末尾。 当前采样74946秒，下一79200秒校准。最终分支/工作区/席位见wave322-final-verification.json。
