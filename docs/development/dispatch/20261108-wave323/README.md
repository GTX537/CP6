# W323 租户导出载荷受控物理处置

W323独立Main.Privacy.TenantExportPayload.v1实际处置：准确租户制品、明确保留政策、独立申请/批准/执行、目标租户所有主体有效hold屏障及ABA；真实删除字节、撤访问、最小原回执/墓碑同commit，原生无所属删除/回填拒绝。无伪subject；本窗不实现备份恢复适配，有该Owner墓碑时现恢复入口明确失败并保持隔离，旧批准随source epoch失效。

Main `2f4eaab416eac5eff12cda327cdb9f76262ac290`，Handoff `34fcd5372b3280a1961f55f96fdf2945b20c3b57`，COORD单人固定DEV1/DEV2；W322 Main777/Handoff298已核两仓采用、准确清理和官方释放，实测3254秒。本窗累计75502秒，余约3.03h。

[范围与实施](../../contracts/tenant-export-payload-disposition-v1.md)。

## DELIVERY

W323完成Main.Privacy.TenantExportPayload.v1当前库准确租户载荷处置：明确保留政策/独立申请批准/当前最终执行、全目标租户所有主体hold与ABA、真实字节删除/撤访问/最小原回执/墓碑同commit及原生防回填。无伪subject。新墓碑推进restore_epoch，未采用的新Owner恢复明确503并保持候选隔离，历史已提交原结果仍可恢复；不冒充备份或外部副本已清。 14项新增实际PostgreSQL18场景、13项受影响租户导出及主体载荷/安全字段恢复回归（含新增处置与既有租户下载Chrome）、23项Vue行为及vue-tsc通过。 [报告](../../evidence/W323-01/DELIVERY.md)。

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余租户载荷恢复实际适配、未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM历史、FILE及其他来源副本、整租户全部Owner导出/处置、WAL/备份/外部副本和完整环境恢复。W306/W249/七天及已闭环FORM不重复；24h继续相邻真实余项，W310预算末尾。 当前采样77196秒，下次79200秒校准；最终分支/工作区/席位见wave323-final-verification.json。
