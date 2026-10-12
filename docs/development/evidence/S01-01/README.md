# S01-01 私有准备与输入核验

## 2026-10-12：用户确认后的任务交付窗口

用户已确认本对话兼任 S01-01 协调，领取、状态同步和本任务合并统一在本分支完成。领取时已重新 fetch 核对 main/origin/main 为 `df5553fd04ec4fc516602a4e2843650c38ae5006`；无其他已登记活动任务。准入登记检查见 [entry-check.json](entry-check.json)。没有领取或编辑其他任务。

现已形成 [profile/source/vector 包](../../../../eng/contracts/s01/README.md)：`CP6-S01-CONTRACT-INPUTS/1.0.0`，33 份来源、16 个 profile、122 条共用向量。范围包括原 23 条数值/单位输入及预期、精确 Schema/类型/注册表身份、原始字节保持、严格 JSON/required null、原生字段投影与版本路由。完整合同采用和业务验收不属于此次交付。

[package-check.json](package-check.json) 是本轮实际静态核验：61 次原件/组合派生哈希检查、22 份本地引用 pin、所有 profile 有有界向量、19 个修订运行场景引用，以及业务/其他任务台账未变。`execution_head_sha` 表示核验时 HEAD；包当时为工作树变更，准确输入身份以报告 `input_files_sha256` 为准，不能把该 HEAD 当成未提交包已存在的源码证据。交付提交固定后在交付回执记录包含性。初次工作树检查保留在 [package-check-initial.json](package-check-initial.json)，其输入摘要只对应当时快照；审查后补齐 FIN-CREDIT 内联字段绑定，仓库来源采用 Git blob 字节 pin，并将新包/生成器固定 LF。最终报告另行生成，不把初次快照当成最终文件。

生成工具只读取归档数据及仓库资产，不执行原包程序。`build-profile-inputs.py` 重建来源清单；`build-vectors.py --write` 重建向量，`--check` 只比较已有产物。重新生成前先审查原规则及来源身份，不能以动态生成的新预期掩盖实现不符。预期不是被测 C#/TS 的输出。

任务级[审查及修正记录](review.md)、[字节稳定检查](byte-portability.json)、[合并前台账检查](ledger-before-merge.json)与[实际交付记录](delivery.md)分别保留适用范围。新增的 `build-vectors.py --check --verify-native-fields` 读取准确归档数据，核对3份Schema的67个字段指针；消费者运行不需要归档路径。

校验中 LEAD `LeadQueryFilters.ownerId` 的 required/UUID/unassigned/null 已核原 Schema；`ContactCreateInput.ownerId` 的独立可选性保持。ERP current/frozen 注册表实际字节单独重算，不仅引用 codec 注释中的 hash。ATP 组合 DDL 的长度与锁序修订一起保留。当前来源齐备性限于这个合同子集；原完整组合的历史缺件仍按原目录记录。

没有业务编译、真实 Owner、数据库、迁移、Actions 或部署；未取消任何运行，未改触发器/分支保护。核验初次准备、后续包检查和交付状态分别留存，历史记录不改称本次业务 PASS。

下文保留领取前准备时点记录。任务范围以[任务卡](../../../superpowers/plans/2026-10-11-s01-task-breakdown.md)为准；实时领取及准入以[唯一台账](../../roadmap.json)为准。

## 已核对的基线与范围

- Main 与 origin/main 已 fetch 核对，均为 `df5553fd04ec4fc516602a4e2843650c38ae5006`。
- 任务分支：`codex/s01-01-exact-inputs-20261012`；worktree：`D:/CP6-worktrees/s01-01-20261012`。此路径仅记录本地工作位置，不是消费者运行配置。
- 根工作区未跟踪资料保持原状。首次创建 worktree 因已有历史长路径失败；使用单次 `git -c core.longpaths=true worktree add` 成功重建，未改全局 Git 配置。后续 Git 检查沿用该选项。
- S01-01 无任务前置，27 份选定原件及对应组合派生文件均可读取，4 份本地资产 pin 可读取。准确核验结果见 [input-check.json](input-check.json)。这只证明来源字节与索引一致。
- ATP PostgreSQL DDL 必须一起保留 `PLAN-IF-004` 与 `PLAN-IF-005`。数据库采用仅针对 PostgreSQL，历史资产中的 SQL Server 内容不增加新验收要求。
- 本次读取台账时 S01-01 为 `PLANNED`、`NOT_CHECKED`、未领取。按[协作规则](../../09-parallel-development.md#领取与开工)，等待协调对话登记及资源窗口，或用户指定本对话兼任协调；准备记录不替代登记。

## 准入确认后的执行步骤

1. 复核届时最新 main 与领取记录。若主线有变化，核对本任务输入与资源差异后再开始交付文件编辑。
2. 在 `eng/contracts/s01/profile-inputs.json` 固定各原生 profile 的 Owner、消费者、Schema/类型身份、原件与组合派生 SHA-256、完整补丁集合。来源定位使用仓库相对引用及内容 hash；外部归档路径只通过既有来源索引解析，不进入新运行配置。
3. 在 `eng/contracts/s01/vectors.json` 提取原 23 条数值/单位向量。原记录没有原生 case ID，使用明确标为本包索引的稳定 ID，并同时保存来源 JSON pointer，不伪造原 AC 身份。移除来源的历史执行结果，只记录输入、预期、字段/profile 和测试层级。
4. 补充三种 FIN-CREDIT 符号 profile、FIN-INV 小数 18/12 位边界及原 `-0.000`、ATP 后继词法、严格 JSON、required null、结构相等、准确版本路由与原字节保持用例。精确大整数与原始 JSON 文本均以字符串表示，预期不通过任一待实现语言动态生成。
5. 关联各 profile 原验收定义及修订采用场景。CRM-INQ 的 `AC-INQ-009/048/053` 分别位于 `catalogs/commercial-finance.json` 的 CRM-INQ-01 cases；只提取类型/Schema/长度子断言，完整权限、下载、H01 等业务流程保持 `NOT_RUN`。
6. 在包 README 说明消费者读取协议、版本和原字节边界；完成结构、引用、来源 hash、向量覆盖和一次任务级完整 diff 审查。无需编译业务系统、运行归档程序或执行数据库迁移。
7. 由协调对话在本任务交付窗口同步总台账、生成页及四份状态文件。每次 push/PR/merge 前重新检查候选及远端受保护主线工作流，按正常保护流程交付；核实远端 main 包含性后才记任务完成。

## 可复现核验

在任务 worktree 根目录使用 Python 3.10+：

```powershell
python docs/development/evidence/S01-01/verify-inputs.py --out docs/development/evidence/S01-01/input-check.json
git -c core.longpaths=true diff --check
```

本机实际解释器为 `C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe`；裸 `python` 的旧版本未作为本次证据。工具读取既有来源索引中的归档定位，重新计算 SHA-256，不执行归档脚本、SQL 或应用。后续复核应另存结果，保留本次历史记录。

GitHub 运行列表只读检查未发现本任务产生的排队/执行中运行，无需取消；没有触发 Actions、修改工作流或分支保护。共用包尚未实施；无 C#/TS 消费者测试、真实 Owner、PostgreSQL 数据库或生产验收。本任务尚未提交完整交付、合并或登记 DONE。
