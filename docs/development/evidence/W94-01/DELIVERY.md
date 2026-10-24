# W94-01：出货包装当前Owner和实际页面

Issue [497](https://github.com/GTX537/CP6/issues/497)，COORD本聊天单人。Main基线`74edef400c18e88ceba133600368637df09ffef5`；任务`BUG-497-PACKAGING-OWNER`，固定DEV-1 Main、DEV-2 Handoff原仓复用，无bind或新增目录。Main实际start2026-10-24T02:23:41.135489Z，原估40–65min＋独立前置约4min，最终按finish计，Handoff不重复累加。[前置与范围](../../dispatch/20261024-wave94/README.md)。W93完整收尾证据已回填，不重复。

## 实际修复

`PACKAGING-OWNER/1`在`GET /api/wms/shipping/owner/v1/packages`及`/{no}`读取原ShippingPackage。现有包装菜单413锚定`wms-packaging`、只新增query定义，不自动授角色。当前包装query与原Outbound query各自独立校验；两者的完整原父单范围交集包含实际租户、本人/源部门2–4、原Order/WorkOrder有效性、头仓库和全部明细实际仓区，含已软删明细以防部分父单泄漏。包装本人范围还要求原包Creator。无原父单或父单/源已删除的包不进入列表和计数；不推断无订单对象的部门，也不复制Owner身份。

分页前过滤，总数与本页在同原生当前Owner事务和表锁内读取，DepartureTime/UUID稳定排序；冻结输入并在返回前重核当前动作、字段/组织/权限版本与主体。1–200页大小、页数/溢出/字段长度/日期区间校验。旧日期是无时区记录，新查询明确只接无offset的原时间，不能伪造UTC。重量/体积以无损decimal字符串返回，不经前端Number或补0；保留原包UUID、原父单UUID/编号、原CaseQty、原Carrier/Tracking/Remarks空值与空字符串。

CarrierCd/TrackingNo/Remarks按当前字段MIN投影；隐藏Carrier/Tracking即使搜索空字符串也403，防止通过计数侧漏。过滤沿用当前PostgreSQL业务文本的bpchar/PAD SPACE和现有collation，保留实际值，不由JS自行改成ordinal比较。旧实体、TenantId/Creator/审计字段不外发。既有无版本列表在两个当前动作后409，详情还检查当前对象后409/404；实际MVC保留403/404/409/503并no-store。无新库模型或迁移，无库存/出库写链变更。

实际PackingShipView采用新分页/包号筛选/原包详情。历史显式为旧出库附属记录，不冒充正式封箱、承运接受或交付；重量/体积原精度和原无时区时间显示。删除随机运单号生成，仅实际输入；不再默认填造承运公司。队列/详情/历史均对身份、撤权、权限目录刷新、筛选、乱序、卸载清空/忽略旧响应。

原Outbound ship仍由其既有Owner执行。确认前冻结原单/表单，确认及发送期间单次在途，发送前重核当前页面身份/动作/所选原单；取消可重新确认。网络或5xx/不明成功形状保持未知提示、不自动重发，同主体权限刷新也不清除在途/未知保护；用户可读原出库单和历史。此为页面生命周期保护，**不是持久requestKey、未知提交恢复协议或正式跨Owner发运原子性**，刷新页面/重新登录后的持久恢复仍属原未完成合同。

## 验证与集中审查

29独立.NET（15原生PG、13既有InMemory出库回归、1两租户权限种子）及15Vue通过，无跳过；相关构建和vue-tsc通过，去重44项。一次作者SPEC/QUALITY审查覆盖完整产品diff、新DTO/Owner/原范围/字段/原生锁、实际HTTP、既有Ship不变、页面撤权/在途/未知及正式合同边界；补充同主体权限目录刷新期间在途保护后定向验证通过，无剩余阻断。

- `owner-red-20261024T022437Z.json`：两真实HTTP越权红灯，原列表和原详情实际200而期望403；已转绿，原失败保留。
- `owner-green-20261024T022916Z.json`：29执行、28通过、1失败。失败测试误认为TrackingNo尾空格应为ordinal不匹配；核对当前PostgreSqlTextQuerySqlGenerator/BusinessTextComparer及实际数据库证明既有bpchar PAD SPACE匹配，原值仍含尾空格。只修正错误测试断言，没有改数据库或产品比较语义。
- `owner-semantics-fix-20261024T023206Z.json`：只复测该1项通过，其余28结果复用未变输入。15原生PG含两个原红灯、当前双动作、本人/源部门2–4、完整头/明细仓区和原源撤销、跨租户/孤儿/软删、63记录分页、金额外的重量体积精度、隐藏字段计数侧信道、冻结输入、最终原源读取故障、同连接/ambient/已追踪拒绝、原生SQL并发父单删除等待，以及真实Program/login/HTTP/no-store/版本/撤权。13既有出库回归包括实际旧Ship建Package，未声称非PG为新基线。
- `owner-ui.json`因完整替换vue-i18n mock缺createI18n导致0用例；`owner-ui-fix.json`13失败是未stub Element表格作用域槽；`owner-ui-stubs.json`12通过1失败为测试读取嵌套Ref漏.value。修正测试夹具；`owner-ui-final.json`15全部通过含两项真实确认/在途补验。所有失败保留。最终`typecheck-result.json`记录vue-tsc实际exit0。
- 3自有PG55435串行，ownership/stopped齐全，无postmaster.pid，数据/log/TRX保留。未运行Actions、全仓回归、现场浏览器、正式业务验收、镜像、既有环境迁移或部署；原依赖构建警告真实保留。

## 采用与剩余

待正常Main产品PR采用→Handoff DELIVERY/合并/finish→同Main分支状态收尾PR/finish→Issue497核对关闭。完成前不声明准确分支已删除或席位已释放；固定路径留复用。

24/375整卡、125目标0完整、247正式运行NOT_RUN不变。完整WP10-19/20、正式PackageVersion/PackCoverage/Seal/Manifest、真实Source/RSV/Stock/OUT同UoW和原请求持久恢复、DEL承运/交付仍UNPROVEN/NOT_RUN；当前历史读取和UI保护不替代已接受合同。七天目标ACTIVE，D2–D7继续，D5/D6实际前置仍待推进。

W89官方跨仓bind自动审批返回`blocked by policy`，CRM未修改；两个原分支可靠保存、deferred席位已释放，确认仍待答复，不重试或绕过。历史W51指定停止data删除拒绝边界保留。
