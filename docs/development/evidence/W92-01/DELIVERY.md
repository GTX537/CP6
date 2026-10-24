# W92-01：仓库／库位独立Owner与原结果名称维护

Issue[491](https://github.com/GTX537/CP6/issues/491)，COORD本聊天单人。Main基线`890cb6427fec6c40cbfd296e9e13bebf0f916c25`，任务`BUG-491-WAREHOUSE-OWNER`；DEV-1 Main、DEV-2 Handoff原仓固定复用，无bind或新目录。Main start2026-10-24T01:26:50Z，原估60–90min＋独立前置约4min，最终按finish计且不重复计Handoff。[范围和前置](../../dispatch/20261024-wave92/README.md)。W91全部收尾证据已归入台账，W89保留确认边界。

## 实际交付与合同边界

全文核对当前控制器、两实体、API、两页面和唯一外部仓库候选消费者，WMS-MASTER-01手册及WP10-01任务跨度；原3886行Spec只核对相关IAM、presence、§6.2/6.3/16范围，不声称全文重读或全部附件完成。三个真实PG红灯证实旧空查询、原库位树和未隔离库位创建缺Owner也成功。完整MASTER要求保留，名称维护按独立适用前置推进。

`WAREHOUSE-OWNER/1`独立`wms-warehouse`/`wms-location`当前原记录读取，可信原会话、租户、本人/部门2–4/全部和实际仓区范围在计数/分页前生效。部门授权新增可信原UUID的warehouse/location两种绑定，库位和父仓库各自具备当前权限/范围；父仓范围不推导所有库位可见。原生固定顺序身份/业务/主数据表锁、冻结查询和最终来源权限检查覆盖读取/修改。原始SQL写者也受锁约束；拒绝外层事务、预跟踪主数据或异contextOwner。默认50/最大200，稳定码/UUID排序，不承诺跨页冻结集合。

仓库名称/地址/管理者/备注和库位名称/条码遵循当前字段MIN。原父引用仅在同仓实际唯一父且可见时披露，隐藏/缺失父不泄漏代码、不宣称完整树。原容量及原预占数量保持numeric(21,8)精确字符串，包括0和最大值；typed容量、确证预约量、生命周期未知分开，0不代表无限。Site未记录，不从BaseCd或仓码推导；不改既有数量表示和存储。

新增`MASTER-NAME/1`名称命令仅接受name presence：空对象KEEP、SET真实文本、库位可CLEAR；仓库CLEAR/空名拒绝。未知字段、身份覆盖、重复JSON字段、无效类型、控制字符/超长整条拒绝，不开放库存控制字段。真实8byte PG行版本通过带引号ETag/If-Match精确比较，未提供428，旧版412，NO_CHANGE也验证权限/字段/行版；NO_CHANGE不UPDATE、不升行版本。名称实体变更与Main原操作结果/私有前后审计在同一事务。没有伪造完整MASTER revision/outbox/space事件采用。

相同原键和相同语义返回不可变原effect/对象/ETag；后续名称变化不重写原结果，异义409。重放和回查使用当前query/对象范围，无需继续持有edit；旧/新名称只留私有审计，不随原结果泄漏。查不到原结果是UNKNOWN，不能作为未执行证明。独立abandon对未知原键写入CLOSED，并与晚到原请求在同一锁内串行；若已提交则返回原结果。封闭后旧请求不能再生效。并发相同原键只产生一次原结果，提交后丢响应可由原键恢复；最终源权限读取失败则实体和journal一起回滚。

旧九入口先执行当前动作及适用对象范围门；三旧读409 OWNER_READ_VERSION_REQUIRED，两旧PUT409 MASTER_WRITE_VERSION_REQUIRED；仓库创建503 MASTER_SITE_DICTIONARY_ADOPTION_UNPROVEN，库位创建422 INITIALIZATION_FENCE_UNSUPPORTED，两个删除503 MASTER_LIFECYCLE_ADOPTION_UNPROVEN，均在业务效果前拒绝。新旧HTTP no-store并保留Owner状态码。query/abandon只登记动作定义，前向迁移/种子重放不授角色；新映射触发器验证真实同租户原UUID及有效父仓库，无EF模型更改。非PG历史控制器逻辑保留，本窗不新增SQL Server实现或验收。

两页使用当前分页观察、原始精确值/未知状态、名称编辑、原键查询与封闭。仅存租户/主体/类型/原对象/原键，不存名称/请求正文；发送前先保存sessionStorage，保存失败不发送。未决原操作阻止新命令，不因网络失败、查询空或错配响应生成新键。重新进入页面可恢复；身份/权限变化清空缓存/草稿/当前未决显示但保留原主体的定位记录，迟到成功/失败/确认/卸载不回填或清除旧主体记录。独立abandon受当前动作门控制；取消草稿/离开页面先确认。出库路由唯一仓库候选消费者改为当前Owner按码搜索，最多200项且明确提示继续缩小条件，不冒充全量目录；路由自身完整Owner不在本窗完成。IAM页面支持两个真实对象类型。

## 本地验证与一次集中审查

19独立.NET（18原生PG、1两租户目录种子）与31Vue通过，无跳过；相关构建及vue-tsc通过，定向补验去重合计50项。作者集中SPEC/QUALITY检查完整产品diff及新文件、动作/字段/来源/部门界限、精度与未知、主数据锁、原结果和审计同事务、旧九入口零效果门、UI身份/权限/迟返/持久定位及唯一外部消费者。审查新增封闭vs晚到写入原生竞争，并扩充同一真实HTTP用例确认scope/封闭null结果序列化；未改变已测产品代码，其余结果复用。无剩余阻断。

- `owner-red-20261024T012803Z.json`：3实际PG失败，均缺Owner未拒绝；原失败/log/TRX保留并转绿。
- `owner-native-20261024T013244Z.json`：新增测试误用Sys_RoleFieldPermissions/FieldKey/Permission导致编译失败，未运行测试/未启动PG；改为实际Sys_RoleFieldPerms/FieldName/Access后再次运行。保留失败，不计成功。
- `owner-native-fixed-20261024T013731Z.json`：18/18。17实际PG含跨租户、203库位真实分页、独立当前双方范围/部门2–4、源父撤权、字段MIN/写入、KEEP/SET/CLEAR/NO_CHANGE和控制字段不变、不可变原结果/不同意图、UNKNOWN/CLOSED/撤权、旧六写零效果、实体与journal回滚和真实提交丢响应、并发同键、原生SQL锁等待、真实Program/login/HTTP/ETag/撤权、原生映射约束与目录重放；1两租户种子。
- `owner-review-supplement-20261024T014349Z.json`：2/2，新增原生close与write竞争+已有HTTP用例扩充；去重后18PG/19.NET。
- `owner-ui.json`31/31：14仓库/库位/外部候选行为，17IAM映射行为（含新增两种对象）；分页/精度/未知、SET/CLEAR/NO_CHANGE、原键恢复/终态/持久化失败、身份/撤权/乱序/卸载、畸形响应和未保存草稿。`typecheck-result.json`实际exit0。未声称现场浏览器或正式业务验收。
- 3个自有PG串行55435，ownership/stopped齐、无postmaster.pid，原数据/日志保留。无Actions、全仓回归、镜像、既有环境迁移或部署；既有构建警告未改。

## 采用与剩余

待Main产品PR精确采用→Handoff DELIVERY/合并/准确分支删除/finish→同Main任务状态收尾PR/准确分支删除/finish→Issue491关闭。完成前不提前声称席位已释放。固定目录留复用，最后证据记录准确来源和实际耗时。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持；完整WMS-MASTER/WP10-01、207AC、Site、typed容量、InitializationFence、生命周期/停用/保护规则和Space采用仍UNPROVEN/NOT_RUN。当前版本是独立现有记录与名称维护，不缩小完整已接受合同。七天目标ACTIVE，继续D2–D7必要工作。

W89跨仓切换自动审批返回`blocked by policy`，CRM未开工。Main484仅准备文档采用，两原W89分支保存并deferred释放，明确确认仍待答复；本窗不重试或绕过bind。历史W51指定停止data删除同样保留不重试。
