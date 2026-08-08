# E13 RackGenerationProfile 权威版本链交付报告

日期：2026-08-08  
功能提交：`19d32650`  
基线：`d0d1c713`（`integration/space-v1-20260730`）

## 1. 交付结论

上一切片遗留的内部缺口已经关闭：Rack 提案现在可以由用户显式选择一份可审计、不可变的 RackGenerationProfile 版本，Generation Run 冻结该精确版本，BuildScene Worker 再据此确定性生成 RackLevel 与 Location。系统不再接受任意未验证 GUID，也没有发明隐式默认尺寸。

这是一条独立的货架生成规格权威链，没有复用或伪装成渲染用途的 `SpaceAssetVersion`。已有 Asset 仍负责 GLB/参数化渲染；RackGenerationProfile 负责货架宽深高、逐层净高/横梁、格口与进深、单元尺寸和承重等生成语义。

## 2. 权威数据与约束

- 新增 `Space_RackGenerationProfile` 头记录和 `Space_RackGenerationProfileVersion` 不可变版本记录；System 方案全租户只读可见，Tenant 方案只对所属租户可见。
- Tenant API 只允许创建 Tenant 方案及不可变 v1；System 写入仍保留给受控平台流程，不开放租户伪造入口。
- 版本定义采用规范化层顺序和服务端 SHA-256 内容指纹；校验 1～1000 层、唯一正层号、正尺寸/格口/进深、单元可装入货架、层高可装入总高，以及最多 10,000,000 个派生库位。
- 数据库增加 scope/owner Check Constraint、有效尺寸和库位数约束、活动编码唯一索引、版本号唯一索引及 scope/owner/profile 复合外键。
- `SpaceContext` 拒绝跨租户写、物理删除、头记录修改和版本修改；公开创建采用 Serializable 事务及 24 小时重放/90 天保留的幂等记录。
- Migration `20260808164544_SpaceE13RackGenerationProfiles`、模型快照和幂等部署 SQL 已同步；真实 SQL 临时库完成迁移，部署脚本连续执行两次通过。

## 3. API、审计与 SDK

新增三条 Design V1 操作：

- `GET /api/space/design/v1/rack-generation-profiles`
- `GET /api/space/design/v1/rack-generation-profile-versions/{versionId}`
- `POST /api/space/design/v1/rack-generation-profiles`

读取要求 `space:model:read` 并记录读审计；创建要求 `space:model:edit`、`Idempotency-Key` 和写审计。所有操作暴露统一 400/401/403/404/409/422/500 Problem Details，创建响应公开 `Idempotent-Replay`。OpenAPI operation 数由 115 增至 118，C# 与 TypeScript SDK 已重新生成并通过漂移检查。

## 4. Generation Run 与 BuildScene

- 首次创建 Run 时，服务端重新查询当前租户可见的 Active 方案头和 Ready 精确版本；不存在、已退休或跨租户版本返回 `SPACE_RACK_GENERATION_PROFILE_NOT_FOUND`。
- Run 继续冻结 `RackGenerationProfileVersionId`；BasedOn recovery 沿用源 Run 的冻结版本，不能借恢复请求替换定义。
- RuleOnly Worker 只读取 Run 中冻结的版本，把它作为 `ExplicitSelected` 绑定到当前权威 Semantic Preview 中所有非拒绝、有几何的 Rack 项，再交给既有确定性合成器生成 RackLevel/Location。
- 冻结版本在执行时不可用会按输入错误失败关闭；未选择方案仍保持原有 `SPACE_RACK_PROFILE_REQUIRED` Blocking 行为。

## 5. Web 行为

建模生成面板会同时读取可见的 Active/Ready 方案，并提供显式、可清空的可选选择器。界面不自动选择第一项；确认框会说明冻结的方案，或说明不选择时 Rack 提案继续 Blocking。选中后展示尺寸、层数、派生库位数和内容指纹，并把精确版本 ID 送入统一 `CreateGenerationRun`。

## 6. 验证证据

| 门禁 | 结果 |
|---|---|
| RackGenerationProfile 领域聚焦 | 3/3 passed |
| 配置服务、Run 冻结与 BuildScene 聚焦 | 13/13 passed |
| 真实 SQL 迁移/幂等/隔离/约束 | 1/1 passed，部署脚本双执行 |
| OpenAPI + 权限/审计聚焦 | 63/63 passed |
| 前端配置加载与显式冻结聚焦 | 2 files / 9 tests passed |
| 前端全量 | 133 files / 711 tests passed |
| 前端 type-check + production build | passed |
| Space Unit 全量 | 487/487 passed |
| 默认 Space Integration | 288 passed / 95 SQL-gated skipped / 0 failed |
| CP6.Tests | 2816 passed / 17 environment-gated skipped / 0 failed |
| OpenAPI/C#/TypeScript SDK drift + TypeScript strict | passed |
| EF pending model changes | none |
| 完整 solution Release（含 Desktop/Android AOT） | 0 warning / 0 error |
| C# whitespace / `git diff --check` | passed |

## 7. 明确保留边界

- 当前公开写入口创建不可变 v1，但尚未提供“给现有方案追加 v2”的管理端点，也没有 System 方案配置入口或完整方案管理 UI；这些应作为独立产品卡处理。
- 选择器当前按 API 上限读取前 200 个方案；大规模方案库的搜索/分页是后续 UX 增强，不影响服务端权威校验。
- 无人工锁时的确定性 Zone/Aisle/Rack 父关系推导、不同 SourceHash 的几何匹配与人工确认仍未完成。
- 外部 Provider、供应商/法务/网络/预算证据、正式获授权 DWG/DXF、20 份黄金集、真实大文件/故障/性能和人工签字仍是独立外部签收项。
- 本切片没有启用 Provider、外部网络、Secret、AI Usage、High Accept 或 Draft 自动写入；所有 Proposal 仍必须经过人工 Decision 与原子 Apply。
