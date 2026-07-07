# Task E-T2: i18n 五语 seed

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md）

**Files:**
- Create: `I18nOaServiceTaskScreenSeed.cs`(Glob `**/I18nOaApproverScreenSeed.cs` 同目录/同模式)
- Modify: `CP6.WebApi/Program.cs`(concat 进 i18n seed 链,带去重)
- Test:(seed 是静态数据,靠 build + 启动 + QA 验;可加一个 key 唯一性单测可选)

- [ ] **Step 1: 实现** — 仿 `I18nOaApproverScreenSeed` 静态 `Sys_Lang[] Items`,五语(zh-CN/zh-TW/en/ja/…按既有语种)键:
  - 面板:`oa.designer.svc.kind/.mode/.action/.connector/.path/.params/.delayMode/.delayValue/.maxRetries/.backoff/.errorEdge` + 三 kind 标签 + delayMode 三选项。
  - 错误:`E-WF-016/E-WF-017/E-WF-018` 文案 + 前端 `errServiceConfig`。
  - **去重**:grep 已有 seed(I18nOaApprover/Inbox/Advanced/Designer)避免 LangKey 重复(参 approver seed 9 键 dedup 做法)。
  - `Program.cs` concat 链加 `.Concat(I18nOaServiceTaskScreenSeed.Items)`(带去重逻辑,同既有)。
- [ ] **Step 2: build 验证** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`(无重复键编译期不报,运行期 SeedLangs 幂等去重)。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): E-T2 I18nOaServiceTaskScreenSeed 五语+concat"`

## 注意
- D-T2/D-T3 前端已引用 `oa.designer.svc.*` 键——本任务的键名必须与前端实际用的键逐一对齐（先 grep `cp6.web/src/views/oa/designer` 里 `oa.designer.svc.` 的全部引用，以前端为准补齐，多余键不加）。
- 环境坑（memory 记录）：i18n 种子曾有"全空守卫导致旧库新增词条被永久跳过"的坑，已在 01f1f6f 修复——本任务照现行 seed 模式即可，不要自己加守卫。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- 零 Space 污染。不重新设计。
