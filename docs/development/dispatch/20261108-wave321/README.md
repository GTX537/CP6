# W321 安全事件已分类字段处置

原WP02-15真实余项：Main.SecurityLog.Fields.v1按准确事件原件及已接受的UserName/ClientIp字段分类实施独立政策/预览/申请/批准/实际清理、全主体hold、原结果/墓碑和恢复采用。保留事件/stable subject/租户身份及其余未核字段，不宣称整个安全事件或整个隐私任务已匿名化。旧CreatedAt无时区，资格只按明确UTC NotBefore政策，不推算法定期限；单事件准确边界，不暗含批量或按同名归属。

Main `90bf9e9f23cab088e9e5dd7c82cc2de58190a7c6`，Handoff `f2c29f43e8e46425255f79bf0aafc17773150613`，COORD单人固定DEV1/DEV2，W320两仓实际采用/准确清理/官方释放已核销。18h采样见wave321-18h-calibration.json；本窗登记累计65842秒，尚余约5.71h；2.5–4h低置信实际Codex执行。原375/59及125/247不变。

[设计与实施顺序](../../contracts/security-log-field-disposition-v1.md)。

## DELIVERY

W321完成Main.SecurityLog.Fields.v1：准确安全事件已分类UserName/ClientIp的显式UTC政策、独立申请/批准/实际置空、全主体hold、最小原回执/墓碑与原生防回填；恢复候选重施准确字段处置，已空及事件缺失分别据实记录，两库UNKNOWN责任和原键恢复直接复用。事件身份、原时间及未知内容保留，仅此Owner，整体PARTIAL。 20项新增实际PostgreSQL18场景、14项受影响认证/身份/载荷恢复回归（含真实认证HTTP、两条新增和一条既有Chrome）、24项Vue/API行为及vue-tsc通过。 [报告](../../evidence/W321-01/DELIVERY.md)。

原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余安全日志未分类上下文/原因/设备与操作者信息、不可归属事件、授权/组织引用、OA/PLM与CRM业务历史、FILE及其他来源副本、整租户导出与受控处置、WAL/备份/外部副本及完整环境恢复。W306和WP12-03/04已闭环，不重复；24h继续相邻真实余项，W310仅预算末尾。 当前采样70055秒；下一72000秒校准。最终分支/工作区/席位见wave321-final-verification.json。
