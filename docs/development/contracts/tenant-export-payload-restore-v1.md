# W324：租户导出载荷的真实备份恢复采用 v1

原WP02-15仍含备份后处置不得复活。W323已交付当前库准确租户载荷实际DELETE/最小回执/墓碑，因新模型无subject，现恢复明确503隔离，不能借主体恢复Unit伪填Guid.Empty或TenantId。本窗完成此准确Owner的候选重施，其他Owner/WAL/外部副本和完整环境恢复仍余。

## 连续授权内的设计

brainstorming采用独立PrivacyRestoreTenantPayloadUnitV1和父结果TenantPayloadUnits可选数组，不更改原RestoreUnit.SubjectId身份。Scope增加默认0/省略字段TenantPayloadTombstoneCount，结果数组null时省略；无新Owner的原wire/摘要保持。沿现当前源政策、全主体hold、IAM共同根/当前会话、独立恢复批准、原键/两次commit责任和候选开放门，不加资格包装或内部重复证明。

当前源按每准确墓碑原operator tenant/policy/target读取当前Enable/目标/最短UTC保留期限，依据原可靠CapturedAt；目标全部主体最新有效hold跨operator tenant阻重施，复查过期仍有效。政策/hold/墓碑变化推进现source epoch，旧批准和开放证明失效。只在这些真实当前条件与独立批准满足时执行，不以候选旧政策/hold替代当前源决定。W323正向恢复unsupported屏障由真实适配替换；历史原结果和未知提交责任仍可接回，不能据旧结果开放过期源版本。

## 外部候选边界与原生效果

候选是外部恢复数据库，CandidateGate继续拥有准确物理身份/环境/归属和SourceRoot→CandidateRoot顺序。一次按准确ArtifactId读实际元数据与payload revision/长度/SHA，和当前源已接受墓碑scope核对；不内部重新哈希源回执。FULL JOIN同时识别孤立payload，缺元数据而有字节必须明确拒绝，不能冒整份缺失/0。已有墓碑必须同源plan/scope/receipt。

有真实载荷为REAPPLIED/实际RemovedBytes；元数据存在而载荷缺失、或整制品不在原备份为ABSENT_FENCED/0，保留其真实区别和准确TargetTenant，均安装同源防回填墓碑。无伪subject，原制品/Manifest和审计不改写；只删除此Owner字节并关闭访问，不称其他业务资料已清。父回执含新独立tenant Unit，实际DELETE/撤访问/墓碑/fence/开放标记同候选事务。前向迁移加入同writer_xid的restore_owns和延迟效果约束，拒孤立或静默跳过DELETE的父成功；新Owner缺迁移/原生fence不可用明确503，保持隔离。必要原生fence检查只在外部候选边界，不给内部调用逐层套资格。

候选可能保存已过时的active hold，不能覆盖当前源已明确解除的决定；只给准确父恢复回执所属效果允许采用当前源决定，普通live/native删除仍按现hold拒绝。SourceRoot及CandidateRoot两次真实提交保持原UNKNOWN和当前开放证明责任，不承诺跨库原子提交；候选已提交而源原结果未保存时继续隔离，撤execute后当前read可接回原结果。

## 页面、验证与收尾

恢复页增加准确租户载荷墓碑数和独立Unit，清楚显示实际删除字节、备份原本缺失0和PARTIAL；确认/取消/UUID身份分区/原回执与刷新失败行为直接沿用。当前处置/导出页说明已接本库与恢复流程；历史W323回执的未接适配说明保留为当时范围，不改写历史。其他Owner/WAL/已下载外部副本继续列余项，不在产品流程堆内部实现说明。

先红测当前unsupported503；随后前向DDL、独立Unit/Owner候选适配、源政策/hold及父结果/页面。实际隔离PG18/pg_dump/restore覆盖准确删除/后生制品缺失0、同revision损坏字节/孤立载荷拒绝、当前源政策/hold/ABA与候选过时hold、原生fence缺失、最终会话/静默DELETE actual commit拒绝、候选及源COMMIT答复丢失/原键恢复、认证HTTP与两人Chrome、相关Vue/类型及无新Owner旧wire/实际恢复回归。只按明确风险定向验证，一次COORD集中SPEC/QUALITY自审，单报告及roadmap/四状态同步，正常采用/准确分支清理/官方finish。无Actions/生产部署/既有数据库迁移或恢复。

原37536DONE/2IN_PROGRESS/337PLANNED，原59余47，125/247不变。W323实测2143秒(35分43秒)，再次低于1.75–2.75h；原有事务、页面和原生协议复用是主要原因。W324仅新增恢复采用但含新独立结果形状/候选边界，估0.75–1.5h低置信实际Codex执行；原24h尚余约2.4h，下次79200秒校准，预算末尾W310核销。较快继续真实余项或实际盘点，不空等凑时、不重开周期。
