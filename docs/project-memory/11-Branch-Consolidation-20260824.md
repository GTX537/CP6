# CP6 分支整顿与恢复盘点（2026-08-24）

## 1. 目标与结论

本轮只处理仓库治理、分支清理和 WIP 恢复，不把未验收功能直接并入 `main`，也不执行生产部署。整顿使用的当前集成基线是：

- `main` / `origin/main`: `0a14581f87ac1955678bdb664911183fc5a2a2a1`
- 根工作区：`D:\CP6`
- 整顿前安全归档：`D:\CP6-archives\2026-08-24-branch-consolidation`
- GitHub 可见性：保持 Public（本阶段明确接受的风险决定）

最终开发结构采用“干净 main + 单任务续开发分支”：旧分支不再作为开发基线；确需继续的内容已经先合并当前 `main`，或从当前 `main` 重建为单职责恢复分支。

## 2. 整顿前基线

- 根工作区位于 `wip-root-before-branch-policy-20260809@e4e33364`，落后当前 `main` 172 个提交。
- 根工作区有 43 个 tracked 修改和 4 个 untracked 文件，内容混合登录、日期时间、Kafka 处置、本机配置和已被后续主线替代的实现。
- 本地共有 9 个 worktree；多个旧 worktree 存在 tracked 修改、未跟踪文件、忽略文件或 Git LFS 指针工作副本。
- 远端有 72 个非 `main` 分支：61 个已是 `main` 祖先，11 个按 Git ancestry 尚未合并。
- 打开的 PR 为 #3、#7、#8；`main` 当时无分支保护/规则集。

## 3. 安全归档与恢复入口

归档目录包含：

- `cp6-all-refs-before-cleanup.bundle`：整顿前 105 refs 的完整 Git 历史包。
- `refs-before-cleanup.txt`、`branch-inventory-before-cleanup.txt`、`worktrees-before-cleanup.txt`：引用和 worktree 清单。
- `dirty-worktrees/*/status.txt`、`tracked-changes.patch`：各脏工作区状态和 tracked patch。
- `original-untracked` / `original-untracked-tree` / ZIP：从旧工作区移动出的原始未跟踪或忽略文件；不是重新生成副本。
- `recovery-*.patch`、`root-wip-disposition.txt`：根 WIP 拆分结果及没有恢复的文件理由。
- `SHA256SUMS.txt`：归档文件完整性校验。

恢复前应先复制整个归档到另一介质，再在空目录验证：

```powershell
git bundle verify D:\CP6-archives\2026-08-24-branch-consolidation\cp6-all-refs-before-cleanup.bundle
git bundle list-heads D:\CP6-archives\2026-08-24-branch-consolidation\cp6-all-refs-before-cleanup.bundle
git clone D:\CP6-archives\2026-08-24-branch-consolidation\cp6-all-refs-before-cleanup.bundle D:\CP6-restore-audit
```

不要在当前工作区直接套用整个根 patch。需要恢复单个主题时，优先从三个恢复分支继续；确需取旧文件时，再从归档副本逐文件比较。

## 4. 根 WIP 拆分结果

### 4.1 登录体验

- 分支：`codex/login-experience-recovery-20260824`
- 提交：`1a5a58f9772307b6c732797a672ba0b043bf153e`
- 范围：`LoginView.vue`、`loginExperience.ts`、`loginExperience.spec.ts`
- 变更：3 文件，1,696 insertions / 710 deletions
- 已验证：helper 聚焦测试 6/6；Vue type-check 通过
- 状态：WIP。需要组件/浏览器验收、可访问性和大幅模板重排审查后才能合并。

### 4.2 日期时间规范化

- 分支：`codex/datetime-normalization-recovery-20260824`
- 提交：`fd0b64fc0c7718488f398ee5eab2561f65b9946a`
- 范围：35 个 Web 文件，集中统一日期时间解析、显示和相关测试
- 变更：129 insertions / 55 deletions
- 已验证：Vitest 174 文件/886 测试；Vue type-check；Vite production build 均通过
- 状态：WIP/接近可评审。还需审查跨域日期语义、补项目记忆和独立 PR。

### 4.3 Kafka Dispose

- 分支：`codex/kafka-dispose-recovery-20260824`
- 提交：`1ee78fa65a148ee74910fbc648d6a0b2300022a4`
- 范围：Kafka producer dispose/error handling 单文件修改
- 变更：10 insertions / 3 deletions
- 已验证：CP6.Core Release build 0 warning / 0 error
- 状态：WIP。缺 Dispose/异常路径行为测试与日志策略确认。

### 4.4 明确不恢复的根目录内容

- `CP6.WebApi/Program.cs`：落后 172 个提交，当前 `main` 注册更完整。
- `launchSettings.json`：包含本机 `KOUSQLSERVER` 配置，不进入共享分支。
- `LocalJsonConfiguration*`：当前 `main` 已有更强实现与测试。
- `index.html`、`vite.config.ts`：工作副本内容已等于当前 `main`。
- `main.ts`：仅有过时注释措辞。
- `env.d.ts`：通用声明对当前工具链冗余。

这些原始内容仍可在归档 patch/原始文件中审计。

## 5. 旧引用与 worktree 处置

### 5.1 远端已删除

- 61 个已被 `main` 包含的远端分支：删除引用，不再保留重复开发入口。
- 9 个经审计只需归档的未合并远端分支：`azure-pipelines`、`backup/main-local-p25-20260802`、`checkpoint/space-candidate-20260730`、`codex/crm-saas-v1-product-freeze-20260814`、`codex/crm-saas-v1-public-contract-20260814`、`codex/crm-v1-spec-approval-20260812`、`codex/fix-openapi-client-drift-20260813`、`codex/space-studio-excel-cad-catalog`、`feat/training-m07`。
- PR #3 随陈旧 Azure 分支关闭；原因是旧 `azure-pipelines-1.yml` 已被当前 self-hosted CI、DEV CD 与 readiness 配置替代。

### 5.2 本地已删除

删除的 10 个旧本地分支：

- `backup/main-pre-space-p25-20260721`
- `backup/pre-main-merge-20260714`
- `codex/space-draft-create-wizard`
- `codex/space-e04-s01-underlay`
- `codex/space-studio-excel-cad-catalog`
- `codex/space-volume1`
- `feat/space-p5-traversal-cost`
- `integration/space-v1-20260730`
- `perf/frontend-page-load-optimization`
- `wip-root-before-branch-policy-20260809`

另在 CRM 分支推送并核对远端 SHA 后，删除其两个临时本地 checkout；远端 Draft PR 分支继续存在。

### 5.3 worktree

- 移除 8 个旧 worktree：旧 `main-integration`、性能、Draft wizard、E04 underlay、Space integration、Excel/CAD catalog、Volume 1、Space backend。
- 所有 tracked 修改在移除前已恢复或形成 patch；未跟踪/忽略文件先移动到归档。
- 新建并保留三个干净恢复 worktree，分别对应登录、日期时间、Kafka；一个分支只承载一个主题。

## 6. CRM 草稿分支

### PR #7：R00 Release Authority / M0 No-Go

- 分支：`codex/crm-r00-release-authority-20260813`
- 当前提交：`a396815fead28af360530ab51639b7220cbe61fa`
- 已合并 `origin/main@0a14581f`；冲突的四个项目记忆文件采用当前 `main`，避免旧状态回灌。
- 相对 `main` 只剩 7 个 CRM/DevOps/ADR 文档文件。
- 状态：GitHub MERGEABLE、Draft；Cloudflare Workers 外部构建失败需要单独归因，产品/发布权威仍待确认。

### PR #8：SaaS V1 Public Contract

- 分支：`codex/crm-saas-v1-public-contract-sync-20260814`
- 当前提交：`25bba538c1d145b5d0cceec6a7c37ee2938cdc40`
- 已合并 `origin/main@0a14581f`；只补当前日期所需的公共契约哈希、ProgramOwner、DEC-001 等项目记忆锚点。
- `tools/Test-CrmSaasPublicContract.ps1` 本地通过。
- 状态：GitHub MERGEABLE、Draft；继续等待 CI 和产品/治理确认，本轮不合并到 `main`。

## 7. 当前主动分支面

整顿提交合入后，预期远端仅保留：

- `main`
- 三个单职责 recovery 分支
- CRM Draft PR #7/#8 两个分支

整顿任务分支在 PR 合并并确认远端 `main` 后删除。任何新开发必须从最新 `main` 建独立 `codex/*` 分支；不得在三个 recovery 分支混入其他任务。

## 8. 验证与已知非绿项

- 当前 `main` 的最近完整基线：Web 174 文件/884 测试、Vue type-check、Vite production build；CP6.Tests 2,934 passed / 19 environment-skipped；Space Unit 549；默认 Space Integration 331 passed / 125 environment-skipped；Client 71；WebApi Release build 0 warning / 0 error。
- 恢复分支的新增验证见第 4 节；CRM 公共契约验证见第 6 节。
- `tools/Test-SpaceGaEvidence.Tests.ps1` 当前显示 36/36 通过但进程返回 1：最后一个“预期无效证据”子测试遗留 `$LASTEXITCODE=1`。这是脚本退出码假红，不是 GA 已达到 Go；必须独立修复脚本并保留所有失败关闭断言。
- Space GA 真实状态仍是 72% / `NoGo`；CRM PR 仍是 Draft；本轮没有生产部署。

## 9. 后续顺序

1. 已完成：整顿 PR #9 合并为 `main@2abf451dcb0e3e776967604874daaff04ff97594`，根工作区已核对 `main == origin/main`。
2. 已完成：GitHub API 回读确认 `main` 严格要求 PR、最新主线、`windows-and-web`/`android`/`sql-integration` 和对话解决；管理员不得绕过，force-push/删除关闭。
3. 独立修复 Space GA 测试脚本退出码假红。
4. 优先审查日期时间规范化分支；登录和 Kafka 分支分别补齐验收与行为测试。
5. 等 CRM #7/#8 的 CI 与产品/治理确认，保持 Draft 直到决策闭环。
6. 把整顿归档复制到第二介质；确认可恢复后再单独决定是否删除本机归档。
