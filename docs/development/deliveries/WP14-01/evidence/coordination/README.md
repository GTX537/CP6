# WP14-01 协调采用说明

协调从已确认的 Main 主线创建独立任务分支，保留原候选 `bd8b7528580567f4b66cbbf065fcebfc472c3a14` 的提交和全部准备文件。实际接收/准入为 [intake.json](intake.json) 所记时点，不回填旧准入。独立集中审查见 [review.md](review.md)，未发现阻塞问题。

`WP14-01-PREP/1.0` 是固定 `df5553fd04ec4fc516602a4e2843650c38ae5006` 的准备快照。当前 main 的完整 roadmap 字节已经改变；本次核对生成器实际读取的 targets、revision_acceptances、target_task_coverage 子集不变，原验收、修订目录、任务定义仍相同，因此复用原映射检查，不重写历史 input-manifest 或冒称旧检查在新主线执行。具体源变化/子集身份见 [current-inputs.json](current-inputs.json)。当前领取及生成页另执行台账结构检查。

S01 输入包 `CP6-S01-CONTRACT-INPUTS/1.0.0` 已由实现 `4258f7b66490f0b49601e5e5033d8508fc5eadbd`、PR176 和状态 PR177 交付。准确回执见 [S01远端核对](../../../../evidence/S01-01/remote-main-check.json)。33来源、16 profiles、122向量是可消费输入；S01-02 等真实消费者仍未实现/验收，不能把该输入包当运行证据。

S02-00 的 `cp6-pg-tenancy-v1` 设计已经通过 PR178 合入；27个新增设计场景仍NOT_RUN，并由对应实现任务采用。它们不挤入原247修订ID的固定分母。WP05-01 新替代依据只有正式采纳后才作为有效附加验收来源，由 WP01-01 和后续全链运行按准确版本增量引用；本快照不将不可恢复旧30/48清单改成 COMPLETE。

125目标业务交付仍0，247运行场景仍NOT_RUN，41历史静态REFERENCE_ONLY。已具备的是验收责任、独占数据及证据需求；真实Owner、独占PG环境、失败注入、跨仓当前包和运行见证尚未执行。无Actions、数据库操作、迁移或部署。
