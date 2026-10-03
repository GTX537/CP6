# WP6：完整应用、双库回归与本地恢复

父任务：[DB-COMPAT-01 / #134](https://github.com/GTX537/CP6/issues/134)。当前为实施与验证中，未完成交付。WP1–WP5 已进入远端 main；WP5 的正常合并、双库冒烟和 13 个自有库清理见 [交付原件](wp5-native/post-merge/manifest.json)。

## 实现范围

同一份 CP6.WebApi 发布产物按 `Database:Provider` 选择 SQL Server 或 PostgreSQL。移除 WP1–WP5 的 PostgreSQL 完整应用临时 guard，保留 provider 校验。两种数据库共用业务接口与初始化入口，四个 Context 的迁移所有者仍由既有 profile 决定。

本地入口为 [`Test-Cp6DatabaseCompatibility.ps1`](../../../scripts/Test-Cp6DatabaseCompatibility.ps1)，支持完整、矩阵或应用分项。固定场景清单要求精确名称、零跳过、真实进程退出与实际数据库身份；分项通过不称为完整验收。测试夹具共享 owner 校验，不自行清理正式 runner 的数据库。运维配置、调用方式与限制见 [运行手册](../../devops/DATABASE-COMPATIBILITY.md)。

## 已执行结果及待办

以下为原始分项运行，不能合称一次正式 Full 执行。最终公共证据归档和远端交付尚待完成。

| 范围 | 当前实际结果 |
| --- | --- |
| Runtime guard | 旧发布物真实 PG 启动失败被记录；修复后的同一份 API 在两库启动，相关定向测试 7/7 |
| 共享测试库契约 | 初始纯契约 RED 45/45 失败，实现后 45/45 通过；审查发现的 partial-config 选择漏口修复后 Core 58/58、Space 4/4、ERP 4/4，均零跳过（包含原 45 复测，不称为 66 个新增业务用例） |
| 首次/重复初始化 | 两库各执行两次实际 db-init，exit 0，迁移齐备；同一数据库的自定义种子保持 |
| 应用与重启 | 两库真实健康/版本、管理员登录、匿名 401、普通用户改密后权限 403、后台通知持久化与重启后分发通过 |
| 原生备份恢复 | SQL BACKUP/VERIFYONLY/RESTORE 与 PG pg_dump/pg_restore 实际执行；恢复前后所有用户表、行摘要及序列/identity 对账通过；旧数据库 Data Protection 分页游标可在恢复应用中继续使用 |
| 历史 SQL 升级 | 固定旧提交 993e844 的真实 API 安装 136 项迁移并保留种子，再以前向迁移到 140；2/2 通过 |
| PG 矩阵 | 前 11 个入口通过，ERP 95 项中 93 通过、2 并发测试失败；原失败保持 |
| ERP 并发诊断 | 实际 PostgreSQL 40001 / SerializationFailure；独立 [BUG153](https://github.com/GTX537/CP6/issues/153) 修正测试投递流程，双库原 ERP 各 95/95；[PR154](https://github.com/GTX537/CP6/pull/154) 已合入主线 59e09f6e，两库原两项合并后各 2/2；两个自有库已清理且 Issue 已关闭，见 [交付原件](../2026-10-03-bug-153-erp-concurrency-retry/native/post-merge/manifest.json) |
| 正式 PG 应用入口 | Application 分项 17 个入口全部通过，包含真实 SignalR 9 项、恢复后的待发通知/幂等重放、前后产物核验、源输入不变检查；两个正式临时库已普通清理并确认不存在。FullAcceptance=false，不代称完整矩阵 |
| 正式 SQL 应用入口 | Application 分项 17 个入口全部通过，包含真实 SignalR、恢复后的待发通知/幂等重放及实际输入、产物核验；两个正式临时库已普通清理并确认不存在。与 PG 使用相同发布文件，FullAcceptance=false |
| 最终业务矩阵的首轮失败 | PG 七个相关项目构建通过，25 个入口通过后在 Space 设计/发布组失败（35 项中 34P/1F），实际原生 40001；独立 [BUG155](https://github.com/GTX537/CP6/issues/155) 已由 PR156 交付并关闭，两库原 35＋新六项各 41/41、合并后原并发/协调并发各 2/2 及两个自有库清理完成，见[交付原件](../2026-10-03-bug-155-space-validation-retry/native/post-merge/manifest.json)。原失败保持，不合称单次 Full |
| 接入修复后的最终验收 | 接入最新主线 2e1f90c6，设计/发布必需清单由 35 扩展至 41，其他 61 个入口保持。生产依赖改变，最终两库 Matrix 与 Application 将使用新构建和目录；修复前两库 17/17 和旧运行物仅保留历史适用范围 |

首次 PG 生命周期执行明确记录本机服务未启用 SSL；仅本次 loopback 测试显式采用 Disable，没有修改服务、全局连接或生产模板的 TLS 设置。SQL 初次原生命令参数失败也保留，修正参数后才完成实际运行。

## 审查与证据边界

以整个 WP6 为一次集中审查，分工覆盖共享夹具与结果判定、应用/进程/恢复快照、生命周期/入口及正式编排。四项边界已定向修正：partial-config 选择、SignalR 代理直连、每次应用启动前产物核对和测试运行目录依赖清单。相应契约 58+4+4、应用产物离线 RED→GREEN 16/16、Entries 510 项离线检查通过；Results 455、Lifecycle 42、runner 147 项离线验证按各自来源保留，不计作原生数据库业务用例。源码静态审查无剩余实质阻断，最终实际矩阵和交付仍待完成。

发布物在本地验证中共 261 个文件，两库使用相同字节。构建基于 cbbb7fc8 加当时工作树的已记录输入，不能称为该提交的洁净构建；合并后按源码、依赖、环境和产物实际相同范围复用。

正式 PG Application 完成后，WP6 接入 PR154 的主线 `59e09f6e68a36734c64144abfcefc81b7c9c8d9d`。SQL Application 复用先前实际构建：两个分项的 2009 个相关源码/配置输入哈希完全一致，261 个 API 发布文件也一致；Source HEAD 的变化单独记录，不冒称重建产物。

全部验证在本机隔离端口与本任务独占数据库执行。没有触发 Actions、修改分支保护、生成生产候选或部署既有环境。私有凭据、JWT、Cookie、完整备份和业务状态文件不进入公共证据。用户既有 SQL Server 数据搬迁不在本目标内；现有 R2/GHCR 生产候选与审批规则仍是发布权威。
