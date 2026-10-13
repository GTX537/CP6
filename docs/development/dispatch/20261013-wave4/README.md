# 第四轮开发派工

上一轮四项合同实现已合并。当前12/375任务DONE、S01为8/12；本轮四项READY/CONFIRMED，尚未收到START。业务目标0/125，247运行场景NOT_RUN、41历史静态REFERENCE_ONLY保持原记录。

| 沿用对话 | 本轮任务 | 仓库 |
|---|---|---|
| A（原S01-03） | [S01-11 ERP-CREDIT 原生金额与冻结重放](S01-11.md) | Main |
| B（原S01-04） | [S01-10 PostgreSQL 精确存储适配](S01-10.md) | Main |
| C（原S01-06） | [WP02-01 Platform 可信请求、消息与后台租户上下文](WP02-01.md) | Platform |
| D（原S01-08） | [S01-07 原生 Ref/Scope 与 NCR/IPQC 类型](S01-07.md) | Main |

四个独立工作树和互斥写入范围已核对，准确路径/基线/profile/向量/固定来源见[机器执行包](dispatch.json)和[准入证据](../../evidence/wave4-dispatch-20261013/README.md)。A/B/D使用Main新树，C切回Platform新树，D从CRM切回Main，不在旧任务树继续开发。

S01-10在自己拥有的隔离PostgreSQL测试资源中做真实存取测试；本轮仅预约本机端口55410及任务目录，尚未初始化或验证数据库，现有5432服务不属于该窗口。数量金额保留任意精度合法输入和原字符串，INV66字段逐项说明scale语义。WP02-01补消息/后台显式上下文及可信来源证明，保留已有HTTP接口，不把既有实现虚报为缺失。

共享Wire、叶子项目、原输入包、真实Owner与既有数据库写入口只读；仅独占文件例外见任务卡。Main台账、生成视图和四状态页由COORD维护。各任务先提交START，再按完整任务交付，依次审查和合并；消息发布不代表聊天已接收或已开工。

S01-12等待S01-07/10/11；WP01-01还需WP05候选正式采纳，不能仅按依赖图自动开工。本轮不启动这些任务。后续数据库唯一方向为PostgreSQL；真实Owner、主机采用、迁移和业务验收仍按各自任务执行。

[Handoff协调入口](https://github.com/GTX537/CP6.AI-Handoff/blob/codex/dev-coordination-20261012/handoffs/CP6-DEV-20261012/README.md)。COORD合并此分配后在新/旧任务replies追加固定Main位置并回读；继续使用四个现有对话。
