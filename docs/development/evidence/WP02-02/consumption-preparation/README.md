# WP02-02 第六轮消费准备交付

本目录交付 **PREPARATION_ONLY 候选**：真实包反例检查器、私有 API/合成运行探针、依赖清单和后续正式发包提案。WP02-02 保持 **BLOCKED / NOT_CHECKED**。本轮没有正式采用、真实 Main 宿主或数据库验收，原 17 PGT 全部 NOT_RUN。准备候选由 COORD 审查、串行集成及同步总台账/四份状态；本执行对话没有修改这些文件。

## 固定来源及范围

| 输入 | 完整身份 / 证据 |
|---|---|
| Handoff 第六轮协调分支 | `ad255c21810a101662fd3e380992a03fde71407e`，任务 WP02-02 的 `20261014T033320Z-assigned-wave6/MESSAGE.md` |
| 启动回报 | Handoff `e0b5492200a5c00e9fdcc6fcb3fd25acba16ba51`，`messages/20261014T053000Z-start-preparation/MESSAGE.md` |
| Main 产品基线 | `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8` |
| Main 准备分支基线 | `1ab2d031e3c501662ecb89a33839fac1cba55d33`，仅快进第六轮派工文档；分支 `codex/wp02-02-consumption-prep-20261013` |
| Platform 当前阅读基线 | `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`；含 WP02-07/11，不能等同旧本地包 producer |
| 旧正式 0.10.2 producer | `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`；[原 publication](inputs/original-publication.json)，SHA256 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181` |
| WP02-27 本地包 producer | `7c59256e13c217fc0e8a66b8f3f46905222fdcbc`，版本 `0.10.3-wp02.27.g7c59256e13c217fc0e8a66b8f3f46905222fdcbc.20261013134250`；[独立 consumer-lock](inputs/local-consumer-lock.json)，SHA256 `1fabaecefb24050d801a9fd894dcd908b8428f7bd486fe4d0a634ad97ffe0b01` |
| 执行环境 | Python 3.13.13；SDK 8.0.424，实际探针 runtime .NET 8.0.30；完整来源、树、环境、依赖 hash 见 [inventory-final.json](inventory-final.json) |

写入只在 `eng/wp02-02/consumption/` 和本目录。原包、producer checkout、共享合同、产品 csproj/lock/NuGet/Program/DI/EF/迁移均未修改。暂存输出在任务 artifacts 目录；未读取私钥、签名、发布、触发 Actions 或接触数据库。

## 实际验证及可复用范围

| 结果 | 原始证据 | 适用范围 |
|---|---|---|
| 检查器 44/44 PASS | [最终测试日志](runs/checker-namespace-green-20261014/tests.log)、[源码 hash](runs/checker-namespace-green-20261014/source-hashes.json) | 合成边界回归；所有“identity matched”也保持正式准入 BLOCKED；不是发布正例 |
| 实际 0.10.2 拒绝，exit 2 | [报告](runs/checker-namespace-green-20261014/old-formal/report.json) | 七包来源/归档 hash 相符；缺 ctx schema，compatibility UNKNOWN，当前信任未执行 |
| 实际本地候选拒绝，exit 2 | [报告](runs/checker-namespace-green-20261014/old-local/report.json) | 七包来源/hash/schema 可核；local file 来源与批准的正式 feed 不同，LOCAL_CANDIDATE_NOT_FORMAL，compatibility UNKNOWN |
| 32/32 schema 与各自 producer Git 原字节一致 | [比对](real-inputs/schema-source-parity.json)、[旧正式输入](real-inputs/old-formal.json)、[本地输入](real-inputs/old-local.json) | 旧包 15、新本地包 17；不归一化 BOM/换行，不把 Release schema 冒充未打包的 native schema |
| 旧 0.10.2 缺全部 20 个所需 context 类型 | [PE 元数据](runs/probe-old-20261014-a1/archive-metadata.json)、[执行报告](runs/probe-old-20261014-a1/report.json) | PEReader 只读检查；报告 PASS 仅表示检查完成，不是包准入 |
| 实际本地 DLL 公共签名清单及 10/10 合成场景 PASS | [签名/场景](runs/probe-20261014-a3/signatures-and-synthetic-runtime.json)、[完整报告](runs/probe-20261014-a3/report.json) | 四个 context DLL 身份独立核对；合成 verifier/authority，不是真实宿主；完整依赖兼容性 Unknown |
| 探针输入解析 8/8 PASS | [日志](runs/root-validation-20261014/probe-parser-tests.log)、[命令/hash](runs/root-validation-20261014/execution.json) | 人造最小 ZIP 的输入拒绝；不替代真实包验证 |
| 45 来源指纹、100 项依赖输入、51 项目及五包运行闭包 | [最终清单](inventory-final.json) | 固定 Git 对象和静态声明图，非 MSBuild 条件求值/产品 restore |
| 未应用依赖补丁检查通过 | [补丁](PROPOSED-DEPENDENCIES.patch) | `git apply --check`；未实际应用，未生成伪造的新锁 |
| 优化模式仍拒绝错误独立来源 hash | [定向结果](optimized-input-check.json) | 审查后将资料采集 assert 改显式 ValueError；未改变已采集正确数据或产品代码 |

探针构建保留 **MSB3277**：[原始 build.log](runs/probe-20261014-a3/build.log) 显示 framework DiagnosticSource 8 与 AspNetCore/Messaging 引用 10 的冲突。10 个合成场景通过只覆盖已执行路径；没有 suppress warning 或声称完整 Main 依赖兼容，后续正式采用须验证完整闭包。

最终 checker/probe 的源 hash 与相应执行记录逐项相符；成功检查复用这些原始结果，不因提交 SHA 改变重跑。资料生成脚本的 assert 加固仅改变错误输入行为，原固定来源采集结果保持有效；优化模式负例另留记录。当前没有新的密码学 Current 验签，旧 publication 或 WP02-27 历史验证时点不能当成本轮当前信任。

[任务级审查](REVIEW.md)最终 Critical/Important/Minor 均为 0；[提交前检查](final-verification.json)记录范围、原始证据字节、JSON、链接、私钥/token 特征扫描及 diff/未应用补丁检查。扫描没有命中，不将其扩大为生产安全认证。

## 八层状态

| 层 | 0.10.2 | WP02-27 本地候选 |
|---|---|---|
| source | nuspec/git producer 身份已比对 | 同左，但不是 Platform 最新集成 main 的包 |
| shape | 缺 ctx schema 和 20 类型 | schema 原字节及 context 类型/公共签名已检查 |
| artifacts | 七归档 hash 对独立原 publication 匹配 | 七归档 hash 对独立原 consumer-lock 匹配；正式 feed 来源检查拒绝 |
| trust | BLOCKED；当前信任 NOT_RUN | BLOCKED；NOT_PUBLISHED / NOT_FORMALLY_TRUSTED |
| consumer | NOT_RUN / compatibility UNKNOWN | NOT_RUN / compatibility UNKNOWN |
| host | NOT_RUN | NOT_RUN |
| runtime | Main runtime NOT_RUN | Main runtime NOT_RUN；仅 task-private synthetic 10/10 |
| business | NOT_RUN | NOT_RUN |

检查器输入是调用者提供的准备身份声明，并非经密码学认证的批准。其独立参考来自固定已审查 producer 记录；脚本没有正式成功通路。任何自洽的伪造输入也不能把主状态推进。归档 SHA512 在独立 SHA256 匹配后计算，明确不同于 NuGet lock contentHash。旧 feed 地址来自历史 publication 声明，不是本轮网络读回证明。

## 后续提案及原始失败

- [Main 依赖提案](DEPENDENCY-PROPOSAL.md)：四个直接引用、16 份受影响锁；三份历史 P10 工具锁继续独立，不冒充七包 runtime 消费。
- [正式版本登记与执行提案](FORMAL-VERSION-PROPOSAL.md)：精确 API/schema/Capabilities/workflow/scripts/tests 窗口、旧三版语义保留、one-build/保护批准/时间戳/撤销/不可变原字节查回、预计 20–50 runner 分钟与 65 分钟现有上限。`0.10.3` 只是提议，[七包版本元数据观测](feed-version-observation.json)不是预留；最终 producer SHA、hash 和正式授权仍待后续决定。
- [17 PGT 原始边界](PGT-BOUNDARY.md)：全部 NOT_RUN、空 execution_evidence；未降低真实宿主/Owner、连接池/权限、事务、迁移/恢复要求。
- [保留文件清单/hash](retained-artifacts.json)：包含初始 31 项 RED/GREEN、边界 41 项 RED/GREEN、命名空间三项 RED 和最终 44 项 GREEN；首次真实包误拒报告保留于 root-validation。两个合法 nuspec 命名空间现已准确允许，未知/混用命名空间仍拒绝。
- probe a1/a2/a3 原报告/日志/源码快照均保留：a2 扩为完整 20 类型和 loaded-DLL 身份检查；a3 将构建 warning 与完整依赖 Unknown 显式记录。旧包元数据检查使用 a2 inspector，确切 hash 记录在其报告；未将它改称 a3 执行。
- 初稿 inventory.json 的两份 P10 关联锁分类已由 inventory-final.json 修正；原稿保留，只有 final 为采用范围依据。没有二次编造正式验收或清除失败。

## 复现入口与发布边界

从仓库根运行，下列输出必须选择新的任务私有目录；真实输入需从固定 producer 记录取得相同 hash 的原归档。路径在原始证据中是执行时环境，跨机器可重建 manifest 路径，但不能更改身份 pins。

```powershell
python eng/wp02-02/consumption/inventory.py --main <Main-repo> --platform <Platform-repo> --dotnet <SDK8-dotnet> --output <new-inventory.json>
python eng/wp02-02/consumption/capture_real_inputs.py --platform <Platform-repo> --cache <NuGet-cache> --local-packages <fixed-WP02-27-seven-packages> --output <new-input-dir>
python eng/wp02-02/consumption/verify.py --input <new-input-dir>/old-formal.json --output <new-output-dir>
python eng/wp02-02/consumption/verify.py --input <new-input-dir>/old-local.json --output <another-new-output-dir>
python -m unittest discover -s eng/wp02-02/consumption/tests -v
python eng/wp02-02/consumption/probe/test_runner.py -v
python eng/wp02-02/consumption/run_probe.py --approval docs/development/evidence/WP02-02/consumption-preparation/inputs/local-consumer-lock.json --approval-sha256 1fabaecefb24050d801a9fd894dcd908b8428f7bd486fe4d0a634ad97ffe0b01 --packages <fixed-WP02-27-seven-packages> --dotnet <SDK8-dotnet> --output <new-probe-dir>
git apply --check docs/development/evidence/WP02-02/consumption-preparation/PROPOSED-DEPENDENCIES.patch
```

verify.py 的预期 exit code 永远是 2，检查报告的各层与原因；不要把脚本非零当作可以自动运行 Actions 的理由。探针只恢复框架引用，task-local props/targets/NuGet.Config 与产品隔离，不下载产品依赖。原始二进制/包/缓存没有提交，清单保留其 hash。

[工作流预检](workflow-preflight.json)覆盖候选与 protected main 的全部 13 workflow：12 份仅手动，R2 candidate 仅 `v*.*.*` tag push；Azure trigger/pr 为 none。此次只推普通任务分支，不推 tag、不创建或启动发布。查询 queued/in_progress/waiting 均为空，没有可取消的本任务运行；取消项和触发变更均为空。后续每次 push/PR 仍须重新核对当时远端。

本轮不执行正式发包；不要求现在批准一个尚无最终源码的 run。COORD 可以审查集成准备资料，再为精确 producer 登记/正式发布及 Main 真实采用安排各自窗口。交付消息包含准确候选 SHA；只有远端 main 实际包含并经 COORD 核对后，才可称准备资料已集成，仍不等于 WP02-02 DONE。
