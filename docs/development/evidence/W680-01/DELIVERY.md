# W680-01 · 库存金额相关 Operations 字段消费核销

2026-11-16 UTC，COORD；基线 `665e34e8966f099bc3404fcdb4c52ef594566ed6`，固定 DEV-1、分支 `codex/w680-01`。官方 START `2026-11-16T09:22:42.498009+00:00`。本窗口领取 wms-dashboard、wms-expiry、wms-stock-dwell、wms-vmi 的实际字段及派生输出核销，必要时在同窗口修复确认缺口。

本窗口完成四组消费者的注册字段与实际用途核销，12 项定向 PostgreSQL 回归通过，零失败、零跳过。没有发现本轮核对范围内需要修改的产品权限缺口；产品、测试、数据库和路由均未改动，仅更新核销分类和状态。WP02-03 保持 IN_PROGRESS，简体中文继续作为标准。

## 实际消费与已采用合同

共同入口为各 Controller → 业务服务的 Owner 分支 → `OperationsOwnerV1.ExecuteAsync`。当前原会话、完整封存字段域、真实动作目录及范围先行；业务数据与来源集合相交后才计数、分页或聚合。`OperationsAccessV1.SourceAsync` 把实际消费的来源加入共同参与列表；最终门核对每个参与来源的当前主体、权限/注册/组织版本及范围，提交完成后才返回读取结果。本轮没有新增资格包装或重复内部校验。

| 消费者 | 实际字段与输出 | 写入、导出和下载范围 |
| --- | --- | --- |
| wms-dashboard | KPI 的 TotalStockValue、仓库汇总的 StockValue 同时要求本资源 Amount 与 stock.UnitPrice 可读，缺任一则拒绝整个金额读取。库存/流水分别与来源当前行集相交；计划、出库、盘点统计各取对应来源。趋势和告警不消费价格，不因缺单价权而被统一禁用。 | Controller 只有四个读取入口；未发现导出/下载入口。按 [W39 已采用合同](../../../../docs/design-handbook/remediation/main-stocktake-dashboard-owner-v1.md) 核字段边界；旧 numeric 汇总、缺价默认和库存价值命名没有在本轮改造成正式 Finance 估值。 |
| wms-expiry | observation-v1 的 UnitPrice 同时要求自身与 stock 单价可读；ReferenceValue 另需自身 Amount。缺价或隐藏时参考值为 null，不补零。行集先交集、计数后分页；旧 expiring 投影只返回允许的单价，LossAmount 固定 null。 | 旧 Dispose 在 PostgreSQL 首先返回 DISPOSITION_OWNER_REQUIRED，空请求也不能宣称成功。没有导出/下载入口；正式处置 Owner 留在其业务任务。 |
| wms-stock-dwell | 总计、90 天以上值、各分组 ReferenceValue 及缺价计数同时受自身 Amount 与 stock.UnitPrice 控制；CustomerName 按本资源字段 MIN，隐藏后保留业务代码且不查客户名。按数量排序，不按隐藏金额排序。 | 仅 observation-v1 和旧 summary 读取。旧非空 decimal DTO 无法表达隐藏、未知或精确值时返回 OWNER_READ_VERSION_REQUIRED，不伪造零。没有导出/下载入口。 |
| wms-vmi | 当前库存取自身与 stock 真实行集交集，过滤有效仓库/库位后再统计；不披露价格或客户名称。历史估算账单按自身真实 UUID 范围，金额要求 amount-read + BillingAmount 字段权，费率要求 rate-read + DailyStorageRate 字段权，CustomerName/Remarks 各自控制。 | 旧读取返回 OWNER_READ_VERSION_REQUIRED；旧 calculate/confirm 在当前动作/可见账单检查后拒绝 VMI_OWNER_ADOPTION_UNPROVEN。没有导出/下载入口，不宣称正式计费、确认或 Finance 接受已实现。 |

效期、呆滞库存按 [W81](../W81-01/DELIVERY.md) 的精确观察合同核对；数量及参考乘积使用精确十进制字符串和 BigInteger 中间值，缺价及未知币种/估值保持原状态。VMI 按 [W88](../W88-01/DELIVERY.md) 的 VMI-OWNER/1 核对：历史金额与费率是分别授权的原存储事实，本轮不擅自添加彼此依赖、不重算旧账单，也不把原 Confirmed 布尔当成正式财务确认。

四个实际页面及其 API 包装已核对，均消费上述读取，没有独立制品导出/下载入口；首页呆滞库存组件沿同一 observation-v1 API。这里只证明这些现有消费者没有该用途入口，不代表共享 Operations 已有 Export/Download 映射，更不预先批准未来新增入口。

## 边界与后续核销

本窗口核销的是上述四个资源的注册受控字段及其直接来源；没有把关联主数据资源整体标成通过。看板的 WarehouseName 取自仓库名称查询，呆滞库存 CustomerName 按自身已采用策略读取关联名称；后续 wms-warehouse / erp-business-partner 核销应对齐这些直接消费者及各自合同，不能仅凭同名字段推断跨资源授权或自动豁免。本次没有把这些跨资源采用问题登记成已证实漏洞，也没有扩充看板所需权限。

看板旧数值表示/缺价算法、完整库存估值与计费/处置业务并非本轮字段核销的完成结论。文件/受控文档 v1、表单附件 v2 的独立用途，订单其他消费者与 Space WP13-04 组合继续保留原范围。

## 实际验证与有效复用

私有证据 `D:/CP6-test-artifacts/W680-01`；`validate.ps1` 保存精确筛选和实际环境，`verification/verification.trx` 为本轮 **12 通过 / 0 失败 / 0 跳过**。产品及所选测试与开工 Main `665e34e8966f099bc3404fcdb4c52ef594566ed6` 完全一致，复用 W679 已构建的程序集缓存 `D:/CP6-test-artifacts/W678-01/build`，本轮未重新编译。缓存路径名不是源码版本。

| 本轮实跑 | 覆盖范围 |
| --- | --- |
| 看板 2 项 | 自身金额/来源单价隐藏、仓区及本人范围，非金额趋势仍可读；告警、流水与总数先限权后聚合，跨租户不混入。 |
| 效期/呆滞库存 6 项 | 字段 MIN、缺价/隐藏及客户名，最大合法数量/参考乘积的精确字符串，来源/self/仓区交集，冻结输入和 raw 库存写者受阻，最终权限读取故障丢弃结果，以及撤权先到/后到的真实双连接顺序。 |
| VMI 4 项 | 当前动作与字段分别隐藏金额/费率/名称/备注，当前 stock 来源及有效位置在总数前相交，租户/本人/软删除账单及旧写无效果，真实账单写者受读取围栏阻挡。 |

Windows / .NET 8 / PostgreSQL 18.6，隔离端口 57367、独有 `verification/permission-consumption-main` 根；用户、权限、库存、客户和历史账单为模拟业务数据，真实 Owner、权限源、数据库事务和并发连接不模拟。实际命令为 `dotnet vstest <缓存>/bin/CP6.Tests/debug/CP6.Tests.dll /TestCaseFilter:<明确方法名> /Logger:trx;LogFileName=verification.trx`，不运行全仓或其他数据库。

本轮没有 HTTP/浏览器重跑；[W39](../WP02-03/stocktake-dashboard-owner/DELIVERY.md)、[W81](../W81-01/DELIVERY.md)、[W88](../W88-01/DELIVERY.md) 的 HTTP、页面及业务证据按原适用范围保留，不改称本轮新执行。PGT 27/27 与询盘 60/60 复用既有验收。

产品路由和封存目录未变，复用 W678 `final-metadata`，实际运行 `index_coverage.py`。四个资源从通用 operations 组转为 inventory-observation，记录实际读取、明确关闭的旧写及未出现的用途；剩余 operations 为 26 组。81 个资源、114 个字段、2,103 个 descriptor 保持，这些是导航规模，不是验收数量目标。

## 审查、状态与生命周期

一次作者 SPEC/QUALITY 集中审查覆盖字段与来源门、派生披露、聚合前范围、独立历史字段合同、关闭旧写、未采用用途及证据适用范围。索引/报告、台账、协作入口和四份项目记忆同步；生成页重算并核对一致性，未变化的进度汇总无需制造差异。文档只检查差异、引用及台账结构，不为文档新造产品测试。

WP02-03 仍 IN_PROGRESS；WP02-02 DONE，375 卡保持 39 DONE / 1 IN_PROGRESS / 335 PLANNED，125 完整目标 0 DONE。下一窗口优先核销仓库/库位、客户名称等主数据字段的实际消费者，再继续其他 Operations 派生输出与写回读。

本轮没有 Actions 触发/取消、工作流或依赖改动、分支保护变更、环境迁移或生产部署。正常 PR 采用、远端树核对及官方 finish 后才声明任务分支处置、工作区状态和席位释放；最终回执保留在私有工件根。
