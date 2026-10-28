# W206 仓库草稿独立入口与旧业务隔离

COORD单人继续CP6-7D-20261022 D2；WP10-01/WP10-10限定窗口。W205 Main628/Handoff181已完成；复用DEV-1/DEV-2，无新树。

## 工作与实际执行预算

1. 原生Site/真实五类仓库字典、仓库DRAFT创建、规范代码唯一/权限范围与数据库隔离，60–100分钟。
2. 独立创建/查询/名称维护页面，原操作回查/封闭与浏览器恢复，25–45分钟。
3. 定向PG18/实际HTTP/UI验证、一次作者SPEC/QUALITY审查、正常PR合并及分支/席位收尾，35–55分钟。

合计120–200实际分钟，按Main官方START至finish，不叠加Handoff。原七天目标ACTIVE，无预算重置或凭波次计算百分比。

## 已接受依据和实现边界

WMS-MASTER-01 v1.1.1 §2.2、4.1、6.1、DEV-M01：Tenant+规范仓库码唯一（跨Site），原码NFC/trim保持大小写，InvariantUpper键不截断；真实Site和类型；创建DRAFT、BizRevision=1、稳定ID/原键结果/审计共同事务。名称维护同值不升版；当前读权下恢复不重写。§6.2.1位置初始化及§8生命周期保护未齐，不激活、不新增Location/库存/Space。

当前真实字典采用已有WarehouseType五个常量的明确不可变版本；不猜未知码或默认1。真实Space_Site不可变原件和当前space-site查询权同事务核对。创建使用现有wms-warehouse的add/字段权、1–5对象范围、真实部门绑定和WmsRoleScope，无自动授予。新仓库留DRAFT，以数据库使用围栏阻旧业务写入；旧仓库不批量回填生命周期。代码碰撞含原有/已删除锚，遇到存量碰撞零创建。

BaseCode和ManagerCode来源的独立关系Owner尚无真实受信适配；保留合法长度与可空合同，非空明确SOURCE_UNAVAILABLE而不是改窄输入或默认推断Sys_User。此受影响变体继续保留，不声称完整DEV-M01验收。

精简验证覆盖真实事务/权限撤销/CAS/规范冲突/旧原始写入隔离/原键并发与未知恢复、实际Program HTTP/CSRF/闭合输入、Vue/类型/构建。无Actions、生产部署、既有环境迁移；完整125目标/247正式验收和所有原任务状态保持。

## 实际交付

[报告](../../evidence/W206-01/DELIVERY.md)，登记21.64分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
