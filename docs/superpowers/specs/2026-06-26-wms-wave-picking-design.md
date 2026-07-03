# WMS 波次拣货（Wave Picking）执行子系统 — 设计规格

> **文档类型**：设计规格（spec）。本文经 brainstorming 流程逐节确认后定稿，是 writing-plans 的输入。
> **日期**：2026-06-26（2026-06-27 并入用户审阅的边界补强 v1.1）
> **模块**：WMS（倉庫管理）/ 新业务增量
> **状态**：边界补强完成 → writing-plans → subagent TDD
> **准确性**：现状盘点与 file 引用实测于 2026-06-26 仓库快照；现有 `OutboundService` 行号引用见 `docs/codemap-wms/03-出庫-出荷.md`。

> **v1.1 变更摘要（2026-06-27 用户审阅补强，落码前优先级）**：
> 1. 活动波次唯一性升级为**数据库强约束**（过滤唯一索引，§4.2）。
> 2. 所有写操作加 **RowVersion 乐观锁 + 事务守卫**（§2.4）。
> 3. `ConfirmPick` **以后端扫描校验为准**（库位/製品/Lot），前端扫描仅体验优化（§6.2）。
> 4. `ConfirmPick` **允许 `pickedQty=0`**（全额短拣，§6.2）。
> 5. 短拣**必须填 `ShortReason`**（§4.3/§6.2）。
> 6. `CancelWave` 边界收紧：Released 波次**一旦存在 Picked/Short 任务即不可取消**（§6.4）。
> 7. `CancelWave` 回退成员单恢复 **`OriginalOutboundStatus`（2 或 5）**，不硬编码 2（§4.2/§6.4）。
> 8. `BatchShip` **逐单独立事务**，返回**成功/失败/跳过**清单；有失败则波次保持 Picked；全短拣无可出量的单收尾到 `PartialAllocated(5)` 不卡 Picking（§6.3）。
> 另：`CompleteWave` 仅接受 Picked/Short 任务（§5.1）；新增 4 个边界/并发测试套件（§11）。

---

## §0 背景与目标

### 0.1 现状盘点（为什么是"增量"不是"从零"）

WMS 已 feature-complete（2026-06-26 核对当前代码）：32 控制器 / 66 服务 / 39 实体 / 39 视图 / 23 测试，核心是**库存写入铁律 `IStockMovementService`**，ERP↔MES↔WMS 闭环 Bridge 已通。需求规格 `docs/MSBBWM_Requirements.txt` 规划的 Phase WM-1~14 + 扩展 WM100~330 + WM-RPT 已逐章落地，记忆里曾经的唯一空缺（RF 手持 WM300）已补齐。

因此本项目是在已完整的出库链**之上**叠加一个新能力，而非重写。

### 0.2 现有出库/拣货链的关键事实（设计地基）

- **出库状态机**（`WmsTxnType.cs:83-92` `OutboundOrderStatus`）：`Draft=0 → Confirmed=1 → Allocated=2 → Picking=3 → Completed=4`，旁路 `PartialAllocated=5`（材料不足反流）/ `Cancelled=9`。
- **引当（`AllocateAsync`）** 已把每个出库行锁定到具体 `仓+库位+Lot`，写回 `OutboundOrderDetail.{WarehouseCd, LocationCd, LotNo, AllocatedQty, AllocateTxnNo}`，并经铁律发 `RSV`（只加 `AllocatedQty`，`PhysicalQty` 不变）。
- **`StartPickingAsync`** 仅把 header `Allocated(2) → Picking(3)`，**拣货行级数据完全不持久化**。
- **`PickingWorkView.vue`** 拣选/短缺确认全是前端本地状态，`confirmPick/confirmShort/onComplete` **不发后端请求、不落库**（已知缺口）。
- **真正扣库在 `ShipAsync`**：出 `AllocatedQty − ShippedQty`，发 `OUT`（同减 `PhysicalQty` 与 `AllocatedQty`），并触发**接缝②**（`ErpBridgeHook.OnShipmentConfirmedAsync` 出荷回写 ERP 受注）。
- **Location 已有动线字段**（`Location.cs`）：`XCoord/YCoord/ZCoord`（可空）、`LocationLevel`（5 段层级）、`IsPickable`、`Barcode`。
- **前端 `statusMap` 缺 `PartialAllocated=5`**（出库列表/详情都只列 0-4、9，后端置 5 时裸显数字）——已知 UI 盲点，本项目顺手修补。
- **WMS 旧错误码 `WM-MSG-xxx` 是 Service 内联裸码、未入 `Sys_Langs`**（codemap 标注的缺陷）——本子系统刻意纠正，新码全部入库五语。
- **`BaseBizEntity` 已含 `RowVersion`**（现有倉庫/Location/Outbound 更新即用其乐观锁，冲突 409）——本子系统全部写操作沿用。

### 0.3 已锁定的核心决策（brainstorming 逐问确认）

| # | 决策点 | 选定 |
|---|---|---|
| 1 | 增量方向 | **波次拣货 Wave Picking** |
| 2 | 厚度 | **完整拣货执行子系统**（组波 + 任务生成 + 行级落库 + 扫描确认 + RF 端 + 汇到 Ship）|
| 3 | 组波方式 | **手动勾选 + 筛选器**（规则自动组波本期不做）|
| 4 | 拣货策略 | **按单拣货 + 动线优化**（无二次分拨）|
| 5 | 短拣处理 | **记短拣 + 释放预留(UNRSV) + 起异常**，Ship 按实拣量出 |
| 架构 | 任务建模 + RF 集成 | **方案 C：纯新子系统**（不碰 MobileTask，零回归风险，自带 PC/RF 拣货屏）|
| 补充 | 出荷 | 做**波次级一键批量出荷**（循环复用 `ShipAsync`）|
| 补充 | 拣货任务主键 | 独立采番 `WPT...`（与项目其它单据一致）|

### 0.4 设计目标

1. 把"已引当 → 拣货 → 待出"这一段从**不落库**变为**全程行级落库**，闭合既有缺口。
2. 多张出库指示合波，拣货任务按**动线序**一趟走完，省动线。
3. 严守**库存写入铁律**：拣货确认正常路径不动库存，唯一库存变动（短拣 UNRSV）经 `IStockMovementService`。
4. **纯加法**：不改 `OutboundService` 现有方法签名，现有 631+ 测试不改照绿；复用现有 `ShipAsync`/接缝②/`MaterialShortage`/`IWmsSequenceService`/`IStockMovementService`。
5. **正确性优先于便利**：并发与异常边界由数据库强约束 + 乐观锁 + 后端校验三道兜底，前端只做体验优化。

---

## §1 范围与边界

### 1.1 本期做（in scope）

- 波次头 + 波次↔出库指示关联 + 行级拣货任务 三张新表。
- 组波（手动勾选已引当出库指示 + 筛选器 + **DB 强约束唯一性守卫** + 条件快照）。
- 下发（Release：炸开拣货任务 + 算动线序 PickSeq + 成员单进入 Picking）。
- 拣货执行（按动线序的扫描式确认，**后端扫描校验**，满拣 / 短拣 / 全额短拣，每步落库）。
- 短拣（UNRSV 释放 + 下调出库行 AllocatedQty + 起 MaterialShortage 异常 + **必填短拣原因**）。
- 波次完成 + 波次级一键批量出荷（**逐单独立事务**，复用 `ShipAsync` → 自动触发接缝② ERP 回写）。
- 波次取消（**边界守卫** + 任务作废 + 成员单恢复 `OriginalOutboundStatus`）。
- 前端三视图（列表 / 组波 / 拣货执行）+ api + types + router + 菜单种子。
- WMS 出库 `statusMap` 补 `PartialAllocated=5`（缺口修补）。
- 新错误码 + 词条入 `Sys_Langs` 五语。

### 1.2 本期不做（out of scope，YAGNI）

状态机/枚举留扩展位，但**不实现**：

- 规则/定时自动组波（仅手动勾选）。
- 按品汇总拣 + 二次分拨（put-to-order / sortation）。
- 分区拣货（Zone picking）/ 多人并拣的区域拆分。
- 3D / 平面图仓位可视化、动线热力图。
- 拣货车 / 容器（tote / cart）管理、一车多单绑定。
- 拣货绩效计件 / KPI 排行。
- RF 复用现有 MobileTask（方案 C 明确不碰 MobileTask）。

---

## §2 架构定位与铁律遵守

### 2.1 分层落点

```
CP6.Entity/DomainModels/Wms/    WavePlan / WaveOrder / WavePickTask + WavePlanStatus / WavePickTaskStatus 枚举
CP6.Core/Services/Wms/          IWaveService / WaveService（依赖 IStockMovementService / IWmsSequenceService /
                                IOutboundService 的现有 Ship / IMaterialShortageService）
CP6.WebApi/Controllers/Wms/     WaveController  [Route("api/wms/wave")]
CP6.WebApi/                     Program.cs DI 注册 + EF 迁移 AddWmsWavePicking + i18n 词条种子
cp6.web/src/                    views/wms/Wave{List,Build,Pick}View.vue + api/wms/wave.ts + types/wms/wave.ts
                                + router + 菜单种子；OutboundOrder 视图 statusMap 修补
CP6.Tests/                      WaveServiceTests + WaveFullFlowTests + WaveConcurrencyTests
                                + WaveCancelBoundaryTests + WaveBatchShipEdgeTests + WaveScanValidationTests
```

### 2.2 铁律遵守声明

- `T_Stock` 严禁直接改。本子系统**唯一**的库存数量变动是**短拣释放 `UNRSV`**，经 `IStockMovementService.ApplyAsync` 发出，同写 `T_StockTransaction`。
- 拣货确认正常路径（满拣）**不发任何库存 Txn**——因为 `RSV` 已在 Allocate 预留，`OUT` 留给现有 `ShipAsync`。
- 批量出荷不自己写库存逻辑，循环调用现有 `IOutboundService.ShipAsync`，由它发 `OUT` 并触发接缝②。

### 2.3 CP6.Core 异常约定

CP6.Core 不引 `BizException`。`WaveService` 抛 `InvalidOperationException("WAVE-MSG-0xx: ...")`，由 `WaveController` catch 转 HTTP（与现有 WMS 控制器一致：业务码 → 400，唯一性冲突/并发冲突 → 409）。

### 2.4 并发与事务控制（v1.1 强化）

**总原则**：所有改库写操作都在**显式事务**内、用 **RowVersion 乐观锁**收尾；DB 层再加**强约束**兜底，不依赖应用层先查后写的 TOCTOU 窗口。

| 操作 | 守卫机制 |
|---|---|
| `CreateWave` | 成员关系唯一性靠 **`T_WaveOrder` 过滤唯一索引**（§4.2）兜底：并发插入同一活动 OutboundNo → 第二个 `SaveChanges` 触发唯一冲突 → catch → `WAVE-MSG-031`(409)。先查仅作友好提示，强约束为准。|
| `ReleaseWave` | 事务内：校验波次 `Status==Draft`（RowVersion 锁），逐成员单校验仍为 2/5；炸任务 + 翻状态一并提交。RowVersion 冲突 → `WAVE-MSG-072`(409)。|
| `ConfirmPick` | 事务内：以 `WavePickTask.RowVersion` 乐观锁。并发双击 → 第一个提交成功，第二个 `DbUpdateConcurrencyException` → 重读：若任务已 `Picked/Short` 返回 `WAVE-MSG-051`，否则 `WAVE-MSG-072`(409)。|
| `CompleteWave` | 事务内 + 波次 RowVersion；校验所有任务已收尾。|
| `BatchShip` | **逐单独立事务**（见 §6.3）；每单 `ShipAsync` 自带其 RowVersion 守卫；波次状态收尾用波次 RowVersion。|
| `CancelWave` | 事务内 + 波次 RowVersion；边界校验（§6.4）。|

> InMemory 单测对事务用现有惯例 `ConfigureWarnings(Ignore(InMemoryEventId.TransactionIgnoredWarning))`；并发冲突测试用 SQLite/真 DB 或显式构造 RowVersion 不一致（见 §11）。

---

## §3 状态机

### 3.1 `WavePlanStatus`（波次头）

```
Draft(0)      组波草稿，可增删成员单、可取消
Released(1)   已下发，拣货任务已炸开，拣货进行中
Picked(2)     所有拣货任务收尾（Picked 或 Short），待出荷
Completed(3)  成员单全部 Ship 完成（或明确收尾）
Cancelled(9)  波次取消
```

合法迁移：`Draft→Released→Picked→Completed`；`Draft→Cancelled`；`Released→Cancelled`**仅当无任何 Picked/Short 任务**（§6.4）。`Picked/Completed` 不可取消。

### 3.2 `WavePickTaskStatus`（拣货任务）

```
Pending(0)    待拣
Picking(1)    拣货中（扫描开始，可选过渡态）
Picked(2)     满拣完成
Short(3)      短拣收尾（实拣<应拣，含全额短拣 pickedQty=0；已释放差额+起异常）
Cancelled(9)  随波次取消而作废（仅 Pending/Picking 任务可被 CancelWave 置此）
```

### 3.3 与 `OutboundOrderStatus` 的咬合规则

| 动作 | 出库指示 status 变化 | 波次 status |
|---|---|---|
| 入波门槛 | 仅 `Allocated(2)` 或 `PartialAllocated(5)` 可入波 | — |
| CreateWave | 不变（仍 2/5）；**快照 `OriginalOutboundStatus`** | → Draft(0) |
| ReleaseWave | 成员单 `2/5 → Picking(3)` | Draft → Released(1) |
| 满拣 ConfirmPick | 不变（Picking 3）| 不变 |
| 短拣 ConfirmPick | 不变（仅 `AllocatedQty` 下调）| 不变 |
| CompleteWave | 不变（仍 Picking 3，待出）| Released → Picked(2) |
| BatchShip 成功单 | `Picking(3) → Completed(4)`（现有 ShipAsync）| Picked → Completed(3)（全部成功/跳过时）|
| BatchShip 跳过单（无可出量+有短拣）| `Picking(3) → PartialAllocated(5)` | 同上 |
| BatchShip 失败单 | 保持 `Picking(3)` | **保持 Picked(2)** |
| CancelWave | 成员单 `Picking(3) → OriginalOutboundStatus(2 或 5)` | → Cancelled(9) |

> **唯一性守卫（DB 强约束）**：一个出库指示同时只能属于一个**活动波次**（波次 status ∈ {Draft, Released, Picked}），由 `T_WaveOrder` 过滤唯一索引保证（§4.2）。`SearchAvailableOrdersAsync` 据此过滤，`CreateWaveAsync` 靠索引兜底。

> **老单兼容**：不入波的出库指示，老的单单拣货流（`PickingWorkView`）照常使用。入波单由波次驱动；建议老视图过滤掉已入活动波次的单（实施时决定，见 §13）。

---

## §4 数据模型

所有新实体继承 `BaseBizEntity`（含 `Id/CreateTime/Modifier/IsDeleted/RowVersion/TenantId` 等基类字段，按现有 WMS 实体惯例）。

### 4.1 `T_WavePlan`（波次头）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `WaveNo` | string(20) | Required, 业务PK | 采番 `WAVE{yyyyMM}{NNNN}`，`IWmsSequenceService.NextAsync("WAVE")` |
| `WarehouseCd` | string(10) | Required | 波次仓库 |
| `Status` | int | 默认 0 | `WavePlanStatus` |
| `PickStrategy` | string(20) | 默认 `OrderPath` | 拣货策略枚举，本期固定 `OrderPath`（按单+动线）|
| `FilterSnapshotJson` | string(1000) | nullable | 组波筛选条件快照（审计/复现）|
| `OrderCount` | int | 默认 0 | 成员出库指示数（物化投影）|
| `TaskCount` | int | 默认 0 | 拣货任务数（Release 后物化）|
| `PickedTaskCount` | int | 默认 0 | 已收尾任务数（拣货进度，物化）|
| `AssignedTo` | string(20) | nullable | 波次负责人作业者CD |
| `Priority` | int | 默认 2 | 1=至急 2=通常 |
| `ReleasedAt/PickedAt/CompletedAt` | DateTime? | nullable | 各阶段时戳 |
| `Remarks` | string(500) | nullable | 备考 |

索引：`WaveNo`(UK)、`Status`、`WarehouseCd`、`AssignedTo`。

### 4.2 `T_WaveOrder`（波次↔出库指示关联）— v1.1 强化唯一性

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `WaveNo` | string(20) | Required | FK → WavePlan |
| `OutboundNo` | string(20) | Required | FK → OutboundOrder |
| `OriginalOutboundStatus` | int | Required | **入波时快照成员单状态（2 或 5），取消时恢复用** |
| `OrderPriority` | int | nullable | 入波时快照出库优先级（排序参考）|
| `IsActive` | bool | 默认 true | **活动成员标志**：波次进入 Completed/Cancelled 时置 false，供过滤唯一索引 |

索引：
- `(WaveNo, OutboundNo)`（UK，防同波重复加同单）。
- **`UX_WaveOrder_ActiveOutbound`：过滤唯一索引 `(OutboundNo) WHERE IsActive = 1 AND IsDeleted = 0`**（EF `HasIndex(x=>x.OutboundNo).IsUnique().HasFilter("[IsActive]=1 AND [IsDeleted]=0")`）。这是"一单同时只属一个活动波次"的**数据库级强约束**，杜绝并发组波 TOCTOU。
- `OutboundNo`（非唯一，查询用）。

> **维护规则**：`CompleteWaveAsync`（→Completed）与 `CancelWaveAsync`（→Cancelled）必须把本波 `WaveOrder.IsActive` 全部置 false，释放唯一占用，允许该单（若未出完）再次入波。

### 4.3 `T_WavePickTask`（行级拣货任务 = 落库核心表）— v1.1 增 ShortReason

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `WavePickTaskNo` | string(20) | Required, 业务PK | 采番 `WPT{yyyyMM}{NNNN}`，`NextAsync("WPT")` |
| `WaveNo` | string(20) | Required | FK → WavePlan |
| `SourceOutboundNo` | string(20) | Required | 回溯出库指示 |
| `SourceLineNo` | int | Required | 回溯出库指示明细行 |
| `ProductCd` | string(20) | Required | 製品CD |
| `ProductName` | string(100) | nullable | 快照 |
| `LotNo` | string(30) | nullable | 引当 Lot（来自出库行；空串=无批管理）|
| `WarehouseCd` | string(10) | Required | 实拣仓（来自出库行 `WarehouseCd ?? header.WarehouseCd`）|
| `FromLocationCd` | string(30) | nullable | 拣货库位（来自出库行 `LocationCd`）|
| `PickSeq` | int | 默认 0 | 动线序号（波次内排序键）|
| `RequiredQty` | decimal(21,8) | Required | 应拣量 = 出库行 `AllocatedQty − ShippedQty` |
| `PickedQty` | decimal(21,8) | 默认 0 | 实拣量（允许 0）|
| `ShortQty` | decimal(21,8) | 默认 0 | 短拣量 |
| `ShortReason` | string(200) | nullable | **短拣原因（短拣时必填，满拣为空）** |
| `Status` | int | 默认 0 | `WavePickTaskStatus` |
| `AssignedTo/PickedBy` | string(20) | nullable | 指派/实拣作业者 |
| `StartedAt/DoneAt` | DateTime? | nullable | 时戳 |
| `ShortageNo` | string(25) | nullable | 短拣起的 `MaterialShortage` 关联 |
| `Remarks` | string(500) | nullable | 备考 |

索引：`WavePickTaskNo`(UK)、`WaveNo`、`(WaveNo, PickSeq)`、`Status`、`AssignedTo`、`SourceOutboundNo`。

### 4.4 `T_Location` 加列（唯一对现有表的改动，向后兼容）

| 字段 | 类型 | 约束 | 说明 |
|---|---|---|---|
| `PickSeq` | int? | nullable | 显式动线序覆盖；为空时回退坐标/字典序算法 |

### 4.5 采番前缀新增

`IWmsSequenceService.NextAsync(prefix)` 新增前缀：`WAVE`（波次）、`WPT`（拣货任务）。沿用现有 `{prefix}{yyyyMM}{NNNN}`、全期间累计、跨月不归零。

---

## §5 服务 API

```csharp
public interface IWaveService
{
    // 组波准备
    Task<List<WaveAvailableOrderDto>> SearchAvailableOrdersAsync(WaveOrderFilterDto filter);
    // 组波（DB 唯一索引兜底）
    Task<string> CreateWaveAsync(CreateWaveRequest req, string userName);   // → WaveNo
    // 下发（炸任务+动线序+成员单进 Picking；RowVersion 守卫）
    Task ReleaseWaveAsync(string waveNo, string userName);
    // 拣货清单（PC/RF 共用，按 PickSeq 升序）
    Task<List<WavePickTaskDto>> GetWaveTasksAsync(string waveNo, string? assignedTo = null);
    Task<List<WavePlanDto>> SearchWavesAsync(WaveSearchFilterDto filter);
    Task<WavePlanDetailDto> GetWaveAsync(string waveNo);
    // 拣货确认（后端扫描校验 + 满拣/短拣/全额短拣；RowVersion 守卫）
    Task ConfirmPickAsync(string taskNo, ConfirmPickRequest req, string userName);
    // 波次完成（仅接受 Picked/Short 任务）
    Task CompleteWaveAsync(string waveNo, string userName);
    // 批量出荷（逐单独立事务，返回成功/失败/跳过清单）
    Task<BatchShipResultDto> BatchShipWaveAsync(string waveNo, BatchShipRequest req, string userName);
    // 取消（边界守卫 + 恢复 OriginalOutboundStatus）
    Task CancelWaveAsync(string waveNo, string userName);
}

public record ConfirmPickRequest(
    decimal PickedQty,            // 允许 0（全额短拣）
    string  ScannedLocationCd,    // 后端校验：须 == task.FromLocationCd
    string  ScannedProductCd,     // 后端校验：须 == task.ProductCd
    string? ScannedLotNo,         // 后端校验：task 有 Lot 时须 == task.LotNo
    string? ShortReason);         // 短拣（PickedQty < RequiredQty）时必填

public record BatchShipResultDto(
    List<string> Succeeded,       // 已 Ship 的 OutboundNo（含 packageNo）
    List<ShipFailureDto> Failed,  // Ship 抛异常的单 + 原因
    List<string> Skipped);        // 无可出量、收尾到 PartialAllocated 的单
```

### 5.1 逐方法行为规格

**`SearchAvailableOrdersAsync(filter)`**
- 返回 status ∈ {Allocated(2), PartialAllocated(5)} 且 `!IsDeleted` 且**不在任何活动波次**（`T_WaveOrder` 中无 `IsActive=1` 行）的出库指示。
- 筛选：`WarehouseCd`、`OutboundType`、出荷日范围、`CarrierCd`、`Priority`、`WebOrderNo` 模糊。
- DTO 含每单行数、引当总量、目标仓，便于勾选。

**`CreateWaveAsync(req, userName)`**
- 校验：`orderNos` 非空（否则 `WAVE-MSG-020`）；逐单校验 status ∈ {2,5}（否则 `WAVE-MSG-030`）。
- 友好预检：逐单查是否已在活动波次（命中→`WAVE-MSG-031`）。**但真正的守卫是 DB 过滤唯一索引**：插入 `WaveOrder{IsActive=true}` 时若并发已有活动占用 → `SaveChanges` 唯一冲突 → catch 转 `WAVE-MSG-031`(409)。
- 每个 `WaveOrder` 记 `OriginalOutboundStatus = 成员单当前 status`（2 或 5）。
- 采番 `WAVE`，建 `WavePlan{Status=Draft, OrderCount=N}`；单事务；返回 `WaveNo`。

**`ReleaseWaveAsync(waveNo, userName)`** — 见 §6.1。

**`GetWaveTasksAsync(waveNo, assignedTo?)`**
- 返回该波次拣货任务，按 `PickSeq` 升序；`assignedTo` 非空则只返回该作业者（或未指派）的任务。
- DTO 含製品名/库位/Lot/应拣/已拣/状态/动线序，供 PC/RF 拣货屏渲染。

**`ConfirmPickAsync(taskNo, req, userName)`** — 见 §6.2。

**`CompleteWaveAsync(waveNo, userName)`**
- 校验：波次 status = Released（否则 `WAVE-MSG-043`）；**所有任务 status ∈ {Picked, Short}**——仍有 `Pending/Picking`（或异常出现 `Cancelled`）→ `WAVE-MSG-050`（尚有未完成/非法收尾的拣货任务）。
- 波次 `Released → Picked`，写 `PickedAt`。（RowVersion 守卫）

**`BatchShipWaveAsync(waveNo, req, userName)`** — 见 §6.3。

**`CancelWaveAsync(waveNo, userName)`** — 见 §6.4。

---

## §6 关键流程

### 6.1 Release（炸任务 + 动线序）

```
事务开始；波次 RowVersion 锁
校验 波次.Status == Draft            否则 WAVE-MSG-043
for 每个成员出库指示 (经 WaveOrder.IsActive=1):
    校验 仍为 Allocated(2)/PartialAllocated(5)  否则 WAVE-MSG-030（中途被改）
    for 每个出库明细 where (AllocatedQty - ShippedQty) > 0:
        建 WavePickTask {
            WavePickTaskNo = NextAsync("WPT"),
            WaveNo, SourceOutboundNo=明细.OutboundNo, SourceLineNo=明细.LineNo,
            ProductCd/ProductName/LotNo,
            WarehouseCd = 明细.WarehouseCd ?? header.WarehouseCd,
            FromLocationCd = 明细.LocationCd,
            RequiredQty = AllocatedQty - ShippedQty,
            Status = Pending, AssignedTo = 波次.AssignedTo }
    出库指示.Status = Picking(3)          // 复用现有 Picking 语义，由波次批量驱动
所有任务按动线算 PickSeq（见 6.1.1）
波次.Status = Released, TaskCount = 任务数, ReleasedAt = now
提交（RowVersion 冲突 → WAVE-MSG-072 / 重试）
```
> 不发任何库存 Txn（RSV 已在 Allocate 预留）。

#### 6.1.1 动线序 PickSeq 算法
1. 收集本波全部任务的 `FromLocationCd`，join `T_Location`。
2. 排序键优先级：① `Location.PickSeq`（显式覆盖，非空优先，升序）；② 坐标蛇形：`ZCoord` 升序 → 同 Z 内按 `XCoord`（通路）分组，奇数通路 `YCoord` 升序、偶数通路 `YCoord` 降序（serpentine 减折返）；③ 坐标缺失退化 `LocationCd` 字典序。
3. 按结果赋 `PickSeq = 1,2,3,...`（波次内连续唯一）。
> 实现为纯函数 `ComputePickSequence(tasks, locations)`，便于单测。

### 6.2 ConfirmPick（后端扫描校验 + 满拣 / 短拣 / 全额短拣）

```
事务开始；task RowVersion 锁
校验 task 存在                          否则 WAVE-MSG-070
校验 波次.Status == Released            否则 WAVE-MSG-043
校验 task.Status ∈ {Pending, Picking}   否则 WAVE-MSG-051（已收尾）

// —— 后端扫描校验（权威，前端扫描仅体验优化）——
校验 req.ScannedLocationCd == task.FromLocationCd            否则 WAVE-MSG-060（扫错库位）
校验 req.ScannedProductCd  == task.ProductCd                 否则 WAVE-MSG-061（扫错製品）
若 task.LotNo 非空: 校验 req.ScannedLotNo == task.LotNo      否则 WAVE-MSG-062（扫错Lot）

// —— 数量校验（允许 0；不可超）——
校验 0 <= req.PickedQty <= task.RequiredQty
     req.PickedQty < 0 → WAVE-MSG-021；req.PickedQty > Required → WAVE-MSG-052

满拣 (PickedQty == RequiredQty):
    task.PickedQty = PickedQty; task.Status = Picked
    task.PickedBy = userName; task.DoneAt = now
    // 不动库存

短拣 (PickedQty < RequiredQty，含 PickedQty==0 全额短拣):
    校验 req.ShortReason 非空            否则 WAVE-MSG-053（短拣必须填原因）
    shortQty = RequiredQty - PickedQty
    _stock.ApplyAsync(UNRSV, qty=shortQty, Warehouse=task.WarehouseCd,
        Location=task.FromLocationCd, Product=task.ProductCd, Lot=task.LotNo,
        RelatedType="WAVE_PICK", RelatedNo=task.WavePickTaskNo,
        Remark=$"短拣解除 {task.WavePickTaskNo}: {req.ShortReason}")        // 经铁律
    出库明细(SourceOutboundNo,SourceLineNo).AllocatedQty -= shortQty         // Ship 按实拣出(零改动)
    shortageNo = _shortage.CreateAsync(MaterialShortage{
        RelatedOutboundNo=task.SourceOutboundNo, ProductCd=task.ProductCd,
        RequiredQty=shortQty, AvailableQty=0, Status=Open,
        Remark=$"波次{task.WaveNo}短拣: {req.ShortReason}" })
    task.PickedQty=PickedQty; task.ShortQty=shortQty; task.ShortReason=req.ShortReason
    task.ShortageNo=shortageNo; task.Status=Short; task.PickedBy=userName; task.DoneAt=now

更新 波次.PickedTaskCount
提交（RowVersion 冲突 → 重读：已收尾返回 WAVE-MSG-051，否则 WAVE-MSG-072(409)）
（可选）best-effort SignalR 推进度
```
> 唯一库存变动是短拣 `UNRSV`，经 `IStockMovementService`，不破铁律。

### 6.3 BatchShip（批量出荷，逐单独立事务，三态清单）

```
校验 波次.Status == Picked              否则 WAVE-MSG-043
result = { Succeeded:[], Failed:[], Skipped:[] }
for 每个成员出库指示 (WaveOrder.IsActive=1):
    重读成员单(独立事务)
    if 该单仍 Picking 且 有可出明细 (Σ(AllocatedQty-ShippedQty)>0):
        try:
            pkg = await _outbound.ShipAsync(outboundNo, ShipRequest{...}, userName)
            //   ShipAsync 内部：OUT 扣库 + 置 Completed(4) + 接缝②ERP回写 + SignalR
            result.Succeeded.add({outboundNo, pkg})
        catch(ex):
            result.Failed.add({outboundNo, ex.Message})    // 该单保持 Picking(3)
    else if 该单无可出量 但 存在短拣(AllocatedQty 已被下调到 0 / <已出):
        该单.Status = PartialAllocated(5)                   // 明确收尾，不卡 Picking
        result.Skipped.add(outboundNo)
    else:
        result.Skipped.add(outboundNo)                      // 其它无可出（已出完等）

if result.Failed 为空:
    波次.Status = Completed(3), CompletedAt = now
    本波 WaveOrder.IsActive = false                          // 释放唯一占用
else:
    波次.Status 保持 Picked(2)                               // 有失败，留待重试
return result
```
> 出荷不写新库存逻辑，完全复用 `ShipAsync`，接缝②自动生效；短拣已把 `AllocatedQty` 下调，故 Ship 自然按实拣量出。**全短拣 0 实拣单不会调用 `ShipAsync`，但被显式收尾到 `PartialAllocated(5)`，不卡 Picking**（满足 §11 边界测试）。失败单留 Picking，可在波次 Picked 态重发 BatchShip（幂等：已 Completed 的成员单不再处理）。

### 6.4 CancelWave（取消，边界守卫 + 恢复原状态）

```
事务开始；波次 RowVersion 锁
校验 波次.Status ∈ {Draft, Released}    否则 WAVE-MSG-043
if Released:
    校验 不存在任何 task.Status ∈ {Picked, Short}   否则 WAVE-MSG-044（已有已收尾任务，不可取消）
    本波全部任务(Pending/Picking) → Cancelled
    for 每个成员出库指示:
        该单.Status = 对应 WaveOrder.OriginalOutboundStatus   // 恢复 2 或 5，非硬编码
波次.Status = Cancelled(9)
本波 WaveOrder.IsActive = false                              // 释放唯一占用
提交
```
> **设计要点**：因边界守卫禁止"已有 Picked/Short 任务时取消"，故 CancelWave **永不需要回补已发生的 UNRSV/AllocatedQty 下调**（那些只在 Short 任务里发生，而 Short 存在即不可取消）。这消除了原 v1.0 "部分回退" 的歧义。Draft 取消无任何下游影响（任务还没炸）。

---

## §7 控制器端点

`WaveController` `[Route("api/wms/wave")]`，统一返回 `{code, message, data}`。

| 方法 | 路由 | Body | 返回 | 错误码→HTTP |
|---|---|---|---|---|
| POST | `/available-orders` | `WaveOrderFilterDto` | 可入波出库指示列表 | — |
| POST | `/` | `CreateWaveRequest` | `{waveNo}` | 020/030→400, 031→409 |
| GET | `/` | query filter | 波次列表 | — |
| GET | `/{waveNo}` | — | 波次详情+成员单 | 070→404 |
| POST | `/{waveNo}/release` | — | ok | 043/030→400, 072→409 |
| GET | `/{waveNo}/tasks` | query `assignedTo?` | 拣货任务（PickSeq 序）| 070→404 |
| POST | `/tasks/{taskNo}/pick` | `ConfirmPickRequest` | ok | 070→404, 043/051/021/052/053/060/061/062→400, 072→409 |
| POST | `/{waveNo}/complete` | — | ok | 043/050→400, 072→409 |
| POST | `/{waveNo}/ship` | `BatchShipRequest` | `BatchShipResultDto` | 043→400, 072→409 |
| POST | `/{waveNo}/cancel` | — | ok | 043/044→400, 072→409 |

控制器 catch：`DbUpdateConcurrencyException`→`WAVE-MSG-072`(409)；DB 唯一冲突（CreateWave）→`WAVE-MSG-031`(409)；`InvalidOperationException`（解析 `WAVE-MSG-xxx`）+ `InsufficientStockException`→400。

---

## §8 错误码与 i18n

**刻意纠正 WMS 旧债**：新码全部按 i18n 三铁律入 `Sys_Langs` 五语（ZhCN/ZhTW/En/Ja/Ko），不重蹈 `WM-MSG` 内联裸码覆辙。

| 码 | 语义 | HTTP |
|---|---|---|
| `WAVE-MSG-020` | 波次成员出库指示不能为空 | 400 |
| `WAVE-MSG-021` | 拣货数量非法（不可为负）| 400 |
| `WAVE-MSG-030` | 出库指示状态不允许入波（须已引当/部分引当）| 400 |
| `WAVE-MSG-031` | 出库指示已属于其它活动波次 | 409 |
| `WAVE-MSG-043` | 当前状态不允许此操作（状态守卫，多场景复用）| 400 |
| `WAVE-MSG-044` | 波次已有已收尾(拣完/短拣)任务，不可取消 | 400 |
| `WAVE-MSG-050` | 尚有未完成/非法收尾的拣货任务，无法完成波次 | 400 |
| `WAVE-MSG-051` | 该拣货任务已收尾 | 400 |
| `WAVE-MSG-052` | 实拣量不可超过应拣量 | 400 |
| `WAVE-MSG-053` | 短拣必须填写原因 | 400 |
| `WAVE-MSG-060` | 扫描库位与任务不符 | 400 |
| `WAVE-MSG-061` | 扫描製品与任务不符 | 400 |
| `WAVE-MSG-062` | 扫描批次(Lot)与任务不符 | 400 |
| `WAVE-MSG-070` | 数据不存在 | 404 |
| `WAVE-MSG-071` | 操作成功 | 200 |
| `WAVE-MSG-072` | 数据已被他人修改，请刷新重试（乐观锁冲突）| 409 |

界面词条同步入库：`wms.wave.*`（标题/按钮/列名/状态文案/KPI/扫描提示）五语。

---

## §9 前端

### 9.1 视图（`cp6.web/src/views/wms/`）
- **`WaveListView.vue`**：波次列表 + 状态筛选 + KPI（草稿/拣货中/待出/已完成）+ 行级状态机按钮（下发/进入拣货/完成/出荷/取消）。BatchShip 后展示成功/失败/跳过清单。
- **`WaveBuildView.vue`**：组波。筛选器（仓库/出荷日/承运商/优先级）+ "可入波出库指示"勾选表（`/available-orders`）+ "已选"汇总 + 创建。
- **`WavePickView.vue`**：拣货执行。按 `PickSeq` 动线序逐任务展示；扫描库位+製品(+Lot)，输入实拣量；满拣一键确认、短拣输入实拣量(含 0)并**必填原因**→落库（`/tasks/{taskNo}/pick`）。前端扫描即时比对仅作体验提示，**最终以后端校验为准**（后端拒绝则报 060/061/062）。顶部进度条。

### 9.2 配套
- `api/wms/wave.ts`（9 端点）+ `types/wms/wave.ts`（WavePlan/WavePickTask/各 DTO）。
- `router/index.ts`：`/wms/wave-list`、`/wms/wave-build`、`/wms/wave-pick`。
- 菜单种子（`docs/wms-menu-seed.sql` 同模式 MERGE upsert）。

### 9.3 缺口修补（顺手）
`OutboundOrderListView.vue` / `OutboundOrderView.vue` 的 `statusMap` 补 `5: PartialAllocated`（含五语），消除后端置 5 时裸显数字的盲点。

---

## §10 迁移

EF 迁移 `AddWmsWavePicking`：
- 建 `T_WavePlan` / `T_WaveOrder` / `T_WavePickTask` + §4 索引（含 `T_WaveOrder` **过滤唯一索引** `UX_WaveOrder_ActiveOutbound`）。
- `T_Location` 加列 `PickSeq int NULL`。
- `CP6Context` 加 `DbSet<WavePlan>/WaveOrder/WavePickTask` + 流式索引/过滤索引配置 + RowVersion 配置（基类已有）。
- `Program.cs` DI 注册 `IWaveService → WaveService`。
- i18n 词条种子（`Sys_Langs` MERGE upsert，五语）。

---

## §11 测试计划

纯加法，**现有测试不改照绿**。

### 11.1 单元 `WaveServiceTests`（InMemory）
- 组波：成功 / 空成员(020) / 非引当态(030) / 重复入活动波(031)。
- 唯一性守卫：已入波单不出现在 `SearchAvailableOrdersAsync`。
- Release：炸任务数 = 成员未拣行数；成员单转 Picking；TaskCount 物化。
- **动线序 `ComputePickSequence` 纯函数**：坐标矩阵→期望蛇形序列；显式 PickSeq 覆盖优先；坐标缺失退化字典序。
- 满拣：task→Picked，**无库存 Txn**（断言 StockTransaction 计数不变）。
- 短拣：UNRSV（AllocatedQty 减、AvailableQty 增）+ 出库行 AllocatedQty 下调 + 起 MaterialShortage + task→Short+ShortageNo+ShortReason 回填。
- **全额短拣 pickedQty=0**：shortQty=RequiredQty，全 UNRSV，task→Short。
- **短拣未填原因 → WAVE-MSG-053**。
- 超拣拒绝(052) / 负数(021) / 已收尾再拣(051)。
- Complete：仍有 Pending→050；全 Picked/Short→Picked。

### 11.2 E2E `WaveFullFlowTests`
- 全链：建出库指示→Confirm→Allocate→组波→Release→逐任务满拣→Complete→BatchShip→断言 OUT 扣库 + **接缝② ERP 受注 ShippedQty/ShipStatus 回写仍通**。
- 短拣链：含短拣任务，断言 Ship 按实拣量出、ERP 回写量=实拣量、MaterialShortage 留 OPEN。

### 11.3 `WaveConcurrencyTests`（v1.1 新增，SQLite/真DB 或构造 RowVersion 冲突）
- 两请求并发 `CreateWave` 同一 `OutboundNo` → 仅一个成功，另一个 `WAVE-MSG-031`（过滤唯一索引兜底）。
- 同一 task 双击 `ConfirmPick` → 仅一次成功，另一个 `WAVE-MSG-051` 或并发冲突 `WAVE-MSG-072`。

### 11.4 `WaveCancelBoundaryTests`（v1.1 新增）
- Released 且无已收尾任务 → 可取消（成员单恢复原状态、IsActive=false）。
- Released 且有 Picked 任务 → 拒绝 `WAVE-MSG-044`。
- Released 且有 Short 任务 → 拒绝 `WAVE-MSG-044`。
- 原状态 `PartialAllocated(5)` 入波后取消 → 恢复 **5**（非 2）。
- 原状态 `Allocated(2)` 入波后取消 → 恢复 **2**。

### 11.5 `WaveBatchShipEdgeTests`（v1.1 新增）
- 一成员 Ship 成功、一成员 Ship 失败 → 波次保持 Picked、返回 Failed 清单、成功单 Completed。
- 全短拣 0 实拣单 → 不调用 `ShipAsync`、收尾到 `PartialAllocated(5)`、出库单不卡 Picking、列入 Skipped。
- 全部成功/跳过 → 波次 Completed、WaveOrder.IsActive 全 false。

### 11.6 `WaveScanValidationTests`（v1.1 新增）
- 扫错库位 → `WAVE-MSG-060`。
- 扫错製品 → `WAVE-MSG-061`。
- 扫错 Lot（任务有 Lot 时）→ `WAVE-MSG-062`；任务无 Lot 时 Lot 校验跳过。

### 11.7 gstack 浏览器 QA
- 三视图真浏览器全流程：组波→下发→拣货（含短拣/全额短拣/扫错拒绝）→完成→批量出荷；验证 statusMap=5 修补、i18n 五语无裸码。QA 固化 `docs/superpowers/qa/wave-picking/`。

---

## §12 实施阶段划分（writing-plans 输入）

> **落码前优先级（用户审阅要求，须在对应 Phase 内首先满足）**：① 活动波次唯一性 DB 强约束 → ② pickedQty=0 短拣 → ③ 扫描字段后端校验 → ④ 短拣原因必填 → ⑤ 取消波次边界 → ⑥ OriginalOutboundStatus 恢复 2/5 → ⑦ BatchShip 成功/失败/跳过清单 → ⑧ 全短拣单不卡 Picking。

建议波次（每波可派 subagent TDD，红→绿→QA）：

1. **Phase W1 — 数据地基**：3 实体（含 `WaveOrder.IsActive/OriginalOutboundStatus`、`WavePickTask.ShortReason`）+ 枚举 + Location.PickSeq + CP6Context + **过滤唯一索引** + 迁移 + 采番前缀。
2. **Phase W2 — 组波 + 下发**：`SearchAvailableOrders`/`CreateWave`（DB 唯一兜底①）/`ReleaseWave`（RowVersion②守卫）+ `ComputePickSequence` 纯函数 + 单测。
3. **Phase W3 — 拣货执行**：`GetWaveTasks`/`ConfirmPick`（后端扫描校验③ + pickedQty=0② + 短拣原因④ + UNRSV + 异常 + RowVersion）+ 单测 + `WaveScanValidationTests`。
4. **Phase W4 — 完成 + 批量出荷 + 取消**：`CompleteWave`/`BatchShipWave`（三态清单⑦ + 全短拣收尾⑧）/`CancelWave`（边界⑤ + 恢复原状态⑥）+ E2E + `WaveCancelBoundaryTests` + `WaveBatchShipEdgeTests` + `WaveConcurrencyTests`。
5. **Phase W5 — 控制器 + i18n**：`WaveController` + 词条种子五语 + DI。
6. **Phase W6 — 前端**：三视图 + api/types/router/菜单 + statusMap=5 修补 + gstack QA。

依赖：W1→W2→W3→W4 顺序；W5 依赖 W2-W4；W6 依赖 W5。

---

## §13 未决 / 遗留

> v1.1 已决：BatchShip 部分失败策略（§6.3 逐单独立事务 + 三态清单 + 失败保持 Picked）。

1. **老 `PickingWorkView` 是否过滤已入波单**：建议实施时加"排除活动波次成员"过滤，避免入波单同时出现在老单单拣货屏（status=3）造成双路拣货。本 spec 不强制。
2. **波次内多作业者并拣**：本期 `AssignedTo` 为波次级/任务级单值，不做实时锁与冲突解决（与 out-of-scope 的 Zone 一并留后续）。
3. **SignalR 拣货进度推送**：可选 best-effort，复用现有 `/hubs/wms`；非本期必须。

---

*生成于 2026-06-26，2026-06-27 并入用户审阅边界补强（v1.1）。基于对当前仓库 WMS 出库链真实源码的盘点 + brainstorming 逐节确认 + 审阅边界强化。下一步：writing-plans → subagent TDD（现有测试不改照绿 + gstack QA）。*
