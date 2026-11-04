# W313 精确处置预览批准与hold

WP02-15/SYS-PRIV-01：实际Main身份资料处置的版本化政策、精确主体预览/计划、独立批准与带依据/复核期限的hold。复用现有identity_admission_revisions真实用户版本与IAM共同门，冻结目标/来源/作用字段/政策/hold版本和scopeDigest；旧计划遇目标/政策/hold变更不可再批准。申请人不能自行批准，仍有平台权的目标先独立受控撤权；不由处置暗中撤权。当前read恢复原操作与新prepare/approve/hold/policy能力分离，UNKNOWN查询/关闭迟到；真实页面展示精确范围、不可逆影响、未采用Owner和逾期复核风险。W314消费批准计划实施实际Owner效果，W315墓碑/恢复；本窗不执行擦除、不把批准状态当业务处置完成。

Main 45db7764a6b9f3d5137c699508021198c18b60fc; Handoff a9759c5c8c3006825442262db9ba69c2c5a775ec; COORD单人DEV1/DEV2。预计0.6–1.2小时实际执行，低置信；W312实际1829秒证明此前1.5–2.5h估计偏高，本窗按当前真实范围下调；W312实测1829秒，累计40105秒；43200秒校准。原375=33DONE/2IN_PROGRESS/340PLANNED，原59余50。

验证：实际PG政策/目标/hold精确CAS、独立批准/撤权/回滚、原键恢复/关闭迟到、跨租户与共同门；真实认证HTTP、Chrome预览/批准/hold流程及相关Vue。一次集中审查；本地新建隔离PG迁移，不迁移既有数据库，不运行Actions、不部署。政策与hold使用明确Owner依据引用及版本；模拟依据不冒充现实法律签认，不引入默认法律保留年限。实际Owner执行/跨Owner处置/恢复tombstone留W314–315。
