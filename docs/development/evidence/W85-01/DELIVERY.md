# W85-01：越库历史观察Owner与旧写入口围栏

Issue [474](https://github.com/GTX537/CP6/issues/474)，COORD本聊天单人。Main基线`25d00d7773d60e448ab8d4c37450ad6f81a3cb31`，任务`BUG-474-CROSSDOCK-OWNER`、固定DEV-1；Handoff固定DEV-2。开工22:56:48Z，预计35–55min＋独立前置约2min，实际以finish计，不重复Handoff；[范围与前置](../../dispatch/20261023-wave85/README.md)。W37–W84不重复。

## 当前合同与交付边界

当前设计以[手册](../../../design-handbook/modules/WMS-XDOCK-01.md)与WP10-32及准确`433993a9448ae3c0__WMS-XDOCK-01_越库作业_完整设计_RECOVERY_v1.1_R2.md`为准。本窗实读R2§1–11/18AC和手册/直接前置；早期v1.0仅历史，Issue已纠正引用。R2要求分阶段原IN/OUT、完整产品/技术/货主/UOM维度、真实原结果、未知/剩余库存保留。XD-G01–06仍未有运行采用。本窗属WP02-03当前权限采用及已有历史安全观察，不是WP10-32新草稿/真实收发编排交付，不把旧单量、两笔Txn或净0拼成R2四量/完成。

`CROSS-DOCK-OWNER/1`接当前原会话、独立query、原生权限及业务表围栏、最终重核。授权后计数再服务端分页，page>=1、pageSize1–500和偏移溢出检查；稳定CreateDate/id排序。503条实际PG记录可分页完整读，无客户端500条伪总数。原21,8数量保精确字符串，空批次、旧状态、TxnNo及dock文本原样作为历史观察。Knowledge=UNKNOWN、SourceState=UNRESOLVED_SOURCE，不生成实际实收/发运/当前四量，也不把旧PLANNED当零效果。

自身scope1核实际Creator；2–4使用可信cross-dock对象UUID部门绑定，5仍受可信租户、存在且未删除的正式仓库/暂存库位和Wms区域约束。旧FromDock/ToDock自由文本不推导物理位置/授权；本窗没有任何新物理效果。若已有入库/出库引用，行可见还须自身/源query的实际父对象范围交集、同仓库及真实产品明细匹配；缺权、软删、错产品、无真实父对象不回退。未关联来源旧行按自身历史范围可读并明确来源未解决。来源匹配只为披露范围，绝不当合法Arrival/Shipment/Root映射。无法确认来源或当前授权的旧数据仍保存在库中，未迁移或覆盖。

SupplierCd/CustomerCd/OperatorCd/Remarks执行当前MIN字段决定；DTO仅显式字段，无TenantId/Creator/Modifier/整实体。详情与列表同门；当前不可见详情404。部门映射PG新增cross-dock允许类型及同租户真实对象校验，复用既有IAM当前动作/CAS/原操作/审计，不根据当前登录人或业务号推部门。真实IAM查找、绑定、禁用及数据库跨租户UUID拒绝均验证。公共枚举与选择器/API类型同步，其他种类与引用保留。

旧PG Search/Get要求新版409 OWNER_READ_VERSION_REQUIRED。Create/Execute/Cancel在当前各自动作/对象门后503 CROSS_DOCK_OWNER_ADOPTION_UNPROVEN，编号、Stock调用、原批次和头状态效果之前拒绝。即使PLANNED且仅有旧IN引用也不直接取消或再IN/OUT，不根据一次空查询认定零效果。原非PG两个历史用例仍通过，不新增SQL Server实现。接口中不实的“两腿一事务”注释已纠正，未把旧算法改成第三套库存账。Controller旧catch排除Owner异常，保真实403/404/503而非改成400。

迁移及新租户启动只登记query目录，不授任何角色；生产Program字段/范围一致。HTTP具名路由、no-store，真实Program/login/读取/详情/旧写拒绝/当前撤权通过。

页面只提供受权历史查询与详情，明确新建/执行/取消不可用。数量不经Number；总数和翻页来自服务端。响应须匹配版本、覆盖、分页和未知事实表达；身份epoch、当前入口/来源动作变化、筛选变化、迟到/乱序、失败或卸载清除/丢弃旧结果。详情只取已显示的实际Id/单号且核返回匹配；隐藏字段不展示。历史执行标记不展示为业务完成，清空状态筛选后翻页也保持同条件。组件测试是实际Vue运行，未声称现场浏览器或完整业务验收。

## 验证与集中审查

18独立.NET用例（15原生PG、2既有非PG越库行为、1两租户启动种子）及21Vue用例通过，无跳过；相关构建和vue-tsc通过，去重39项。一次完整作者SPEC/QUALITY覆盖当前权限/来源/字段、精确历史边界、分页、原生读围栏、部门身份及实际HTTP/页面链，无剩余阻断；结果未变直接复用，不全仓重跑。

- `owner-red-20261023T225842Z.json`：3真实PG红灯，原服务缺Owner时仍读/新建/取消旧半成功引用。
- `owner-boundaries-20261023T230147Z.json`：编译失败、未启动测试/PG；新投影缺数值工具namespace，已修。
- `owner-native-20261023T230253Z.json`：测试编译失败、无测试/PG；夹具对init-only Enabled赋值，改为实际record with表达式。两个失败记录保留，不虚计执行。
- `owner-reviewed-20261023T230424Z.json`：18/18，无跳过。15原生PG含真实权限/源/字段、503条分页、部门2–4/IAM启停、跨租户原生校验、最终权限不可用及脏/外层事务拒绝、真实Program/HTTP和原写零效果；双连接实际pg_stat_activity观察旧原生UPDATE阻塞，读取期间返回同一旧cut，提交后旧writer才执行。另2历史非PG和1两租户启动目录。
- `crossdock-ui.json`：10/10；`crossdock-department.json`：11/11（含本次真实越库对象选择与绑定、既有IAM原操作/身份回归）。
- 首次类型检查发现StandaloneKind缺新增字面量，已在直接消费者`api/sys/iam.ts`补齐，`crossdock-types-final-result.json` exit0。这和既有部门组件新增实际行为用例纳入本任务串行窗口，不扩展其他IAM业务。
- 2个实际自有PG串行55435，ownership/stopped齐、无PID，数据/日志/TRX及红灯保留；2个编译失败不计PG。无Actions、既有环境迁移、镜像或部署；已有构建警告保留。

## 采用与未完成范围

正常Main产品PR→远端逐blob核对→Handoff DELIVERY/合并/finish→同任务Main状态PR/finish。未核最后证明前不提前声称分支删除/席位释放；实际时间以生命周期证明和后继登记为准。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持。完整WP10-32及其18AC、XD-G01–06、原IN/OUT/RSV原结果/关闭、完整库存/财务、CRM真实Site/Form Owner/公开受理仍待相应任务。七天目标ACTIVE。W51指定停止data删除曾被自动审批以blocked by policy拒绝，原地保留、不重试。
