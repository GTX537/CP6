# W83-01：物料缺料跟进独立Owner

Issue [468](https://github.com/GTX537/CP6/issues/468)，COORD本聊天单人。Main基线`9f9192152584b0afab5dd7afe3611a5cd4f3e0ce`，任务`BUG-468-MATERIAL-SHORTAGE-OWNER`，固定DEV-1；Handoff固定DEV-2。开工22:15:20Z，预计60–90min＋独立前置约2min，最终以finish计，不重复累计Handoff；[范围与前置](../../dispatch/20261023-wave83/README.md)。W37–W82不重复。

## 行为与合同

PostgreSQL缺料管理接入当前OperationsOwner原会话、独立query/resolve/dismiss、原生权限/业务围栏和最终重核。自身WorkOrder范围与源mes-work-order:query范围必须交集；有RelatedOutboundNo的行再交自身/源出库query及仓库区位，要求实际同WorkOrder、Material出库、实际同Product明细。来源不存在、软删、错源或无权不回退。scope1核真实缺料Creator及整来源对象，2–4使用实际WorkOrder订单或无订单UUID部门绑定，5仍可信租户/来源/Wms范围。无RelatedOutboundNo的实际WorkOrder缺料允许在没有出库query时读取；不能虚构没有证据的仓库归属。关联的无订单出库部门范围沿现有拒绝行为，本窗不扩展该源Owner合同。

`MATERIAL-SHORTAGE-OWNER/1`通过owner-v1读取与处理。授权后计算Total、WorkOrder筛选内的OpenCount，再按状态筛选、DetectedAt/id稳定分页；page>=1、pageSize1–500并防偏移溢出。RequiredQty/AvailableQty/ShortQty以精确字符串表示，差额复用BigInteger数值语义并保原非负展示规则，极值`9999999999999.99999998`未舍入。仅显式DTO投影，无整实体/额外租户或actor回传。

Remark遵循现有数值MIN字段权限。命令携真实RowVersion、显式SetRemark；隐藏/只读时保留原值，未明确SetRemark的非null写入拒绝。首个await前冻结查询/命令及byte[]。真实RowVersion与OPEN检查、当前服务端actor、保存和最终来源重核同事务；原生并发一个成功，另一版本409，不自动重试。提交确认丢失仍503 OPERATIONS_OUTCOME_UNKNOWN。旧PG无版本写409 ROW_VERSION_REQUIRED，旧列表409 OWNER_READ_VERSION_REQUIRED；非PG历史接口语义保留，不增加SQL Server开发。

父出库allocate通过内部OperationsAccess能力在同数据库/事务创建缺料，验证实际父动作/出库/物料；公开Create在PG不能自行写入。父路径不额外要求独立缺料管理授权，原子失败不留缺料/通知。既有父提交后发布队列保留。PG缺料Notifier用实际IWmsNotifier/SignalRWmsNotifier发送可信租户组空InventoryInvalidatedV1，不向Clients.All广播单号、物料、数量，也不在活动事务内发出；实际出库及反冲调用共享这个适配器。既有非PG历史反射通知保其明确范围。

GenerateFromShortage旧直读、估价及建采购申请路径在PG读取/编号/价格/写入前503 PURCHASE_REQUEST_OWNER_REQUIRED。本窗只保护该消费者，完整采购独立Owner、受理原操作和实际建PR待对应任务，未冒称采购闭环。缺料RESOLVED/DISMISSED只标记跟进状态，不改变库存或创建采购申请。

生产Program登记字段/范围；PG目录迁移只添加query，不授角色。审查发现启动种子缺相同动作，新租户不能只靠既有租户迁移，已在WmsPermissionSeed.OwnerActions补齐；本任务串行纳入同一公共目录范围。两租户实际种子复验幂等且无query角色授予，PG实际迁移重放幂等且仍无权。

HTTP统一Owner错误/no-store。页面采用具名版本、精确文本、服务端计数/分页和实际版本命令；当前入口/源/动作授权、身份epoch、乱序和失败均清数据/编辑选择，重复提交拒绝，未知结果无自动重发。成功文案明确“库存数量未变”，隐藏字段不因空输入覆盖。UI用例是实际SFC运行测试，未声称现场浏览器或生产验收。

## 验证与一次集中审查

37独立.NET用例（21原生PG、15既有非PG行为、1两租户启动种子）及9Vue用例通过，无跳过；相关构建和vue-tsc通过，去重46项。一次作者完整SPEC/QUALITY覆盖独立入口、当前来源与字段、精确数量、冻结输入、原生并发、事务失败/未知、父原子创建、实际Program/login/HTTP和Notifier链、页面及目录消费者；新租户目录缺口已修复，仅追加对应种子/HTTP/迁移复验，无剩余阻断。产品未变成功结果直接引用。

- `owner-red-20261023T221713Z.json`：3条真实PG红灯，原服务未拒绝缺Owner读取、公开创建和无权解决。
- `owner-boundaries-20261023T222106Z.json`：33项中21通过、12失败。15历史相关行为全部通过；PG夹具重复固定WorkOrderNo触发现存全局alternate key 23505，改为真实租户唯一前缀，未改变数据库键。该失败保留，不写为初始全绿。
- `owner-native-20261023T222304Z.json`：修正夹具后18/18PG通过。
- `owner-reviewed-20261023T222608Z.json`：21/21PG通过，补双连接真实阻塞及仅一个版本提交、同时间分页/计数/无出库授权未关联来源和非法输入、实际目录迁移无自动授权；不是仅顺序模拟版本冲突。
- `seed-adoption-20261023T222722Z.json`：3/3通过，只复验新增启动目录两租户、实际Program HTTP和迁移；与上批去重计数。
- `shortage-ui.json`：9/9；`shortage-types.log`空且执行exit0。未变数值文本工具和共用身份意图行为沿W81等既有证据，不重复全仓验证。
- 5个自有PG夹具串行55435，ownership/stopped齐且无PID，数据、红灯和TRX保留。已有构建警告保留。无Actions触发、既有环境迁移、镜像或生产部署。

## 采用与剩余范围

候选进入正常Main产品PR，核远端blob后交换Handoff DELIVERY并合并/finish，再同任务Main状态PR/finish。全部核对前不提前声明分支删除/席位释放；实际耗时及最终状态以本地生命周期证明和后继登记为准。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持；WP02-03、完整库存/财务/采购、CRM真实Site/Form Owner/公开受理未关闭。七天目标ACTIVE，持续接后续实际前置。W51指定停止data删除曾被自动审批以blocked by policy拒绝，继续原地保留，不重试。
