# E12-S05 标准 GLB 交换格式导出交付报告

- 状态：功能实现、全量门禁、远端备份、no-ff 受控集成、合并态复验和临时资源清理全部完成
- 起始基线：`ad77540d46fa6b5e4df1af6015b2adefe59299c5`
- 功能提交：`dd505f6fc8018c094ed83180c6a2da87798e6171`
- no-ff 集成提交：`c4b139ab38a795d0ecd733458d233ec16b3f582e`
- 原功能分支：`codex/space-e12-s05-gltf-export`（历史进入远端受控集成后已删除）
- 数据库 Migration：无

## 1. 标准选择与交付结果

E12-S05 选择 glTF 2.0 的单文件二进制容器 `.glb`，响应媒体类型为 `model/gltf-binary`。Khronos glTF 2.0 规范将 glTF 定义为面向运行时交付、与 API 无关的 3D 资产格式，并定义了 GLB 的单文件容器、JSON/BIN chunk、四字节对齐和小端头部；规范同时规定右手坐标系、`+Y` 向上、长度单位为米。规范来源：[glTF 2.0 Specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html)。

导出端点为：

- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}/exports/gltf`
- 权限：`space:planning:exchange:read`
- 成功响应：`.glb` / `model/gltf-binary`
- 完整性元数据：`ETag`、`X-Space-Exchange-Schema`、`X-Space-Exchange-Sha256`
- 缓存与嗅探护栏：`Cache-Control: private, no-store`、`X-Content-Type-Options: nosniff`

规划场景页只对 Ready、Clone Succeeded 且 Production Isolated 的分支显示下载入口；新增四个页面词条，均有简中、繁中、英文、日文和韩文运行时种子。本卡没有新增数据库表或迁移。

## 2. 几何、语义与坐标契约

交换包包含活动楼层、区域、巷道、货架、货架层、库位和通用元素。货架、可定位库位和通用盒体元素使用一份共享的带面法线单位立方体网格和三种材质；楼层边界、区域/巷道多边形、巷道中心线、货架层规格以及 CP6 稳定 LogicalId 保存在节点 `extras.cp6` 中。文件根元数据固定站点、模型、分支、基础 Published 版本、场景版本、内容修订、各类数量和安全边界。

CP6 源坐标为 `LOCAL_MM_Z_UP`，GLB 固定转换为 `(x,y,z) mm -> (x,z,-y) m`，同时把源 `RotationZ` 转为 glTF 的 Y 轴四元数。货架和通用元素从源下角锚点转换为中心点；库位位置由货架旋转、层底高、梁高、列号和深度号确定。测试直接解析 GLB 头部、JSON/BIN chunk、accessor、bufferView、法线引用、坐标、旋转和缩放，而不是只验证文件扩展名。

通用 glTF 查看器可以显示盒体货架、库位和元素；CP6 专有语义位于标准允许的 `extras` 中。当前版本没有把楼层/区域/巷道多边形三角化为可见 mesh，因此非 CP6 工具不会自动渲染这些语义边界。这是低成本可视化交换包，不是 CAD authoring 或 DWG 无损往返格式。

## 3. 确定性、规模与失败关闭

- 仅内部身份可调用；租户上下文、站点读权限、Site/Branch/Model/Version/Clone Job 血缘在读取几何前失败关闭。
- 仅接受基础 Production 版本与 PlanningScenario 版本严格配对、克隆成功且不占生产 Draft/Published 指针的分支。
- 关系数据库读取在 Serializable 事务内完成，所有类型使用稳定排序；输出不写入时间戳，同一快照生成完全相同字节和 SHA-256。
- 总数据节点上限为 50,000。每类查询按剩余预算读取至 `remaining + 1`，超限返回可恢复的 422，不会先把任意大场景全部载入内存。
- 活动对象缺失活动楼层、巷道缺失活动区域、库位缺失一致货架/层几何等情况返回明确 409，不导出带猜测的场景。
- 导出不读取库存、人员、设备事件或历史任务运行态，不修改生产指针，不产生发布、合并或写回事实。

## 4. API、SDK 与前端

Design V1 OpenAPI 从 83 增至 84 个唯一 operation，C# 与 TypeScript 生成客户端均新增二进制 `FileResponse` 下载方法并通过漂移和严格编译门禁。控制器声明 200 binary 响应，以及 400/401/403/404/409/422/500 `application/problem+json` 错误响应。

前端使用 Blob 下载并主动回收 Object URL；按钮有权限指令、分支就绪条件、单次下载锁、成功提示和问题详情错误回退。浏览器下载文件名包含场景版本号与分支 ID，服务端响应文件名另包含场景内容修订号。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| GLB 服务与二进制结构聚焦 | 2 passed / 0 failed |
| 权限、API、OpenAPI 与种子聚焦 | 65 passed / 0 failed |
| 前端下载聚焦 | 2 files / 9 tests passed |
| Space Unit 全量 | 272 passed / 0 failed |
| Space Integration 默认全集 | 247 passed / 0 failed / 63 SQL 环境门禁 skipped |
| CP6.Tests 全量最终复验 | 2,777 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 123 files / 676 tests passed |
| 前端严格类型检查 | passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution 非增量 Release | 0 error / 10 条既有 warning |
| `SpaceContext` 与 `CP6Context` EF pending model | 均无待迁移模型变化 |
| Design V1 SDK drift | passed；84 unique operations |
| TypeScript SDK strict no-emit | passed |
| Git 差异检查 | passed |

第一次把四套全集并行运行时，既有 `IntegrationEventRetryWorkerTests.Worker_backfill_reloader_cannot_overwrite_another_workers_future_claim` 因高负载在 49 秒处取消；该用例随后隔离运行 1/1 通过，且无并行负载的 CP6.Tests 完整复验为 2,777 passed / 17 skipped。最终门禁采用后者，未隐藏第一次环境抖动。

默认 Space Integration 和 CP6.Tests 没有连接发布 SQL Server，因此 63 与 17 项环境门禁跳过不能记作通过。发布前仍需在发布环境执行真实 SQL 回归。GLB 当前由仓库内的二进制解析测试验证，没有把外部 Khronos Validator 运行冒充为已完成证据。

## 6. 明确未做与后续边界

本卡不做 DWG/DXF 写回、CAD 图层保真、块/样条/标注/外部引用往返、材质贴图烘焙、运行态快照、外部公开分享、场景合并、生产发布或生产写回。glTF 规范本身面向运行时交付而非 authoring；因此本卡不能替代 E12-S06“DWG 回写技术试验与授权评估”。

E12-S06 继续等待可用于写回的正式黄金样本、明确的 DWG SDK/供应商授权条款和可审计的技术试验环境。在这些输入到位前，不会用自研二进制写入或未经授权的转换链冒充 DWG 回写能力。E03-S04、E04-S05、E06 与 E13 CAD 后续链同样继续受正式黄金集、供应商证据和冻结 Worker 约束。

## 7. 远端备份、受控集成与资源清理

功能提交 `dd505f6f` 先推送到远端临时分支备份，再以 `--no-ff` 合入 `integration/space-v1-20260730`，集成提交为 `c4b139ab`。远端集成 tip 与本地一致，功能提交已确认是远端集成祖先，且合并提交文件树与功能提交完全一致。

合并态串行复验再次通过 GLB 2/2、权限/API/OpenAPI 65/65、双 EF 和 SDK drift；同一文件树的前端聚焦为 9/9。随后已删除远端功能分支、功能工作树和本地功能分支，并清理 Git worktree 元数据。移除前功能工作树包含 35,537 个文件、2,869,523,790 字节（约 2.672 GiB，含物理 `node_modules`、编译和测试产物）；`main` 未被本轮操作修改。
