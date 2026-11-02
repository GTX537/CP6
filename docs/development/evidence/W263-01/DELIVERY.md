# W263-01 / 第五期三天旧元数据接入

第五期三天W263完成旧CodeGen元数据显式接入：保留原ID/表列/审计原件，原件指纹CAS，同事务建立V1与原操作恢复，接入后数据库禁止旧写；真实会话HTTP及Vue接入/未知恢复已验证。

[三天计划](../../planning/20261102-three-day-cycle5.md)和[接入合同](../../codegen-metadata-adoption-v1.md)。基线Main `cf2c90f3ec678eb574c0da8950b1690a798274e2`，COORD单人，codex/w263-01一个任务分支，DEV-1/Main及DEV-2/Handoff固定目录复用。

## 实现及实际验证

D1读取旧原件的完整实体指纹，以旧GenTable.Id接入同租户V1；表/列事务锁串行检查来源，原ID/列/审计字段留在原表，不删、不改。共用W262 Owner事务/IAM门、权限边界与原回执，增加明确接入命令类型和不可变adoptions关联。接入提交同事务生效旧写禁令；已接入V1可正常CAS，普通Save拒绝原旧ID绕过和他人旧名称。旧表列冻结包含移入/移出、删除和截断；改V1名称不释放旧原件。

D2真实Main当前会话HTTP新增读取旧原件和明确接入，严格JSON、不接受客户端替换Document/TenantId；原操作恢复仍使用saves/read。D3面板按ID读取并明确接入，草稿修改不被接入偷带，成功后保留草稿继续V1保存；source变化412要求重读。原命令发送前保留、未知刷新/查询/原样重试、W262已有pending兼容。

13 actual PostgreSQL18 checks PASS/0 skipped (6 adoption cases and7 affected W262 regressions);Vue type-check exit0;new adoption and W262 normal-save actual Chrome/Vue checks PASS with API/identity fixtures. No failed tests in W263. Owned PG/browser stopped;no Actions/deployment.

实际PG证据D:/CP6-local-test/wave263/foundation/adoption-20261102T084255Z.json，模拟用户/租户/角色和业务元数据，真实本地PG18事务/锁/迁移与Main Program登录cookie/CSRF。新增6项覆盖：逐行JSON证明旧原件/ID/审计保持，接入原键与后续CAS/历史，旧表列改删和移入移出/截断拒绝；跨租户404、原件变化412、旧ID绕过拒绝、非法旧模板不损原件；最终检查失败回滚、真实commit后响应丢失恢复、撤save/view；真实旧列写持锁导致接入等待并拒绝旧指纹，反向接入持锁时旧Save等待后409；实际HTTP完整接入→继续保存、伪造Document/CSRF/跨租户/注销；带已有V1历史/命令和旧表列的前向迁移及重复迁移保留。另7项W262受影响Owner/HTTP/迁移回归通过。没有借用跳过项。

Node22.22.0 vue-tsc --build退出0。实际Chrome/Vue两组页面均通过：新脚本同目录check-page.mjs，adoption-page-01/result.json；W262正常保存脚本在当前代码重跑，save-regression-page-01/result.json。页面API与身份明确模拟，未宣称浏览器→真实PG完整部署链。新页面证明读取零写、原ID、指纹412保留草稿、明确接入不发送Document、接入后CAS、unknown刷新/404/后续403仍保留/原命令重试及升级前pending兼容。W262页面回归包含身份切换、晚到响应、存储失败不发送等。adopted.png已人工检查，故意失败场景的提示toast为测试注入结果。

集中审查补充旧MenuName/Label尾空格及可空字段的直接期望值检查后，定向复跑原件保持一项通过，证据adoption-values-20261102T085122Z.json；复用其余12项成功结果，唯一场景数仍13。两个自有PG实例均已停止。

本轮没有失败/跳过测试，既有不相关编译分析器警告保留。自有PG已核对身份后停止，原数据/日志保留；测试宿主、浏览器/Vite均退出。W261纯比较/模板未改，复用不变证据。

## 集中审查、余项与生命周期

一次作者SPEC/QUALITY审查覆盖原ID/租户/权限、原件一致性与原命令、两入口并发、写冻结/迁移、后续CAS、UI未知/草稿/旧pending和准确状态同步。表锁按元数据低频管理使用，沿用10秒超时；不自动重试/吞错，不新增内部证明层。

显式接入能力已交付；存量逐对象采用仍需用户在页面执行，非法旧模板需在旧入口修正，不冒全部旧数据已迁移。当前V1没有列表/删除；旧读取仍历史原件。文件实际应用/路径和并发保护、编号实际业务消费者/旧域切换及完整规则生命周期、CRM过期事件消费者/投递和整卡联合验收仍余项。原375卡27DONE/2IN_PROGRESS/346PLANNED、原59局部余56、125完整目标0与247正式验收NOT_RUN保持。W262及前轮按最终proof核销，不重复。

本提交是正常PR采用前快照；Main/Handoff精确远端采用、准确本地/远端分支删除和官方finish由C:/Users/tt/CP6-planning-reports/20261017-foundation/wave263-final-verification.json核销。没有Actions运行/取消/触发变化，无既有环境迁移或生产部署。固定目录保持复用。
