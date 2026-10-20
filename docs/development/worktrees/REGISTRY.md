# CP6 工作区登记与本轮回收结果

负责人：COORD（当前总控聊天）；任务 WORKSPACE-20261020。四仓地址均已核实为 `https://github.com/GTX537/<仓库名>.git`。本轮没有创建工作树或克隆，没有修改业务代码、数据库、Docker、客户端设置或合并历史业务分支。

## 实际结果

| 范围 | 清理前目录 | 清理后目录 |
|---|---:|---:|
| CP6 | 107 | 87 |
| CP6.CRM | 38 | 38 |
| CP6.Platform | 66 | 66 |
| CP6.AI-Handoff | 46 | 7 |
| 合计（含独立副本主检出） | 257 | 198 |

总逻辑占用 **186.93 → 158.49 GiB**；正常 `git worktree remove` 成功 **59** 个，失败 **0** 个，移除目录盘点容量合计 **28.55 GiB**。D 盘卷空闲实测 **36.16 → 65.63 GiB**，净增 **29.47 GiB**。卷空闲变化包括 NTFS 元数据及同期少量分支/文档写入，并非逐文件已分配字节；不把逻辑容量与卷变化混为一个数。后测时集成席位切到最新基线，解释与移除容量的差异。

本轮只移除已核对任务结束/交付、当前远端保存、无未提交/未跟踪/忽略文件/链接/运行引用的准确登记路径。删除前再次核对实际路径、origin、HEAD、当前远端和进程；未使用 force、reset、clean、递归删除、分支删除或 prune。首个脚本调用被 PowerShell 大小写 JSON 键检查阻止，尚未删除任何目录；修正字段名后正常执行，失败记录保留在聊天。长路径初扫错误已用 Windows 扩展路径重测，最终容量读取错误为 0。

逐目录路径、仓库、容量、分支/HEAD、未提交与忽略项、独有提交、任务线索及保留原因见[完整登记](inventory-20261020.md)和其原始 JSON。保管责任均由 COORD 跟进，原任务作者不能从目录名推定。

## 固定目录与额度

四个指定主目录分别为 `D:/CP6`、`D:/CP6.CRM`、`D:/CP6/CP6.Platform`、`D:/CP6.AI-Handoff`，负责人均为 COORD。额外四个 CRM 独立副本列为保护存量，不作为第二套主目录使用或新增依据。

| 槽位 | 固定路径 | 用途 / Owner |
|---|---|---|
| DEV-1 | `D:/CP6-worktrees/wave17-dispatch-20261018` | 总控串行开发/治理，COORD；本轮实际复用 |
| DEV-2 | `D:/CP6.AI-Handoff-worktrees/dev-coordination-20261012` | Handoff资料席位，COORD；也计入开发额度 |
| DEV-3 | 未分配 | COORD调度；先认领已确认安全的存量，满额排队 |
| INTEGRATION-1 | `D:/CP6-worktrees/s02-00-20261012` | 已结束S02-00目录转固定集成/验证，COORD；已切 detached `c7ae8c1d`，无业务改动 |

这是一套四仓合计 **3 开发 + 1 集成** 的额度，不是每仓各一套。固定席位每次复用仍检查成果保存、原进程、所有本地资料和正确新基线。各任务独立分支，不复用上一任务分支继续混写。其他历史目录不能直接激活；认领也必须先占用上述槽位。机器可读登记为 [registry.json](registry.json)。

## 保留项与后续处理

- `D:/CP6-worktrees/main-integration` 有暂存业务改动及忽略构建内容；保留，未重置或当作可用集成席位。
- `D:/CP6-V2`、`crm-prd-freeze`、`crm-prd-freeze-v3`、部分 Space/设计分支有当前远端未覆盖的本地提交；保留，未擅自推送或合并。
- CRM 两份旧文档候选仍对应 Open PR #5/#6；保留，不以工作区干净判断结束。
- 许多目录含忽略/未跟踪证据，如 `architecture-audit-20260905/audit/evidence/diagnostics-fin/diagnostics-fin.csproj`、`.artifacts/pr-body.md`、原始日志/TRX、发布物和依赖链接。未证明全部可重建/可丢弃，整体保留；没有只按 bin/obj 名称删除。
- 四份额外 CRM 独立副本和三个 Platform 临时目录（登记存在、实际不是 Git 工作区）需人工核对，详见明细。没有用 prune 隐藏这些目录或删除资料。
- 原根目录用户资料、运行引用目录、数据库、Docker及共享缓存均保留。`CP6-portfolio`、`CP6.CloudPackages` 的 origin 不在允许范围，未纳入清理。

这些保护存量意味着目前仍有 198 个物理目录；没有声称已缩到 4 个。规则已经阻止新任务无限增加目录，进一步缩减需要逐项确认本地独有资料的保存方案。

## 可重复盘点

脚本 [tools/worktrees/inventory.py](../../../tools/worktrees/inventory.py) 只使用标准库、Git 和 Windows PowerShell。默认只读仓库和远端引用，不 fetch/remove/prune；不指定 `--output` 时报告写 stdout，指定后仅写 JSON/Markdown 报告。

```powershell
python -B D:/CP6/tools/worktrees/inventory.py --output C:/Users/tt/CP6-planning-reports/20261017-foundation/worktrees-current.json
```

默认核查四个指定仓库及其全部登记工作树、四个工作树容器和客户端受管目录，额外副本必须通过 origin 白名单；不遍历目录链接。支持 `--root`、`--container`、`--active`、`--registry` 指定环境，不能访问的路径/登记明确报告。容量不重复计算已登记嵌套目录。4 项定向脚本安全验证通过；本机完整前后盘点已实际执行，未运行业务测试/构建或 Actions。

## 客户端设置（需用户操作，未修改）

本机安装 `OpenAI.Codex 26.1013.3656.0`。只读检查安装包确实包含 `Automatically delete old worktrees` 和 `Auto-delete limit` 两个设置；没有可用的设置界面控制工具，因此未修改 UI 或内部配置文件。

请打开客户端 **Settings → Worktrees**，打开 **Automatically delete old worktrees**，将 **Auto-delete limit** 设为 **6**。固定重要目录可按客户端支持方式设为 permanent 或保留其置顶聊天；不要把自动回收根改成混有其他资料的旧目录。

[官方工作树文档](https://learn.chatgpt.com/docs/environments/git-worktrees) 说明自动清理针对客户端受管工作树，可配置保留数量；运行中/置顶/永久工作树受到保护。这不能自动接管大量用 Git 命令创建的历史目录，仍需执行本仓生命周期规则。

## 规则保存

CP6 的 AGENTS.md 和本登记由本轮治理 PR 同步；其他三仓已通过仅修改 AGENTS.md 的正常 PR 保存：

- [CP6.CRM PR](https://github.com/GTX537/CP6.CRM/pull/92)，远端集成 `3ff290c6ecb125c2165915cdf615292dac40ba84`。

- [CP6.Platform PR](https://github.com/GTX537/CP6.Platform/pull/78)，远端集成 `0de157d8cfa128a09da075052777098d3a7f9ba7`。

- [CP6.AI-Handoff PR](https://github.com/GTX537/CP6.AI-Handoff/pull/1)，远端集成 `320c1ae618a278c73369a0345a8fcf4662380abc`。

四个工作树容器另放置适用同一范围的 AGENTS.md，使旧分支在物理目录中也能读取新规则；副本内容来源为本轮已审查规则，未覆盖历史业务工作树内的文件。
