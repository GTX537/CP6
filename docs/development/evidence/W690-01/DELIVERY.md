# W690-01 账套、成本中心版本和制造费率发布

COORD 从远端主线 `f98e07675906ff4d35c99acf9fb3fc80a2a243ac` 复用固定 DEV-1，分支 `codex/w690-01`，接续 W689-01。受测产品及用例提交为 `1104c664df4eaab6a4ce4804869855bd6f356aec`。[W689 交付](../W689-01/DELIVERY.md)

## 当前接入

- 财务 Owner 从服务端配置 `FinanceCosting:EnvironmentId` 和 PostgreSQL 登记读取真实模式账套。当前租户和可用账套成员共同决定候选。操作者显式选择法人及账套，取得短期上下文。成本中心候选绑定同一租户、账套、操作者、成本中心根 ID 及批准版本 ID。查询、写入及消费核对当前有效状态。[FIN-COST-01 R2 §2、§4、§13.2、§21.5](../../../design-handbook/modules/FIN-COST-01.md) · [FIN-MASTER-01](../../../design-handbook/modules/FIN-MASTER-01.md)
- 使用 `/api/fin/cost/v1/options` 和 `/api/fin/cost/v1/selections?kind=COST_CENTER` 获取上下文与成本中心。现行发布入口为 `/api/fin/cost/owner-v1/rates`，写请求绑定 `X-Finance-Context`。`rate-draft`、`rate-review`、`rate-release` 独立分配。复核人与制订人不同，发布人具备有效发布资格，发布时再次检查原复核人的当前资格和动作权限。费率源继续采用 `mes-process-cost-rate` 对象与字段权限。预览绑定发布主体、费率版本和时间线修订，重复提交同一预览复用原发布。修改区间只替换相交的半开日期段。[实际路由](../../../../CP6.WebApi/Controllers/Fin/ManufacturingRateVersionController.cs)
- 工单通过数据库登记绑定账套。MES 完工与手工重归集使用同一个 Finance Owner，按账套会计日、实际工作中心及工序取唯一已发布版。归集原件保存账套、费率、成本中心根及版本、政策、费率依据和工序 ID。当前适配使用 `APPROVED_ACCOUNTING_DATE`、两位金额和 `HALF_UP` 的现行 A2 制造成本计算。发布保存八位小数，超出 A2 单价容量的版本在归集前拒绝。既有未绑定工单沿原用版及结转路径。
- 账套成员、成本中心批准版、政策、费率依据、专业资格及工单绑定来自 `cp6_fin_mfg_v1` 中的受信任登记。当前登记的 `rate_bases` 用于批准版、工序适用性及零费率依据，正式 `StandardBasis`、完整 Finance Master 录入与资格工作流仍待采用。本轮选择在现行 Owner 提供发布流程。完整 R2 指令 API、其他候选种类、正式上下文引用和 Vue 流程沿 WP11-39 交付。[WP11-39](../../task-plans/WP11.md#wp11-39)
- 已绑定账套的成本单可归集。账套级 GL 接入前，手工结转返回 `COST_LEDGER_SETTLEMENT_ADOPTION_REQUIRED`，MES 保留已归集成本单并记录同一原因。出库开票遇到同类绑定时在开票与凭证前返回 `COST_LEDGER_SHIPMENT_ADOPTION_REQUIRED`。工单已有成本单时拒绝首次绑定，旧数据转入正式账套沿独立切换任务推进。

## PostgreSQL 本地业务验证

Windows、.NET SDK 8.0.424、PostgreSQL 18.6，隔离端口 57377。真实前向迁移、签名 HTTP、工单完工和财务 Owner。模拟账套、成本中心历史版、工单、库存、用户、角色、专业资格、政策及依据。私有证据目录 `D:/CP6-test-artifacts/W690-01/`。

- 46 项定向用例通过，其中 33 项使用 PostgreSQL，13 项为成本组件回归，零跳过。编译零错误。`final.trx` 32 项通过，`shipment-verified.trx` 15 项通过。修订后的回归覆盖制造费率和开票，其中未绑定工单以原单位成本生成成本凭证。其他成本、重启和事务回归复用开票边界调整前的通过结果。首次开票回归发现计算属性映射错误，保留 `shipment.trx`，修复后重跑相关路径。
- 验证会话及账套成员边界、跨操作者和跨账套选择凭据、独立复核、撤销批准资格后的发布拒绝、发布竞争与重复提交，以及旧区间的保留。
- MES 完工自动归集人工 `213.35`，追加发布后的手工重归集为 `170.88`，同一成本单追加用版并保留原证据。撤销账套成员后拒绝相关读取和归集。账套级过账和出库开票保护路径均无新增凭证。现行未绑定工单的结转、重启恢复及故障回滚有定向回归。[W690 PostgreSQL 用例](../../../../CP6.Tests/Erp/ManufacturingRateAdoptionPostgreSqlTests.cs)
- 任务级集中审查覆盖账套和实际工序绑定、来源与独立审批权限、精度、版本不可变、区间竞争、归集和过账边界。实际 HostBuilt 元数据为 82 个授权资源、114 个受控字段、2,110 个 MVC descriptor，增加 7 个入口。项目台账按本轮范围同步。[当前权限入口](../../authorization/current-main/README.md)
- 将受测 Web API Debug 程序集发布至隔离本地目录，四个产品程序集与受测文件 SHA-256 一致。`local-publish-sha256.json` 保存 310 个发布文件摘要。`local-publish-http.json` 绑定受测源码 SHA、实际启动地址和 HTTP 结果。健康检查返回 200，匿名账套查询返回 401。

WP02-03 继续进行。完整 WP11-39 和 R2 验收待完成。本轮业务验证使用隔离本地环境，生产部署次数为零。
