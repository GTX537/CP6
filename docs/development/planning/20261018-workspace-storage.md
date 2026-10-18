# CP6 工作目录复用与存储回收计划

> 执行方式：总控按本文串行推进；采用本仓精简验证，不新增审查代理或逐步骤产品测试。用户于2026-10-18要求继续四对话开发并控制工作树增长。本次完成盘点、规则与目录复用，批量删除仅列候选，未执行。

**目标：** 保持每任务独立分支和并行写入隔离，将活动目录固定下来，避免每次派工、审查、登记和收尾再复制整仓。

**方式：** 分支保存任务历史，worktree是可轮换的执行目录。先停增量，再回收已结束的目录；证据、未合并候选和用户原件独立保护。数据库仍只按已批准的PostgreSQL窗口运行。

## 实测与优先级

2026-10-18只读扫描：CP6登记107个worktree（含根），Handoff46个。已统计CP6目录156.24 GiB、Handoff10.78 GiB；CP6根自身47.58 GiB已包含在156.24中，不重复相加。两仓工作目录下docs类别86.28 GiB、bin15.64、obj2.62、node_modules5.98、Git目录7.97。另计 `D:/CP6-local-test` 10.15 GiB、C盘NuGet cache3.31、npm cache3.48。扫描时D盘约55 GiB可用。

这是逐路径逻辑文件长度，嵌套登记工作树分开计数，不遍历reparse目录；硬链接可能逐路径计数，231次读取失败未计入，不是磁盘分配字节或保证能回收的容量。私有完整明细在 `C:/Users/tt/CP6-planning-reports/20261017-foundation/storage-inventory-20261018.json`。多数新建全检出约1.04 GiB，其中docs约0.84 GiB；重复文档检出比下载缓存更应优先控制。不要为省空间先清空NuGet/npm缓存而导致每轮重新下载。

## 固定活动目录安排

| 用途 | 上限/使用方式 | 当前落实 |
|---|---|---|
| Main根与集成 | 1个主工作区；保留用户7项未跟踪资料 | `D:/CP6`，不在此日常开发 |
| COORD | 1个复用目录；每项协调任务独立分支 | 复用 `D:/CP6-worktrees/wave17-dispatch-20261018`，本次已切换新分支，不新增目录 |
| A/B/C/D实现 | 最多4个活动产品目录，每目录同时仅一个任务 | 本轮B/C/D沿已派目录；A只读审查不另建CP6目录。运行中的目录不改名、不搬动 |
| 只读审查 | 默认不增产品目录 | `git show`/`git diff`读取固定提交和原证据；必要执行验证才占用已释放的槽位 |
| Handoff | 后续目标COORD+原四对话共5个资料目录 | 本轮沿原实施/复查资料分支，收尾后逐个合并为固定席位；同时写多分支先串行，不能互相覆盖 |
| 包缓存 | 同仓/可信源统一使用既有NuGet与npm下载缓存 | 不迁移全局缓存，不跨任务共享可写node_modules、bin/obj |
| 验证与证据 | 一个任务一个私有证据根 | 减少重复源码/日志副本；有效原报告与日志直接引用 |

当前107/46是待审历史存量，上限约束后续新增，不表示立即删除或强制腾空其他工作区。一个新任务仍从最新确认main创建新分支；只复用已经完成、释放且由同一Owner独占的物理目录，不复用旧任务分支。

## 立即执行：停止继续增长

- [x] 完成只读容量盘点；识别重复docs是主要占用，记录逻辑容量限制。
- [x] 核对原COORD分支c5a69432已经在远端main，工作树干净后，在同一目录从main2f20834a创建 `codex/workspace-storage-policy-20261018`；原任务提交和分支保留。
- [x] 接收A/B/C/D真实START/PROGRESS，保持本轮工作树/端口/写集不变；此计划不打断当前实现或扩大测试。
- [ ] 后续COORD文档任务均复用此目录；每次先检查工作树、忽略文件、未提交内容及未结束进程，再 `git fetch origin main`、从该固定main `git switch -c codex/<task> origin/main`。不执行reset/clean来腾空目录。
- [ ] 已合并窗口在同一次收尾检查目录是否可释放，避免再为“收件/准入/修正登记/收尾”分别新建整仓。

## 首批可回收候选：约5.20 GiB，尚未删除

以下5处均已核对HEAD是origin/main `2f20834af3e0192cdb0d0fda404f0fc398be592d` 的祖先，tracked/untracked状态为空，`git ls-files --others --ignored --exclude-standard`也为空；不是活动A/B/C/D目录。完整清单在 `C:/Users/tt/CP6-planning-reports/20261017-foundation/storage-reclaim-candidates-20261018.json`。

| 绝对路径 | 已合并HEAD | 逻辑GiB |
|---|---|---|
| `D:/CP6-worktrees/bug132-closeout-20261018` | `34fedd8c` | 1.04 |
| `D:/CP6-worktrees/bug132-coord-takeover-20261018` | `be5e04b7` | 1.04 |
| `D:/CP6-worktrees/wave16-receipt-20261018` | `398acc0d` | 1.04 |
| `D:/CP6-worktrees/wave16-review-fix-20261018` | `d2a0a6db` | 1.04 |
| `D:/CP6-worktrees/wave15-receipt-20261017` | `95870bcf` | 1.04 |

- [ ] 实际回收前重新核对Owner结束、准确分支/提交、路径、无运行进程/打开服务、干净状态和忽略内容；任何不一致只跳过该项，不强制清理。
- [ ] 以原生Git `worktree remove` 逐项移除已确认目录，不使用force、递归通配删除或跨shell拼接删除。绝对目标必须是清单中的 `D:/CP6-worktrees/` 直接子目录；保留本地/远端分支和Git历史。
- [ ] 回收后核对worktree登记、远端提交包含性及卷剩余空间，报告实际释放量。尚未完成该步骤时不能写成已节省5.2 GiB。

这里的删除是后续独立执行批次；本轮用户请求是规划，尚未删除上述目录。其余历史目录需要逐项检查，不能仅按日期或“目录名像旧任务”删除。

## 下一批：稀疏检出与构建残留

- [ ] 先在已释放的COORD资料席位试点sparse-checkout，只检出本任务需要的AGENTS、docs/development、project-memory及对应报告。校验台账工具所需来源；原始归档通过已登记只读根解析，不能因为稀疏检出而报缺文件或降低验收。
- [ ] Handoff席位只检出本轮入口、自有tasks和需要读取的waves；其他固定提交用git show读。新资料分支优先复用该聊天释放的目录，保留不同任务的消息路径。
- [ ] 代码席位暂不盲目删项目/目录。确认csproj引用、MSBuild imports、生成输入与测试读取依赖后再决定是否稀疏化；普通编译范围仍只限受影响项目和必要依赖。
- [ ] 历史bin/obj/node_modules只按已结束任务和重建能力回收；若报告/收据绑定其中实际发布字节，先按下节保存。各活动任务输出保持隔离，禁止多个进程共享写同一obj/bin，也不把另一个分支node_modules的版本假定为锁文件版本。
- [ ] npm沿原package-lock执行，未获独立依赖变更窗口不迁pnpm或改锁。共享npm缓存节省下载，不等于多棵node_modules自动去重。NuGet优先复用现有已验证cache，不以全局clear作为日常收尾。

稀疏检出先作为后续小范围试点，本轮未执行；不直接改变正在运行的B/C/D/Handoff目录。

## 证据、数据库与原件保留

所有原始测试日志/TRX、真实失败记录、最终DELIVERY/REVIEW继续保留；报告已在Git时不再复制一整套证据目录。固定收据、发布文件哈希及其依赖的实际发布物不可当普通bin任意删除。确需压缩归档时保存一份可恢复归档，核归档及恢复后原字节哈希、修正证据定位，再考虑移除重复副本；不先删后补。

原始设计/Excel、根工作区7项未跟踪文件、未合并候选、已登记回退/发布物、用户选择的既有PostgreSQL副本及任何业务数据库不进入一般worktree清理。临时PG只有自有owner、准确data_directory/PID及停止证据匹配后才另行评估归档；不按端口或目录年龄删除数据库。

分支/提交保存代码历史，worktree目录本身不是历史归档。Git元数据目录目前约8 GiB，先回收大量重复检出；不通过重写共享历史、强推、删除远端分支或激进gc解决本次占用。

## 预算与检查时点

目标为Main根1+COORD1+执行最多4；Handoff逐步收敛到5个资料席位。D盘低于30 GiB时优先回收已确认的历史目录，再安排新的大publish/数据库fixture；这是本机调度提醒，不放宽测试或生产门禁。30 GiB是本项目规划阈值，不是存储设备性能结论。

在派工和任务收尾时更新目录Owner/占用、未合并候选与可回收清单即可，不按每个小提交重复全盘扫描。技术依据：[Git worktree](https://git-scm.com/docs/git-worktree)、[sparse-checkout](https://git-scm.com/docs/git-sparse-checkout)、[NuGet缓存](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)、[npm cache](https://docs.npmjs.com/cli/v11/commands/npm-cache/)。本轮只做文档/台账校验，无Actions、产品回归、目录删除或数据库操作。
