# S01-01 私有准备与输入核验

本记录属于对话 A 的 S01-01 准备资料。任务范围以[任务卡](../../../superpowers/plans/2026-10-11-s01-task-breakdown.md)为准；领取及准入以[唯一台账](../../roadmap.json)为准。本记录不修改台账、生成视图、其他任务报告或四份项目状态文件，不自行取得协调角色。

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
