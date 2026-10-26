# W147 版本化单位 Owner 与原产品单位绑定

> COORD按已获持续授权，单人在当前对话使用executing-plans实施；沿项目精简节奏做定向验证和一次任务级SPEC/QUALITY。原七天目标保持ACTIVE。

目标：为WP04-33建立真实可维护、可停用、有不可变版本和原操作恢复的单位配置Owner，并将实际原产品身份版本显式绑定到准确单位版本。继续为工程/冻结/工单/排程提供前置，不提升完整业务验收。

基线：Main cbb2b17197349a3a8c202b137d3ffe8bbfba0dc6；Handoff 229645ace197e60ca980357ca30e2566b3bec331。四仓规则、origin、当前引用、开放PR、任务Actions和固定席位预检wave147-preflight.json通过。GitHub当前开放BUG搜索为空。DEV-1 codex/w147-01与DEV-2 codex/w147-handoff按官方start领取；无增树/重绑。W146已完整收尾，本轮只按最终proof一次归档其快照和限定记录。

|任务|估计实际执行|
|---|---|
|原设计/源码核对与准入设计|20–30分钟|
|单位原版本、共同事务、原结果、精确转换、产品显式绑定及页面|80–110分钟|
|隔离PostgreSQL/真实HTTP、定向页面/恢复、类型构建|50–60分钟|
|一次集中审查、台账、正常PR/分支/finish|30–40分钟|

合计180–240分钟。早期120–180分钟仅单位提供者的暂估；本轮纳入实际产品绑定，按完整范围更新，保留原七天所有出口。最终Main START至finish不与Handoff叠加；只读前置核查与登记时间分别说明。

## 已确认设计与选择

当前M_GenericCode的Unit组仅存下拉代码，ProductMaster.QtyUnit只有字符串；均未提供量纲、精确换算、允许精度或停用许可。采用新的原生单位profile Owner：Tenant+独立UUID为身份，稳定业务代码和创建者不可重用/改绑，追加版本为唯一配置来源；不从通用字典的名称、Attr/Num字段或相同代码推导资格。原字典保留其历史用途。

以不可变版本和持久头建立配置，优于在泛用Num1/Attr中塞无版本语义；全库替换旧单位写者属于另一项迁移，本轮不执行。产品采用通过显式绑定原Product UUID及真实product source版本到准确profile引用；T_ProductMaster仍是唯一产品主账，不复制平行产品表，不以相同代码自动匹配为业务批准。

### 版本化单位协议

新UOM-PROFILE/1保存原UUID、精确Code、显示名、DimensionKey、QuantityScale、可空PackMultiple、Mode(BASE/RATIO)、准确BaseRef、正整数字符串Numerator/Denominator及Enabled。Code和DimensionKey按原样比较，不以大小写/名称猜同一身份。人类显示名可trim/NFC；配置或状态改变必须CAS原版本并追加新版本，停用也不释放代码。历史和原操作不可覆盖/删除/TRUNCATE。

BASE明确1/1且无上级；RATIO必须显式引用同租户、同量纲、当前启用的BASE原版本，不支持链式或仿射换算。更新BASE后，旧派生引用不自动升级；后续当前采用拒绝，原历史仍可读。QuantityScale与小数/整数串技术上限1024，覆盖已接受PRICE0..18和EST0..12能力，不按CLR decimal或旧decimal(18,4)截断。比例以BigInteger计算，结果只有在目标精度/包装倍数内能精确表达时返回，否则422；不默认为1、不舍入、不跨量纲。后继业务消费者仍独立检查自己的原精度上限。

### 权限、事务和恢复

独立erp-uom-profile:query/add/edit能力，仅登记定义，不自动授权。范围1本人按创建者原UUID，2/3/4使用明确uom-profile部门绑定，5租户；创建部门来自当前可信creation-context，不从编码或当前部门推断。当前会话、租户、角色、部门版本及操作权限在Main实际事务内获取并最终重核；历史版本和原操作结果也受当前对象权限约束。

复用OperationsOwnerV1、MainOwnerOperationJournalV1和当前IAM映射协议；单位schema单独原生表锁顺序固定，查询SHARE、写SHARE ROW EXCLUSIVE，等待结束后才观察最新版本，保护并发/原生写者。业务版本、部门创建绑定、审计和不可变原结果同一事务提交。相同OperationId与准确输入返回原结果，异体409；UNKNOWN只查原操作，未提交可关闭并挡迟到写；不自动重发。浏览器只保存按可信Tenant/Actor分隔的公开恢复引用，页面锁协调多个标签，身份切换隔离后续响应。

### 原产品单位采用

绑定命令须当前erp-product:edit及独立uom-profile:query，分别应用两对象数据范围。精确固定原Product UUID、product身份源版本及QtyUnit原代码和profile版本；代码不相等、原产品缺源、停用/缺profile/基准漂移均拒绝，不补EA或通用字典fallback。绑定追加原版本，与原操作/审计同事务；当前读取在产品与profile共同门内重核。产品头ABA、profile更新/停用或部门撤回后，新采用不能拿旧绑定当当前许可，旧绑定历史保留并准确说明当前适用性。

新产品单位来源表示只证明该绑定/该事务的事实；原CommercialProductIdentityV1 wire保持，正式制造/工程映射、币种、全产品子图和最终业务HoldExact仍有各自前置。不能因此解除SCHED_OWNER_PROTOCOL_NOT_READY或Transfer等门。

## 实施步骤与文件

- [x] 先在CP6.Tests/Erp/UomProfileOwnerPostgreSqlTests.cs通过真实Kestrel/PG调用尚缺的/api/uom-profiles/owner-v1/creation-context、commands，保存404红灯；既有Harness/真实登录复用，不用假profile。再写完整变更、停用、原结果、转换和产品采用行为。
- [x] CP6.Persistence.PostgreSql/Migrations/Core/20261026153000_UomProfileOwnerV1.cs新增受保护原配置版本/头、产品绑定版本/头和原部门kind，保留所有旧kind；菜单/能力定义无Role授权。仅正向迁移及隔离测试库迁移，已有环境不迁移。
- [x] CP6.Core/Services/Erp/UomProfile*.cs实现DTO、精确整数/小数计算、版本存储与独立Owner；CP6.Core/Services/Erp/CommercialProductUnit*.cs实现当前原产品选择、显式绑定及原结果/历史读取。SQL参数绑定，Tenant在每个查询/FK中。
- [x] CP6.Core/Services/Integration/OperationsOwnerV1.cs、OperationsAccessV1.Uom.cs及Sys/IamStandaloneDepartmentMappingV1.cs加入真实单位资源/范围/创建绑定；CP6.WebApi/Program.cs注册定义和服务。公共授权/共享模型编辑由本COORD串行持有，不开第二实现分支。
- [x] CP6.WebApi/Controllers/Erp/UomProfileController.cs及产品单位controller提供无缓存API。scope/creation-context、列表/历史、commands、operations读取/close及精确转换；产品lookup/绑定/历史/原结果不绕过双Owner。
- [x] cp6.web/src/api/erp/uomProfiles.ts、auth/uomOperationRecovery.ts、views/erp/UomProfilesView.vue及相应spec，router入口和IAM精确kind补齐。创建/修订/停用/历史/原操作恢复及原产品显式选择绑定均使用准确字符串；未确认旧操作时不新发命令。
- [x] 新PG/HTTP行为与实际受影响product identity/部门/原操作回归集中执行；页面/恢复相关Vitest和vue-tsc一次必要检查。分支最终相对最新主线一次SPEC/QUALITY，修正仅定向复查；普通文档不跑产品测试。
- [ ] 单份DELIVERY、台账/生成页/四状态同步，Handoff消息正常PR逐仓采用，Main正常PR并核远端，准确任务分支删除、停止自有进程和官方finish释放席位。

## 验证入口和判据

使用既有隔离PG18 fixture及Kestrel宿主，命令形如`dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter FullyQualifiedName~UomProfile --logger trx`，输出路径由本机已有runner设置在D:/CP6-local-test/wave147/foundation，不拷贝源码/依赖。

关键正反例：原配置与结果同提交/回滚；并发相同CAS只一个新版本；同命令相同输入重放、异体拒绝、关闭挡迟到；当前撤权、租户隔离、范围1/2/3/4/5和显式部门创建/撤回；历史不可写；精度13/18及大整数比例无损，跨维度/不可整除/包装倍数拒绝；base修订或停用不自动重定向派生；原产品绑定缺单位不回落EA，准确产品source版本与双对象授权，产品/profile ABA后绑定失效而历史保留。既有计划写503及旧产品身份wire继续保持。

前端用真实表单行为和API适配边界测试：保留小数/比例原串，原产品/单位版本来自当前选择；UNKNOWN后查询/关闭而不重新提交；跨标签和身份切换不清他人引用。类型构建使用`node node_modules/vue-tsc/bin/vue-tsc.js --build --pretty false`。所有成功与失败保留真实适用范围，不以测试/文件数量作为目标，不冒称正式跨域业务验收。

## 不变的后继

单位提供者及原产品采用之后仍须币种/双Owner工程映射、冻结工程基线、WP08-17意图、正式WO Root/Snapshot/Occurrence五域及SCHED同一UoW/UI。D2/D5/D6/D7原出口保持。无Actions、额外树、Docker/socket重试、生产或既有数据库迁移，不以本轮完成替代完整WP04-33或完整125目标/247验收。

## 实际交付

范围、验证/复用、未覆盖项与收尾快照见[单份报告](../../evidence/W147-01/DELIVERY.md)。当前登记72.28分钟；Handoff已正常采用/清理/释放，Main仍待正常PR采用与finish；最后生命周期检查框在本提交时保持未完成。最终以本机wave147-final-verification.json核对，后继准入一次归档，不以规划快照冒充已收尾。
