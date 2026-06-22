# 04 · 設備 Machine + OEE（Phase4）

> 先读 [`README.md` §0/§1/§0.6](README.md)。全部 `文件:行号` 与代码片段 2026-06-22 实测、逐字引用。

## 0. 架构定位

Phase4 = 设备主数据 + 停机履历 + OEE 日次集计 + 两个后台 Worker（OEE 周期重算 / 设备空闲监视）+ SignalR 实时推送 + Control Tower 大屏。三前端页（`MachineListView`/`OeeAnalysisView`/`ControlTowerView`）、两 Controller（`Machine`/`Oee`）、两 Service、三表（`M_Machine`/`T_MachineDowntime`/`T_OeeDaily`）、两 Worker。

---

## 1. 设备一览/检索 — GET /api/mes/machines

**前端**：`/mes/machine-list`（`router/index.ts:99`）→ `MachineListView.vue`，`search()`（`:272-280`）→ `machineApi.search`，结果同时渲染「設備稼働状態グリッド」绿/红灯卡片 + 明细表，灯色由 `MACHINE_STATUS_OPTIONS` 映射。api（`mes.ts:261-266`，`paramsSerializer:{indexes:null}` 让 `statuses` 序列化为 `statuses=1&statuses=2`）。type `MachineSearchQuery`（`types/mes/mes.ts:560-567`）、返回 `MachineDto[]`（含富化 `currentWorkOrderNo`/`todayOee`）。
**后端**：Controller `Search`（`MachineController.cs:19-24`，`[Route("api/mes/machines")][Authorize]`）。Service `SearchAsync`（`MachineService.cs:27-...`）：软删+条件 → 富化「現在稼働指図」(查 `WorkOrderProcesses.ProcessStatus==1` 按设备 GroupBy) → 富化「本日 OEE」(查 `OeeDailies` 当天建字典回填 `dto.TodayOee`)。实体 `Machine`(`M_Machine`,业务 PK `MachineCd`,`Status` 0停/1稼働/2故障/3メンテ/4切替,`PlannedRunMinutesPerDay`默认480,`StandardCycleSec`,`CapacityPerHour`,`ActiveFlg`)。无错误码。

---

## 2~4. 设备 新建/编辑/删除 — POST · PUT /{cd} · DELETE /{cd}

**前端**：同页 dialog。`openCreate()`/`openEdit(row)`/`onDelete(row)`，`onSave()`（`MachineListView.vue:301-320`）前端校验 `machineCd`+`machineName` 必填后 `create`/`update`。设备CD 编辑态禁改。api `create`(`mes.ts:270-272`)/`update`(`:273-275`)/`delete`(`:276-278`)。
**后端**
- 新建 Controller `Create`（`MachineController.cs:34-43`）→ Service `CreateAsync`（`MachineService.cs:73-...`）：
```csharp
if (string.IsNullOrWhiteSpace(dto.MachineCd)) throw new InvalidOperationException("ME-MSG-001");
if (string.IsNullOrWhiteSpace(dto.MachineName)) throw new InvalidOperationException("ME-MSG-031");
if (await _db.Machines.AnyAsync(x => x.MachineCd == dto.MachineCd && !x.IsDeleted))
    throw new InvalidOperationException("ME-MSG-005"); // 既に存在
// PlannedRunMinutesPerDay <= 0 ? 480 兜底
```
- 编辑 `UpdateAsync`（`:108-...`）：取实体不存在→`ME-MSG-043`，逐字段覆写（**MachineCd/Status 不覆写**，编辑不改主键与运行态）。
- 删除 `DeleteAsync`（`:134-142`）：**逻辑删除** `m.IsDeleted=true`，无级联校验。
**校验与错误码**：`ME-MSG-001`(MachineCd 未输,**复用**原义"手配/製品CD")、`ME-MSG-031`(MachineName 未输,**复用**原义"不良内容")、`ME-MSG-005`(设备CD重复,**无i18n**)、`ME-MSG-043`(不存在,**无i18n**)、成功 `ME-MSG-041`。

---

## 5. 设备状态变更 — POST /api/mes/machines/{cd}/status

> ⚠️ 端点存在但 `MachineListView.vue` **未提供直接切换按钮**（全仓 grep 未见 view 调用 `changeStatus`）。状态实时变更主要来自停机登记/后台监视，经 SignalR 推送。

**后端**：Controller `ChangeStatus`（`MachineController.cs:67-77`，body `ChangeStatusRequest{int Status}`）。Service `ChangeStatusAsync`（`MachineService.cs:144-153`）：改 `Status` 落库 → **SignalR 推送** `NotifyMachineStatusChangedAsync`（try/catch 吞错）。
**数据流（实时回路）**：`POST /{cd}/status` → `ChangeStatusAsync`(UPDATE) → `IMesNotifier` → `SignalRMesNotifier`(`Clients.All`+`Group("machine:{cd}")` 发 `MachineStatusChanged`) → FE Hub 回调刷新灯格。

---

## 6. 停止（停机）登记 — POST /api/mes/machines/downtimes

**前端**：明细表「停止登録」→ `openDowntime(row)`→对话框，`emptyDt()` 默认 `downtimeType:2(故障)` + `startTime=now`。`onRegisterDowntime()`（`:336-347`）→ `machineApi.registerDowntime(dtForm)`。停止区分 `DOWNTIME_TYPE_OPTIONS`(1計画/2故障/3材料待ち/4作業者不在/9その他)，终了日时留空=继续中。api（`mes.ts:289-291`）。
**后端**：Controller `RegisterDowntime`（`MachineController.cs:88-97`）。Service `RegisterDowntimeAsync`（`MachineService.cs:196-244`）：校验 `MachineCd`(`ME-MSG-001`)→采番 `NextAsync("DT")`(`DTyyyyMMdd-NNNN`)→算停止分钟(仅 endTime 已填)→INSERT `T_MachineDowntime`(`DowntimeType<=0?9` 兜底)→**联动设备状态**(仅未终了)：
```csharp
machine.Status = dto.DowntimeType == 2 ? 2 : (dto.DowntimeType == 1 ? 3 : 0);
// 故障(2)→设备故障(2)；计划停止(1)→メンテ(3)；其他→停止(0)
```
保存后双推 SignalR（`NotifyDowntimeRegisteredAsync`+`NotifyMachineStatusChangedAsync`，try/catch）。实体 `MachineDowntime`(`T_MachineDowntime`,PK `DowntimeNo`,`StartTime`/`EndTime`(null=継続中)/`DowntimeMinutes`(自动算)/`DowntimeType`)。
**校验**：`ME-MSG-001`(MachineCd 未输)。

---

## 7. 停止履历一覧 / 关闭 — GET /downtimes ＋ POST /downtimes/{no}/close

> ⚠️ API 存在但 `MachineListView.vue` 当前未挂停机一览表（`searchDowntimes`/`closeDowntime` 前端未调用，为扩展页预留）。
- 履历 Controller `SearchDowntimes`（`:81-86`）→ Service `SearchDowntimesAsync`（`:159-194`，分页+`OnlyOpen(EndTime==null)`+富化设备名）。
- 关闭 Controller `CloseDowntime`（`:99-108`）→ `CloseDowntimeAsync`（`:246-270`）：补 `EndTime`、算分钟、已终了再关→`ME-MSG-042`、把状态 2/3 设备**复位稼働中(1)**。
**错误码**：`ME-MSG-043`(不存在)/`ME-MSG-042`(已终了)，均无 i18n。

---

## 8~11. OEE 查询/本日/再计算/推移

- **OEE 日次查询** `GET /api/mes/oee`（`/mes/oee`→`OeeAnalysisView.vue`，`router/index.ts:100`）：`loadAll()`（`:188-205`）`Promise.all` 拉「本日 OEE」+「区间历史」。Service `SearchAsync`（`OeeService.cs:22-42`）**纯读 `T_OeeDaily`**（软删+`MachineCd`+`OeeDate` 区间，降序）。无计算。
- **本日实时 OEE** `GET /oee/today`（`:26-32`）→ `CalculateTodayAsync`（`OeeService.cs:47-63`）逐台 `CalculateForAsync` **算后直接返回，不落库**。前端 30 秒轮询此区。
- **OEE 再计算** `POST /oee/recalculate`（`:43-48`，成功 `ME-MSG-041`）→ `RecalculateAsync`（`OeeService.cs:66-121`）逐台算 → **UPSERT `T_OeeDaily`**（按 `OeeDate+MachineCd` 查 existing：无则 Add，有则覆写 + `Modifier`，缺省 `"system"`）。
- **OEE 推移** `GET /oee/trend`（`:34-40`）→ `GetTrendAsync`（`OeeService.cs:123-135`）读 `T_OeeDaily` `GroupBy(MachineCd)`→`Dictionary`，前端 SVG 自绘多设备折线。**仅读已落库数据**（依赖 recalculate/Worker）。
- 实体 `OeeDaily`(`T_OeeDaily`,复合 PK `OeeDate+MachineCd`,各率 `decimal(8,4)`)。

---

## 12. Control Tower 大屏 — 聚合 + SignalR 实时

**前端**：`/mes/control-tower`（`router/index.ts:103`，另有 standalone 全屏 `:203-208`）→ `ControlTowerView.vue`，4 象限（KPI/设备状态格/日次推移/实时事件+延迟告警）。`loadAll()`（`:180-191`）并行拉 4 端点。**SignalR** `setupHub()`（`:199-225`）注册 5 回调（`ProductionReported`/`DefectIssued`/`MachineStatusChanged`→`refreshMachines()`/`WorkOrderStatusChanged`/`DowntimeRegistered`）。hub 客户端 `utils/mesHub.ts` 单例连 `/hubs/mes`，`withAutomaticReconnect([0,2000,5000,10000,30000])`。
**后端基建**：Hub `MesHub`（`app.MapHub<MesHub>("/hubs/mes")` `Program.cs:2235`，分组订阅 `SubscribeWorkOrder/SubscribeMachine`）；`IMesNotifier`→`SignalRMesNotifier`（5 推送方法各发 `Clients.All`+对应 Group；依赖逆转：Core 只依赖接口，SignalR 实现在 WebApi 层，测试用 `NoOpMesNotifier`）。

---

## ⭐ OEE 计算专讲 — OeeService.CalculateForAsync（OeeService.cs:141-210）

```csharp
// 1. 計画稼働時間
var planned = m.PlannedRunMinutesPerDay > 0 ? m.PlannedRunMinutesPerDay : 480;
// 2. 停止時間（重叠停止记录区间裁剪累加，上限封顶 planned）
var downtimeMinutes = 0;
foreach (var d in downtimes) {
    var start = d.StartTime < dateFrom ? dateFrom : d.StartTime;
    var end = d.EndTime ?? DateTime.Now; if (end > dateTo) end = dateTo;
    if (end > start) downtimeMinutes += (int)(end - start).TotalMinutes;
}
if (downtimeMinutes > planned) downtimeMinutes = planned;
var actualRun = planned - downtimeMinutes;
// 3. 良品/不良数（ProductionResult から，按 MachineCd + CreateDate 落日）
var good = qty?.Good ?? 0m; var defect = qty?.Defect ?? 0m; var total = good + defect;
// 4. 三率 + OEE
var availability = planned > 0 ? Math.Round((decimal)actualRun / planned * 100m, 4) : 0m;
var quality = total > 0 ? Math.Round(good / total * 100m, 4) : 0m;
decimal performance = 0m;
if (m.CapacityPerHour.HasValue && m.CapacityPerHour > 0 && actualRun > 0) {
    var capacityForRun = m.CapacityPerHour.Value * (decimal)actualRun / 60m;
    if (capacityForRun > 0) performance = Math.Round(total / capacityForRun * 100m, 4);
    if (performance > 100m) performance = 100m;
} else { performance = total > 0 ? 100m : 0m; }       // 降级：CapacityPerHour 缺失
var oee = Math.Round(availability * performance * quality / 10000m, 4);
```

**三率拆解（百分数 0~100）**：
1. **可用率 Availability = 实稼働 / 计划稼働 × 100**。分母 `PlannedRunMinutesPerDay`(默认 480=8h)；停止时间累加与目标日重叠的 `T_MachineDowntime`（区间裁剪 `end=EndTime ?? Now`，封顶 planned）；`actualRun = planned - downtime`。
2. **性能率 Performance = 实产量 / 理论产能 × 100**。理论产能 `= CapacityPerHour × (actualRun/60)`；分子 `total`(良+不良) 总产出；封顶 100%。**降级**：`CapacityPerHour` 缺失/≤0 或 `actualRun=0` → 有产量记 100%、无产量记 0%。
3. **良品率 Quality = 良品 / (良品+不良) × 100**。良/不良来自 `T_ProductionResult`（按设备+`CreateDate` 落目标日，`Sum(GoodQty)`/`Sum(DefectQty)`）。
4. **OEE = 可用率 × 性能 × 良品率**（三百分数相乘除 10000 回百分制）。例：85%×90%×98% = 74.97%。
> 边界：无实绩无停机 → availability=100/quality=0/performance=0 → OEE=0。

---

## ⭐ 后台服务专讲

### A. OeeCalculationService（`BackgroundServices/OeeCalculationService.cs`，`AddHostedService` `Program.cs:401`）
- 周期 5 分钟（`:20`），启动延迟 20 秒。
- **算什么**：维护 `lastDay`，跨日 `recalcYesterday=true` 先重算前日再算本日。**按租户循环** `TenantScopeRunner.ForEachTenantAsync` 逐租户开 scope 设 `CurrentTenantId`，调 `RecalculateAsync(...)` UPSERT `T_OeeDaily`（userName=`"OeeWorker"`）。异常 try/catch 记日志不中断。
- **推什么**：**不推 SignalR**，只刷库。前端 OEE 实时性由 OeeAnalysisView 30 秒轮询 + 灯格 `todayOee` 富化承担。

### B. MachineStatusMonitor（`BackgroundServices/MachineStatusMonitor.cs`，`Program.cs:402`）
- 周期 30 秒（`:21`），空闲阈值 10 分（`:22`），启动延迟 30 秒。
- **算什么**：取 `Status==1` 设备 → 查其在 `T_ProductionResult` 的最终实绩时刻(`GroupBy+Max(CreateDate)`) → **空闲判定**(无实绩 或 最终实绩早于 `Now-10分`) → 命中设备 `Status=0` + `Modifier="MachineMonitor"`。亦经 `TenantScopeRunner` 逐租户。
- **推什么**：逐台 `notifier.NotifyMachineStatusChangedAsync(cd, 0)`（try/catch）→ 前端 ControlTower/灯格即时变灰。

**Worker → SignalR → FE 完整线**：`MachineStatusMonitor`(每30s扫空闲) → UPDATE `M_Machine.Status=0` → `IMesNotifier.NotifyMachineStatusChangedAsync` → `SignalRMesNotifier`(`Clients.All`+`machine:{cd}` 发 `MachineStatusChanged`) → `/hubs/mes` → FE `ControlTowerView.hub.on('MachineStatusChanged')` → `pushEvent`+`refreshMachines()`。

---

## 校验与错误码汇总（grep 实证）

| 码 | 触发点 | 含义 | i18n |
|---|---|---|---|
| `ME-MSG-001` | `MachineService.cs:75,198` | 必填(MachineCd) | 有（原义"手配/製品CD"，复用） |
| `ME-MSG-031` | `MachineService.cs:76` | 必填(MachineName) | 有（原义"不良内容"，复用） |
| `ME-MSG-005` | `MachineService.cs:79` | 设备CD重复 | **无** |
| `ME-MSG-040` | `MachineController.cs:30` | 设备不存在(404) | **无** |
| `ME-MSG-041` | `MachineController`多处/`OeeController.cs:47` | 成功 | 有(多变体) |
| `ME-MSG-042` | `MachineService.cs:252` | 停机已终了 | **无** |
| `ME-MSG-043` | `MachineService.cs:111,137,147,249` | 数据不在 | **无** |
> OEE 端点无业务校验码，仅成功用 `ME-MSG-041`。

---

## 涉及文件清单

| 层 | 文件 | 角色 |
|---|---|---|
| FE view | `views/mes/MachineListView.vue` | 设备一览/灯格/CRUD/停机登记 |
| FE view | `views/mes/OeeAnalysisView.vue` | OEE 本日卡片/趋势/日次表/再计算 |
| FE view | `views/mes/ControlTowerView.vue` | 4 象限大屏 + SignalR |
| FE api/type/util | `api/mes/mes.ts`(machineApi 261/oeeApi 303) / `types/mes/mes.ts` / `utils/mesHub.ts` | — |
| BE Controller | `Controllers/Mes/MachineController.cs` / `OeeController.cs` | 设备 CRUD+停机 / OEE 4 端点 |
| BE Service | `Services/Mes/MachineService.cs` / `OeeService.cs` | 设备+停机+SignalR / **OEE 三率 141-210** |
| BE Notifier/Hub | `Services/Mes/IMesNotifier.cs`(+NoOp) / `WebApi/Services/SignalRMesNotifier.cs` / `WebApi/Hubs/MesHub.cs` | 实时基建 |
| BE Worker | `BackgroundServices/OeeCalculationService.cs` / `MachineStatusMonitor.cs` / `TenantScopeRunner.cs` | 5分OEE重算 / 30秒空闲监视 / 逐租户 |
| 实体 | `DomainModels/Mes/Machine.cs`/`MachineDowntime.cs`/`OeeDaily.cs`/`ProductionResult.cs` | M_Machine/T_MachineDowntime/T_OeeDaily |
| DTO | `DTOs/Mes/MachineDto.cs`/`PagedResultDto.cs` | — |
| DI/路由 | `Program.cs` | Service 283-284 / Notifier 398 / HostedService 401-402 / Hub 2235 |
| i18n | `Seed/I18nMesScreenSeed.cs` | ME-MSG-001/031/041（005/040/042/043 缺） |

## 关键发现
1. **OEE 公式**在 `OeeService.cs:177-193`：三百分数相乘除 10000，performance 在 `CapacityPerHour` 缺失时降级"有产出即 100%"。
2. **两条 OEE 路径**：`/today`(现算不落库,30秒轮询) vs `/recalculate`+Worker(UPSERT 落库,趋势/历史/灯格依赖)。
3. **两 Worker 推送差异**：`MachineStatusMonitor` 推 SignalR；`OeeCalculationService` 不推只刷库。均经 `TenantScopeRunner` 逐租户。
4. **前端缺口**：`changeStatus`/`searchDowntimes`/`closeDowntime` 已定义但未被 view 调用。
5. **错误码缺口**：`ME-MSG-005/040/042/043` 无 i18n；`ME-MSG-001/031` 被复用与原义不符。
