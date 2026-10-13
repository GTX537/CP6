FIN-CREDIT 的三个原生金额域需要保留 38 位精度、最多 8 位小数，并在最终字段边界检查符号和规范词法。新增 `CreditAmount`、显式 profile、字符串 JSON converter 和字段 attribute，使用 BigInteger 系数/scale 精确比较及加减/累加；中间越界可恢复，最终非法结果拒绝输出。

本地执行原任务过滤命令，实际 .NET 8.0.29 / net8.0：**205/205 通过，0 跳过**，含全部 25 条冻结向量、原生 4/33/5 字段绑定、profile/token 误用、10,001 位中间系数、20,000 项累加及严格 JSON/原字节保持。完整 RED/GREEN、源码/依赖/产物摘要与审查见 `docs/development/evidence/S01-03/`；初次 PowerShell 日志捕获中断如实保留，修正记录器后的完整 RED 同样为 204 失败/1 环境检查通过。

本 PR 交付精确值库；现有授信服务的业务采用、存储与完整财务场景仍待后续任务。共享台账/生成页/四状态页由 COORD 在交付窗口同步并串行合并。未运行 Actions、数据库、迁移、包发布或部署；发布前检查候选和 protected main 的真实工作流事件。
