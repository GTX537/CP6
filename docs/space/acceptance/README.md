# CP6 Space MVP 验收资产

本目录是 `Space MVP Scope Baseline v1.0` 的验收入口，服务于 E02-S01、E07-S04、E09 和 E13-S14～S15。

## 1. 当前内容

| 文件/目录 | 用途 | 发布门禁 |
|---|---|---|
| [`v1.3-ga/ga-evidence-index.json`](./v1.3-ga/ga-evidence-index.json) | 核心 GA Owner、外部输入、WP0–WP8 证据和五方签字的失败关闭索引 | 核心 GA |
| [黄金数据与基准协议](./01-golden-dataset-protocol.md) | 数据版本、标注、分层、指标和证据 | Beta/GA |
| [WMS 与权限场景矩阵](./02-wms-permission-scenarios.md) | 绿地、存量、故障、外部角色和越权验收 | Alpha/Beta/GA |
| [`v1.0.0/manifest.json`](./v1.0.0/manifest.json) | 冻结包机器清单 | 开发启动 |
| `v1.0.0/seeds/*.dxf` | 五类完全合成 CAD 种子 | 技术试验，不计发布指标 |
| `v1.0.0/expected-elements.jsonl` | 种子样本机器可读答案 | 技术试验 |
| `v1.0.0/expected-issues.json` | 非标准/噪声样本期望问题 | 技术试验 |
| `v1.0.0/provider-ir.jsonl` | 可发送 Provider 的最小化 IR 示例 | AI 安全契约 |
| `v1.0.0/layer-mapping.json` | 图层语义映射基线 | CAD/AI 试验 |
| [`development-v2.0.0/manifest.json`](./development-v2.0.0/manifest.json) | 20 份可重复生成的 L1～L5 合成 DXF 及哈希清单 | 扩展开发语料，不计发布指标 |
| [`development-v2.0.0/case-index.json`](./development-v2.0.0/case-index.json) | 版本、图元、图层和开发场景索引 | CAD 解析/映射/问题/UI 回归 |

## 2. 种子与正式黄金集的区别

- `v1.0.0` 的五份 Seed 是最小合成开发资产，每类布局一份。
- `development-v2.0.0` 扩展为 20 份合成 DXF，每类布局四份，并覆盖 AC1009、AC1015、AC1021、AC1027、AC1032 文件头。
- Seed 可以用于解析器、规则、Provider 和测试代码开发。
- Seed 不得计入 AI 发布门禁的覆盖率、准确率或高置信度精确率。
- Beta 前必须另建至少 20 份黄金 CAD，每类至少 4 份，并分成：
  - 10 份校准集。
  - 5 份验证集。
  - 5 份发布留出集。
- 发布留出集在当前发布周期内不得用于 Prompt、阈值、规则或映射调优。

## 3. 目标测试目录

冻结资产先保存在 `docs/space/acceptance`，便于审查。E07-S04 实现时把通过授权和标注审查的不可变版本复制到：

`CP6.Tests/TestData/Space/Acceptance/{semanticVersion}/`

复制后必须保持文件 SHA-256 不变。测试项目不得直接依赖 `docs` 中的可变工作副本。

## 4. 不可变规则

- 已用于发布证据的版本禁止原地覆盖。
- 文件、标准答案、映射、规则或容差变化都必须提升语义版本。
- 每个源文件必须有 SHA-256、授权来源、单位、坐标和布局分类。
- 原始客户文件不得直接进入仓库。
- 任一资产缺少授权、脱敏或哈希证据时不得用于发布门禁。

## 5. 快速验证

验收包至少通过：

1. `manifest.json` 和其他 JSON 可以解析。
2. 每个 DXF 使用成对 group-code/value 行并以 `0/EOF` 结束。
3. Manifest 中 SHA-256 与文件一致。
4. 每个 Seed 在 `expected-elements.jsonl` 中至少有一个目标元素。
5. `provider-ir.jsonl` 不包含文件路径、客户名称、用户信息或原始二进制。

