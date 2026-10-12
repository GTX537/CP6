# S02-00 · B PostgreSQL 多租户设计交付候选

日期：2026-10-12。范围：Main 任务 S02-00 的设计文档、验收计划及来源核验；无业务实现、schema/角色创建、数据迁移或环境切换。

## 交付内容

- [设计 cp6-pg-tenancy-v1](../../../design-handbook/architecture/02-postgresql-tenancy.md)：共享控制/消息域 + 租户业务 schema；租户专用 LOGIN/连接池、共享行 RLS、可信绑定、EF 缓存、同连接事务、迁移和恢复。
- [27 个正反验收场景](../../acceptance/postgresql-tenancy.json)：请求/actor、前置、步骤、预期、证据与后续负责任务；全部 NOT_RUN。
- [准确来源](source-inventory.json)：Main/CRM/Platform 远端 main 固定 SHA；31 个源码/规则文件的 Git blob SHA-256，12 个原正文/组合/接受输入的归档原字节摘要。
- [集中审查](review.md)与[只读检查程序](verify.py)。文档检查结果保存在本目录 `validation.json`，不是数据库/业务验收结果。

## 基线与领取边界

业务分支：`codex/s02-00-postgresql-tenancy-20261012`；独立 worktree：`D:/CP6-worktrees/s02-00-20261012`。从已核对 Main/origin/main `df5553fd04ec4fc516602a4e2843650c38ae5006` 创建。CRM `c778a3052a4416b82facb07b1244fc98fb6ad8a1`；Platform `30bd23af6808d217a23878bd9437513043c52834`；均核对实际远端 main，无跨仓写入。

接手时唯一台账为 S02-00 `PLANNED / assignment=null / entry_gate=NOT_CHECKED`。用户将本对话指定为 B；已请求协调登记。本报告及设计是任务私有准备成果，**未把自己登记为已准入或 DONE**。本任务无 S01 代码前置，只读核对共享资源；没有修改 roadmap、生成页、四份状态页、公共代码/模型/迁移或其他对话文件。

协调对话需核本任务 `Main:s02-00` 资源与候选文档后登记 assignment/entry_gate，在交付窗口同步台账、生成视图、PROJECT_STATE、05-Completed、06-Todo、CHANGELOG-AI，完成正常保护合并和远端 main 包含性核验。准备成果/任务分支提交不替代这些步骤；下游在此之前不得把本任务当已交付前置。

准备期间远端 main 前进至 `92fb94db40c54be90f2fbb18b0922d264c060796`，仅增加 Handoff 协作规则及状态记录，业务源码/工作流无变化。本分支已 fast-forward 到该主线。原源码核验保留真实 `df5553fd` 来源；采用新增直接通信规则，通过本任务 Handoff 分支 `codex/dev-handoff-s02-00` 发布 START，协调入口为固定 `e476bc58b05349c34c9361b4af2f0cde1ce92aea` 的本轮 README。正式设计仍在 CP6，本轮 Handoff 仅放消息/提交定位。

## 执行步骤和静态核验

1. 核主线、任务范围、协调状态和原数据库决定，建立独立工作树。
2. 定向核 Main 租户/消息/PG 映射、CRM 实际 SQL 事务依赖、Platform 当前 Inbox/Outbox 语义，固定来源身份。
3. 比较三种布局，写出本版选择、权限/事务/恢复边界和兼容阶段；原全局键及原 Owner 合同保留。
4. 形成 27 个 NOT_RUN 场景，一次任务级语义审查，做 Markdown/JSON/来源字节检查。
5. 保存任务提交；交协调完成共享状态同步及合并窗口，未完成部分不得记为 DONE。

本机实际解释器为 CPython 3.13；可复现命令（参数使用本机资料位置，仅是证据工具，不是运行配置）：

```powershell
& 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe' `
  docs/development/evidence/S02-00/verify.py `
  --crm-root D:/CP6.CRM --platform-root D:/CP6/CP6.Platform `
  --archive-root 'D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs' `
  --out docs/development/evidence/S02-00/validation.json
git -c core.longpaths=true diff --cached --check
```

检查只读取固定 Git blob、原归档和本任务文件，验证原件 hash、JSON 必需字段/任务引用、场景均 NOT_RUN、相对链接、代码围栏及 diff 空白。Git 源文件按原 blob 字节核对；新文档按 UTF-8/LF 文本摘要记录，明确避免 Windows checkout 换行被误当原件变化。它不查询数据库、不执行归档程序、不运行 build/test。修改了这些输入才需要重核；提交 SHA 变化本身不生成“新业务验证”。

`validation.json` 是首次实际检查：126 项 PASS、27 运行场景 NOT_RUN；其报告文本摘要对应增加上述通信补记之前的版本，原记录保留。交付文本和暂存差异复核写到 `validation-delivery.json`，只重新核本任务文档及其输入，不扩展业务验证。

## Actions 与未验证范围

接手及交付准备时查询 CP6 queued/in_progress 均为空；无可归属于本任务的运行需要取消。普通工作流仅 workflow_dispatch；R2 candidate 保留 `v*.*.*` Tag push，本任务只推任务分支，不建 Tag。候选与远端 main 的工作流及相关桥配置核验记录在 `workflow-preflight.json`。未启动/取消 Actions，未修改触发或仓库设置。

以下均未执行：业务编译/测试、连接真实 PG、角色/ACL/RLS 效果、schema 隔离、实际包加载、CRM PG 采用、所有 Owner 原子性、逐租户迁移、容量/升级时长、单租户导出恢复、Main/CRM 跨库对账和生产验收。27 场景与原模块验收不因文档静态结果改变。最终任务状态和远端交付事实以协调登记为准。

## 下游消费边界

选定设计版本为 `cp6-pg-tenancy-v1`。WP02-02 负责实际 Main 映射/角色/模型/迁移/恢复证明；WP02-01/03/04 负责可信上下文、最终门和生命周期；WP02-06/07/08/09/10 分别交付 CRM 存储/身份、调用方 Inbox、Outbox 和真实 Owner 事务。S01 纯类型和独占测试库精度验证继续独立推进。既有数据迁移、旧写者切换、生产发布仍是独立任务。
