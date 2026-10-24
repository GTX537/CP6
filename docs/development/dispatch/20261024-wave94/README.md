# W94 出货包装独立查询Owner与实际页面

COORD本聊天单人，Main `74edef400c18e88ceba133600368637df09ffef5`，Handoff `fd2548aba6d1c8b29279cd7fb5917eb4104b68ac`；Issue[497](https://github.com/GTX537/CP6/issues/497)。W93 Main495/496、Handoff68、Issue494已完整收尾，19.73min＋独立前置约4min，准确分支删除、固定席位释放。W89跨仓确认仍待答复，不重试bind。

## 前置与边界

最新AGENTS/POLICY哈希未变，Main/Handoff fetch、官方status均已核对，三席位空闲干净；无重复开放Issue/PR。已读当前ShippingController/ShippingPackage、完整PackingShipView、Outbound包创建/原Owner/完整父单范围、现有菜单及动作注册；WP10-19全卡、WMS-OUT-01适用手册与原文§3/§14已核。不声称重读全部98913行原件。原包装菜单413的派生键明确锚定为wms-packaging；独立query动作只登记不授角色。原Outbound Owner身份和库存写链不变。

## 实施顺序与实际耗时

- [x] 原HTTP未授权包查询PG复现并保留失败。
- [x] PACKAGING-OWNER/1历史记录：当前包装query与原Outbound query共同门，租户、原父单完整明细/源/仓区范围、包装自身Creator或继承原源部门范围，先过滤后计数分页；孤儿/删除原单不可见。字段CarrierCd/TrackingNo/Remarks MIN，隐藏字段不得作为搜索侧信道；稳定顺序，冻结输入和最终当前权限，同原生Owner锁。只投影原包字段，不伪造Seal/Manifest/DEL。
- [x] 新版本列表/原包详情；旧读入口经当前权限后409要求版本。实际页面换用新读，队列/详情/历史身份、撤权、目录更新、乱序和卸载失效。发运仍用原Outbound ship，确认前冻结原单和表单、单次在途、提交前重核页面身份；未知不自动重发。删除随机运单号生成，只接实际输入。
- [x] 相关PG、HTTP、字段/范围/锁与Vue行为、类型检查；一次集中SPEC/QUALITY。无全仓回归/Actions/正式业务验收/既有数据库迁移/生产部署。
- [ ] 正常Main产品合并，Handoff交付/finish，同Main分支文档收尾合并/finish，Issue核对关闭，准确任务分支清理和固定席位释放。

预计40–65min：Owner15–25、页面10–15、验证审查交付15–25；独立前置约4min。Main实际start `2026-10-24T02:23:41.135489+00:00`，finish计时，不重复Handoff。固定DEV-1/DEV-2原仓复用，隔离PostgreSQL55435，保留测试失败/数据并停止自有进程。

完整WP10-19/20、正式PackageVersion/PackCoverage/Seal/Source+RSV+Stock+OUT同UoW、持久原请求恢复/承运交付验收仍UNPROVEN/NOT_RUN。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升。七天全目标ACTIVE，本窗口不能替代D4/D5/D6真实公共前置。

## 验证完成

29独立.NET（15原生PG、13既有InMemory出库回归、1两租户权限种子）及15Vue通过，无跳过；相关构建和vue-tsc通过，去重44项。当前业务文本PAD SPACE语义不变，原值保真；一次集中审查通过，PG停止，待正常采用/收尾。
