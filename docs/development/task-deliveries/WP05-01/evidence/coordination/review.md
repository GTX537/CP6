# WP05-01 集中独立审查

结论：**With fixes；候选306b1f3暂不宜作为完整的WP05-01设计任务交付合入。** 发现一个P2实质覆盖问题，未发现伪造旧30/48项、冒充业务PASS或擅改PostgreSQL/Owner边界。补齐下面的来源覆盖后，可仅对独立补充、引用关系及其静态证据定向复审，不需要重跑业务或全仓验证。

审查对象：`306b1f3ba61456b5dc117189710b37031bf57a19`；已确认main：`8570384f1e28065afee4ce416c9f492dde734131`。按requesting-code-review技能和CP6任务级集中审查节奏执行。未写候选分支，未运行Actions、数据库、服务、Office或业务测试。

## P2：补齐两个主册标题之外的已知可恢复义务

定位：[verify.py:133](D:/CP6-worktrees/wp05-01-20261012/docs/development/task-deliveries/WP05-01/verify.py:133)、[verify.py:145](D:/CP6-worktrees/wp05-01-20261012/docs/development/task-deliveries/WP05-01/verify.py:145)、[README.md:38](D:/CP6-worktrees/wp05-01-20261012/docs/development/task-deliveries/WP05-01/README.md:38)、[README.md:92](D:/CP6-worktrees/wp05-01-20261012/docs/development/task-deliveries/WP05-01/README.md:92)。

当前静态检查把正文全集定义成两个主册内可匹配的295个标题/字段位置。221个恢复场景与目录完全相等也成立，但这两项不等于所有已登记规范来源的语义覆盖。工作簿、R1细则和CP22来源只被hash固定，缺少部分准确义务到观察点的映射；现有泛化观察语句和少数修订向量不能证明这些拒绝路径被承接。任务卡要求每项义务可追准确来源、可执行观察点及适用对象；README92也明确可知义务未映射时拒绝采纳。

已直接确认的缺口：

- **ITER已接受工作簿。** 原件 `docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，SHA `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。`引数説明!AP18`明定写命令缺并发前置428；旧BR030只有不匹配412。`引数説明!B30/P30/AP30`明定ReturnTo只限受控内部路由、禁止任意外部URL且返回前重验权限/状态；两个矩阵没有该条。类似遗漏还包括mode不作安全凭证、RequestKey不可取token、`項目説明 (入力値)!AM50`拒绝日期歧义/客户端FactTime覆盖，以及`画面項目制御`准确动作页面归属、修改确认和失败保留输入。APR-ITER正文第9/14行已明确接受此工作簿及画布/处理义务；这些不是Office视觉未验证或丢失旧清单造成的未知。
- **CP22有界REL消费者。** [cases.json:6746](D:/CP6-worktrees/wp05-01-20261012/docs/development/task-deliveries/WP05-01/cases.json:6746)及6783仅观察role15可进入编码清单、不能宣称18角色或实际采用。原件 `docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，SHA `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`，113–126明定真实五步前驱、PREPARE四capture/其余十一capture、独立issuer和SDK pin、current head、缺源RequiredFenced与零后继派发；161–165明定REL全body、GRANTED/current/有效窗、精确scope/version/ranges/quantity及真root后预约。这些需独立的准确观察；“只统计17角色”和README单句post-root说明不覆盖真实输入错误的拒绝行为。
- **R1准确条件。** [07-shared-consumer-mappings.md:11](D:/CP6-worktrees/wp05-01-20261012/docs/design-handbook/remediation/07-shared-consumer-mappings.md:11)至35、67至79还有集合完整性/传输上限、canonical规则、不可变无损别名、回执revision恢复、原PRODUCT与RelationSet/Binding跨身份核对等明确限制。原2+6条和10个细化向量只承接部分场景；应逐条件明确复用或补观察，不能按向量数认定当前补充已全覆盖。

修复建议：保留当前proposed六份规范制品及manifest原字节、原221/295计数，增加具独立版本和来源绑定的后继补充。对已经被原BR/PROC/TC完整承接的工作簿条款只增加映射，不制造重复义务或新旧AC数；对缺少的具体条件增加正反例/观察、Owner责任及NOT_RUN。校验器的覆盖全集必须声明来源类别，不能继续仅以固定295标题数证明完整。补充后的设计任务交付可以完成；规范正式采纳仍是独立决定，WP01-01台账/完成门修订与两个业务目标的实际验收仍另记。

完整修订建议见 [补充矩阵JSON](wp05-review-supplement.json) / [补充矩阵Markdown](wp05-review-supplement.md)：43项来源映射建议（工作簿18、CP22 15、R1 10），每项保准确原文、hash、Sheet/单元格或行号、原矩阵复用ID、范围及可执行观察。它们是审查建议，不是43个新增历史AC，不是已采纳标准。

## 确认正确的边界

原221条恢复断言按目标保留原文及别名，未改造成30/48清单；共用FB场景不宣称全局唯一用例。两历史缺口保持NOT_INVENTORIED/UNRECOVERABLE/UNPROVABLE，缺件hash和旧成员ID为空。采纳PROPOSED、业务NOT_RUN、完成门NOT_CHANGED均真实。PostgreSQL-only、TenantId/授权/精度、跨Owner真实证据、DP未定项均保留。

设计任务交付与新依据采纳分离是正确的；WP01-01以明确采纳为前置，并非WP05-01文档提交自动触发。README对完成门的双路径只是后续设计，当前工具未放宽。协调窗口登记/状态同步/远端main包含性由协调对话完成，不应要求本执行分支修改共享状态文件。

## 实际审查与验证范围

- 审查候选相对共同基线的全部15个新增路径，读取主稿、delivery、adoption、manifest、sources、矩阵结构/全部引用与状态、verify及证据。对原场景按完整性机器比对，并围绕ITER评审/关闭/租户、REL额度/Unknown/发布/采用/安全终结做语义核对。未声称对所有外部原件逐字全文或Office布局重审。
- 读取WP05-01、WP01-01任务卡、S00、PostgreSQL基线、两模块手册、适用R1/CP22。工作簿以已存结构化阅读辅助扫描参数、状态、控制、检查、查询/更新/文件/导航、消息、IF和角色安全类别；补充矩阵每个单元格最终直接从原XLSX XML读取并固定，未重算公式或渲染Office。
- 39个来源对准确Git blob或原始归档字节逐一hash校验；固定输入相对当前main只有roadmap变化，业务规范输入未变。六份规范制品均与候选Git blob的manifest hash相等。
- 复执行verify逻辑，只有报告mkdir/write_bytes在内存中移除以遵守只读授权，未改磁盘脚本。2026-10-12T07:21:51Z：PASS，5581资料断言、0错误；221恢复+8 R1+10细化及295正文定位；manifest `979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`。这只是现有检查范围的完整性结果，不反证上述语义覆盖缺口。
- `git diff --check`通过；`git -c core.longpaths=true status --short`为空。普通status的五个超长路径假删除经此核实消除，无文件修复或清理。当前main已新增S01等交付，二点树差分会显示那些主线增量缺失；审查未把它们误算候选删除，合并须保留主线成果。
- 未触发/取消Actions，未改触发器。只复核候选已有workflow-inspection证据，不把它改称对当前远端新执行检查。没有业务PASS、数据库/服务/端到端/生产或规范采纳验证。
