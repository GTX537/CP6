# 全量任务拆分验证记录

本次只验证任务规划、台账及派工工具。375项任务覆盖14包/125目标，全部PLANNED且未领取。业务代码、原有数据库、247运行场景与生产环境均未执行；工具PASS不是业务PASS。

规划起点为Main `312cde1577e073fa7ce0d317431edabf5d8dd18b`。Main现存入口核对该规划SHA；CRM和Platform分别核对`c778a3052a4416b82facb07b1244fc98fb6ad8a1`、`30bd23af6808d217a23878bd9437513043c52834`。旧实现评估中的更早Main `5ca90701bfe1e886ea9e179f1e8e5f413bb44f43`及逐文件来源仍保留，未改称本次重新审计业务。

## 本地检查

从仓库根使用Python 3.10+执行：

```powershell
python docs/development/tools/manage_roadmap.py --write
python docs/development/tools/manage_roadmap.py
python -m unittest discover -s docs/development/tools -p "test_*.py"
git -c core.longpaths=true diff --check
```

本次实际解释器为Python 3.13，准确版本、输入SHA-256与执行范围记录于本目录输入清单。工具检查定义与状态一致、依赖、125主体覆盖、领取/路径别名/资源互斥、真实证据所需结构、生成视图一致。它不认证证据真实性、不读取对话实时状态、不创建分布式锁；实际领取仍由单一协调对话处理。

最终结果：[62项工具回归](tools-final.txt)通过；[完整静态核对](plan-validation.json)确认375卡、125主体覆盖、358现存入口、320拟新增路径及8,533处本地链接；[独立审查结构核对](review-final-structure.json)另核对3,554依赖边无环、19生成视图一致及其审查范围内8,999处链接/锚点。计数来自各自实际范围，不相加为业务通过数。

输入SHA-256记录实际受测Windows工作树的CRLF字节。沿现有Git文本规则，提交对象存LF；[源码对应表](git-source-map.json)记录两者SHA-256及准确Git blob，并逐字证明仅行尾转换。未修改全局配置或源码行尾规则，也不把转换后的字节冒称原始执行文件。仅为本目录原始`.log`/`.txt`输出增加精确路径属性，保留CRLF和unittest产生的尾空格；已逐文件证明Git blob与执行原件字节相同，其他文件继续原有空白检查。

## 失败与修正

- `dispatch-tests-red.txt`先验证缺少派工门会失败，随后`dispatch-tests-initial-green.txt`记录37项通过。
- `manage-roadmap-definitions-*.log`与`manage-roadmap-summary-red.log`记录定义校验/卡片生成/第二状态源与总览回归；`manage-roadmap-all-green.log`为该阶段59项工具测试结果。
- `policy-guard-red.txt`及其green记录防止通过删除dispatch_policy绕过全量WP任务定义的校验。
- `ledger-initial-full.json`及`normalized-source-red.txt`保留来源约束过严的真实失败：原修订清单含仓库tools生成器。新定义将路径正规化并允许仓库内docs/tools输入；原目标的历史引用不修改。
- `plan-validation-first-fail.json`保留首次源码入口检查失败：规划文档06-first-slices新增于更早源码评估之后。修正本次现存入口核对的Main版本为实际规划起点312c，而非伪造旧5ca文件存在。
- `worktree-alias-red.txt`与`review-fixes-green.txt`保留同一工作树不同路径写法漏检的复现和修正；使用resolve后再统一大小写/分隔符。
- `tools-full-draft.txt`为372任务中间版61项工具回归，不能代替最终输入的检查。所有日志均为工具/文档验证，无业务验收状态回写。

[独立任务级审查](review-full-breakdown.md)结论：五类问题已修正并定向复核，无未解决的实质阻塞。[证据归档补充复核](review-evidence-addendum.md)确认14原输出逐字保留与4项源码Git映射准确。正常PR和远端main核对仍按仓库流程执行。

## 复用与未验证范围

原来源目录、125目标/13,404目标—验收关联、全部revision_acceptances/source pins和S01准确输入未变。复用[原清单核对](../../assessments/20261011/evidence/inventory-validation.json)与[S01来源核对](../s01-planning/plan-validation.json)的原适用范围，没有重跑全量历史验收或重新散列全部归档。

任务卡正反场景是派工摘要，完整原AC/TC、附加断言、修订场景及所有Required消费者仍在目标完成门中。条件门只区分实际实例输入；明确内部能力必须有提供者交付与消费者真实采用。PostgreSQL布局等待S02-00设计，不借本轮创建schema/迁库。

无Actions触发或取消、无触发配置变化；交付前分别核对候选和远端受保护main的工作流及相关输入，采用正常保护PR合并。实际PR和远端包含性由本轮交付回执与远端Git历史核对，不以本地工具检查代称。
