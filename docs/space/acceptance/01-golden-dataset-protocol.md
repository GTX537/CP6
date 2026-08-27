# Space 黄金数据与性能基准协议

## 1. 目的

本协议规定 CAD/AI 质量、低成本建模和 10,000 库位性能的权威数据来源。实现团队只能优化算法，不能通过更换数据、删除失败样本或修改分母改变结果。

## 2. 五类布局

| ID | 布局族 | 必须覆盖 |
|---|---|---|
| L1 | 规则矩形货架仓 | 规则墙体、正交货架、巷道、门和月台 |
| L2 | 多楼层货架仓 | 至少两层、楼层局部坐标、相同编码命名冲突防护 |
| L3 | 斜放/非正交货架仓 | 非 90° 货架、斜巷道、旋转块 |
| L4 | 综合仓 | 墙、柱、门、月台、货架、托盘和静态常见设备 |
| L5 | 非标准/噪声仓 | 非标准图层、缺失块属性、重复/噪声图元和未知实体 |

完整 20 份黄金集要求每类至少 4 份。

## 3. 数据分层

| 分层 | 数量 | 可否调优 | 用途 |
|---|---:|---|---|
| Calibration | 10 | 可以 | Prompt、规则、映射和阈值校准 |
| Validation | 5 | 仅在版本冻结前 | 回归和候选版本比较 |
| Release Holdout | 5 | 当前发布周期禁止 | Beta/GA 发布门禁 |

五份合成 Seed 归类为 `DevelopmentSeed`，不属于上述 20 份。

## 4. 单资产必备文件

正式黄金资产目录必须包含：

- 原始 DWG/DXF。
- `metadata.json`：单位、坐标系、布局族、授权、脱敏和来源。
- `expected-elements.jsonl`：逻辑元素、来源 Handle、规范几何、关系和关键属性。
- `expected-issues.json`：应产生的 Blocking/Warning/Info。
- `provider-ir.jsonl`：允许外发的最小化特征。
- `mapping-profile.json`：图层、块和属性映射版本。
- 可选 `expected-locations.csv`：稳定 LogicalId、编码、层级、坐标和状态。

## 5. Manifest 必填字段

```json
{
  "datasetVersion": "1.0.0",
  "sampleId": "L1-001",
  "split": "Calibration",
  "layoutFamily": "L1",
  "sourceFile": "input.dxf",
  "sourceSha256": "<64 hex>",
  "unit": "Millimeter",
  "coordinateSystem": "FloorLocal-ZUp",
  "mappingProfileVersion": "space-cad-mapping-v1",
  "ruleSetVersion": "space-v1",
  "license": "Synthetic|ApprovedOriginalWork|ApprovedCustomerDerived",
  "deidentificationEvidence": "<reference>",
  "expectedAnswerVersion": "1.0.0"
}
```

正式包还必须记录应用提交 SHA、Parser/Provider/模型版本和验收日期。

## 6. 标注规则

- 每个元素使用稳定 `expectedId`，禁止用数据库运行时 GUID 作为标准答案。
- 几何统一为毫米、Z-up、Floor Local。
- 元素必须包含 `sourceRefs`，至少追踪到 Layer、Handle 或 Block。
- 关系必须显式记录 Floor、Zone、Aisle、Rack 和 Location 父子关系。
- 不确定对象进入 Expected Issue，不得为了提高准确率从分母中删除。
- 一名实名复核人完成可追溯标注复核；发现不一致时提升答案版本并重跑，不要求第二标注人或独立 QA。
- SoloDeveloper 可使用开发者原创、由 AutoCAD 原生保存且权利声明完整的 `ApprovedOriginalWork`；不得伪装为客户来源。`ApprovedCustomerDerived` 仅用于确有客户授权来源的样本。

## 7. 匹配与容差

| 类型 | 匹配标准 |
|---|---|
| 点/插入点 | 欧氏距离 ≤1mm |
| 直线端点 | 双向端点距离 ≤1mm |
| 角度 | 绝对误差 ≤0.1° |
| 闭合多边形 | IoU ≥0.98 且面积相对误差 ≤0.1% |
| 文本/编码 | 修剪空白后精确匹配；大小写规则由映射方案声明 |
| 父子关系 | Logical relation 精确匹配 |

单位未知或坐标无法确定时必须产生 Blocking Issue。

## 8. 指标

| 指标 | 公式 | 门槛 |
|---|---|---:|
| 目标覆盖率 | 正确生成目标数 ÷ 标准答案目标总数 | ≥80% |
| 整体准确率 | 正确生成目标数 ÷ 自动生成目标总数 | ≥90% |
| 高置信度精确率 | 高置信度正确提案数 ÷ 高置信度全部提案数 | ≥95% |
| 人工操作下降 | 1 - AI路径操作数 ÷ 纯编辑器操作数 | ≥70% |
| AI 提案时效 | 50MB CAD 到可审查提案 | P95≤15分钟 |
| 首次 Ready | 上传成功到第一次 Ready | ≤60分钟 |

高置信度默认阈值为 0.90，但阈值本身不代表精确率。高置信度组的 95% Wilson 置信区间下界必须 ≥90%；样本不足时关闭批量接受快捷入口。

## 9. 标准 10,000 库位仓

- 500 个货架。
- 每货架平均 20 个库位，总计精确为 10,000。
- 至少 5 个 Zone、20 个 Aisle、2 个 Floor。
- 包含 SKU、库存、批次、容器和至少 100 个拣货任务。
- 同一模型用于 2D、3D、WMS 模拟器和发布恢复测试。

E07-S04 实现时生成 `expected-locations.csv` 和 `wms-seed.json`，并把生成器版本和随机种子写入 Manifest。生成器不得在测试运行时使用未固定随机数。

## 10. 性能报告

性能环境和运行方法以 [ADR-0004](../adr/0004-performance-acceptance-environment.md) 为准。报告必须保存原始结果、哈希、P50/P95、失败率、硬件、浏览器、Migration、数据包版本和应用 SHA。

## 11. 版本升级

以下任一变化提升 Major 或 Minor：

- 标准答案语义改变。
- 容差或指标公式改变。
- 数据集分层改变。
- 新增或删除发布门禁样本。

只修正文案、补充不影响计算的元数据可以提升 Patch。已发布版本永不覆盖。

