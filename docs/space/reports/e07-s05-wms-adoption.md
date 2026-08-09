# E07-S05 存量 WMS 采纳与绑定交付报告

- 状态：已完成并合入 Space 受控集成分支
- 工作分支：`codex/space-e07-s05-adoption`
- 设计提交：`0e124e66`
- 功能提交：`15ccf992`
- no-ff 集成提交：`389bf4ec`
- 迁移：`20260731090933_SpaceE07S05WmsAdoption`

## 1. 交付闭环

本卡完成“拉取、放置、绑定、未绑定和差异对账”完整竖切：

1. 从当前 `ISpaceWmsAdapter` 拉取站点 WMS 位置目录，并保留
   Adapter、真实/模拟数据源、外部身份、LogicalId、版本和状态哈希。
2. 以独立的 `Space_WmsAdoption` 账本记录 WMS 身份和最近观测，
   不把库存、任务等运行态写入 Design Revision。
3. 支持把 WMS 位置绑定到现有 Draft 库位几何；编码切换为
   `Adopted/Bound`，绑定后不能被另一个 WMS 编码静默覆盖。
4. 支持在选中货架的有效空格中创建库位并立即绑定；尺寸、承重取自对应
   Rack Level 规格，LogicalId 复用 WMS 稳定身份。
5. 支持最多 1,000 项批量绑定，重复目标、缺失几何、编码冲突或并发冲突
   均整批失败，不产生部分副作用。
6. 刷新时同步未绑定、编码重复、几何缺失、编码漂移和 WMS
   缺失/停用差异；未绑定为 Warning，其余为 Blocking。

## 2. 权威边界

- WMS 采纳账本是站点/适配器级外部身份账本。
- Design Revision 继续只保存几何、位置编码和绑定状态。
- 库存、批次、容器、货主和任务仍由 WMS 运行态接口提供，本卡没有复制。
- 绑定和放置只允许 `Draft` 版本；`Ready`、`Published` 等状态失败关闭。
- 货架、楼层、层规格和目标库位必须处于 `Active` 生命周期。
- 停用或最近目录中缺失的 WMS 位置不能绑定，并产生 Blocking 差异。
- 生产 DI 继续解析 CP6 WMS 适配器；标准模拟器始终明确标记
  `Simulated`，不会冒充真实 WMS。

## 3. API、权限与 SDK

Design V1 新增 5 个端点：

- `POST /versions/{versionId}/wms-adoption/refresh`
- `GET /versions/{versionId}/wms-adoption/locations`
- `POST /versions/{versionId}/wms-adoption/locations/{adoptionId}/bind`
- `POST /versions/{versionId}/wms-adoption/bindings:batch`
- `POST /versions/{versionId}/wms-adoption/locations/{adoptionId}/place`

读取使用 `space:model:read`；绑定、批量绑定和放置使用
`space:model:edit`；刷新同时要求 `space:integration:manage` 和
`space:model:edit`。管理员权限种子已补齐 `integration:manage`。

OpenAPI、C# SDK 和 TypeScript SDK 已同步重生。刷新端点明确公开
502/503 Problem Details；所有写请求携带 WMS 账本 rowversion，
陈旧提交返回稳定并发冲突。

## 4. 编辑器工作台

Design V1 编辑器新增 WMS 存量采纳侧栏：

- 显示真实/模拟来源、状态和差异代码；
- 支持状态/差异筛选及每页 100 项游标分页；
- 选择货架后可人工选择现有未绑定库位几何；
- “按库位顺序预填”只生成可审阅映射，不自动提交；
- 支持单项绑定、批量绑定和放入首个有效空位；
- 非 Draft 场景只读，刷新和模型写操作仍由前后端权限双重保护。

## 5. 持久化与约束

`Space_WmsAdoption` 包含租户、站点、适配器、数据源类型、WMS
LogicalId/ExternalId、编码、活动状态、外部版本、SHA-256 状态哈希、
最近观测、绑定目标和 SQL Server rowversion。

数据库约束包括：

- Tenant + Site + Adapter + WmsLogicalId 唯一；
- 非空 ExternalLocationId 唯一；
- 非空 LocationLogicalId 唯一；
- 可选 ModelVersion 采用租户复合外键；
- 状态/编码查询索引和租户查询过滤器。

迁移同时提供 EF Migration、模型快照和可审阅 SQL Server 增量脚本。

## 6. 验证证据

| 检查 | 结果 |
|---|---:|
| Space UnitTests 全量 | 218 passed |
| Space IntegrationTests 默认全集 | 56 passed / 48 SQL-gated skipped |
| WMS 采纳聚焦（含真实 SQL） | 11 passed |
| 其中 KOUSQLSERVER 隔离库 | 3 passed / 0 skipped |
| OpenAPI、权限、权限种子 | 35 passed |
| 前端 Vitest 全量 | 98 files / 579 tests passed |
| 前端 WMS 聚焦 | 2 files / 4 tests passed |
| Vue/TypeScript type-check | passed |
| 前端 production build | passed，仅既有大 chunk 提示 |
| `CP6.slnx` 完整构建 | 0 warnings / 0 errors |
| EF pending model changes | none |
| SDK drift | none |
| `git diff --check` | passed |

no-ff 合并后再次通过 Space Unit 218、WMS 采纳 11（含
KOUSQLSERVER 3 项）、前端聚焦 4 项和 Vue 类型检查。

## 7. 后续

E07-S01 至 E07-S05 已形成完整集成基线。下一张建议卡为
E08-S01“统一运行态数据源接口”，在既有 CP6/模拟器契约上统一库存与任务
DTO；本卡的采纳账本继续只负责外部位置身份和 Design 绑定。
