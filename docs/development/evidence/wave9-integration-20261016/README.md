# 第九轮局部交付集成

2026-10-16，COORD 已按 A→B→D→C 串行正常合并；本报告汇总合并事实和复用范围，详细实现与原始失败保留在各任务的单份报告。

| 对话/任务 | PR | 含共享状态的任务提交 | 当次核对远端 main | 实现与验证 |
|---|---|---|---|---|
| A / WP01-01 | [PR #225](https://github.com/GTX537/CP6/pull/225) | `01a42c4d86df4dd7434c853dd5dd3f95123210ef` | `ceb02a60ab4628e4ef41fa9136001fdb03babfc7` | [原报告](../WP01-01/trust-stages/DELIVERY.md) |
| B / WP02-02 | [PR #227](https://github.com/GTX537/CP6/pull/227) | `6578314b66f0949d46dad2bfbccb658df974c3bc` | `7e33b610cef87266140dfe1f1b2f52e1847d18de` | [原报告](../WP02-02/identity-priority-tenant-isolation/DELIVERY.md) |
| D / WP02-16 | [PR #226](https://github.com/GTX537/CP6/pull/226) | `3b44bf09f18faa1db280a6ad5279a2a3171a66d3` | `28839d413c02fafcfa98ca9b26eb342aa7a61776` | [原报告](../WP02-16/codegen-preview/DELIVERY.md) |
| C / WP02-28 | [PR #228](https://github.com/GTX537/CP6/pull/228) | `0353366f7486f1bb88cb7b341ca65ac8ad303eaf` | `86d8478db18747cfa95ad57bb6f1c2b06bb22d19` | [原报告](../WP02-28/DELIVERY.md) |

四份候选和 Handoff 固定提交、实际 START、修改范围及原测试/审查适用范围已核对；候选产品源码在状态同步时未改。每次合并后核对任务提交包含性及该次 PR 的准确文件，未管理员绕过、强推或删除分支。各任务的 `wave9_delivery` 已记 MERGED，局部窗口 CLOSED，完整任务与业务状态单独保留。

## 本轮得到的能力

- WP01-01：采纳只依赖 authority；execution 配置/签名错误只拒绝相应运行。用户已采纳依据保持，未配置的真实独立信任仍拒绝自动收口。
- WP02-02：Identity 四表共享 schema 隔离、受保护目录及 EF 适配与 ERP 共用；保留全局消息唯一域和原 RowVersion，可在同一物理连接/事务顺序提交或回滚。未把旧 writer/dispatcher 切换到新路径。
- WP02-16：有版本的即时安全预览 API 与页面可用，固定模板、追溯摘要、文本转义和权限检查；旧字典响应兼容，预览不持久化、不写文件、不采番。
- WP02-28：真实 Main 的受保护只读身份诊断及缺证/错配回退拒绝。原宿主验证有三份程序集匹配、两份未加载，整体 Unknown；没有正向回退兼容或正式 OBS 资格证明。

## 验证与复用

复用各固定候选的相关测试、一次集中 SPEC/QUALITY 及已修复部分复查：A 的 27 adapter + 8 manager 成功和旧夹具修正单项成功；B 的真实 PG18.6 Identity 5/5 与 ERP 定向 4/4；D 的 CodeGen 25 项和合法名称修正后 20 项、类型与页面夹具；C 的单测36项及尾斜杠修正后真实宿主验证。准确执行时间、失败和未变输入均见原报告，不将复用写为新运行，也不把重叠用例相加为交付指标。

COORD 新执行台账生成/结构检查、必要链接和集成差异检查。三份 C# 候选同属 Core/WebApi/Tests 编译图，在最终组合上仅补一次 Release 编译以核对组合是否可编译；未重跑数据库或业务测试。命令 `dotnet build CP6.Tests/CP6.Tests.csproj --no-restore -c Release -m:1 -p:UseSharedCompilation=false -warnaserror:MSB3277 -v:minimal`，构建源 HEAD `65ab386cf832db338e2c1885300fb204f2cd29ea`，退出码 0；日志在本地 `C:/Users/tt/CP6-planning-reports/20261016-wave9-integration/integration-build.log`。现有 NU1902/NU1903 和未改源码警告保留；编译通过不等于发布扫描或真实运行通过。状态提交/SHA 改变未引发相同测试重跑。

真实 manager 仍 `inventory_coverage_valid=true`、`basis_adoption_valid=false`、两运行目标 false，原因 `INDEPENDENT_VERIFIER_UNCONFIGURED`。这是真实机器信任尚未配置，不是再次要求用户采纳。只读/生成台账检查通过并不改变业务验收。

## 剩余范围与资源

WP01-01、WP02-16 完整任务保持 PLANNED/NOT_CHECKED；WP02-02、WP02-28 保持 IN_PROGRESS/CONFIRMED。没有新增完整 DONE：24/375，业务0/125，247修订场景NOT_RUN，41历史引用保持。WP02-10等原依赖、无法找回的历史缺件不变。

后续仍需独立 authority 实际信任配置、真实宿主/当前授权/Owner 与消息 writer 接入、其他上下文和完整 PostgreSQL 生命周期、编号/保存事务，以及两制品准确 schema/driver 下的正向兼容。各部分按对应任务开展，本报告未开放新窗口或新增发布权限。

执行者自有 PG/HTTP/Vite 已按原报告停止；COORD 没有重启它们。保留所有 worktree、失败记录、数据及既有未跟踪文件（包括 C 的 Python 缓存），未清理他人内容。任务分支无 Actions，工作流/Azure 内容与已检查输入一致且分支/PR 不命中自动过滤；无触发/取消/配置变更、正式发包、既有业务迁库或部署。
