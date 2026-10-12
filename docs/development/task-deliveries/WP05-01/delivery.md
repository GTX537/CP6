# WP05-01 · C 交付报告

日期：2026-10-12。范围：DESIGN；业务运行 `NOT_RUN`；规范采纳 `PROPOSED`；总台账状态以协调对话的实际提交为准。

## 实际产物

- [设计主稿](README.md)：独立新依据、历史缺口、旧→新可覆盖/不能覆盖范围、观察方法、采纳记录及完成门后续设计。
- [来源清单](sources.json)：39个固定输入。仓库输入按 `92fb94db40c54be90f2fbb18b0922d264c060796` 的Git blob字节固定，原归档按原始文件字节固定；不受Windows工作树CRLF转换影响。
- [场景矩阵](cases.json)：ITER 99 + REL 122条已恢复断言原文不变；R1 8条；OA/ENG细化10条，后两组与原场景存在语义重叠，分别计数。
- [正文矩阵](normative-coverage.json)：295项定位含ITER逻辑模型93字段、8项未决参数及REL八页字段，不把TC数量当全部正文覆盖证明。
- [索引](coverage.md)、[待采纳记录](adoption.json)、[规范制品hash](basis-manifest.json)及[只读静态核验器](verify.py)。

业务源码、原设计资产、原清单、共享合同、总台账/工具、生成视图和四份项目状态文件没有由C修改。项目状态由协调对话在本任务交付窗口同步，不以“并行”为由省略。

## 执行步骤与范围

1. 核对原任务卡、源码/资料评估、两个模块正文、准确归档断言、R1和现行完成门。分支从当时确认的 `df5553fd04ec4fc516602a4e2843650c38ae5006` 创建，随后快进纳入 `92fb94db40c54be90f2fbb18b0922d264c060796` 的直接沟通规则。
2. 在 `codex/wp05-01-acceptance-basis-20261012` 独立worktree内准备仅本任务资料。领取前不接线业务写入；START通过Handoff发送协调登记申请。
3. 逐项形成新定位及旧缺件记录；完整原断言、原AC/TC别名、职责分配依据和PG05解释保留。READ/WRITE/Unknown与Owner边界按正文及R1观察。
4. 集中做一次资料静态核验与任务级人工审查；普通文档任务不编译、不运行业务测试或远程Actions。
5. 提交候选资料后通过Handoff DELIVERY固定SHA交给协调对话；正式状态同步、受保护合并与远端main包含性在协调交付窗口完成。

## 本地核验

运行方式（Python 3.10+，从任意工作目录执行）：

```powershell
& '<Python 3.10+解释器>' -X utf8 '<任务目录>/verify.py' `
  --archive-root '<含docs/原归档路径的本地根目录>' `
  --out '<独立输出路径>/static-check.json'
```

本机使用 CPython 3.13（`C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe`）。工作树内缺少外部归档是正常边界；本次 `--archive-root D:/CP6` 只读原材料。下次缺原归档或hash不符须报告失败，不从网络猜找同名文件。脚本读取固定Git blob、完整原件、JSON Pointer及行范围；只输出资料检查报告。

实际核验：[static-check.initial.json](evidence/static-check.initial.json) 为 PASS（2026-10-12T06:09:59Z，5576项资料断言，0错误）；39个来源、239条分组场景定位、295项正文定位及本任务链接核对通过。最终核验见 [static-check.final.json](evidence/static-check.final.json)：UTF-8 LF修正后的规范manifest `979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`，5578项资料断言、0错误。全部运行保持NOT_RUN。未编译、不运行业务测试；本资料结果不认证规范采纳或远端交付。

## 集中审查结论

审查清单：历史身份/未知hash没有补造；已恢复221项无遗漏；R1与原件并存边界；全量正文位置及逻辑建议不越权变DDL；采纳/任务交付/运行分轴；现有目标完成门保持；PostgreSQL与TenantId约束；只改任务目录；新链接有效；无凭据、环境配置、业务代码或发布门禁改动。

集中人工审查无阻断设计缺陷；正式生效仍待采纳与工具门。准备过程已修正两项资料问题：补齐初稿遗漏的93个逻辑字段及8个DP定位；固定仓库Git blob与原档原始字节两种hash口径，避免Windows CRLF被误认为源内容变化。它们是设计资料修正，不是已执行业务回归。暂存字节复核曾失败：[staged-byte-check.failure.json](evidence/staged-byte-check.failure.json)。原因是Python写出CRLF而Git提交转为LF，导致sources.json制品hash不一致；已将本任务文本显式保存UTF-8 LF并重算manifest。原档字节未改；初次PASS只适用于暂存前的旧制品，最终结果另列，不覆盖失败。

## 协调与未完成范围

已发布START：Handoff `a410aa59e7e567deb8932c8fab2fbb14ce74bdd6`，路径 `handoffs/CP6-DEV-20261012/tasks/WP05-01/messages/20261012T060646Z-c-start/MESSAGE.md`；远端资料分支已核对包含。START不替代台账准入。

正式交付提交与DELIVERY回执由后续固定提交消息引用，避免在文件内预填自身未来SHA。本文件不声称main已包含候选。待协调完成领取/窗口、状态同步和远端main核对；待用户对本版明确采纳。两个目标仍保留历史 `NOT_INVENTORIED`，未实现独立完成门修订；真实Owner、UI/worker、数据库、原场景及R1运行均未验证。

## Actions与其他边界

接手时检查排队/执行中Actions，均为空，无取消项。普通工作流仅手动；R2候选仅版本Tag触发，推资料分支不匹配。发布前还需核对候选和当时远端受保护分支；没有触发修改、手动运行、重跑、部署、数据库迁移或分支保护修改。

Handoff发布前核对私有仓、资料分支/默认分支无工作流、Actions `enabled=false`，只非强推C的专属资料分支。通信资料不自动并入Handoff默认分支，不动根CURRENT或旧设计指针。
