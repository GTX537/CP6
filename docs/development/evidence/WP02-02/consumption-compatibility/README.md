# WP02-02 第七轮准备交付

执行日期：2026-10-15 UTC。本轮完成固定源码的完整依赖构建、私有宿主指定路径诊断及可审查的 [源码采用候选](SOURCE-ADOPTION-CANDIDATE.md)。范围仍为 **PREPARATION_ONLY**；WP02-02 **BLOCKED / NOT_CHECKED**，formal package trust **BLOCKED**，Main consumption 与原 17 PGT **NOT_RUN**。本报告不推进 WP02-28 或业务台账。

## 最终实际结果

最终 owner：`0615c0909a5a4ee4976ca49cd1339694`。Main 产品源 `1129953156b7775152a74ad2f51cb95ef430ee56`，Platform 源 `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`；任务分支基线为文档主线 `99fc0ce383d2d02c1404a8adbb3abe971b41d332`。

| 项目 | 结果 | 证据 |
|---|---|---|
| 固定源码与四项临时替换 | PASS | [完整 Git/导出指纹清单](runs/0615c0909a5a4ee4976ca49cd1339694/snapshot-manifest.json)；生产原件未改 |
| WebApi / DatabaseCompatibility.Probe / 私有 Probe | 各 restore/build exit 0 | [命令与状态](runs/0615c0909a5a4ee4976ca49cd1339694/state.json)；所有 build 显式 `-warnaserror:MSB3277`；前两个入口只编译 |
| 完整依赖与私有锁 | PASS，0 errors | [15 assets / 15 deps / 17 locks / 189 packages](runs/0615c0909a5a4ee4976ca49cd1339694/dependency-graph.json)；完整 delta、来源、归档与内容哈希 |
| 私有 loopback 宿主 | 31 assertions PASS | [实际 HTTP/上下文/SDK/清理](runs/0615c0909a5a4ee4976ca49cd1339694/runtime/runtime.json)；200/401/403/503；认证 authority 明确 synthetic |
| 程序集身份 | MATCHED_SPECIFIED_PATHS | [运行前 PE 身份](runs/0615c0909a5a4ee4976ca49cd1339694/pre-runtime-identities.json)与[独立策略结果](runs/0615c0909a5a4ee4976ca49cd1339694/runtime-policy.json)；12 个候选中实际加载 11 个目标程序集，包含全部 5 个 Platform 运行程序集 |
| DiagnosticSource | 私有运行使用包 10.0.8 的 DLL | [三入口图/缓存/输出/实际加载对照](runs/0615c0909a5a4ee4976ca49cd1339694/diagnostic-source-binding.json)；AssemblyVersion 10.0.0.0，未外推 Main 运行 |
| 清理 | PASS | Stop、Dispose、propagator 恢复；实际 ConnectionRefused，2044.4377 ms；没有遗留自有宿主 |
| 生产输入及 PGT | 未变 / 未执行 | [115 份输入前后原字节相同，17 PGT 仍 NOT_RUN](input-audit-after.json) |

实际 SDK 本地记录为 21 Log、7 Trace、7 Metric，flush 成功、无 dropped record、无外部 exporter。源合同的 Completeness=Incomplete 原样保留；这不是遥测全集或 SLO 验收。图检查器只证明 restore/build 图，所以自身 compatibility 保持 UNKNOWN；独立运行策略仅给出指定路径匹配，未声明完整业务兼容。

## 测试与审查

29 项工具测试按相关修改分批执行并复用未受影响的成功结果，没有因 SHA 或文档变化重复全量运行：

| 测试组 | 数量 | 本轮最终适用证据 |
|---|---:|---|
| Git 快照、四项 overlay、路径/不可改输入、独立构建根 | 11 | [GREEN-msbuild-normal-cache.log](runs/251a47e365f24dd1aef98e2ef88d491b/testreports/GREEN-msbuild-normal-cache.log)；同目录保留初始及各定向 RED/GREEN |
| Runtime policy、精确 owner、junction、原报告保护 | 6 | [27 项阶段测试中的该 6 项](runs/d817efb6b8154c22b6dd4ac1f008a0be/all-tools-green.log)及[输入哈希/命令](runs/d817efb6b8154c22b6dd4ac1f008a0be/execution.json)；含 16 类合成报告反例 |
| Graph 来源/锁/包哈希、长路径、junction、部分失败 | 12 | [最终 graph GREEN](runs/df880f32ce4e477f9cadcfde3c3d5607/testreports/graph-hash-green.log)及[执行元数据](runs/df880f32ce4e477f9cadcfde3c3d5607/testreports/graph-hash-green.json) |

另执行 [8 类真实报告副本篡改拒绝](runs/0615c0909a5a4ee4976ca49cd1339694/negative-report-mutations.json)和[已有输出真实 CLI 拒绝](runs/4b9a455498bf44108ccb1e79a8338156/existing-output-rejected.json)。前者不冒充物理删 DLL 后的加载器故障；后者 exit 2，报告 hash 前后相同且未启动宿主。

按执行包使用 subagent-driven-development，进行一次任务级集中审查，随后只复查修复与实际证据。审查修复了策略输出 owner 边界、Probe 覆盖已有报告、graph 在 resolve 前拒绝 junction，并复核长路径、清理预算及两组哈希语义。结论见 [REVIEW.md](REVIEW.md)。

## 失败保留与证据来源

| owner | 真实结果及处理 |
|---|---|
| `65a0ae32a96d4f2f92d92f623849b34d` | 首次 Main restore 0/build 1，普通超长路径使已存在 DLL 无法读取，后续扫描 WinError 3；未继续的根不记通过。[原日志](runs/65a0ae32a96d4f2f92d92f623849b34d/logs/) |
| `84981a57453d4841adb6cb0e02a52875` | 两 Main restore 因 Scriban 与扩展 NuGet 路径不兼容失败；私有构建成功，运行因 2 秒清理预算失败。原报告及错误 graph 保留，[修正后的部分图](runs/84981a57453d4841adb6cb0e02a52875/graph-v2.json)仍为 UNKNOWN |
| `0615c0909a5a4ee4976ca49cd1339694` | 新 owner 使用普通短 NuGet 路径、Python 扩展 IO 和有界 10 秒拒绝连接检查；上表实际结果 |

[DIAGNOSIS.md](DIAGNOSIS.md)记录原裸 DLL MSB3277、路径与清理失败的责任及修正依据。图助手最初混同内容哈希与原始归档哈希，实际反例与错误报告同样保留；修正后分别严格核验两组哈希，未放宽错误拒绝。早期未另存的 graph console 测试不重建成原始日志，见[来源说明](runs/810f99a3dcf04e1fb806c513ca3b754c/testreports/validation-provenance.md)。

`3e2c36fd94784963800fe718f719b191` 是尚未执行 .NET 的早期投影，因 Platform 资源导出范围待补而被后续 owner 取代；其本地原始目录保留，未当构建证据。各实际失败 owner、cache、bin、obj、原锁与私有锁留在授权 artifacts 根供审计；仓库只归档无秘密的日志、JSON 清单和报告，不提交包缓存、二进制或源码副本。

归档逐文件原字节 hash/来源见 [archive-initial.json](archive-initial.json)、[archive-delegated.json](archive-delegated.json)、[archive-final-run.json](archive-final-run.json)。测试通过不抹去任何真实失败。WebApi 最终仍有 5 warnings，包括现有 NU1903；没有改变依赖或伪称生产漏洞门禁通过。

## 交付边界

没有执行 Main Program、数据库探针、迁移、生产部署、签名、包发布、tag push 或 GitHub Actions；没有取消任何运行、改变 workflow 触发或分支保护。[工作流预检](workflow-preflight.json)检查 Main 全部 13 工作流及依赖、protected main、完整候选文件列表和 Platform RFC3161 两条窄路径边界。运行只使用自有随机 loopback 和 owner 目录。

本执行对话只交付任务分支候选与固定 SHA Handoff，COORD 串行集成并维护路线图及四份状态。远端 main 未包含本候选前，不宣称主任务完成或清理 worktree。后续正式采用、当前包信任、真实认证/Owner、PostgreSQL 和原 17 PGT 的准入详见 [SOURCE-ADOPTION-CANDIDATE.md](SOURCE-ADOPTION-CANDIDATE.md)。
