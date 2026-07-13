## Task T10: 韩文（Ko）译文润色（live QA 记录的别扭词条）

> **票10。** live QA 记录 `I18nOaServiceTaskScreenSeed.cs` 的部分 Ko 词条别扭（Konglish 直译 / 词义偏差）。修法=按上下文润色下列词条的 `Ko` 字段（**只改 Ko，其他四语不动**）。下表为定案替换；执行前对照 live QA 记录确认无遗漏（若 QA 记录了本表未列的词条，一并按同风格润色）。

| LangKey（行号） | 现 Ko | 改为 Ko | 理由 |
|---|---|---|---|
| `oa.designer.svc.title`（:13） | 서비스 태스크 | 서비스 작업 | 「태스크」Konglish 音译，「작업」为地道韩文「任务/作业」 |
| `oa.designer.svc.kind.dataWriteback`（:15） | 데이터 기록 | 데이터 쓰기 | 「기록」=记录，偏离「回写」；「쓰기」=写入，贴 writeback |
| `oa.designer.svc.action`（:20） | 액션 | 동작 | 「액션」音译，属性标签用地道「동작」 |
| `oa.designer.svc.timerAction`（:39） | 실행 시 액션 | 실행 시 동작 | 同上，去 Konglish「액션」 |
| `oa.designer.svc.errorEdge`（:46） | 실패 엣지 | 실패 분기 | 「엣지」音译 edge；流程图语境「분기」（分支）更达意 |
| `oa.designer.svc.errorEdgeHint`（:47） | …이 엣지로 진행됩니다 | …이 분기로 진행됩니다 | 与上「분기」一致 |

**Files:**
- Modify: `CP6.WebApi/Seed/I18nOaServiceTaskScreenSeed.cs`（上表 6 处的 `Ko =` 字段）

- [ ] **Step 1: 逐条改 Ko 字段** — 按上表精确替换。示例（`:13`）：

```csharp
        new() { LangKey = "oa.designer.svc.title",              ZhCN = "服务任务",       ZhTW = "服務任務",       En = "Service Task",   Ja = "サービスタスク",   Ko = "서비스 작업" },
```

  （`:15`）：

```csharp
        new() { LangKey = "oa.designer.svc.kind.dataWriteback", ZhCN = "数据回写",       ZhTW = "資料回寫",       En = "Data Writeback", Ja = "データ書き戻し",   Ko = "데이터 쓰기" },
```

  其余 4 处（`:20`/`:39`/`:46`/`:47`）同法只改 `Ko =`，ZhCN/ZhTW/En/Ja 保持不动。

- [ ] **Step 2: 编译验证**（seed 是静态数据，靠编译 + 键唯一性兜；无逻辑测试）：
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
```
  预期：编译成功。（键未变、仅值变，运行期 SeedLangs 覆盖式幂等，无重复键风险。）

- [ ] **Step 3: commit**
```bash
git add -A && git commit -m "fix(wfs-service-task): T10 韩文译文润色（去 Konglish 音译，服务任务面板 6 词条）"
```

---

## Global Constraints（每个 Task 都遵守）

- **测试基线不回归：**
  - 后端：`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿——基线 **1509 测试**（5 skip = SQLite 既知限制）。`--filter Wf` 既有 Wf 测试字节等价（除本计划显式改动的测试断言外）。
  - 前端：`npm run test`（vitest run）**320 全绿** + `npm run type-check` 通过。**type-check 须大堆**（vue-tsc 内存密集）：
    - Bash 工具：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`
    - PowerShell：`$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`
- **EF 迁移 clean：**`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 报无 pending（本计划**不新增迁移**——无实体/DbSet 改动）。
- **零跨模块污染：**只碰 `CP6.Core/Services/Wf/**`、`CP6.WebApi/{Program.cs,Middleware,Seed}`、`cp6.web/src/views/oa/designer/**`、`cp6.web/src/utils/signalr.ts`、对应 `CP6.Tests/Wf/**`。**绝不碰** `views/space/**`、`Services/*Space*`、任何 Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff。
- **零硬编码色：**前端一切颜色走 Design System token（`var(--cp-danger)` 等，见 `cp6.web/src/styles/tokens.css`），禁十六进制字面量。
- **i18n 五语齐全：**任何新增文案键必须五语齐全 `ZhCN/ZhTW/En/Ja/Ko`，加进 `I18nOaServiceTaskScreenSeed.cs`，运行期 SeedLangs 幂等去重。
- **TDD 节奏：**先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→本地 commit（**不 push**）。提交信息风格：`fix(wfs-service-task): <中文描述>`。
- **独立性：**11 个 Task 互不依赖，可任意顺序 / 并行执行。建议顺序见文末「执行顺序」。

