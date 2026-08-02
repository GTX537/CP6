# E11-S01 运营诊断交付报告

- 状态：功能分支验证完成，待进入 Space 受控集成分支
- 起始基线：`b6770e7943a7daf3d313e91d695f78ef808ec6f3`
- 合同提交：`66b6c17f15b4589e6654537ae550476c90d21c25`
- 功能提交：`53a07d46a0df10a9e5254ea89fc969deb71e2a45`
- 功能分支：`codex/space-e11-s01-operational-diagnostics`
- Migration：无

## 1. 交付结果

E11-S01 增加内部只读运营诊断端点：

`GET /api/space/operations/v1/sites/{siteId}/diagnostics?fromUtc=&toUtc=`

它只读取当前租户、请求 Site、当前 Published/Active 模型和 E10 已有人员/WMS
运行证据。时间窗固定为 UTC 半开区间，最长 24 小时且不能越过既有 30 天人员
保留期；超过 100,000 条位置证据时以 422 失败关闭。响应定义版本为
`space-operations-diagnostics-v1`，即时计算，不持久化诊断结果。

端点使用 `space:operations:diagnostics:read`，仅内部主体可访问；GET 强制记录
`space.operations.diagnostics.read` 审计，审计失败沿用现有失败关闭行为。请求不接受
租户、版本、人员、来源或阈值覆盖，响应不含人员 ID、外部人员号、UserId 或可逆匿名键。

## 2. 冻结口径

- 路径只累计同一人员、同层、严格递增且相邻不超过 300 秒的二维观测段；缺坐标的
  同库位连续点只确认零位移，跨层、超时、模型外和不同无坐标库位均计为未知段。
- 折返要求两个连续向量均至少 1,000 mm 且夹角至少 150°；总数保持精确，返回证据
  稳定排序并最多 100 条。
- 停留是同人、同层、同库位连续 observed-presence episode，至少 300 秒才计入。
- 拥堵仅表示两个以上不同人员在同一库位的半开观测区间真实重叠，不表示物理碰撞、
  通道密度或传感器覆盖区外人群。
- 模拟人员事件全部排除；当前模型外的真实事件单独计数并打断连续轨迹，不按相似编码
  猜测映射。
- WMS 只提供当前正物理库存位置，因此只计算“正库存去重库位数 / 当前活动库位数”的
  `LocationOccupancyPressure`：低于 85% 为 Normal，85% 起为 Watch，95% 起为
  Critical。真实体积、重量、托盘容量始终为 `null/Unavailable`，原因固定为
  `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`。
- WMS 不可用时仍返回人员诊断，并将占用结果标为不可用；WMS Site、Published version、
  楼层或库位越界仍使整个请求以 502 失败关闭。

## 3. Viewer 与本地化

Viewer 工具栏新增按需 `DIAG` 面板，支持最近 1/8/24 小时，且与 KPI 面板互斥。
面板展示路径覆盖质量、折返、停留、observed co-presence、分层库位占用、真实容量
不可用原因、人员与 WMS 各自观测时间；折返、停留、拥堵可复用 Locator 定位，分层
占用可切层。关闭、切换面板或卸载后的旧响应不能回写，失败时保留上次成功结果。
本卡不修改库存、ABC、筛选、热图或设备/人员图层的颜色权威。

新增 35 个诊断界面键，均提供简中、繁中、英语、日语和韩语种子并同步生成快照。
i18n 静态门禁仍报告集成基线已有的 911 个缺失键；本卡将快照从 4,424 增至 4,459，
缺失总数保持 911，因此没有新增 i18n 欠账。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 诊断引擎聚焦 | 4 passed；含半开重叠边界与 100 条截断 |
| 诊断服务聚焦 | 7 passed |
| 权限、审计、HTTP 契约与种子聚焦 | 59 passed；最终五语种子复核 11 passed |
| Space Unit Release 全量 | 240 passed / 0 failed / 0 skipped |
| Space Integration 默认全集 | 205 passed / 0 failed / 62 SQL 环境门禁 skipped |
| CP6.Tests Release 全量 | 2744 passed / 0 failed / 17 环境门禁 skipped |
| 前端聚焦 | 2 files / 12 tests passed |
| 前端全量 | 116 files / 643 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | 0 errors / 10 条既有 warnings |
| EF pending model | 无待迁移模型变化 |
| Design V1 OpenAPI/C#/TypeScript SDK drift | passed；operations 仍为 68，无生成物漂移 |
| TypeScript SDK strict no-emit | passed |
| Git 差异检查 | passed |
| i18n 静态门禁 | 911 项既有基线欠账；本卡净新增 0 |

完整 solution 首次以 `--no-restore` 运行时发现新工作树中 8 个非本卡工程缺少
`project.assets.json`。完成一次 solution restore 后，同一 Release 非增量构建以
0 error 通过；这不是源码回归，也未修改依赖清单。

## 5. 当前路线与明确不做

2026-08-02 再次执行 E02 readiness：工具测试 10/10，50 MiB 与 100 万实体容量资产
通过；严格数据集审计仍因缺正式 20 份授权黄金 CAD、10/5/5 格式分布、L1～L5
覆盖和 DWG/DXF 版本矩阵退出 3，ODA/APS preflight 仍因法务、供应商包、凭据、
治理和冻结 Worker 证据退出 4/4。因此 E02-S02 及 CAD 下游仍不能合法启动。

本卡不包含推荐、调度、审批、WMS/WCS/PDA 写入、历史诊断存储、趋势、预测、外部
Portal、CAD 或外部 AI Provider，也不改变 E02/E03/E13/E06 主链优先级。下一步应
重新按依赖选择 E11 后续独立只读卡；任何写回或建议闭环必须另行冻结合同与验收。
