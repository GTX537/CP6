# WGOV-20261020：分支与工作区执行入口交付

Owner COORD；单聊天执行，基线CP6 `7c8479a6`。本次落实后续规则，没有重新进行上轮历史清理、业务修改、数据库操作、生产部署或Actions调度。

## 已实施

- 四仓合计2开发+1集成、每仓一个主目录；原DEV-3取消。固定路径/用途/Owner见[登记及使用命令](REGISTRY.md)，四仓AGENTS和四个工作树容器AGENTS统一指向[政策](POLICY.md)，总协调入口同步；其他项目规则保留。
- 原目录内新增`tools/worktrees/manage.py`及离线解释器选择器`manage.ps1`，复用inventory和registry；没有数据库/守护进程或另一套管理平台。四仓共用CP6 common dir的状态、OS文件锁及原子写入。
- start/status/resume/record-pr/finish/bind实用入口：配额/占用/正确仓库/最新基线/旧成果保存/分支PR重复/本地文件及进程检查；中断不自动抢占；入场远端检查期间HEAD变化会拒绝继续。集成验证只用detached source，无额外功能分支。
- 收尾核实实际GitHub PR/source/remote target，支持PR squash/rebase映射及closed-PR精确patch采用；本任务分支按期望SHA条件删除，新的远端提交阻止删除。固定席位保留并释放；暂缓的精确源HEAD须已推送、PR已处置、原因/下一步/恢复位置完整，可保留分支释放目录。
- 容量基线51.156 GiB，统计含C盘既有CP6报告/归档、各容器与新发现的CP6 Git目录；60 GiB总量、8 GiB单席位、C/D最低25/50 GiB空闲提醒，先定位增长。缓存复用，未知忽略资料阻止回收；本轮仅移除一个已确认空的旧`__pycache__`目录，没有复制/压缩完整仓库。

## 实际验证与审查

Python3.13执行`-B -m unittest discover -s tools/worktrees -p "test_*.py"`：**24通过，0失败/跳过**，包含既有5项盘点安全检查。首轮入口未实现时11项红灯真实失败；实现后通过。真实OS多进程争用只能一方占用；覆盖过期记录、错误仓库、重复分支、脏/忽略资料、运行进程、未保存暂缓、依赖、远端条件删除失败、PR源不符和detached集成验证。

真实CLI负向检查直接使用实际七目录：以更严格的1开发测试策略触发QUOTA（不创建额外目录）、同席位第二领取触发OCCUPIED、本轮尚未提交时finish触发LOCAL_FILES；每项均非零退出，原目录/成果/占用保留。生产策略始终2+1。记录位于既有报告目录的`wgov-cli-negative-results.json`。

真实正向路径：DEV-2按Handoff→CRM→Platform→Handoff先正常remove再add，跨仓永不同时多占一树。CRM、Platform、Handoff均通过start领取、正常PR合并、finish删除准确本地/远端任务分支并释放席位。未测试的产品/数据库/生产流程不称通过。

集中SPEC/QUALITY审查覆盖完整管理diff、并发/保存/条件删除/路径边界和文档链接；未改业务与依赖配置。审查及实际操作发现并修正：进程查询结果错误读取pid；Handoff领取尚未结束就编辑导致提交先落detached，该提交`c4d28de`随后完整快进到原任务分支并通过PR2合并，新增入场HEAD变化回归；PR编号不符被拒绝后纠正；Git缺失引用的退出码改为quiet检查。原失败记录保留，未隐藏/跳过测试。

## 跨仓采用与收尾证据

| 仓库 | PR | 源提交 | 目标采用提交 | 分支/席位 |
|---|---|---|---|---|
| CP6.CRM | [94](https://github.com/GTX537/CP6.CRM/pull/94) | `97c09db4` | `da5f9784` | 本任务本地/远端分支删除；DEV-2释放后替换 |
| CP6.Platform | [79](https://github.com/GTX537/CP6.Platform/pull/79) | `79b421b5` | `a89e90eb` | 本任务本地/远端分支删除；DEV-2释放后替换 |
| CP6.AI-Handoff | [2](https://github.com/GTX537/CP6.AI-Handoff/pull/2) | `c4d28de0` | `822aa0b0` | 本任务本地/远端分支删除；DEV-2保留detached且释放 |

CP6自身的准确源/目标提交及最终释放由[本次PR345最终说明](https://github.com/GTX537/CP6/pull/345)和`D:/CP6/.git/cp6-workspaces/state.json`核对，避免为回填自身提交SHA再建收尾分支。本次仅处置`codex/wgov-20261020`任务引用，未重新删除上轮历史恢复分支。四主目录保留；DEV-1、DEV-2、INTEGRATION-1留作复用；所有交付必须明确报告分支处置、目录状态、席位释放。

推送/PR/合并前检查源和受保护目标的工作流及路径过滤：CRM/Platform手动入口，Handoff无workflow，CP6沿用手动/受保护tag及未命中本次路径的窄PR检查。未调度Actions，也不将本地验证伪装远端成功。

## 不能自动约束与用户操作

已核实本机Codex26.1013.3656.0安装代码，未操作UI。请打开既有席位的Local项目，关闭新聊天Worktree/New worktree开关；Settings→Worktrees开启Automatically delete old worktrees，Auto-delete limit设6。详细步骤见REGISTRY。该保留数不是四仓额度；固定项目根受客户端保护。

脚本不能拦截原生Git/客户端预建/资源管理器复制，无法自动识别改名隐藏的业务重复、无Git元数据的任意副本和所有外部写入者；负责人须核实业务依赖、实际验证和进程。超额例外仍需用户授权，临时席位由总控登记与正常回收。规则文本不是系统级隔离。无客户端设置写入权限不影响已落地的仓库入口与实际收尾。
