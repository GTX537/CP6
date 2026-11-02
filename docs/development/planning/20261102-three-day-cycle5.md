# CP6 第五期三天执行计划：旧CodeGen元数据显式接入

COORD单人按writing-plans/executing-plans执行。W262远端Main cf2c90f为基线；一个完整任务W263-01、同一分支、一次集中审查，复用DEV-1/Main与DEV-2/Handoff。估算三天各6小时核心+2小时缓冲，实际完成以验收/生命周期为准。原WP02-16/SYS-NUM-01的元数据身份、scope、明确保存、CAS和unknown合同继续适用。

## D1：旧原件接入事务

- [x] 新增CodeGenMetadataAdoptionV1.cs，Owner改partial共用原事务/权限/原回执和Persist写入；读取旧Table+Columns生成SourceFingerprint和固定模板Document，保留所有列字段/顺序，原表列ID/审计字段留在旧表原件。Adopt(OperationRef,SourceFingerprint)不接收替换Document，以旧GenTable.Id作为MetadataId；同一原操作先恢复，再判断当前原件和目标未采用。
- [x] 新增20261102090000_CodeGenMetadataAdoptionV1.cs前向迁移：adoptions绑定tenant/原ID与V1文档，不可改删；旧表/列在已接入后拒绝更新/删除/插入/移动/截断。使用原表和列的事务表锁，在读取指纹/接入最终决定期间与旧写入互斥。只显式接入目标行，不改写旧行或迁移既有环境。
- [x] Save允许已接入对象按原ID继续CAS；拒绝用旧ID另建或用他人旧名称接管。接入后改名仍保留旧原件和名称归属，不能删旧原件释放历史。新增PG行为测试覆盖原件无损、旧写拒绝、旧写竞争、指纹变化412、同键恢复/异义、跨租户、回滚与未知提交；相应W262路径回归。

## D2：当前会话HTTP

- [x] 在现CodeGenMetadataControllerV1增加GET legacy和POST legacy/adopt，复用当前可信租户/原会话及CSRF/NoStore；严格请求DTO不接收TenantId/Document。未找到404、原件已变412、已接入409，非法模板输入明确拒绝且原件保留。原结果复用saves/read。
- [x] 真实Main Program/登录cookie/实际PG验证读旧原件→明确接入→V1继续保存、旧Save拒绝、跨租户、CSRF、撤save后的原结果恢复、注销拒绝；所有业务输入为模拟，已有正常迁移和W262证据按影响复用。

## D3：页面与收尾

- [x] CodeGenMetadataPanel和api/pub/codegen.ts加入读取旧原件、按读取原件接入V1。保持读取ID，不把草稿编辑偷偷纳入接入；接入后当前草稿可重新预览按V1保存。pending兼容已存在W262保存记录，新增接入命令类型，发送前存原请求，刷新/查询/原样重试，身份变化隔离和412保留草稿继续有效。
- [x] 实际Chrome/Vue夹具检查接入原键/原ID、来源冲突、未知刷新/恢复、接入后CAS、旧pending兼容；Vue类型检查。相关PG/HTTP运行一次、一次任务级集中SPEC/QUALITY审查，报告真实边界与余项。
- [ ] 同步roadmap/生成视图/四状态，核销W262最终完成，正常PR采用/远端核对、准确任务分支清理、官方finish释放席位；原375卡状态不因局部接入改DONE。

执行验证命令：本地run_wave263_main.py adoption 'FullyQualifiedName~Codegen_metadata'；Node22 node_modules/vue-tsc/bin/vue-tsc.js --build；Node22 docs/development/evidence/W263-01/check-page.mjs。PG测试使用自有隔离实例，失败先分析原因只补受影响检查。新增schema仍共享Owner命名空间，TenantId/当前权限不变；没有Actions、生产部署、文件应用或编号消费者切换。

三阶段实现及本地验证/集中审查完成；最后一项状态同步完成，合并和席位收尾由wave263-final-verification.json核销，提交前VERIFYING。
