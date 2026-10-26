# W156 版型工具准确原来源与当前Owner读取

COORD单人，原七天目标ACTIVE。W155两仓完整合并/准确分支删除/席位释放已核实，本轮归档一次，只关闭WP08-16/WP10-47.wave155_time_basis。

## 原范围与直接前置

WP05-04要求保留完整BaseCd/WdPtnNo/WdRev和历史原件；WP05-08需要准确工具规格来源。已核对两卡、ENG-TOOL手册相关身份/版本/权限/页面边界及原ENG-TOOL-COMPATIBILITY第3–6节。现有PlateMold是可变旧行，商业订单15位截码不能当工具身份，原RowVersion不是技术批准。原表仍为唯一旧主账；本窗口先交付后续旧writer兼容/受控技术版本所必需的真实原来源，不把观察原件称为批准规格。

建立Main.Erp.PlateMoldSource.v1：完整native身份和原UUID/租户、PG不可变原技术来源/摘要/单调观察版本，准确十进制字符串/null；旧有效期/传输状态与技术知识/实物状态分列。实际捕获原表INSERT/UPDATE/DELETE，历史从首次真实观察开始，不能补造此前原件。当前读取须原表/触发器/原件一致，精确Ref被同Main事务实际消费者检查；未知技术批准与实物资格保持UNKNOWN。技术源不夹带商业金额、供应商/客户私有资料，也不改订单/PE或把旧附件标志当文件证明。

原erp-plate-mold:query与当前租户/创建人/可信部门对象范围控制当前、历史和内部源读取；IAM可登记原UUID部门，新增query目录不自动授角色。新只读工具来源页面及旧两页入口，支持准确基地/完整编号/业务修订、原件观察历史，当前403/身份切换清理。

## 任务与实际预算

预计60–100分钟：原表持久捕获/当前Owner与内部精确源25–40；IAM/接口与页面20–30；定向PG/HTTP/UI验证、一次审查和两仓收尾15–30。最终按Main START至finish一次计时，不叠加Handoff。

- [x] 实际原件与当前源一致性、精确身份/数值/历史/原生令牌。
- [x] 当前Owner/IAM与内部同事务读取、当前/历史接口及可操作页面。
- [x] 正反例验证、一次SPEC/QUALITY、台账和四状态同步。
- [ ] 正常两仓PR、精确远端采用、准确分支清理与官方finish。

原旧writer的expectedNativeRowVersion+expectedTechnicalControl、合法修订/有效期/原命令共同事务和原订单Owner联动仍由本目标继续实施；正式工具技术批准、实例/DOC关联、工艺/工程冻结和WO/SCHED不可由来源观察代替。本窗口不升级完整WP05-04/ENG-TOOL或七天阶段，不执行Actions、既有环境迁移、部署。

## 实际交付

[单份报告](../../evidence/W156-01/DELIVERY.md)，登记22.01分钟；实现、定向验证及集中审查完成。Handoff已采用释放，Main正常PR/finish待最终proof。
