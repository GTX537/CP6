# WP05-01 后继补充定向复核

审查对象仅为 scratch 中43项补充与新校验器，不重做原WP05/WP14全量审查。读取原候选306b1f3的已有规范制品，未修改候选分支。未执行24项既有测试、旧验收器、原归档程序、Office公式、数据库、业务测试或Actions。

结论：**P2已关闭。43项映射补足原集中审查报告指出的工作簿/CP22/R1来源覆盖；R1-09拒绝重复输入的措辞已在最终补充中修正并完成定向核对，本次范围无剩余阻塞问题。** 校验器代码未变，既有24项测试按准确来源复用；没有重做全量审查或业务验收。

## 已发现并修正：R1-09不能把重复输入静默去重

- 文件：`C:/Users/tt/CP6-planning-reports/20261012-first-wave-review/wp05-supplement-proposed.json:1258`
- ID：`WP05-01-REVIEW/R1-09`
- 修正前值（历史scratch）：`数组按occurrence排序并去重复输入`
- 准确固定来源：`92fb94db40c54be90f2fbb18b0922d264c060796:docs/design-handbook/remediation/07-shared-consumer-mappings.md:75`，同一row已保留原文`输入引用数组排序并拒绝重复`。
- 原因：去重会把非法重复输入转换为合法集合，违反原合同要求的拒绝行为，并使后续执行者对反例选择错误预期。
- 改为：`关系集数组按occurrence身份排序，输入引用数组排序并拒绝重复输入引用`（或相同明确语义）。保留原43项ID/计数、原审查建议历史字节及六份旧规范制品。补充的新manifest/静态记录应绑定修正后新SHA。

## 覆盖缺口的关闭依据

| 原报告问题 | 后继补充承接 | 本次定向判断 |
|---|---|---|
| 工作簿不只两个主册标题：mode、缺并发前置428、RequestKey秘密排除、ReturnTo、日期歧义/FactTime、页面归属、确认与输入保留等 | WB-01..18，准确Sheet/单元格；已有义务标REUSE或准确映射，缺少的输入反例给具体观察 | 可以关闭该来源类别的已报告缺口；未恢复旧30AC，未伪称Office视觉验收 |
| CP22仅原两向量不能覆盖真实有界消费者 | CP22-01..15覆盖真实五步、4/11 captures、issuer/SDK pin/current head、RequiredFenced、独立专业truth、REL全body/效力/范围、post-root、receipt序列和历史设计/实际采用边界 | 可以关闭报告指出的准确义务缺口；仍只在role15内部，不新增第18顶层角色，不声称实际provider已部署/采用 |
| R1精确canonical、完整集合/容量、不可变别名、回执恢复、跨身份及同事务冻结 | R1-01..10逐段绑定第7–35、67–79行，给完整反例/观察 | R1-09上述措辞已修正；覆盖能够关闭已报告问题，无需重做全量审查 |

43项的scenario/required_observation/Owner范围均已阅读。补充与原审查建议的每行内容相同，仅approval_state从REVIEW_SUGGESTION_ONLY变为PROPOSED；未混入正式采纳或运行结果。R1各row没有声称复用旧ID，其空prior_basis_ids与新增精确观察的动作一致。其他实际列出的44个不同prior_basis_ids全部存在于旧矩阵；已核标题/职责对应，WB的20个PROC与具体页面/ACT映射对齐，CP22指向原POS/NEG两个有界修订场景。

## 不可变边界与校验器

- 六份旧规范制品实际字节全部匹配旧basis-manifest；旧manifest摘要仍为`979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`。旧221恢复、8 R1、10细化、295正文位置不被43项覆盖映射计数替换。
- 新版本`CP6-WP05-01-ACCEPTANCE-BASIS/1.1.0-proposed`有独立base_version/base_manifest pin；43项不是新增历史AC或全系统唯一用例分母。
- 新旧均PROPOSED/NOT_RUN；旧30/48仍NOT_INVENTORIED、UNRECOVERABLE、UNPROVABLE，旧成员ID/缺件hash仍null；gate_implementation保持NOT_CHANGED。正式采纳及WP01-01完成门实施必须另记。
- 读取完整`verify-wp05-supplement.py`和24项测试代码。校验器使用重复JSON键拒绝、准确JSON Pointer、单一完整locator模式；Git来源从注册表固定40位revision以`git --no-optional-locks cat-file blob`读取，不读同名当前工作树来替代；归档只按注册相对路径在显式archive-root内读取并核hash。Workbook只读ZIP/XML文本或公式原文，不求值，不解包写盘。
- 固定旧manifest/六制品、原计数、gap及状态边界均检查；49个来源定位按单元格原文、含首尾行摘录或JSON值比对；prior IDs需在旧集合内。
- 程序中唯一写入为调用者显式指定的`--out`报告及其父目录；读取文件/注册来源列入保护路径，正常输入下拒绝输出与这些文件重叠。没有执行原文件、shell命令拼接、隐含报告文件、数据库或网络业务调用。测试脚本写入显式scratch，只复制制品并调用校验器；未在本次复核运行。

## 复用证据与实际操作

已只读核实已有`wp05-supplement-static-check.json`：581检查、49来源引用、PASS，时间2026-10-12T07:41:27.796172+00:00。其checker hash与当前文件一致：`843dc0cd305e56c6867c2bf2e4ab9e9c699a0bc9446b42308e8388cd74ebebff`；其原supplement hash与本次初读文件一致：`9dc3a77a960a499a348d0bac155856d15e81e0886117ba9a1b73e714412d8f95`。原建议hash也与声明相同。

读取现有green summary，24/24通过；这些是既有局部校验器结果，本次没有重新执行。文字修正不改变来源位置/原文、校验器或旧6制品，可复用这些准确范围；修正后的新补充字节需另记，不能把原SHA的报告改称已验证新字节。

本次实际操作为文件阅读、JSON结构/差异读取、原6制品/manifest及已有证据摘要对照；只写本报告。后续已定向读取协调分支最终补充、README/manifest（记录见下）；未核其入库/远端交付，最终交付仍由协调串行窗口处理。

## 修正后定向确认与P2关闭

核对时间：2026-10-12T07:52:49.417294+00:00。最终目录：`D:/CP6-worktrees/fw-s02-1012/docs/development/task-deliveries/WP05-01/supplements/review-r1`。

- 最终`wp05-supplement-proposed.json:1258`明确写“数组按occurrence排序并拒绝重复输入引用”。逐JSON字段比较表明，唯一变化是R1-09的required_observation；其余42行、R1-09其他字段、来源/ID及全部顶层元数据均未变。最终SHA-256为`d308e9bfdd6e2389f0e78a89990217c2c4a47eaeea0546e28cffac24d407c5ec`，与协调者提供值及manifest一致。
- 最终checker与已审版本逐字节相同（843dc0cd…）；新manifest列出的README、测试脚本、checker和supplement四项实际字节均匹配。原basis-manifest及六份规范制品在协调工作树仍匹配原摘要。
- 已读最终README/manifest：仍PROPOSED、decision_record=null、NOT_RUN、gate_implementation=NOT_CHANGED；43只作映射数，221/295及旧30/48缺口保留；正式采纳另记，旧批准不自动覆盖新版本；没有将S02设计交付称作建库或运行采用。没有发现交付边界漂移。
- 24项检查器测试沿用原准确checker及旧proposal测试输入的既有结果，不称新执行。协调者正在另行生成修正后supplement的资料静态记录；本定向审查没有重复运行该工具，也不提前声称该新记录通过。

原报告的覆盖P2及本次补充发现的R1-09措辞问题均已关闭。本结论仅是已指明问题的定向复核，不认证正式规范采纳、业务执行、WP01-01完成门或远端main交付。
