# Space Draft 摘要元数据报告

日期：2026-08-15
任务分支：`codex/space-draft-summary`

## 结论

Space Studio 项目入口现可直接展示活动 Draft 的来源、创建者、创建时间、更新时间、状态和当前未关闭的 Blocking 问题数，不再要求用户根据 `BasedOnVersionId` 或问题列表自行推断。

现有两条版本创建权威的来源语义固定为：无基线的 Draft 返回 `Blank`，从当前 Published 初始化的 Draft 返回 `PublishedVersion`。创建者返回持久化的 Actor ID；历史或系统数据没有创建者时明确显示“系统/历史数据”，不伪造人员姓名。

## 合同与查询边界

- `SpaceVersionDto` 新增必填 `creationSource`、`createdAtUtc`、`updatedAtUtc` 和 `openBlockingCount`；`createdBy` 保持可空，以兼容历史和系统数据。
- `updatedAtUtc` 取版本最后修改时间；没有修改记录时回退到创建时间。
- Blocking 摘要只统计该 Version 下 `Open + Blocking` 的问题，不包含已解决 Blocking、Warning 或 Info。
- 列表按当前分页可见 Version 一次分组聚合，详情按单 Version 聚合；查询继续受既有 Tenant Query Filter 和 Design V1 Site 读权限保护。
- 本纵切没有数据库 Schema 或 Migration 变化；OpenAPI、C# 与 TypeScript SDK 已同步。

## 工作台行为

- 活动 Draft 摘要展示来源、稳定创建者身份、创建/更新时间和 Blocking 数量。
- Blocking 大于零时使用阻断语义色和加粗，同时保留文本数值，不只依赖颜色表达。
- 日期按用户浏览器区域格式显示；损坏或历史缺失值明确显示“未知”。

## 自动化证据

- Space Integration 真实 SQL Server LocalDB：444/444、0 skipped；新增用例同时验证列表与详情中的来源、创建者、审计时间，以及只计 Open Blocking。
- Space Unit：537/537。
- CP6.Tests：2,926 passed、19 个既有外部环境门禁 skipped、0 failed；OpenAPI 专项验证四个新增非空字段。
- Web：167 个测试文件、856/856；Vue TypeScript 和 production build 通过。
- OpenAPI/C#/TypeScript SDK 漂移检查通过；EF `has-pending-model-changes` clean。
- 完整 `CP6.slnx` Release 以非增量、单线程、禁用节点复用和共享编译方式通过：0 warning / 0 error。

## 未关闭范围

- 当前版本实体只存在 `Blank` 与 `PublishedVersion` 两种创建权威。System/Tenant Template 目前写入已经存在的 Draft，不创建新 Version；未来四模式创建向导落地时必须持久化并扩展对应来源，不能从内容反推。
- 创建者当前提供稳定 Actor ID，不冒充人员显示名；若产品需要姓名，应接入受权限和历史快照约束的身份解析合同。
- Tenant 私有模板、Blank/Published/System/Tenant 四模式统一向导、独立 QA 接受、真实 Provider、黄金 CAD、双仓 Pilot 与五方签字仍未关闭。

因此本纵切关闭当前已支持 Draft 创建路径的 LM-FR-002 摘要缺口，但 LM-FR-001/WP1 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。
