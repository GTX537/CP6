# Task B-T1 报告：NCrontab 引包 + WfCronHelper（时区口径 + 预览）

**状态**：DONE ✅
**Commit**：`124da4a`（已 push 至 `feat/wfs-event-trigger`）
**分支**：`feat/wfs-event-trigger`

## 交付文件（仅 brief 指定 3 文件 + csproj）
- `CP6.Core/CP6.Core.csproj`：追加 `<PackageReference Include="NCrontab" Version="3.3.3" />`（内联，非 CPM；插在 `Microsoft.Extensions.Caching.StackExchangeRedis` 与 `RabbitMQ.Client` 之间，字母序合理位置）。
- `CP6.Core/Services/Wf/WfCronHelper.cs`：static helper（IsValid / NextUtc / PreviewUtc）——按 brief 代码块逐字实现。
- `CP6.Tests/Wf/WfCronHelperTests.cs`：6 个 [Fact]，按 brief 逐字转录。

## 依赖审查记录
NCrontab 3.3.3 = 最新稳定版（Atif Aziz），MIT 许可，单包无传递依赖。已记入 commit body。`dotnet restore CP6.Core` 成功拉包（本地缓存/公共源命中，无需回退 3.x 其它版本）。

## TDD 证据

### RED（Step 3）
`dotnet test --filter WfCronHelperTests` → 编译失败，6 处 `error CS0103: The name 'WfCronHelper' does not exist in the current context`（WfCronHelperTests.cs:10-58）。确认测试先行且真实红。

### GREEN（Step 5）
- `--filter WfCronHelperTests`：**Passed! Failed:0, Passed:6, Skipped:0**（167ms）。
- `--filter Wf`（Wf 闸）：**Passed! Failed:0, Passed:264, Skipped:0**（15s）。
- 全量 `dotnet test CP6.slnx`：**Passed! Failed:0, Passed:1912, Skipped:5, Total:1917**（1m4s）= 基线 1906 + 新增 6。

## Gates
1. ✅ 新测试通过；全量 1912 绿（1906 + 6），5 skipped（既有 SQL/Sqlite 门控跳过，与本任务无关）。
2. ✅ `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration."（Engine-zero-diff / zero-new-migrations 约束满足）。
3. ✅ `git show --stat HEAD` 仅 3 文件：CP6.Core.csproj(+1) / WfCronHelper.cs(+39) / WfCronHelperTests.cs(+62)。外科式 staged，未触 sdd scratch 内 controller-owned 文件。

## 自审
- **时区口径**：NextUtc 用 `TimeZoneInfo.Local` 作为 app 默认时区解释 cron，存储/比较一律 UTC（SpecifyKind Utc→本地→GetNextOccurrence→SpecifyKind Unspecified→ConvertTimeToUtc）。返回值 Kind==Utc（NextUtc_IsStrictlyFuture 断言验证）。符合 spec §9 一期口径。
- **月末口径③**：helper 无 L 语义，doc-comment 已注明「每月末」预设按 28 日近似；边界行为由 Day31_SkipsShortMonths（4→5 月 31 日）与 Feb29_OnlyLeapYear（2026→2028）验证 NCrontab 跳过无效日期。
- **严格未来 / misfire**：GetNextOccurrence 天然从 afterUtc 之后起算，宕机跨过的历史到期点直接跳过（供 B-T2 timer scan 消费）。
- **PreviewUtc**：以上一次结果为游标迭代，UTC 升序；非法 cron 时 NextUtc 返回 null → 循环 break（空/短列表容错）。

## 偏差
无。测试逐字转录，实现逐字照 brief 代码块。csproj 插入位置为字母序合理点（Microsoft.* 段尾、RabbitMQ 前）。

## 关注点 / 移交 B-T2
- Day31/Feb29 两测试依赖 `TimeZoneInfo.Local`——本机（Windows Server，UTC）下绿。若 CI/其它机器时区不同，"local.Month==5 / local.Day==31" 断言因 helper 本身也用 Local 换算而自洽（测试用同一 Local 反算），无脆弱性。
- WfCronHelper 已就位，B-T2 的 `ScanTimersOnceAsync` 可直接消费 NextUtc 计算 NextDueUtc。

---

## 审查修复追加（B-T1 review findings）

**分支**：`feat/wfs-event-trigger`　**触碰文件**：仅 `WfCronHelper.cs` / `WfCronHelperTests.cs` / 本报告（外科式 staged，Engine 零 diff、零迁移）。

### Finding 3 报告文本更正
原「自审 / 关注点」段称测试在本机绿因「host 是 UTC」——审查复核实测**本机 `TimeZoneInfo.Local` 实为 Pacific Standard Time（观测 DST）**，非 UTC。原 6 测试仍绿的真实原因是：helper 与测试**对称使用同一个 `TimeZoneInfo.Local`** 符号做正/反算（对任一固定或非固定偏移都自洽），而非「host 是 UTC」。特此更正。此对称性也正是 Finding 2 缺陷此前未被这 6 个测试暴露的原因——它们从不构造落在 DST 春季缺口内的本地时刻。

### Finding 1 — 不可达 cron 哨兵泄漏（Important）
语法合法但永不匹配的 cron（如 `0 0 30 2 *`，2 月 30 日不存在），NCrontab 3.3.3 的 `GetNextOccurrence` 回吐内部哨兵 `9999-12-31T23:59:59.9999999` 而非报「无」。原 `NextUtc` 直接 Local→UTC 透传，返回一个「看似合法」的日期；`PreviewUtc` 亦会连吐哨兵。契约意图 `NextUtc` 返回 `DateTime?` 且 `null`=无下一次。
- **修复**：`NextUtc` 中 `nextLocal.Year >= 9999` 视为「永不」→ 返回 `null`；`PreviewUtc` 因委派 `NextUtc`，收到 `null` 即 `break`（天然停）。

### Finding 2 — DST 春季跳变缺口抛异常（Important）
`ConvertTimeToUtc(nextLocal, TimeZoneInfo.Local)` 在 `nextLocal` 落入本地时区不存在的春季跳变窗口时抛 `ArgumentException`（本机 PST 实证）。
- **修复**：抽出私有 helper `ToUtcSafe(DateTime local)`——转换前若 `TimeZoneInfo.Local.IsInvalidTime(local)` 则按 30 分钟步进推进至有效时刻（DST 缺口 30–60 分钟，上界 6 步做安全网），`NextUtc` 与 `PreviewUtc`（经委派）共用同一守卫，零重复。

### RED 证据（新增 3 测试对当前代码先红）
`dotnet test CP6.Tests/CP6.Tests.csproj --filter "FullyQualifiedName~WfCronHelper"` → **Failed:3, Passed:6**：
- `NextUtc_UnreachableCron_ReturnsNull` → `Assert.Null Failure … Actual: 9999-12-31T23:59:59.9999999Z`（Finding 1）
- `PreviewUtc_UnreachableCron_ReturnsEmpty` → `Collection: [9999-…, 9999-…, 9999-…]`（Finding 1）
- `NextUtc_AcrossDstTransitions_NeverThrows_StrictlyIncreasing` → `System.ArgumentException: The supplied DateTime represents an invalid time`（Finding 2，实证本机为 DST 观测区）

### GREEN
- 覆盖闸 `--filter "FullyQualifiedName~WfCronHelper"`：**Failed:0, Passed:9, Skipped:0**（原 6 + 新 3，6 个原测试字节不变仍绿）。
- 全量 `dotnet test CP6.slnx`：**Failed:0, Passed:1915, Skipped:5, Total:1920**（= 修复前 1912 + 新 3）。
