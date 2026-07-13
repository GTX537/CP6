### Task E-T3: DoD 验收（全量闸）

- [ ] 后端全量：`dotnet test CP6.Tests/CP6.Tests.csproj` → **1509+N 通过（5 skip）**，零失败零新 skip。
- [ ] 前端全量：`cd cp6.web && npm run test`（**320+N 全绿**）+ `npm run type-check` + `npm run build`。
- [ ] **零 EF 迁移**：`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` → clean；`git diff --stat main..HEAD` 无 `CP6.Core/Migrations`、无 `CP6.Entity` 改动。
- [ ] **零跨模块污染**：`git diff --stat main..HEAD` 无 `views/space`/`*Space*`/WMS/ERP/FIN/MES 文件；`composables/useBreakpoint.ts` 零改动（只消费）。
- [ ] 引擎零改动：`git diff main..HEAD -- CP6.Core/Services/Wf/` 为空（`TransferAsync` 只调用）。
- [ ] spec §7 测试矩阵逐条对号（见下「覆盖核对」）。
- [ ] QA harness 三件套齐；live QA（用户在场，隔离库 CP6DB_OA + 前端 dev server）另行排期——移动端 375px 走查与桌面像素走查在 live QA 完成。
- [ ] `git log --oneline main..HEAD` 提交信息全部 `feat(wfs-inbox)|test(wfs-inbox)` 前缀。

### 覆盖核对（spec §7 → 任务/测试）

| spec §7 条目 | 任务 | 测试 |
|---|---|---|
| 三态坍缩默认真 | A-T1 | `NotifyMatrixTests.IsEnabled_ThreeStateCollapse_DefaultsTrue` |
| 各类型×通道跳过矩阵 | A-T3 | `PersistentWfNotifierTests.{InAppOff_*, EmailOff_*, BothOff_*, TypesIndependent_*}` |
| 合并写不覆盖他键 | A-T2 | `PrefMergeTests.SaveMerge_PatchesTopLevelKey_PreservesOthers` 等 4 例 |
| 缓存不跨请求 | A-T2 | `PrefMergeTests.IsEnabledAsync_CachesWithinInstance_NotAcrossInstances` |
| 遗留数据兼容（D2 向后兼容） | A-T1/A-T3 | `IsEnabled_LegacyFlat_*` / `LegacyFlat_*` + QA 幕 2 |
| 逐条事务部分成功 + 失败明细 | B-T1 | `BatchTransferTests.Batch_PartialSuccess_*` / `Batch_MidLoopFailure_*` |
| 上限 500 | B-T1 | `Batch_Over500_Rejected_WithHintKey` |
| from==to 拒 | B-T1 | `Batch_FromEqualsTo_Rejected` |
| 跨租户拒 | B-T1 | `Batch_TargetCrossTenant_Rejected_SamePathAsMissing`（+停用/不存在两例） |
| 审计行齐全 | B-T1/B-T2 | `Batch_WritesEngineAudit_*` + OperLogFilter（全局既有）+ QA 幕 3 |
| TransferAsync 语义不变回归 | B-T1 | `--filter "Oa|Wf"` 全量闸（引擎零 diff） |
| rowMode 跨页分页正确性（同实例 3 任务跨页界） | D-T1 | `PendingRowModeTests.Merged_Paging_GroupsBeforeSkipTake_*` / `Expanded_Paging_*` |
| rowMode 偏好写回 | D-T2 | `parseRowMode` vitest + QA 幕 4 |
| 移动端 375px 三页走查 | E-T2 | QA 幕 5（真浏览器） |
| 桌面像素零回归 | E-T2 | QA 幕 6 + 每任务 build/test 闸 |

### 执行顺序与依赖

A-T1 → A-T2 → A-T3 → A-T4（波内顺序）；B-T1 → B-T2 → B-T3；C-T1 → C-T2；D-T1 → D-T2。四波之间无契约依赖可并行（同分支顺序执行推荐 A→B→D→C：B-T1 preview 消费 D-T1 签名的默认参兼容已在 B-T1 注明，两序皆编译）；E-T1 → E-T2 → E-T3 收尾，依赖前四波全部合入。

---

*生成于 2026-07-05。铁律：引擎动作（TransferAsync）只调用不改动；spec 冲突登记 C1~C8 不改 spec 只按登记口径实现；每 Task `git show --stat` 复核零 Space/跨模块污染。*





