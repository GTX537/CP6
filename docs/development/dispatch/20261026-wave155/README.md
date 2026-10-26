# W155 原Owner时间单位定义与实际资源工时读取

COORD单人；原七天目标ACTIVE、完整范围保持。W154两仓合并/分支删除/席位释放已复核，本轮归档一次，仅关闭WP08-16.wave154_resource_sources。

## 依据、真实缺口与实现

完整WP08-16及WP10-47任务卡已读；MES-RES-01手册和原文主数据/复合类型/RES-WRITE-1/能力修改/技术引用段核对。任务卡明确需要原资源基础能力与工时UOM，且不以ROU先发布为前提。当前UomProfile有准确比例、量纲与当前Owner，但字符串代码/量纲不证明物理时间含义；不能凭SEC/HOUR名称或同名量纲猜测秒数。

本轮在实际Uom Owner新增Main.Erp.UomTimeBasis.v1版本化定义：仅实际BASE原配置的精确Ref，明确SECOND/MINUTE/HOUR含义、启用/撤回和理由，当前uom edit与对象范围/最终门，原件/头/审计/原键同PG事务。不得创建重复UOM主账或隐式改变原比例。更改原配置、关闭定义/基准或无权读取使当前定义不可采用；历史与原结果仍需当前原Owner读取。

实际ResourceSource同Main事务读取准确原来源、目标单位和其基准时间定义，将原standardCycleSeconds、plannedRunMinutesPerDay或dailyCapacityHours准确换算。量值只取原表见证，不接受客户端替换；null/缺定义/失效明确UNKNOWN，过期准确Ref拒绝，禁止舍入、代码猜测或默认系数。原480仍只是原字段值，不因此成为正式批准产能或资格。提供时间定义页面和资源目录实际换算入口，最小原操作恢复，身份/权限清理。

## 任务及预算

预计45–70分钟：时间含义存储/原Owner原操作20–30，实际资源读取/页面15–25，定向验证/集中审查和两仓收尾10–15；最终按Main START至finish计，不叠加Handoff。

- [x] 原单位Owner持久时间定义、准确基准版本、CAS/无变化/历史/当前有效性与原键恢复。
- [x] 实际资源原量值到准确单位的同事务读取和精确运算，不猜测未知语义或制造生产资格。
- [x] 时间定义与资源换算页面、真实PG/HTTP/前端定向验证，一次SPEC/QUALITY。
- [ ] 唯一台账/四状态同步、正常两仓PR/精确远端采用/准确任务分支删除/官方finish。

不执行Actions、既有环境迁移或部署。完整资源能力批准/归属计划、正式RES/ROU/BOM/WO/SCHED和D2/D5/D6/D7剩余出口继续，不以本定义适配器关闭完整WP08-16/WP10-47或任何完整目标。

## 实际交付

[单份报告](../../evidence/W155-01/DELIVERY.md)，登记23.08分钟；实现、定向验证和集中审查完成。Handoff已采用释放，Main正常PR/finish待最终proof。
