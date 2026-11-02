# CP6 第四期三天执行计划：CodeGen版本化元数据

COORD单人，采用writing-plans/executing-plans。以W261远端Main77184c0为基线，一个完整元数据交付任务W262-01分三阶段；同一分支连续实现、一次集中审查，DEV-1/Main及DEV-2/Handoff固定席位。本期估算三天、每日6小时核心+2小时缓冲，实际完成由验收和生命周期证明。

## D1：PostgreSQL元数据Owner与原结果

- [x] 在CP6.Core/Services/Pub/CodeGenMetadataV1.cs及CodeGenMetadataOwnerV1.cs定义固定DTO和独立事务Owner；使用可信TenantId、原MetadataId、ExpectedRevision、OperationRef，view/save权限边界明确，先查原操作再判断CAS，新操作才要求save。
- [x] PostgreSQL前向迁移cp6_codegen_metadata_v1，新文档/不可变历史/不可变命令结果同事务。命令长键及完整JSON为bytea，保留Unicode/NUL/字段值；操作哈希索引同时核对原键和原输入。版本单调，原主体恢复原结果；提交未知明确503，不能改键盲重试。
- [x] 新元数据按租户/实体名唯一，沿用旧PG名称比较语义；不自动导入旧GenTable/GenColumn。新旧同名写入在原表锁与数据库入口串行互斥，旧已有同名明确拒绝新建。历史旧读写保持；新版本文档不得被旧Save另建同名覆盖。
- [x] CP6.Tests/CodeGenMetadataPostgreSqlTests.cs实际PG验证租户、同版本竞争、原结果/同键异义、提交前回滚/提交后丢响应、长键/NUL无损、旧数据保持与双入口互斥。当前共享IAM门采用现有合同，不新增证明包装。

## D2：真实会话HTTP

- [x] CP6.WebApi/Controllers/Pub/CodeGenMetadataControllerV1.cs新增当前版本、保存、历史版本及原操作查询；独立DI scope绑定可信租户与IamAdministrationHttpV1.CurrentActor，使用同事务当前会话/权限最终检查。请求不接受租户/主体，JSON边界严格。
- [x] 真实Main Program、登录cookie和CSRF执行模拟元数据场景；覆盖匿名、跨租户、撤save后原操作恢复、撤view/注销拒绝及等待IAM门期间撤权。新文档是本期采用范围，历史数据迁移及真实文件应用仍独立任务。

## D3：页面与恢复

- [x] cp6.web/src/api/pub/codegen.ts和CodeGenMetadataPanel.vue接入版本化元数据；CodeGenView保持独立即时预览/比较。新保存生成固定operation并保留原输入，unknown期间禁止发新保存，可查原结果或原键重试；412保留草稿并明确重读版本，不静默套用。
- [x] 读取已存V1文档载入元数据后按原preview/comparison展示；浏览器会话/租户变更拒绝旧回复。页面不把旧无版本Save作为V1保存；旧API不被静默迁移。
- [x] 相关PG/HTTP和页面行为验证、类型检查；复用W261不变比较证据。唯一交付报告、台账/生成页/四状态同步，正常合并/远端核对、准确分支删除和官方finish。本期不宣称完整WP02-16或原375全完成。

数据库仅PostgreSQL；模拟业务验收注明输入/环境。新增schema为共享Owner命名空间，每行TenantId及授权保持，不宣称一租户一schema或权限隔离。无Actions、生产部署、既有环境迁移、自动文件应用或旧业务编号切换。

D1/D2/D3实现及本地验证完成；最后一项的审查/状态同步完成，正常合并和席位收尾由wave262-final-verification.json核销，提交前保持VERIFYING。

W263接续按wave262-final-verification.json核销：第四期实际全部采用、准确分支清理和官方finish完成；此处只归档最终事实，不重复执行。
