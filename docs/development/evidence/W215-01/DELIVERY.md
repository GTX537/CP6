# W215 — 库位库存事实与占用覆盖评估

## 结果

规则候选现在可以通过实际独立Stock/Location/Warehouse当前权限读取本地库存原记录。准确候选原件及ETag、原始数量精度、同事务读取与最终撤权、范围/截断/归档余额、实际HTTP和页面已接入。完成的是原七天D2规则应用所需的真实库存查询来源；完整容量占用和写入保护仍待完成。

库存Physical和Allocated分别展示，后者不重复计入容量；混合产品/单位不合计。活动库存集合完整也不代表未收货、预留或任务义务完整；未知容量保持null，规则与作业许可保持false。没有调用W213/W214内部原语来伪造完整Owner证明。

## 验证

7 actual PostgreSQL/Program HTTP;29Vue/type/build PASS;0skip。后端7项实际PG18/ProgramHTTP覆盖最大精度、混合单位、仓库范围、隔离新库位空集合、独立创建人范围、归档余额、101行截断、原记录不一致、当前候选/字段/来源/跨租户拒绝、最终Stock撤权，以及HTTP封闭DTO/If-Match/no-store。前端29项覆盖实际面板与既有候选页面，准确请求、精度、覆盖提示、错误响应、撤权与迟到数据；类型检查和构建通过。

原始来源：`D:/CP6-local-test/wave215/foundation/final-validation.json`。首次页面运行中新面板10项通过，原候选套件因缺少新增API mock无法导入；补全测试依赖后通过。类型检查发现测试夹具缺少RuleView必需字段，补全真实类型字段后通过。失败日志均保留，不冒充首次通过。后端只有一次编译/定向执行；后续代码未改，复用实际通过结果。沿用已验证OperationsOwner表锁/最终授权协议，无无关全仓回归、Actions或环境迁移。

一次COORD集中SPEC/QUALITY审查覆盖完整diff：权限与租户/仓库身份、候选绑定、精确数量和覆盖语义、只读事务、HTTP和前端身份边界。只读结果没有业务写集合，也不生成可用于08E的持久ImpactId。

## 状态与剩余

W214按远端收尾证据仅归档一次，并关闭其WP10-01/WP10-10有限执行意图记录。W215仅新增相同两任务的有限读取采用记录，不关闭完整任务。125目标、247正式验收、375任务状态及D1/D4之外原阶段出口不变。

剩余：真实Source/Root/Claim与未收义务完整覆盖、准确换算/非重叠/委托，全部效果写入口保护、原prepare键墓碑和实际08E/Transfer/Stock/REP/RMA；D3正式排程、D5/D6真实环境、D7最终集成继续。既有隔离屏障仍保留，未启用任何新作业。

Handoff：https://github.com/GTX537/CP6.AI-Handoff/pull/191；源 `aa9db9978eb42578d98538938b068b4f3b8df74c`，通信目标 `b842475c6a389b0a5cdb83d555c791374f95e0b8`，已合并并finish。Main验证完成，正常PR/官方finish待本提交之后执行；最终远端采用/分支清理/席位证明保存在外部 `wave215-final-verification.json`，下一次真实准入再统一回写本轮有限生命周期。此文不预写自身未发生的合并。

Main登记至报告 14.15 分钟，初估60–100分钟；最终实际时间以Main官方START→finish为准，Handoff不重复累加。固定DEV-1/DEV-2复用，主目录用户文件和依赖缓存保留，无新增目录或重绑。

[范围](../../dispatch/20261029-wave215/README.md) · [读取合同](../../location-stock-assessment-v1.md)
