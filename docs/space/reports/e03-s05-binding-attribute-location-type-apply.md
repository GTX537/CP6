# E03-S05 Bindings、Attributes 与 LocationType 权威 Apply 扩展报告

日期：2026-08-08  
集成基线：`0cff2123`（`integration/space-v1-20260730`）  
功能提交：`b5aa87b2`
验证报告提交：`713ac99d`
no-ff 集成提交：`691c2d31`

## 交付结论

标准建模 Excel 的 `Bindings`、`Attributes` 与 `Locations.LocationType` 已建立无歧义的版本化持久合同，并进入 E03-S05 原有的 Serializable CommandBatch。Apply 仍只消费服务器重新打开、重新投影并校验过的权威 Excel 与 Match Artifact；同一批次中的 Rack、RackLevel、Location、绑定、属性、修订提升和命令审计要么全部提交，要么全部回滚。

本扩展没有把 Excel 声明冒充 WMS 已观测事实。`Bindings` 是随 ModelVersion 克隆、校验和发布的设计声明；既有 WMS Adoption 仍是独立运行事实，运行时库存仍从 WMS 读取。标准工作簿没有 Zone/Aisle 工作表，本次继续不伪造导入格式。

## 冻结合同

### Bindings

- `WmsWarehouseCode` 必须逐字符等于目标 Site 通过现有仓库解析器得到的权威 WarehouseCode；Apply 同时把当前运行源的 AdapterId 固定进版本快照。缺少解析器、Site 仓库或适配器身份时失败关闭。
- 外部身份键为 `AdapterId + WarehouseCode + ExternalLocationId`，同一版本内活动记录唯一；它可重新指向工作簿中的另一个 Location，但不能跨 Tenant 或 ModelVersion 移动。
- 每个在 Bindings 表中出现的 Location 必须恰有一个 `WmsPrimary`，可以有多个 `WmsAlias`。数据库以过滤唯一索引阻止同一 Location 出现两个活动 Primary；Primary/Alias 互换采用同一事务内两阶段切换，避免依赖 SQL 更新顺序。
- 映射方案声明 Bindings 表为权威时，当前批次所覆盖 Location 中被省略的旧声明会软删除，并追加确定性 Remove Command；不会物理删除历史或修改 WMS Adoption。

### Attributes

- 只允许挂到版本内的 `Rack`、`RackLevel` 或 `Location`；Rack/Location 的 BusinessKey 为业务编码，RackLevel 固定为 `RackCode/LevelNo`。
- Namespace 固定为 `Owner`、`Batch`、`Container`、`Manufacturing` 或 `Custom`；同一目标的 `Namespace + Key` 活动记录唯一。
- Value/Unit、Excel Source 与 SourceRef 随版本持久化；权威表中省略的旧属性软删除并产生确定性命令。多态目标不能由客户端提交 LogicalId，Apply 只从已复核的业务键解析。

### LocationType

- `Storage`、`Staging`、`Picking`、`Buffer` 是唯一允许值，大小写输入在领域边界规范为标准拼写；空值继续允许。
- LocationType 存入 `Space_LocationRevision`，参与场景、规划交换、内容哈希、版本克隆和发布快照，不再只存在于临时工作簿投影。

## 数据、版本与消费链

- 新增 `Space_LocationExternalBinding` 与 `Space_DesignAttribute`，均带 Tenant、ModelVersion、Source、SourceRef、审计字段和软删除；新增 LocationType 列。
- Migration `20260808131619_SpaceE03S05ExcelDesignMetadata` 由 EF 生成；增量 SQL 使用 `sqlcmd -I` 在临时库连续执行两次，最终恰有 1 条 MigrationHistory、2 张新表和 1 个 LocationType 列。
- 普通 Published→Draft 克隆和 Planning Scenario 克隆统一升级到 `space-clone-v2`，复制活动绑定/属性、保留 LogicalId，并重映射行 Id 与 SourceId。
- Validation 规则与处理器升级为 v2；内容哈希包含排序后的绑定和属性。无目标、Adapter 不匹配、非唯一 Primary 或属性目标不存在均产生 Blocking Issue。
- Publish Preview/快照包含 LocationType、全部声明绑定和目标属性；既有 WMS Adoption 在运行身份选择上继续优先。Design Scene v1、OpenAPI、C# SDK、TypeScript SDK 同步暴露新增只读数据。

## 幂等、遗漏与故障语义

- Excel Apply 处理器升级为 `space-excel-cad-apply-v2`，层级计划 Schema 升为 2；新 Binding/Attribute Id 与 CommandId 继续由冻结输入确定性生成。
- 相同 Artifact、CommandBatch 或 Job 重放不会创建重复元数据；属性值更新、绑定目标/模式更新和权威遗漏均保留 before/after 命令证据。
- 仓库不一致、目标不存在、Primary 数量错误、Namespace/ObjectType 非法、版本/来源漂移或数据库约束冲突均使整个事务失败，不提升 Floor Revision 或 ContentRevision。
- 被版本声明绑定或 WMS Adoption 绑定的 Location 不会因 Locations 权威表省略而被禁用。

## 验证证据

| 检查 | 结果 |
|---|---|
| 元数据领域 + Validation 聚焦 | 15/15 passed |
| Excel/CAD Match + Apply 聚焦 | 12/12 passed |
| Space Unit 全量 | 471/471 passed |
| 默认 Space Integration | 275 passed / 94 SQL-environment-gated skipped / 0 failed |
| 场景 + Clone/Migration 真实 SQL | 10/10 passed / 0 skipped |
| CP6.Tests 全量 | 2811 passed / 17 environment-gated skipped / 0 failed |
| Migration 增量 SQL 连续执行两次 | passed；1 history / 2 tables / 1 column |
| EF model drift | clean |
| OpenAPI/C#/TypeScript SDK drift | clean |
| TypeScript SDK strict no-emit | passed（锁文件 TypeScript 6.0.2） |
| 完整 `CP6.slnx` Release（Desktop/Android AOT） | 0 errors / 7 个未改动测试文件既有 warnings |
| 受影响 C# whitespace 与 `git diff --check` | passed |

第一次完整构建因 124 秒工具时限退出，残留 MSBuild 子进程锁住 Android 中间产物；确认父进程已消失后结束该残留进程，同一单线程完整构建通过。TypeScript 首次临时检查指定了不存在的精确版本 6.0.0；按仓库 lock 固定的 6.0.2 重跑通过，两者均未通过改代码或降低门禁规避。

## 剩余边界

本卡关闭了当前标准 Excel 合同中最后三个已知失败关闭字段，但不等于生产 CAD/Excel 正式签收。正式 DWG/DXF 仍等待获授权原生 Provider、组织黄金集和真实大文件/异常/性能证据；生产环境仍需部署包含本 Migration、Processing Worker 与本功能的新镜像，并执行生产等价备份、迁移和 WMS 发布/恢复演练。外部 AI Provider、E12-S06 DWG 写回和跨职能 Beta/GA 证据仍是独立缺口。

合并后清理本隔离工作区 37 个可重建 `bin/obj` 目录、9,093 个生成文件、1,783,694,326 bytes（1,701.06 MiB，约 1.66 GiB）；源码、Migration、报告和远端 Git 历史均保留。
