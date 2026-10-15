# WP02-02 第七轮任务级审查

审查对话：`/root/wave7_review`；完成时间：2026-10-15 01:26 UTC。结论：**本次 PREPARATION_ONLY 候选未发现未闭合的实质问题，可进入主对话最终静态检查与交付窗口。** 该结论不批准生产源码采用、正式包信任或 Main 业务采用；WP02-02 继续 BLOCKED / NOT_CHECKED。

## 范围与方法

按 AGENTS 与第七轮执行包进行一次任务级集中审查，阅读生成器、固定输入合同、四项 overlay、图检查器、运行前 PE 捕获、私有 Kestrel 探针、运行策略、相关测试和交付文档。之后只增量复核所发现问题的修复、真实失败带来的定向修改及最终证据，没有重新执行构建或重复已成功测试。

相对文档主线 `99fc0ce383d2d02c1404a8adbb3abe971b41d332`，审查时 Git 状态仅有 `eng/wp02-02/consumption-compatibility/` 与本证据目录新增文件，无已跟踪生产文件改动。Main 产品输入仍为 `1129953156b7775152a74ad2f51cb95ef430ee56`，Platform 为 `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`。Git 对象原字节导出、精确四项临时引用替换、独立 owner 路径、原 NuGet 配置与锁保留，以及禁止 Main/数据库执行的边界均已检查。

## 发现与闭合

| 发现 | 原影响 | 修复与核验 |
|---|---|---|
| P1：runtime_policy 输出根未限制为精确任务 owner，resolve 过早消除原路径链接 | 可接受任务外同名 32hex 根或链接路径 | CLI 和 validate 复用 exact owner_root；原路径先 no_links，输出独占创建；任务外根、junction、已有报告保护反例通过 |
| P2：私有 Probe 可覆盖普通已有 runtime.json | 重复直接调用二进制会覆盖原失败证据 | CLI 拒绝已有文件/目录，最终 FileMode.CreateNew；真实重复 CLI exit 2，原报告 hash 不变且未启动宿主 |
| P2：graph 长路径规范化在链接检查前 resolve | owner 内 alias junction 被消去后可通过包含性判断 | canonical 对原路径先 no_links；普通与扩展路径的 contained/restore-cache 四个子反例先失败后通过 |

运行中发现的 Windows 长路径、Scriban 对扩展 NuGet 路径不兼容、清理检查 2 秒预算及 NuGet 两组哈希语义问题也已增量复核。最终仅给 NuGet/MSBuild 使用普通 owner/n，Python IO 保留扩展路径；没有共享缓存或信任降级。清理检查增加有界预算，但仍只有真实 ConnectionRefused 才通过。包内容哈希与完整归档 SHA-512 分组严格比较，篡改归档及 cache hash 的反例仍拒绝。

测试来源与数量沿用 [交付报告](README.md)：11 项快照/runner、6 项运行策略（含 16 类合成报告变更）、12 项 graph 测试，合计 29 项。审查读取已有真实日志和执行元数据，没有将审查阅读改称重新执行测试。

## 最终实际证据

最终 owner 为 `0615c0909a5a4ee4976ca49cd1339694`。已读取其原始及归档的 [命令状态](runs/0615c0909a5a4ee4976ca49cd1339694/state.json)、[依赖图](runs/0615c0909a5a4ee4976ca49cd1339694/dependency-graph.json)、[运行报告](runs/0615c0909a5a4ee4976ca49cd1339694/runtime/runtime.json)和[独立策略结果](runs/0615c0909a5a4ee4976ca49cd1339694/runtime-policy.json)。

- 三个入口 restore/build 全部 exit 0，每次 build 显式 MSB3277 错误门禁；WebApi 与数据库探针只编译。
- 图为 PASS：15 assets、15 deps、17 私有锁、189 packages、0 errors；图自身 compatibility=UNKNOWN 与指定运行路径结论分开记录。
- 私有宿主 31 assertions 通过；HTTP 200/401/403/503、Background/Message 上下文恢复与拒绝、21 Log/7 Trace/7 Metric 均有实际证据。authority 明确 synthetic，Completeness=Incomplete 原样保留。
- 运行前 12 个目标 DLL 候选与实际加载的 11 个目标程序集逐一对照，含全部五个 Platform 运行程序集；策略 MATCHED_SPECIFIED_PATHS、无 findings。独立只读核对了预捕获及运行报告 SHA-256 与策略输入绑定。
- Stop/Dispose/propagator 恢复成功，端口实际 ConnectionRefused，耗时 2044.4377 ms。未把超时或取消视为端口关闭。
- 三入口 assets 均选择 DiagnosticSource 包 10.0.8；私有实际加载 hash `5de9dc5c14e36e7798b69805b57b6d2c4136a6c2266c6db2c2351eb8eda792f6` 与该包 net8.0 DLL 相同。包 10.0.0 的不同 DLL hash 亦作只读对照；不能凭 AssemblyVersion 10.0.0.0 混同两包。
- [输入前后对照](input-audit-after.json)记录 115 份生产输入未变，原 17 PGT 全为 NOT_RUN、execution_evidence 为空。

两次早期实际失败及原错误 graph 均保留；后续成功未覆盖旧报告。[8 类真实报告副本变更](runs/0615c0909a5a4ee4976ca49cd1339694/negative-report-mutations.json)全部拒绝，文档准确说明这不是物理删 DLL 的加载器故障实验。

## 适用范围与未验证内容

[源码采用候选](SOURCE-ADOPTION-CANDIDATE.md)已明确固定身份、消费者、准确四项临时替换、复现顺序、依赖/运行证据、撤销与回退及后续正式采用所需窗口。该候选没有把私有宿主运行外推为 Main 宿主运行，也没有把源码输出的 AssemblyVersion 0.8.0.0 改称正式包版本。

未验证 Main Program、真实宿主认证与当前 authority、业务 Owner/租户授权、PostgreSQL/迁移、原 17 PGT、生产遥测全集/SLO、当前正式签名信任、包发布及部署。现有 NU1903 等警告保留，不能以本诊断代替生产漏洞门禁。本审查没有运行 Actions、改变工作流/分支保护或执行远端合并。

主对话仍需完成最终归档字节/JSON/链接/diff、敏感内容及 push 前工作流检查，并按 Handoff/COORD 窗口交付固定提交；这些检查不在本记录中冒称已经完成。

核心代码审查指纹（SHA-256）：

| 文件 | SHA-256 |
|---|---|
| run.py | `e7feb185b813c2ebd6a63b89283774e3676bb2253183accb9dbeee2df49b5afa` |
| snapshot.py | `2e956e42b76567117f58615667d80684f11d13f7787afce35dd26978e217f2d8` |
| runtime_policy.py | `a7d8d2e2959e4c52412dc8355143eab4201cb240ed1c88d4bc02732d1081804c` |
| inspect_graph.py | `30e27d1299602bcd4efea8df458f40a861fd2c7d142c69926ecb6dadfc0c641d` |
| probe/Program.cs | `fb6e3f31337191e5220d7180bdc1bfe34cd92f14478a2ffe346eea2a275d5d65` |
