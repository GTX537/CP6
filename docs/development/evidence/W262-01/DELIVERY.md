# W262-01 / 第四期三天CodeGen版本化元数据

第四期三天W262完成CodeGen新元数据V1：PostgreSQL租户版本保存/CAS、不可变历史与原操作恢复、真实当前会话HTTP、Vue冲突和未知结果恢复。旧数据不自动接管，生成文件未应用。

[计划](../../planning/20261102-three-day-cycle4.md)分D1存储Owner、D2当前会话HTTP、D3页面恢复，同一任务分支codex/w262-01从Main `77184c0e7568272f71ccee9fd47fa0cddf928f5e`连续执行。COORD单人，固定DEV-1/Main与DEV-2/Handoff，无新增工作树。合同见[元数据V1](../../codegen-metadata-v1.md)。

## 实现

D1新增固定DTO和独立PG事务Owner，使用原租户/主体/MetadataId，版本CAS、新旧名称互斥、不可变版本和原请求回执。名称按已有PG比较语义；完整JSON及长操作键bytea，不缩窄可空/Unicode/NUL字段。原操作先查，撤save后有view仍可恢复；其他主体不能拿原操作，当前权限及会话在最终决定点核对。提交后丢响应明确UNKNOWN，不改键重试。新迁移只建V1共享Owner schema和旧表写入口触发器，无旧数据接管或租户身份删改。

D2新增当前、历史、保存及原查询HTTP，绑定真实Main当前原会话，严格JSON与CSRF，独立DI scope使用同一事务IAM完成门。旧Save仅新增明确V1名称冲突映射。D3页面先预览再版本保存，发送前保留原请求；未知锁定新保存，原查/原重试，刷新恢复；412保留草稿并要求显式读最新，身份变更清空旧草稿且拒绝旧响应。可空和MenuName等加载字段保存保持。存储失败不发写请求。

## 实际验证

7 actual PostgreSQL 18 checks PASS/0 skipped:6 Owner/real-session HTTP cases plus1 nonempty legacy upgrade/idempotence case. Vue type-check exit0;actual Chrome/Vue behavior PASS with API/identity fixtures. Unchanged W261 comparison evidence reused. No test failures in W262. No Actions/deployment.

实际后端输入为模拟租户、用户、角色、元数据；使用自有本地PG18隔离实例和真实应用Program/登录cookie/CSRF，不是生产环境。metadata-20261102T075853Z.json的6项包含同版本竞争一胜一412、同键异义、历史/原结果稳定、长键/NUL/尾空格、跨租户与名称冲突、旧保存拒绝、撤save/view、最终检查失败回滚、真实Commit后拦截响应丢失恢复、匿名/CSRF/伪造租户、注销和等锁期间撤凭证/撤save。upgrade-20261102T080842Z.json另1项从上一迁移建立有旧表/列的库，升级和重复迁移后逐行JSON完全一致，新Owner无自动导入。两个自有实例已由fixture核对身份后停止，数据/日志保留。

本地证据目录D:/CP6-local-test/wave262/foundation；final-validation.json汇总真实来源。Node22.22.0的vue-tsc --build退出0。页面脚本同目录check-page.mjs，实际Chrome/Vite/Vue，API及身份响应明确模拟；page-01/result.json记录15次相关请求与16类行为，包括未知刷新、404仍待确认、拒绝重试仍保留、原请求逐字段相同、412草稿、显式重读、null/NUL字段、存储失败零发送、原结果查询、身份切换与晚到响应、权限隐藏和不调用旧Save。不是浏览器到真实PG的全链部署验收。

W261比较服务/组件及模板未改，复用其42项CodeGen测试与比较页面证据，不因提交或SHA变化重复运行；本期新PG/HTTP/UI证据覆盖实际改动。W262没有失败/跳过测试，没有增加Actions。编译历史分析器警告保留。

## 集中审查与真实余项

一次作者SPEC/QUALITY集中审查完整任务：租户/原主体/权限、CAS/原操作顺序、提交未知与回滚、PG名称锁和旧数据边界、前向迁移、UI存储/身份/412、无文件应用及台账准确性。普通验证保持本地，未改依赖、工作流、分支保护或生产发布门禁。

D1/D2/D3限定实现已完成。WP02-16余项为旧元数据明确接入、实际文件应用及路径/并发保护、实际业务编号消费者/旧域切换和完整规则生命周期、整卡联合验收；当前V1没有列表/删除。CRM新过期事件消费/投递仍单独余项。原375卡27DONE/2IN_PROGRESS/346PLANNED、原59局部余56、125完整目标0及247正式验收NOT_RUN保持。七天/W249、前面三期和W261不重复。

## 生命周期

本提交为正常合并前快照，Main/Handoff待正常PR、远端采用、准确本地/远端分支删除及官方finish。最终结果由C:/Users/tt/CP6-planning-reports/20261017-foundation/wave262-final-verification.json核销。本地测试宿主、浏览器/Vite和自有PG均已结束；固定目录保留复用。没有Actions运行/取消/触发变更，没有既有环境迁移或生产部署。
