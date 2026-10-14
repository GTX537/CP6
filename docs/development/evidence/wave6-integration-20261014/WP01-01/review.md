# WP01-01 集中合并前审查

结论：**PASS — 可合并有界 PREPARATION_ONLY 准备包。P1 0 项，P2 0 项。** 本结论不代表完整 WP01-01 完成、正式采纳、实现准入或任何业务验收。

- PR： https://github.com/GTX537/CP6/pull/205
- 候选：`938f53eb44438c066bdc38a60e2d987264871803`
- 基线：`1ab2d031e3c501662ecb89a33839fac1cba55d33`
- 工作树：`D:/CP6-worktrees/wp01-01-prep-20261013`；开始和结束均干净，HEAD 与候选一致。
- 核心 manifest SHA-256：`b9e26854717573ba4216e48cb2e576ff4440985b4e77f9605748cc6605e9ab26`。
- 审查仅写本报告及同名 JSON；未修改源码、Git 状态、远端、数据库或 Actions。

审查遵循仓库 AGENTS.md、Main 本轮 WP01-01 派工卡、原 WP01-01 条件及 WP05 原版/1.1.0-proposed 范围，使用 review 技能的差异检查规则。以完整候选相对固定基线作为一次任务审查单位，没有重启前轮测试或审查循环。

## 范围与来源身份

完整差异为39个新增文件，全部在 `docs/development/evidence/WP01-01/preparation/` 和 `eng/wp01-01/preparation/`。五份 Python 源码、全部说明和收据已读；大型生成 JSON/矩阵按完整成员和完整原断言比对，未仅抽样行数。原 WP05 制品、adoption.json、manage_roadmap.py、roadmap、四份状态、产品源码、项目依赖及工作流均无变更。`git diff --check` 通过；常见令牌/私钥模式扫描无命中。

独立读取固定 Git blob 或只读归档原始字节，33个核心制品的长度/SHA-256 与 manifest 全部一致；58个输入出现位置（39个登记来源）全部符合 input-lock。原六制品、组合 manifest 和补充四制品没有换源。组合 manifest 为 `c5c926728287ac9ee0b5048300deefa789ec7c019ed88168bd74e120dc53ebf2`，原 manifest 为 `979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`。

首次独立核对出现两项表示差异，已明确解释而非改写证据：intake.json 是排除在核心 manifest 外的父级元数据，工作树 CRLF 规范化后与候选 Git LF 完全相等；manager 原执行收据固定的是工作树 CRLF 字节，其 SHA 为 `992e88abcaefd904ac9c0b555405df443f0e521170998237e3ca2f81c917545b`，候选/基线 Git LF SHA 为 `e1a86a1c26cb2fc928d4eb2a5baf1261e46cd60ab9735846fdfc476a03db2e4a`。原始执行字节相符，差异归因和两个 hash 均保存在 JSON。

## 义务、决定及完成门边界

577个唯一矩阵 ID 全部与原 cases/body/supplement 的完整原断言相等；case Owner、分配依据、overlay 和补充 Owner/来源/复用 ID 没有丢失。对象、操作、观察、Owner、来源维度齐备。577只是导航行数；221恢复场景、295正文定位、43补充映射、8目标R1及10细化保持分别统计。

`prepare.py:110` 起的分类提取与正文保全逻辑、`semantic-audit.md` 及原 WP05 补充相符。特别核查：REQ/BR/PROC全部子条件仍由完整原断言约束；字段目录逐字段列观察；PostgreSQL物理表示/合法域未被建议类型缩窄；工作簿ECO冲突未被N/A豁免；CP22保原Owner与真root之后独立预约；R1重复输入引用必须拒绝，Inbox与业务应用边界未合并。导航摘要不是新合同或运行证明。

DP-ITER-01–08 均 UNRESOLVED。旧30/48保持 NOT_INVENTORIED / UNRECOVERABLE / UNPROVABLE，成员与缺件hash仍null。决定模板为UNSIGNED，actor/time/authority/effective_at/decision_text/adoption_id均null。原WP05 proposed状态未变；本次分配没有被当成采纳。

`comparator.py:20–38` 对已声明的畸形候选结构拒绝；`:44–57` 固定ITER/REL白名单及版本/pins/采纳/历史缺口，`:59–88` 核准确集合、适用记录层级、非合成运行形状、全部任务/R1/仓库/DP。其他123目标拒绝已有完整枚举测试。`:89–91` 与异常分支均固定 PREPARATION、actual_adoption=false、actual_completion=false。当前真实contract的跨仓闭包仍UNRESOLVED，不能靠填写合成记录满足当前提案。

比较器明确只检查形状：不验证签名、Owner真实性、文件内容、可信时间或远端包含性；其合成正例不能作为实际接受。这项边界已在代码、README、gate-proposal、测试和两份原审查中一致披露，不能直接将其接入生产manager。

实际 `manage_roadmap.py:148–157` 的原清单/R1/实现/交付门、`:271–282` 的全部主体任务门及`:94–95` 的WP01原清单汇总门均保持。生产门代码没有改动。候选实际台账中 WP01-01 仍 PLANNED / NOT_CHECKED；0/125、247 NOT_RUN、41历史静态及123原清单完整均为未改变的原证据范围。

## 验证复用

本次只运行独立只读身份/断言集合检查、Python AST解析、差异与敏感模式检查。没有重跑以下成功测试；逐条完整命令、原时点、退出码、stdout/stderr路径与hash、脚本身份、依赖、环境、来源SHA及适用限制见同名JSON的 `evidence_reuse`。

| 原证据 | 原执行时点（UTC） | 原结果 | 本次依据 |
|---|---|---|---|
| prepare-final | 2026-10-14 05:43:56–05:44:06 | 3509检查，39来源、892引用，exit 0 | 同脚本、58输入及输出hash |
| comparator-tests-final | 05:44:06–05:44:07 | 23测试，exit 0 | 同测试和comparator字节；日志hash |
| bundle-tests-final | 05:44:07–05:44:08 | 10测试，exit 0 | 同测试、prepare/comparator及完整矩阵/contract/模板/原输入 |
| manage-roadmap-readonly | 05:44:08–05:44:10 | 结构/来源/生成视图PASS，exit 0 | 原基线未变的manager/台账；原工作树字节匹配 |

环境为本机 CPython 3.13.13 / win32，原命令使用 `-B -X utf8`。原 source_revision 为固定基线；测试文件当时运行字节与当前候选逐hash相同，不能把候选提交时间或本次审查时间写成原测试时点。原SPEC/QUALITY与quality-integrity的33核心制品及四条命令身份声明已独立核对。

早期22断言失败、畸形输入3失败/3异常、跨仓未决1失败仍原样保留。初始runner漏记实际第四份状态文件的限制由bookkeeping-correction单独披露；候选/基线第四份状态Git字节相同。修正后的runner静态审查和hash核对完成，但没有称其已重跑原四条命令。

## 后续阻塞与未验证范围

1. COORD仅登记准备交付，串行同步台账/生成页/四份状态，复核最新远端与工作流安全条件，正常合并并核远端main包含性；本审查不替代这些未来交付步骤。
2. 具名正式采纳仍须绑定准确1.1.0-proposed完整hash、目标、actor/time/authority/effective_at及明确“以当前可知义务建立新标准，无法证明旧30/48逐项等价”，处理覆盖、例外和失效条件。
3. DP01–08与真实provider/consumer仓库闭包继续待准确授权后继决定；Main/Platform/CRM基线存在不等于业务依赖或采用。
4. 生产完成门须后续独立准入、版本化实现与回归；其他123目标和历史缺口不得绕过。WP01原汇总门仍独立阻塞。
5. 真实主体任务、逐义务业务证据、目标R1、Owner/宿主/UI/worker/持久化/权限租户并发/PostgreSQL专项及实际跨仓远端/包/合同消费收据均待交付。

未执行Office渲染/公式、业务运行/外部provider、数据库/迁移/并发、权威签名核验或真实跨仓接受。没有重新逐字穷尽审查全部外部原文，也不能恢复旧30/48等价性。没有远程Actions查询、触发、重跑、取消或触发器修改；最新远端与工作流状态由COORD负责。以上均不阻止本次仅准备资料合并，也绝不构成主任务完成。
