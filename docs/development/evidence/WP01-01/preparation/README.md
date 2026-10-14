# WP01-01：替代验收依据采纳准备

本包仅为 **PREPARATION_ONLY**，供用户审查是否具名采用 WP05-01 `1.1.0-proposed`。当前没有采纳、没有生效日期、没有修改实际完成门。WP01-01 完整任务保持 **PLANNED / NOT_CHECKED**；业务仍为 **0/125**，247 项运行 **NOT_RUN**，41 项历史静态 **REFERENCE_ONLY**。

建议决定的含义是：**以当前可知义务建立新标准，无法证明旧30/48逐项等价。** 历史 ITER 30 / REL 48 的成员身份和原断言仍不可恢复；不生成猜测旧编号，不把现存工作簿、72 TC、LR/FB/PG 或本矩阵改名为旧清单。

## 用户应先审阅的材料

1. [语义复核与剩余边界](semantic-audit.md)：正文、工作簿、CP22、R1 的具体责任、冲突处理和不能覆盖的部分。
2. [逐 ID 可读矩阵](obligation-matrix.md)及[完整机器矩阵](obligation-matrix.json)：每条都有对象、操作、观察、Owner、准确来源及完整原断言；摘要不代替原断言中的全部子条件。
3. [未签署决定模板](decision.UNSIGNED.json)：actor、time、authority、effective_at、decision_text 均为空。模板中的“建议措辞”不代表任何人说过或批准过；本轮派工和 START_PREPARATION 不是采纳。
4. [完成门差异提案](gate-proposal.md)：仅 ITER/REL 白名单；原路径、其他123目标和共同门保留。实际工具、总台账和原 WP05/adoption.json 均只读。
5. [来源锁定](input-lock.json)、[完整来源校验](source-verification.json)及[准备包文件哈希](preparation-manifest.json)。

## 固定版本及不同计数

组合 manifest SHA-256：`c5c926728287ac9ee0b5048300deefa789ec7c019ed88168bd74e120dc53ebf2`。

原 1.0.0 manifest SHA-256：`979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee`。六份规范文件和补充四文件的字节身份均列入 input-lock；取固定 Git blob，不能用 Windows CRLF 工作树字节替代。

| 视图 | 本次核对的数量 | 可以说明什么 |
|---|---:|---|
| 可恢复场景 | ITER 99，REL 122，共221 | 目标限定的原断言与别名逐条承接；共用 FB 不等全域独立场景 |
| 正文定位 | 295 | ITER REQ30/BR40/FLD78/PROC20/逻辑字段93/DP8；REL动作18/页面字段目录8 |
| 补充来源映射 | 43 | 工作簿18、CP22 15、R1 10；复用映射不新增原 AC |
| 目标 R1 | ITER2、REL6，共8 | 精确运行集合；不替221、295或43 |
| OA/ENG细化 | 10 | R1细化视图，与上述场景有重叠 |
| 矩阵行 | 577 | 上述不同视图的导航行数；不是独立义务总数、业务成绩或覆盖百分比 |
| 历史旧清单 | 声明30/48 | `NOT_INVENTORIED / UNRECOVERABLE / UNPROVABLE`，旧成员及缺件hash仍null |

39 个登记输入全部按原始字节或准确历史 Git blob核对，892 个结构化引用出现位置全部检查（包括目录指针、原件行、Owner分配、解释补遗、正文范围、工作簿单元格、补充原文）。这不等于逐字重审每份外部文档、Office布局/公式重算或运行验收。登记 Git 输入对本轮基线的差异仅 roadmap；历史规范来源未被偷偷换成新版本。

## 本地复现

从仓库根执行，Python 使用兼容3.13，`<archive-root>`指向只读原归档所在仓库根。本包不访问网络、服务或数据库。

```text
python -X utf8 eng/wp01-01/preparation/prepare.py --root . --archive-root <archive-root>
python -B -X utf8 eng/wp01-01/preparation/test_comparator.py
python -B -X utf8 eng/wp01-01/preparation/test_bundle.py
python -B -X utf8 docs/development/tools/manage_roadmap.py
```

`prepare.py`只写本准备目录的派生矩阵/模板/校验记录，不执行原始材料内程序。比较器是纯内存提案模型，不是产品入口；不接入 manager、签署验证、真实证据解析或远端包含性证明。测试中的正例全为合成形状，最多返回 `proposal_conditions_satisfied=true`，始终 `actual_adoption=false / actual_completion=false / scope=PREPARATION`。

本地原始输出、失败轨迹、环境和未验证范围见[验证记录](validation.json)。`tdd-red.txt`保留最初缺实现的失败；`tdd-malformed-red.txt`保留新增畸形输入测试捕获的失败/异常，不把它们改成成功。没有取消或触发 Actions，没有改变触发器，没有产品/数据库/依赖/发布改动；发布及协调状态同步由父对话独立处理。

初次成功runner遗漏实际第四份状态文件的hash，独立纠正说明及只读核对见[记录修正](bookkeeping-correction.json)，原成功记录保持原样。后续可用 `python -B -X utf8 eng/wp01-01/preparation/run_checks.py --archive-root <archive-root> --run-label <new-label>` 留下独立日志；重复label拒绝覆盖历史。UTF-8 JSON/代码固定LF，原始文本日志以`-text`保留实际输出字节。

准备材料可交审查，正式采纳、完成门实现、实际Owner/宿主/worker/持久化验证、每项业务证据和各仓远端交付仍是后续工作。决定签署后也不能据此关闭 ITER/REL 或 WP01 汇总。
