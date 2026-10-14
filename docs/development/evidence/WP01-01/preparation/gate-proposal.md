# PROPOSED：仅ITER/REL替代完成门差异

这是供独立后续任务评审的逻辑差异，**不是可应用补丁**。当前 `manage_roadmap.py`、roadmap、原WP05制品及adoption.json未改变。`gate-contract.proposed.json`仅是本准备比较器的输入模型，不是已批准生产schema。

| 门 | 当前行为 | 后续提案 |
|---|---|---|
| 原验收路径 | original_acceptance_inventory=COMPLETE且非空原用例全PASS、有真实执行证据 | 原路径原样保留 |
| ITER/REL | 缺失原清单阻DONE | 仅这两个准确目标可增加独立新依据路径；不改原清单字段 |
| 其他123目标 | 原完成门 | 完全保留；新版本/采纳/矩阵不得作为旁路 |
| 共同门 | R1全PASS、实现验证、远端main交付与必需主体任务 | 保留并核每个实际消费Owner/宿主/worker/持久化及跨仓合同 |
| WP01汇总 | 独立检查所有原清单COMPLETE | 当前仍阻塞；本提案不修改汇总门，也不声称替代路径能满足它 |

```text
可讨论的目标完成 = (原路径 OR 目标属于{PLM-ITER-01,PLM-REL-01}且新路径有效) AND 共同门
新路径有效 = 准确版本+组合/base manifest及全部制品完整hash
           AND 可核具名正式采纳，准确actor/time/authority/effective_at及目标
           AND 明确“以当前可知义务建立新标准，无法证明旧30/48逐项等价”
           AND 旧NOT_INVENTORIED/UNRECOVERABLE/UNPROVABLE及null成员/hash原样保留
           AND 可知正文/场景/工作簿/CP22/R1所有适用子条件有真实业务证据
           AND DP与能力缺口有准确授权后继决定及实施验证
共同门 = 全部必需主体任务交付 AND 目标R1运行全PASS
       AND 真实Owner/页面/worker/持久化/权限租户并发专项证据
       AND 各实际依赖仓远端main包含任务及准确包/合同消费证据
```

ITER当前主体任务：WP05-18/19/20/21/61。REL：WP05-32/33/34/35/36/42/61。不得只检其中一项；WP05-01设计、WP01-01门工具、WP14-07跨域验证及每项实际前置仍按任务卡/当时主线履行，不因不在此主体清单就免除。跨仓范围以真实消费关系明确，Main采用不能借Platform/CRM静态测试或运输ACK。当前主体任务只确定Main；三个仓的基线只是证据库存，不自动建立普遍CRM依赖。模型将准确provider/consumer跨仓依赖闭包标为UNRESOLVED，提案比较也据此拒绝；后续须按已接受合同和当时实际前置审定适用仓集合及逐合同收据，不能省略真实跨仓交付。

证据至少绑定本版hash、逐ID及其全部子断言、源身份/Owner、实现SHA/包版本、PostgreSQL/迁移身份、隔离环境、可信Tenant/Site/操作者/权限、原操作键与载荷、输入输出、真实持久前后状态/回执/审计、时点及证据hash。不能只填一个PASS；STATIC、REFERENCE_ONLY、合成Owner、未跑/失败/受阻皆不能作业务完成。复用同一次运行须逐项显示各Owner的独立责任。

未来如获准调整WP01汇总，必须另行给出准确范围和测试，把“123目标原件完整＋2目标采用新标准”与“125目标原件完整”分开。当前汇总门仍保持原逻辑；不能在本包或任务合并时顺手更改，也不能把original_inventories_complete增加2。

## 准备比较器的严格限度

`compare(candidate, contract)`是纯内存的结构检查。contract由已固定矩阵和当前台账产生；检查版本/hash、白名单、采纳形状、历史标签、准确义务/任务/R1/仓库/DP集合及记录必需字段。它不读取证据文件验证实际内容，不验证签名/authority/可信时间、不执行服务、不核远端祖先关系。即使用户输入完全虚构而格式正确，也只能得到提案条件形状满足，永不产生真正采纳或完成。

输出恒定 `scope=PREPARATION / actual_adoption=false / actual_completion=false`。正例测试全是人工合成的数据形状，里面`signed`或`synthetic=false`只是被模拟的输入断言，绝不是实际签署或真实业务；真实模板保持UNSIGNED/null。这个工具不可被生产manager直接调用为授权判定器，后续实现须另做可信解析和实际证据验证。

反例覆盖：无采纳、缺actor、错误组合hash/采纳hash、错误目标、缺或重复义务、静态PASS/合成运行冒充业务、旧清单标签改为COMPLETE、其他目标绕门、缺主体任务、缺R1、缺跨仓、DP未解决、缺持久前后状态、错Owner目标、FAIL/BLOCKED/NOT_RUN/REFERENCE_ONLY及畸形JSON结构。另以实际台账枚举所有123个其他目标验证拒绝。

后续门修订需独立版本、迁移/回归与协调串行窗口；本包不创建任何真实批准记录、目标DONE、状态迁移或部署凭据。
