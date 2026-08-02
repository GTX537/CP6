# E10-S06 仓库 KPI 快照、利用率与 ABC 口径交付报告

- 状态：已进入 Space 受控集成分支
- 功能分支：`codex/space-e10-s06-warehouse-overview`
- 起始基线：`240a32bcde7d28632ab4ff2029d578d9ff0be03f`
- 合同提交：`bffe1877`
- 功能提交：`0676ba4a`
- no-ff 集成提交：`5f86edcb`
- Migration：无

## 1. 交付结果

E10-S06 在当前 Published/Active Space 模型与统一 WMS 运行源上增加只读仓库总览，
并将面积、占用、作业、ABC 和异常的来源、时间与不完整状态显式交给调用方。

1. 新增
   `GET /api/space/design/v1/sites/{siteId}/runtime/overview?abcWindowDays=90`，
   沿用 `space:model:read`；ABC 窗口允许 1～365 个完整自然日。
2. 楼层面积按边界毫米坐标的鞋带公式计算；只要任一活动楼层边界无效，站点总面积
   即不伪造，楼层明细和异常仍保留。货架占地使用宽×深，只表达建模占地率。
3. 库位占用率是正库存物理库位数 / 活动库位数。库位容量主数据尚不存在，因此
   容量利用率固定为 `null`，并返回 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`，
   不以库存数量或货架体积替代容量。
4. 库存只返回分单位计数，不跨单位合计数量。作业快照返回活动任务去重数和活动
   Stop 数，不把它命名为吞吐量。
5. ABC 只统计完整自然日窗口内的正数 OUT 交易，按出库量降序、物料号顺序排名；
   排名前累计占比小于 80% 为 A、小于 95% 为 B，其余为 C。当前有库存但窗口内
   无正出库的 SKU 为 Unclassified；多 SKU 库位按 A > B > C > Unclassified 着色。
6. 库存、作业和 ABC 分别携带来源、观察时间、接收时间与可用性；任一来源异常或
   楼层面积缺失时返回可解释的部分快照。越界、重复或不可信适配器数据失败关闭。
7. 异常汇总覆盖活动/严重设备告警、编码不一致、超分配行、缺失楼层面积和未分类
   ABC SKU，不加入趋势、预测、周转率或虚构容量。

## 2. Viewer 总览与 ABC 覆盖

- Viewer 新增按需仓库总览面板，展示来源 ID、来源/观察时间、KPI、容量不可用原因、
  ABC 分布、异常和逐楼层明细；失败时保留最后一次成功快照。
- ABC 使用固定颜色：A 红、B 橙、C 蓝、Unclassified 深灰，空库位保持中性色。
- ABC、E10-S05 库存空间筛选与作业热图互斥；启用一个颜色权威会关闭另外两个，
  关闭 ABC 后恢复底层库存覆盖模式。
- ABC 状态跨库存轮询和楼层切换保持；请求版本与卸载保护阻止旧响应覆盖新状态。

## 3. API、SDK 与数据边界

Design V1 从 67 增至 68 operations。OpenAPI、生成 C# SDK 与 TypeScript SDK 已同步，
包括必填且允许 JSON `null` 的总览字段，生成器 `-Check` 无漂移。

本卡不新增数据库表、Migration 或持久化快照。WMS 继续是库存、作业和出库事实源；
Design Revision 只提供当前 Published/Active 几何与稳定空间身份。本卡不包含外部
Portal、容量主数据、设备控制、MQTT/OPC UA、历史趋势、预测、CAD 或外部 AI Provider。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit Release 全量 | 236 passed / 0 failed / 0 skipped |
| Space Integration 默认全集 | 198 passed / 0 failed / 62 SQL-environment skipped |
| E10-S06 CP6 适配器真实 SQL | 3 passed / 0 failed / 0 skipped |
| CP6.Tests Release 全量 | 2739 passed / 0 failed / 17 environment-gated skipped |
| 权限与 Design V1 OpenAPI/SDK 聚焦 | 46 passed / 0 failed |
| 前端全量 | 115 files / 639 tests passed |
| 前端最终聚焦 | 3 files / 25 tests passed |
| 前端类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量构建 | 0 errors / 10 条既有 warnings |
| EF 模型与 OpenAPI/C#/TypeScript SDK 漂移 | passed；无待迁移模型变化 |
| Git 差异检查 | passed |
| i18n 快照 | 未通过：集成基线已有 881 项，本卡新增 30 项，共 911 项 |
| 合并态后端冒烟 | 合同 23/23；Runtime/适配器 81/81；权限/OpenAPI 46/46 |
| 合并态前端冒烟 | 3 files / 25 tests；严格类型检查 passed |

i18n 结果作为显式技术债保留：本卡没有手工篡改生成快照，也没有把缺少正式翻译的
文字伪装为已完成。它不影响类型、构建或运行测试，但在发布本地化验收前必须补齐。

验证期间还删除了一个 2026-08-01 遗留、无活动会话的随机测试临时库
`CP6SpaceE07_ecc9d2972cca40f0957ba9abfef1db2b`；删除后已确认不存在，产品数据库
未被修改。该临时库删除不可恢复。

## 5. 后续

E10 P2 的 S01～S06 至此均已具备完成证据。下一张独立实施卡应在 E11 诊断链与其他
已解锁 backlog 中重新按依赖选择；不能把当前快照直接扩写成趋势、推荐或执行控制。
CAD/E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入，本卡不改变
该优先级或失败关闭边界。
