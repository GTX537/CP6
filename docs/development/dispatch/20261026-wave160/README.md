# W160 实物版模独立身份、原件与资料入口

COORD单人，原七天ACTIVE。W159两仓完整收尾，仅归档一次并关闭WP05-04/06.wave159_technical_drafts。

## 范围与前置

WP10-48/WP05-06直接前置；原WMS-TOOL-01 R2 §§2–7和ENG-TOOL-COMPATIBILITY §10.2。实际PlateMoldStock稳定UUID/PLT2身份、不可变原生观察版本、当前头和当前权限；旧UsedShots保本周期计数，LifetimeShots、正式CycleNo、Site及工程关联缺证保持UNKNOWN。不会从仓库BaseCd猜Site，也不以旧Status=0声称使用资格。

本窗口交付真实独立查询/当前头/准确历史、有限资料编辑（色数、尺寸、制造日期/登记成本、最大次数、备注）、原键恢复、同事务内部HoldExact与旧HTTP/服务写入口隔离；同Main PostgreSQL最终授权、租户/可信部门/仓区范围及成本/客户产品字段策略，准确decimal字符串。真实页面沿用/wms/plate-mold并服务端分页。资料变更不创建Stock、不改变周期/寿命/位置/工程定义或实物资格。

现有数据没有可信Site/正式维护轮次和终身记录。本窗口不冒充完整WP10-48完成；新建与正式映射、周期事实、实物使用/维护/报废、ENG正式双Role关联留在原任务。迁移仅隔离PG验证，不执行既有环境切换。

## 执行预算

预计80–130分钟：后端与权限30–45；页面与定向验证30–50；一次SPEC/QUALITY、台账四状态及两仓收尾20–35。实际按Main START至finish一次计时。

- [x] 独立Owner、不可变来源/当前头与当前权限。
- [x] 有界资料写入、原键恢复、内部HoldExact与旧写者隔离。
- [x] 实际页面、定向PG/HTTP/UI验证及集中审查。
- [ ] 正常两仓PR、远端采用、准确任务分支删除和finish。

原七天全部出口及375任务正式状态保持，无Actions/部署/既有环境迁移。

## 实际交付

[单份报告](../../evidence/W160-01/DELIVERY.md)，登记20.43分钟；本窗口验证和审查完成。Handoff已释放，Main待正常PR/finish。原任务/七天范围未缩小。
