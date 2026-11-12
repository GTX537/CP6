# W501-01 询盘合成夹具完整性

新增 `CRM-INQ-FIXTURE-INTEGRITY/20261112-R1`。输入为 W82 告知摘要修订的准确派生件 `8255b4dd3f07481082a3d2b9fe1f4b85582cce8b8e3682b846769cc7e00d45ce`。原 FX25/FX26 多处 `transaction.writes` 未随写集变化更新，含两条零写入的重放和冲突。原 FX26 的 ETag 和后续告知内容曾更新，七个 `bodyDigest` 仍保留旧正文的值。修订使用原写集长度和原规范化 JSON SHA-256 规则重算这十四项，逆向替换逐字节恢复 W82 父件。实际派生 SHA-256 为 `addf7f1d62bff189adcd8c526657210e2420f93ae7537d27e0da132467ed0dc4`。 [修订](../../../design-handbook/remediation/addenda/CRM-INQ-FIXTURE-INTEGRITY-20261112-R1/profile.json) · [验证器](../../../design-handbook/remediation/addenda/CRM-INQ-FIXTURE-INTEGRITY-20261112-R1/verify_fixture.py)

## 本地 Python 3.13 实测

| 输入和命令方式 | 实际结果 |
| --- | --- |
| W82 父件 `--parent` | 静态 PASS，退出 0。28 个家族、136 步、30 份阶段快照、11 个正文摘要通过。验证请求、响应、时钟、合成来源、前后图摘要、主键、写集及伴随行。逆向核对原字节。 |
| W82 父件 `--fixture` | 静态 FAIL，退出 1，定位 `FX25-S06.transaction.writes`。同一回执的前一次 PASS 被替换。 |
| 派生件恢复一个旧响应摘要 `--fixture` | 静态 FAIL，退出 1，定位 `FX26-S02.expectedHttp.bodyDigest`。 |
| 将 W82 原件路径当作 `--report` | 参数拒绝，退出 2，核对父件 SHA 仍为原值。 |

复现入口为 `python -B verify_fixture.py --parent <W82 fixture> --report <external receipt>`。加 `--output` 可生成一份新的外部派生文件。原命令、证据和两次负例在 `D:/CP6-test-artifacts/W501-01/final-validation-runs.jsonl`，最后一个回执为故意执行的摘要负例，正向 PASS 由日志第一条固定。

AC-INQ-035 采纳完整合成夹具的类型、来源和一致性。AC-INQ-060 采纳实际静态通过、失败与未运行记录的区分。业务执行和 CRM 采用均为 `NOT_RUN`，当前 PostgreSQL 外键、非空和事务行为在 AC-INQ-047/057 继续验证。原询盘 51 PASS / 9 NOT_RUN。本轮主仓集中审查并核对结构、链接和差异，Actions 运行及取消均为 0，部署 0。分支与 DEV-1 处置以主仓 PR 为准。
