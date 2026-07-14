# E-T2 过审台账 — ITenantClock + 时区消费点接线 + DST 口径 + 租户管理页时区

**分支** feat/wfs-engine-infra　**commit** c3191043f120e6a5cedf2d373964d0f42c9a609f
**闸门** 后端 2098 绿/5 skip（基线 2089 +9）　前端 463 绿（基线 458 +5）　type-check ✅　build ✅　EF drift clean（零迁移）

---

## 一、时区解析链（ITenantClock）

`CP6.Core/Services/Wf/ITenantClock.cs`（新增，`AddScoped` 注册 Program.cs:187）：

```
GetTenantTimeZone()  ── per-scope 缓存一份 tz（同 scope 只查一次租户表、只解析一次 id）──
  ① 当前租户 Sys_Tenant.TimeZoneId（共享表直查 Id，无需 IgnoreQueryFilters）→ FindSystemTimeZoneById 可解析则用
  ② 缺省 → WfsInfraOptions.DefaultTimeZone（Wfs:DefaultTimeZone）可解析则用
  ③ 再缺省 → TimeZoneInfo.Local（服务器本地，存量行为字节等价）
```

- **不可解析永不抛**：`TryResolve` 捕 `TimeZoneNotFoundException`/`InvalidTimeZoneException` → 记 `LogWarning` → 逐级回落。运行期坏配置不炸引擎；只有**保存时**才拒绝（E-WF-028，见三）。
- **跨平台 id**：.NET 8 `FindSystemTimeZoneById` 原生容纳 IANA/Windows 双制式（"Asia/Tokyo" 在 Windows Server 亦解析——DST 定点测试用 "America/New_York" 已在本机 Windows 实证通过）。
- **ctor** `(CP6Context, ITenantContext, WfsInfraOptions, ILogger<TenantClock>? = null)`；logger 可选 → 契约测试 `new TenantClock(db, ctx, opts)` 三参构造成立。

## 二、消费点清单（ServiceTaskNodeHandler）

ctor 追加 `ITenantClock? clock = null`（同 workdays/opts 可选参姿态；DI 自动注入，DefaultHandlers/既有单测 `new(...)` 零破坏，clock 缺省→服务器本地=现行为，天然向后兼容）。

| 消费点 | 改动 |
|---|---|
| `ComputeTimerDueUtcAsync`（生产入口） | 不再收 `DateTime.Now` 参；内部 `tz = _clock?.GetTenantTimeZone() ?? Local`，`nowLocal = ConvertTimeFromUtc(UtcNow, tz)`（跨零点当日边界随租户时区），委托 core。OnEnterAsync:109 调用点同步去参。 |
| `workdays` 分支（core） | 顺延 N 工作日后 `fireLocal @ _workdayFireHour` 经 **DST 安全回转**到 UTC（tz 源换 `_clock`）。 |
| `ComputeDueUtc` untilDate/untilExpr | 新增 `ComputeDueUtc(node, vars, TimeZoneInfo tz)` 三参重载；旧二参重载委托它传 `TimeZoneInfo.Local`（既有静态调用点/单测字节等价）。`ParseLocalDateToUtc` 加 `tz` 参 + DST 安全。 |
| `ComputeTimerDueUtcForTestAsync`（测试重载） | tz 仍取 `_clock`（缺→Local），注入 nowLocal，令 DST 口径可定点验证。A-T3 既有 `WorkdaysDelayModeTests`（无 clock→Local）零改写通过。 |

**存量 null 全等**：无 TimeZoneId 且无 clock → tz=服务器本地 → 与现状字节等价（A-T3 三测 + 静态 ComputeDueUtc 测全绿佐证）。

## 三、DST 口径（写死于 `ConvertLocalToUtcWithDstPolicy`，workdays 与 untilDate 共用）

- **春跳缺口**（本地时刻不存在，`tz.IsInvalidTime`）→ **逐时前移取下一有效本地瞬间**（即 +DST 偏移，通常 1h；上限 6 步防呆）。定点测：NY 2026-03-08 02:30（缺口）→ 03:30 EDT(-4) == **07:30Z**。
- **秋拨歧义**（本地时刻重复出现）→ `ConvertTimeToUtc` **默认按标准时**解释（取标准时那次），不特殊处理。定点测：NY 2026-11-01 01:30（歧义）→ 01:30 EST(-5) == **06:30Z**。
- **兜底**：极端边界仍抛 → 按 UTC 兜底（同既有 ParseLocalDateToUtc 姿态，不炸引擎）。**日本无 DST，此策略对其为恒等**（东京 workdays 落点测 09:00 JST==00:00Z 佐证）。

## 四、租户管理页 + E-WF-028（保存时拒绝）

**保存校验（服务层）** `TenantAdminService.UpdateAsync` 加 `string? timeZoneId = null`（可选参→既有 4 参调用/测试零破坏）：
- 空/空白 → `null`（视作清空，沿用 app 默认）。
- 非空 → `FindSystemTimeZoneById` try/catch，失败抛 `InvalidOperationException("E-WF-028")`，**不落任一字段**（名称+时区均不变，测已佐证）。控制器 catch→`BizException`（E-WF-028 本地化）。
- `TenantDetail` 加 `string? TimeZoneId`（末位可选参→既有构造零破坏）；`GetAsync` 回读。

**前端** `views/platform/TenantListView.vue` 编辑对话框加时区下拉（`filterable clearable`，候选来自 `api/platform/timezones.ts` 的 `TIMEZONE_OPTIONS`）+ 自愈口径提示 `tz-hint`；`openEdit` 改 async 拉 `tenantApi.get` 回填当前 tz+remark（列表行不含）；`doUpdate` 用 `normalizeTimeZoneId` 规整后提交。vitest `timezones.spec.ts`（5 测）：候选非空/含常用/value 唯一/label 齐、规整空→null＋trim。

**自愈语义（提示文案传达）**：改时区**不批量重算**既有触发器 `NextDueUtc`，下次发火后按新时区自愈（最多一次旧时区发火）。

## 五、约束合规

- **零跨模块污染**：碰 Wf/Platform 服务 + WebApi Program/Controllers/Platform + cp6.web `api/platform`+`views/platform`+`types/platform`。
- **例外声明**：前端触及 `views/platform`（非 `views/oa`）——租户管理页属 **Platform 域**，为 plan File Structure 明示的「Modify 租户管理页」计划内例外；TenantController 系 Platform 域控制器，**不在 Oawf 守卫面**，未触发重基线。
- **零迁移零实体改动**：TimeZoneId 列 A-T1 已在；FlowNode 无新字段；EF has-pending-model-changes clean。

## 六、F-T1 键交接（i18n LangKey，五语 ZhCN/ZhTW/En/Ja/Ko）

视图新引三前端 t() 键（后端 Sys_Lang 驱动，F-T1 待种）：

| LangKey | 语义 | 建议文案（ZhCN） |
|---|---|---|
| `platform.tenant.timeZone` | 时区字段标签 | 时区 |
| `platform.tenant.timeZonePlaceholder` | 下拉占位 | 选择时区（留空＝沿用默认） |
| `platform.tenant.timeZoneHint` | 自愈口径提示 | 改时区不批量重算既有触发器到期时刻，下次发火后按新时区自愈（最多一次按旧时区发火）。 |

另 **E-WF-028** 错误码（LangKey=`E-WF-028`）由 F-T1 统一种（brief 明示归 F-T1）：「连接器/节点 TimeoutSec ≥ 租约 或 时区 id 不可解析 → 拒绝保存」。

## 七、concerns

- 无必修。DST 定点测依赖运行环境 tz 数据库含 America/New_York 规则（本机 Windows Server 已实证）；若某部署环境裁剪了时区库，`FindSystemTimeZoneById` 抛→`TryResolve` 回落（不炸），DST 测在此类环境可能失配——CI/生产标准镜像均带全量 tz 数据，非现实风险。
- 前端时区下拉为常用捷径白名单（21 项），非完备集；用户输入白名单外 id 仍由后端 E-WF-028 兜底校验（`filterable` 允许搜索既有项，但当前不支持自由输入非白名单值——如需任意 id 输入可后续加 `allow-create`，本波按「常用捷径」口径收敛）。
