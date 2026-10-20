# CP6 固定工作区登记

负责人 COORD；BACKLOG-20261020 已实际处理历史成果，原登记198个目录收敛为7个。完整业务处置、实际测试、合并、恢复位置和空间测量见 [收尾报告](BACKLOG-CLOSEOUT-20261020.md)，当前路径见 [清单](inventory-backlog-20261020.md)；机器规则见 [registry.json](registry.json)。

| 用途 | 路径 | Owner |
|---|---|---|
| CP6主目录 | `D:/CP6` | COORD |
| CRM主目录 | `D:/CP6.CRM` | COORD |
| Platform主目录 | `D:/CP6/CP6.Platform` | COORD |
| Handoff主目录 | `D:/CP6.AI-Handoff` | COORD |
| DEV-1，总控串行开发/治理 | `D:/CP6-worktrees/wave17-dispatch-20261018` | COORD |
| DEV-2，Handoff资料 | `D:/CP6.AI-Handoff-worktrees/dev-coordination-20261012` | COORD |
| DEV-3 | 空闲，先复用安全目录 | COORD |
| INTEGRATION-1，集成验证 | `D:/CP6-worktrees/s02-00-20261012` | COORD |

四仓合计最多3开发+1集成，每目录一个任务写入，满额排队。固定目录每次复用仍检查原成果保存、进程、未跟踪/忽略文件和新任务基线；业务任务不得新建目录绕过额度。各仓AGENTS.md中的现行要求继续生效。

## 重复盘点

```powershell
python -B D:/CP6/tools/worktrees/inventory.py --output C:/Users/tt/CP6-planning-reports/20261017-foundation/worktrees-current.json
```

[脚本](../../../tools/worktrees/inventory.py)默认只读。核查4个指定仓库、登记工作树、工作树容器、旧嵌套clone/worktree入口和客户端目录；必须通过origin白名单。地方副本的本地origin/所有者差异需要显式核实，不自动加入信任或授权删除。指定输出只写报告，不执行fetch/remove/prune。

## 历史与客户端

上一轮257→198的治理记录仍可从[PR342](https://github.com/GTX537/CP6/pull/342)及[历史盘点](inventory-20261020.md)复核，其“待处理”清单已由本次实际收尾接续。

上一轮只读核实客户端 OpenAI.Codex 26.1013.3656.0 包含自动工作树删除及保留数量设置。界面仍需用户打开 **Settings → Worktrees → Automatically delete old worktrees**，把 **Auto-delete limit** 设为约6；固定重要目录按客户端支持方式保护。本轮未修改UI，自动清理不替代本登记或接管任意旧Git目录。
