# 2026-09-07 文档分类整理记录

[返回文档中心](../README.md) · [维护规则](../CONTRIBUTING.md)。

## 基线与范围

- 基线为远端 `main@5881ce89`，完整提交记录在 [迁移清单](docs-reorganization-20260907.json)。本地 `main` 当时与远端分叉，有两条未推送提交，因此本任务使用独立分支与 worktree，不混入它们或根工作区的未跟踪资料。
- 基线中 `docs/` 有 2,060 个 Git 跟踪文件，其中 813 个 Markdown。根目录有 32 个散落文件，缺少统一文档首页。
- 31 个根目录文件按用途迁入 `product/`、`architecture/`、`requirements/legacy/`、`archive/erp-integration-202606/`、`space/research/` 和 `interview-prep/`。每个旧路径、新路径和原始 Git blob 记录在迁移清单中。
- `docs/` 根目录保留文档中心、维护规则和原有手册导出脚本。脚本引用的日文 Markdown 源在基线中不存在，本次保留其位置与行为，在维护规则中说明限制。
- 补充产品、架构、需求、手册、项目记忆、任务 / QA、面试、研究、种子、归档和盘点目录入口；修复入站路径、移动文档自己的相对路径，以及发现的旧失效链接。
- 既有 `docs/file/` 原始资料、CRM 审批材料、R2 规范、Space 验收资产、DevOps ADR、发布脚本与流水线不在迁移范围。Word 等附件按原版本保留；分类整理不等于正文重新核验或业务验收。

## 可复现检查

从仓库根目录运行。Python 检查不访问外网，只检查本地文件目标及大小写，不检查标题锚点。原始资料目录 `docs/file/` 中的第三方样例不作为维护文档检查输入。

```powershell
python -B -X utf8 -m unittest discover -s scripts/tests -p test_check_docs_links.py

python -B -X utf8 scripts/check-docs-links.py --all-docs

pwsh -NoProfile -File tools/Test-CrmSaasPublicContract.Tests.ps1
pwsh -NoProfile -File tools/Test-CrmV1Prd.Tests.ps1

git diff --check
```

迁移附件校验使用 Git blob 身份，避免 Windows 与 Linux 的文本换行差异。清单内 15 份非 Markdown 文件（Word、图片、图源和文本底稿）应与原始 blob 相同；16 份 Markdown 允许导航及相对路径调整。

```powershell
$taskManifest = Get-Content docs/_inventory/docs-reorganization-20260907.json -Raw | ConvertFrom-Json
foreach ($taskMove in $taskManifest.moves) {
    if (Test-Path -LiteralPath $taskMove.from) { throw "Old path still exists: $($taskMove.from)" }
    if (-not (Test-Path -LiteralPath $taskMove.to -PathType Leaf)) { throw "Missing target: $($taskMove.to)" }
    $taskOriginal = git rev-parse "$($taskManifest.baseCommit):$($taskMove.from)"
    if ($LASTEXITCODE -ne 0 -or $taskOriginal -ne $taskMove.gitBlobBefore) { throw 'Baseline blob mismatch' }
    if (-not $taskMove.to.EndsWith('.md')) {
        $taskBlob = git hash-object "--path=$($taskMove.to)" $taskMove.to
        if ($LASTEXITCODE -ne 0 -or $taskBlob -ne $taskMove.gitBlobBefore) { throw "Changed attachment: $($taskMove.to)" }
    }
}
```

导航与正文引用的差异可用 `git diff --find-renames 5881ce89 -- docs README.md .superpowers/sdd` 审查。迁移清单刻意保留旧路径用于检索，不把这类审计字段当作失效链接。

## 本地验证结果

| 检查 | 结果 |
| --- | --- |
| `check-docs-links.py --all-docs README.md` | 830 份 Markdown、1,421 个本地文件链接，0 错误；不包含原始资料 `docs/file/`，不检查外网和标题锚点 |
| 检查器行为回归 | 8/8 通过，覆盖移动后失效路径、大小写、Unicode / 图片、代码示例、目录、越界、标题及文档发现范围 |
| 迁移目标 | 31/31 存在，31 个旧文件位置均已迁出；没有丢失文件 |
| 非 Markdown 附件 | 15/15 与原始 Git blob 相同 |
| Word 内部引用 | 7 份 Word 中的 8 个相对链接目标仍可达；历史 ERP 目录保留一页兼容跳转，正文仍只在架构目录维护 |
| 基线引用保留 | 重映射旧路径后，921 条原有有效且唯一的本地引用全部保留，新增失效引用为 0 |
| 受约束内容 | CRM、R2、Space acceptance、DevOps ADR、原始资料目录及流水线没有 diff |
| 差异格式 | `git diff --cached --check` 通过 |
| CRM 公共契约 / PRD 回归 | 20/20 与 54/54 通过；测试的稀疏检出清单增加架构目录，实际验证器与批准摘要未修改 |

这些结果只证明分类、链接和附件完整性；应用构建与远端必需门禁仍由本任务关联 PR 的检查及合并后 `main` 运行记录判定。
