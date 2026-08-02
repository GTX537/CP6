# E10-S05 货主、SKU、批次和容器空间筛选交付报告

- 状态：已进入 Space 受控集成分支
- 功能分支：`codex/space-e10-s05-inventory-filters`
- 起始基线：`c573ff2517dcff629e81ed1a0b09646d76401489`
- 功能提交：`65c59555`
- no-ff 集成提交：`e270c2cc`
- Migration：无

## 1. 交付结果

E10-S05 在 E08-S01～S03 的统一 WMS 运行源与库存定位合同上增加货主维度，
并把一次性定位结果扩展为 Viewer 中可持续、可清除、可跨层解释的空间筛选状态。

1. `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate` 新增可选
   `ownerId`；货主、`materialNumber`、`lotNumber`、`containerNumber` 至少一个，
   多个条件固定按精确 AND 匹配。
2. 货主在服务边界去空白并规范为大写；查询仍只覆盖当前 Published/Active
   Space 库位，继续使用稳定 LogicalId、500 位置分块和 10,000 位置上限。
3. 定位条件和每个聚合命中都返回货主事实；服务端重新验证适配器返回行的
   正库存、货主、SKU、批次和容器，越界结果以
   `SPACE_WMS_RUNTIME_CONTRACT_VIOLATION` 失败关闭。
4. CP6 真实适配器在 WMS 边界过滤货主。容器实体自身没有货主列，因此通过
   同仓库、库位、SKU、批次的唯一 `T_Stock` 业务键取得货主；不存在匹配库存
   关系时不返回该容器，不在浏览器或 Space 设计模型中猜测、复制业务事实。
5. 标准模拟器执行相同的精确 AND、正库存和货主语义；Real/Simulated/
   Unavailable、观察时间、接收时间与延迟合同保持不变。

## 2. Viewer 空间筛选

Viewer 新增独立库存空间筛选面板，原有按编码/物料/批次/容器的一次性定位流程
继续有效，二者不互相替代。

- 货主、SKU、批次、容器可单独或组合输入，输入在浏览器端先去空白，货主
  规范为大写，服务端再次规范与验证。
- 命中库位使用琥珀色，当前楼层未命中库位使用深灰色压暗；筛选状态优先于
  库存状态色，并在库存轮询刷新及楼层切换后继续生效。
- 面板显示本层命中数、全站命中数、楼层数、各楼层命中数、来源类型、来源 ID
  与观察时间。点击楼层摘要可切换楼层；零命中与来源不可用保持不同语义。
- 清除筛选会恢复当前库存覆盖模式。较旧并发请求、已清除请求或组件卸载后的
  响应不能覆盖新状态；查询失败保留最后一次成功筛选并给出安全提示。
- 作业热图与库存空间筛选互斥；启用筛选时先关闭热图，避免两个颜色权威互相覆盖。

## 3. API、SDK 与数据边界

Design V1 保持 67 operations，没有新增路径或数据库迁移。OpenAPI、生成 C# SDK
和 TypeScript SDK 已同步；生成器补齐了运行态定位条件中合法 JSON `null` 的
TypeScript 类型，`-Check` 无漂移。

本卡继续遵守以下边界：

- WMS 是货主、SKU、批次、容器和数量的业务真相源；Design Revision 不保存这些事实。
- 本卡只读，不增加库存写入、导出、外部 Portal 权限、Grant 或字段策略能力。
- 不包含 ABC 口径、利用率快照、仓库 KPI 或总览；这些属于 E10-S06。
- 不包含设备控制、MQTT/OPC UA、历史分析、预测、CAD 或外部 AI Provider。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 运行态合同聚焦 | 2 passed |
| Runtime/CP6/模拟适配器聚焦 | 68 passed |
| 权限与 Design V1 OpenAPI/SDK 聚焦 | 45 passed |
| 前端 E10-S05 聚焦 | 4 files / 22 tests passed |
| Space Unit Release 全量 | 236 passed / 0 failed |
| Space Integration 默认全集 | 190 passed / 0 failed / 61 SQL-environment skipped |
| CP6.Tests Release 全量 | 2738 passed / 0 failed / 17 environment-gated skipped |
| 前端全量 | 114 files / 632 tests passed |
| 前端类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量构建 | 0 errors / 10 条既有 warnings |
| E10-S05 真实 SQL | 1 passed / 0 failed / 0 skipped |
| 完整真实 SQL 矩阵 | 250 passed / 1 既有基线失败 / 0 skipped |
| EF 模型、OpenAPI/C#/TypeScript SDK、差异门禁 | passed；无待迁移模型变化 |

完整真实 SQL 矩阵的唯一失败仍为
`SpaceExcelPreflightSqlServerTests.Sql_start_atomically_pins_source_job_and_idempotency`：
既有测试种子同时新增 `SpaceModel` 与 `SpaceModelVersion` 时形成循环外键图，失败发生
在 E10-S05 查询前。该失败已在 E10-S03 之前的旧集成基线独立复现；本卡新增的
SQL Server 用例已证明货主+容器组合查询可翻译、命中正确货主并拒绝错误货主。

## 5. 后续

正式 backlog 的下一张独立卡为 E10-S06“仓库 KPI 快照、利用率与 ABC 口径”。
CAD/E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入；本卡不改变
该优先级或失败关闭边界。
