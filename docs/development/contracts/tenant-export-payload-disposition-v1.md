# W323：准确租户导出载荷的受控物理处置 v1

W322只关闭访问，不曾交付物理清除。原WP02-15仍含保留政策、独立批准、hold、真实不可逆效果/最小回执和备份副本控制。新租户制品没有主体字段，不能复用W320的subject语义或以Guid.Empty/TenantId伪装主体。

## 已批准目标内的设计与实施

brainstorming在用户授权的连续单人24h窗口采用独立Main.Privacy.TenantExportPayload.v1，复用现PrivacyDispositionOwnerV1事务/IAM根/当前权限/最终会话、MainOwnerOperationJournalV1与已接受的政策/计划状态协议。直接包装主体处置会混淆身份；一次性无审批删除不能满足原合同。本实现独立tenant载荷scope、schema、原命令和效果，绝不添加资格包装/重复证明层。

每计划准确ArtifactId和同operator tenant可见的原tenant制品，冻结准确Environment/OperatorTenant/TargetTenant/原SHA/长度/用途/捕获时刻、原生payload revision、政策版本和hold head。元数据不返回原PII。政策明确PolicyId、准确目标租户、BasisRef、Enable、MinimumRetentionDays(0–36500)和RequireAccessClosed，按原回执可靠UTC CapturedAt与数据库时钟计算最早时刻；模拟依据不宣称法定保留期限。新tenant-payload-read/policy/prepare/approve/revoke/execute分别授权，不自动授角色；申请人不能自批。批准/执行按当前政策/hold/原行修订/批准者权限重判，旧政策和hold ABA均使旧批准失效。

## 整租户副本的hold

该载荷包含多主体资料，采用保守且明确的整份副本规则：同环境、准确TargetTenant下任一主体当前有效hold均阻整份载荷处置，跨operator tenant生效，过复查时间仍有效。聚合只返回最新全租户hold事件版本、当前有效/逾期数量；每主体按最新序列判当前，目标租户任一hold变更改变聚合HeadVersion，解除再启用等ABA不恢复旧批准。沿用现主体hold权威写入入口，不伪造租户subject、不增加另一hold事实源。其他目标租户hold不干扰。

## 原生实际效果和恢复限制

实际同事务写最小执行/墓碑/原命令并DELETE准确载荷、关闭访问，回执记录真实RemovedBytes与原revision，保留原制品/Manifest/审计和操作元数据，不复制PII。真实COMMIT丢答按原UUID取结果，读失败不得重复执行，关闭未到原UUID阻迟到；撤execute后当前read仍可接回。原生表写加入现IAM根，未所属DELETE/TRUNCATE与已墓碑回填拒绝；延迟约束拒孤立执行、墓碑或未删字节/未关访问/缺原命令。原生DELETE也按准确目标所有主体hold判定。对现私有schema/类型直接信任，不为内部输入重哈希。

本窗明确只交付当前数据库此Owner效果，备份/WAL/外部已下载副本及候选恢复适配仍余。新政策/墓碑推进现restore_epoch，旧候选批准随真实源变化失效。有新Owner墓碑时，源恢复预览/申请/审批/执行最终入口明确PRIVACY_RESTORE_TENANT_PAYLOAD_OWNER_UNSUPPORTED(503)，在候选访问/开放前快速失败，保持隔离；不填伪subject、不写NoOp恢复、不把旧Owner的成功冒新载荷清除。未来适配独立任务。无新墓碑时原恢复wire/摘要/行为保持，现两库UNKNOWN恢复责任不变。

## API、页面和验证

新独立typed API在现privacy-dispositions-v1/tenant-export-payloads分面，严格JSON、当前平台会话、no-store；页面准确制品/政策/申请/独立批准/执行、聚合hold、当前访问与真实删除字节、剩余副本范围。保留身份分区UUID，取消零写/跨标签协调、身份改变丢迟到、原回执不被刷新失败抹去；不在前端持久化PII或整回执。

先实际红测（当前未所属DELETE仍被允许），再PG前向DDL/Owner/严格API/页面；本地实际PG18精确删除/原回执、跨tenant/独立批准/当前撤权、政策/hold ABA和逾期、原生写者/防回填/孤立效果、COMMIT答复丢失/最终会话/审计回滚、实际认证HTTP/两人Chrome与受影响Vue/类型，按影响复用既有成功证据。重点确认新Owner墓碑使旧恢复真实失败且候选不开放，无墓碑原路径保持。一次COORD集中SPEC/QUALITY审查，单报告及roadmap/四状态同步，正常Main/Handoff采用、准确分支清理及官方finish。无Actions/生产部署/既有数据库迁移或处置。

原37536DONE/2IN_PROGRESS/337PLANNED，原59余47，125/247不变，WP02-15整体仍PARTIAL。W322实测3254秒(54分14秒)，低于1.75–3h低置信估计，主要复用既有事务/页面。W323含整租户hold/native效果/恢复拒绝和二人UI，估1.75–2.75h低置信实际执行；约21h已用，下次79200秒(22h)校准，W310在预算末尾盘点核销。不空等、不以单窗替代24h，不新开周期。
