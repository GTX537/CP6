# Task T10 报告：韩文(Ko)译文润色

**Status:** DONE
**Commit:** 150ba9b（feat/wfs-cleanup-tickets，已 push）

## 改动
按票面定案表精确润色 `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs` 6 处 Ko 字段（其余四语与新加键未动）：

| LangKey | 旧 Ko | 新 Ko |
|---|---|---|
| oa.designer.svc.title | 서비스 태스크 | 서비스 작업 |
| oa.designer.svc.kind.dataWriteback | 데이터 기록 | 데이터 쓰기 |
| oa.designer.svc.action | 액션 | 동작 |
| oa.designer.svc.timerAction | 실행 시 액션 | 실행 시 동작 |
| oa.designer.svc.errorEdge | 실패 엣지 | 실패 분기 |
| oa.designer.svc.errorEdgeHint | …이 엣지로 진행됩니다 | …이 분기로 진행됩니다 |

## 更新语义核实结论（关键）
`SeedLangs`（Program.cs:1755-1761）为 **insert-only / 判存跳过**：
`toInsert = items.Where(i => !existing.Contains(i.LangKey) ...)` —— 只对不存在的 LangKey 做 AddRange，**已存在的键不 UPDATE**。
故票面 Step 2 注释「运行期 SeedLangs 覆盖式幂等」**不准确**——改 seed 常量只对全新库生效，已部署库这 6 个键早已存在，Ko 旧值不会被覆盖。

**补救：** 新增 `docs/seeds/wfs-svc-ko-i18n-fix.sql`（照仓内 `*-i18n-*.sql` 先例命名），6 条幂等 `UPDATE ... WHERE Ko = 旧值`，只改 Ko 字段。重跑零副作用（新值不再匹配旧值 WHERE）。**部署时须对每个已存在租户库执行一次此脚本。**

## 验证
- `dotnet build CP6.WebApi` → Build succeeded（0 error）。
- 全量 `dotnet test` → **Passed 1835 / Failed 0 / Skipped 5**，持平基线 1835。
- `git show --stat` → 仅 2 文件（seed + sql），零跨模块污染。

## 疑虑 / 未决
- 票面「若 QA 记录本表未列词条一并润色」：我仅见 brief 定案 6 键表，无原始 live QA 记录，按 6 键表为权威执行。
- **一致性观察（未改，因票面未点名 + 父指令「别动新加的键」）：** 票8 追加的相关键仍用旧风格音译——`oa.designer.svc.timerActionKind.write` = `데이터 기록 액션`（含「기록」，与 dataWriteback 改后的「쓰기」不一致），`timerActionKind.none/write` 用「액션」（与 action 改后的「동작」不一致）。若后续要求全面统一 Ko 风格，可作为跟踪票处理。
