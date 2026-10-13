# 第三轮四项合同任务协调交付

S01-03/04/06/08 已完成任务级审查、正常保护合并及所属仓库远端 main 包含性核对；据实际回执登记 DONE。全部任务12/375、S01为8/12，仅为任务计数，不代表工时或业务完成率。

| 任务 | 正常合并 | 最终候选 | 已核远端 main | 验证 |
| --- | --- | --- | --- | --- |
| S01-03 | [GTX537/CP6](https://github.com/GTX537/CP6/pull/188) | `b24f76445ffdc9b4df0de77eb394a6aa47726a0c` | `08bf7c923fa0b21af39d5e1f1fd550ed635246ed` | 复用205/205，25固定向量 |
| S01-04 | [GTX537/CP6](https://github.com/GTX537/CP6/pull/190) | `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8` | `688383439311a3babf00bae98ee426122ce60cd1` | 复用132/132，9固定向量 |
| S01-06 | [GTX537/CP6](https://github.com/GTX537/CP6/pull/191) | `f406e4e515a7ce82d77db5307e467e7bcdbc25e4` | `bd25558ea4c0981b0cff1e4fa77ab8f61277211c` | 复用C#60、TS/Schema81、类型检查；22固定向量 |
| S01-08 | [GTX537/CP6.CRM](https://github.com/GTX537/CP6.CRM/pull/90) | `912f6afa1d64cc0777721c03b5b39c0db91d60da` | `1e4eea61c31d7547c3b3495d392eefbed99b7339` | 修正后C#186、Web142、importer6、类型检查；30固定向量及49UI |

## 接收与源验证

[固定接收记录](intake.json)保存四个 Handoff 分支及全部 START/DELIVERY/QUESTION 的准确提交、路径、字节摘要和内容。worker_ack_at取真实START，coordinator_received_at取实际读取时间，没有补造早期协调ACK。原READY快照及原候选保留；三项Main直接采用，CRM在修正两项阻塞后采用。

- S01-03：[审查](S01-03/review.md)、[原验证复用](S01-03/original-validation.json)、[合并回执](S01-03/remote-main.json)。38位授信金额与JSON边界；授信业务/存储未验收。
- S01-04：[审查](S01-04/review.md)、[原验证复用](S01-04/original-validation.json)、[合并回执](S01-04/remote-main.json)。精确数量/金额/比例；完整Rational/MAVG对象待后续采用。
- S01-06：[审查](S01-06/review.md)、[原验证复用](S01-06/original-validation.json)、[合并回执](S01-06/remote-main.json)。C#/TS/标量Schema采用R1；完整ATP wire/Owner/DDL未验收。
- S01-08：[原阻塞审查](S01-08/initial-review.md)、[修正定向审查](S01-08/fix-review.md)、[本轮执行输入](S01-08/executed-source.json)、[收尾验证](S01-08/coordinator-final-check.json)、[合并回执](S01-08/remote-main.json)。CRM证据JSON内相对路径属于CRM仓库，完整日志从该固定CRM候选的 docs/evidence/s01-08/terminal-r1/ 读取。

三项Main原验证记录区分当时未提交工作字节、执行基线HEAD与之后绑定提交；source_sha=null如实表明当时没有最终提交，台账source_sha只是随后固定该实现的提交，不声称测试在该提交执行。原TRX/日志/配置及文件摘要可重定位；S01-06原环境部分为执行者声明，稀疏typecheck日志依原命令退出码记录。各自局限完整保留。

三个Main候选远端合并文件逐一匹配，最终共同树bd25558ea4c0981b0cff1e4fa77ab8f61277211c的测试/源输入不变；SDK默认glob覆盖三个独立namespace，共享项目/依赖/构建配置未变，无具体交互疑点。因此复用原205/132/60+81结果，没有新构建整个合并组合；这一未运行范围明确保留。后续Main改动仅合同文档和协调状态。

## Issue189：终态合同与环境门禁

原CRM候选2ff7f1e把旧Schema看作天然优先，并将49UI中的合法99/null当成拒绝正例；原Node20/npm10.8也不符合仓库engines，故未直接合并。Main既定Wire第57行已要求90/99公开expiry/artifact/proof为null，本轮以[明确版本补充](../../../design-handbook/remediation/addenda/CRM-INQ-TERMINAL-20261013-R1/README.md)同步两个遗漏节点，原Schema/原件/输入包和历史接受不改。来源[PR192远端回执](terminal-source-remote-main.json)固定ce38171868312588ce8f4b9e843ed6c39f4d0b0f。

CRM显式选择CRM-INQ-TERMINAL-PROJECTION/20261013-R1，同时匹配native locator、patch及effective摘要；不隐式升级旧入口、不在封闭payload新增version。effective SHA fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d；根及五个内嵌export覆盖99时序、90/99 null与缺失/非null拒绝，30三时钟不回退。全部49原UI接受并往返，原22输入和30向量expected不变。四INQ-VERSION只作native locator子断言，不能独立证明新profile。

本轮新跑C#186、受支持Node22.22/npm10.9.4 Web142（82任务+60既有）、importer6及typecheck。原错误预期、Node20记录、RED及本地编码操作失败均作为历史保留；最终338输入/47产物/22日志经独立核对。Main来源和CRM实现均已在各自远端main，原复现和回归完成后，[Issue189已关闭](issue-189-closed.json)。关闭的是合同库错误，不是实际下载授权或业务上线验收。

## 保持边界

全部125业务目标仍0交付，247修订运行NOT_RUN、41历史静态REFERENCE_ONLY、ITER/REL缺失及WP05的PROPOSED不变。真实HTTP/S03、Owner授权/事务、租户隔离、内部历史expiry持久化、PostgreSQL存储和生产验收均未由本轮证明。

每次push/PR/merge检查候选与protected-main事件及依赖；各task/workflow-pre-merge.json和来源工作流回执保存结果。Main12个普通workflow仅manual、R2只version tags；CRMworkflow仅manual并保留预先disabled_manually。无Actions启动/重跑/取消、触发/保护配置变更、包发布、数据库操作或部署。CRM治理门禁及新增链接检查通过。没有自动领取下一轮，也未清理原工作树。

本协调状态经正常PR进入Main后，COORD在Handoff追加四份回执并按固定提交回读；上传不会唤醒休眠对话。下游只按实际已交付依赖另行核对准入，后续数据库唯一方向PostgreSQL。

收尾时间说明：原回执 observed_at 是各 Git/API 检查之后的本机时间，merged_at/closedAt 是 GitHub 服务器时间；两时钟有偏差，原值保留，不能将它们直接相减推断操作顺序，也不改称开始时间。审查后重新读取五个PR、两仓当前远端包含/文件blob与Issue状态，各回执追加真实 verification_observed_at；台账交付观察引用这一新核验时点。[本次只读复核](receipt-reverification.json)。
