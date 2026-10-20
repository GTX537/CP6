# CP6 固定工作区登记与入口

负责人 COORD；当前任务 WGOV-20261020。[统一政策](POLICY.md)为四仓规则来源，[registry.json](registry.json)为固定配置。实时占用以status为准，不把本文静态描述当作抢占许可。上轮历史清理已结束，见[报告](BACKLOG-CLOSEOUT-20261020.md)，本轮不重复清理。

| 用途 | 路径 | Owner |
|---|---|---|
| CP6主目录 | `D:/CP6` | COORD |
| CRM主目录 | `D:/CP6.CRM` | COORD |
| Platform主目录 | `D:/CP6/CP6.Platform` | COORD |
| Handoff主目录 | `D:/CP6.AI-Handoff` | COORD |
| DEV-1，总控串行开发/治理 | `D:/CP6-worktrees/wave17-dispatch-20261018` | COORD |
| DEV-2，跨仓串行开发/资料 | `D:/CP6-worktrees/dev-2`，绑定以status为准；本轮最终Handoff | COORD |
| INTEGRATION-1，集成验证 | `D:/CP6-worktrees/s02-00-20261012` | COORD |

四仓**合计最多2开发+1集成**；DEV-3取消。DEV-2由原Handoff席位先安全释放再替换，按CRM→Platform→Handoff串行使用，同一时刻不多建一份。固定Owner负责登记回收，实际写入任务必须start领取。满额排队，不得新增clone/源码副本绕过。

## 开工、状态、收尾

```powershell
# 每次开工先看实时占用、Git目录、改动和进程
& D:/CP6/tools/worktrees/manage.ps1 status
& D:/CP6/tools/worktrees/manage.ps1 status --capacity
# 从所属仓最新远端main领取空闲席位，建立唯一任务分支
& D:/CP6/tools/worktrees/manage.ps1 start --repository CP6 --slot DEV-1 --task BUG-999 --owner COORD --target main
# 实施/修复持续使用返回的workspace和分支，PR按原项目流程审查合并
& D:/CP6/tools/worktrees/manage.ps1 record-pr --repository CP6 --task BUG-999 --owner COORD --pr <PR编号>
# 下列尖括号必须换成实际值；HEAD是最终源提交，不是合并后的main SHA
& D:/CP6/tools/worktrees/manage.ps1 finish --repository CP6 --task BUG-999 --owner COORD --head <完整源SHA> --outcome merged --pr <PR编号> --validation '实际验证/有效复用范围' --review '集中审查结果' --report <永久报告或PR链接> --dependencies-reviewed
# 暂缓：准确源HEAD已推送、开放PR已处置后可释放目录并保留分支
& D:/CP6/tools/worktrees/manage.ps1 finish --repository CP6 --task BUG-999 --owner COORD --head <完整源SHA> --outcome deferred --validation '已验证及未运行范围' --review '实际结果' --report <永久报告链接> --reason '具体缺失的合同决定' --next-action '决定后从origin/codex/bug-999恢复' --dependencies-reviewed
& D:/CP6/tools/worktrees/manage.ps1 resume --repository CP6 --task BUG-999 --owner COORD --head <已核实任务SHA> --reason '原阻塞已解除'
# 空闲席位跨仓替换；先remove后add，不占额外目录
& D:/CP6/tools/worktrees/manage.ps1 bind --slot DEV-2 --repository CP6.CRM --owner COORD
# 集成验证使用已保存的候选SHA，保持detached，不为验证另建功能分支
& D:/CP6/tools/worktrees/manage.ps1 start --repository CP6 --slot INTEGRATION-1 --task BUG-999-CHECK --owner COORD --source <远端已保存源SHA> --depends-on CP6:BUG-999
& D:/CP6/tools/worktrees/manage.ps1 finish --repository CP6 --task BUG-999-CHECK --owner COORD --head <同一源SHA> --outcome verified --validation '实际相关验证结果' --review '本任务检查结论' --report <原任务报告链接> --dependencies-reviewed
```

入口选用现有Python≥3.10（本机离线uv管理的3.13），不联网安装或建虚拟环境。Git和gh需已可用/认证。start可带`--baseline <完整SHA>`核实预期基线，`--depends-on CP6:任务编号`登记依赖；省略target使用所属仓配置，Handoff实际为`codex/t02c-entry-handoff-20260921`，没有main。

finish成功返回分支处置、目录状态和席位释放；失败保留占用。合并收尾按准确旧SHA定向删除本任务远端/本地分支，远端发生新提交则拒绝。若本机Git推送需代理，可用进程环境变量`CP6_GIT_PUSH_PROXY`，不把代理/凭据提交仓库。中断finish核对记录可重试；中断bind须总控核对remove/add阶段，不自动重放删除。有价值依赖缓存阻止bind删除，先安排保留策略。没有强删或超额绕过开关。

实时记录固定在`D:/CP6/.git/cp6-workspaces/state.json`，OS锁及原子替换保护四仓共同领取；不得清空或复制来抢占。`--registry`仅供候选验证/经审查的策略变更，不能绕过配额；状态仍指向CP6 common dir。任务记录含PR（创建前null，创建后record-pr，finish再次核实）、保存证明、原因和下一步。

status不fetch/切分支，capacity额外扫描大小；start/finish会fetch目标核实远端。[原inventory.py](../../../tools/worktrees/inventory.py)仍只读，用Python≥3.10执行`inventory.py --output <报告.json>`可详细盘点，无fetch/remove/prune副作用。没有Git元数据的目录仍需人工核实来源。

## 容量提醒

当前范围基线51.156 GiB，包含四主目录及嵌套资料、CP6容器、发现的其他CP6 Git树、C盘既有CP6规划报告/归档及检查点；嵌套去重，链接不跟随而报告。**总量≥60 GiB、单席位≥8 GiB、C盘空闲<25 GiB或D盘空闲<50 GiB**提醒，先定位增长原因，不按数字删成果。C/D卷实际空闲另列，受其他进程影响，不冒充任务释放量。不能移到C盘/其他容器/归档来绕过统计。

## 客户端需要手动设置

只读核实本机 **OpenAI.Codex 26.1013.3656.0** 安装代码：新聊天有 **Worktree / New worktree** 开关，文案说明先创建树再开始聊天；设置包含 **Automatically delete old worktrees** 和 **Auto-delete limit**。AGENTS无法在代理开工前拦截客户端创建。

1. 将已有固定席位路径作为 **Local** 项目打开，新聊天前关闭 **Worktree / New worktree** 开关，移除已选中的`/worktree`模式；不要选择创建新工作树。
2. **Settings → Worktrees → Automatically delete old worktrees** 开启，**Auto-delete limit** 填6，移开焦点或按Enter保存。固定席位作为项目根保留；重要受管聊天可Pin。客户端代码保护项目根及已固定/运行中聊天。
3. 客户端保留6个不等于CP6可以额外开6个，也不接管所有手工Git目录；其删除前快照机制不能替代本项目Git保存与容量检查。

本轮无界面控制工具，未修改客户端设置。脚本会拒绝配额/占用/仓库/基线/未保存文件/未证实采用等错误，但原生Git、文件复制、改名隐藏的重复任务、未知外部写入者仍依赖任务遵守与总控检查，不是系统级拦截器。
