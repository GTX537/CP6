# W213-01 — 容量份额与原子转化底座交付

COORD 单人在七天目标 D2 的直接前置中实施。基线Main `7e287d305807225d12f6c44a44b1dd0ec5831769`，分支 `codex/w213-01`，DEV-1固定目录复用；未新建源码树。W212只归档一次，原125目标/247正式验收/完整任务状态保留。

## 已实现

- 内部PostgreSQL容量域，实际Main事务、租户/环境/Sandbox隔离，按库位/指标而非产品的确定顺序锁；准确单位/精度版本不可通过另建域绕开。
- 不可变实存与未收份额、Owner/Lineage/Share身份和别名映射。部分收货准确消费未收区间并登记物理份额；60+40转10后70+30；同义务别名不再次加量，独立新增不能任挑旧义务。
- 准确范围的释放和重叠拒绝，原稳定操作输入/结果持久化；当前前驱、容量条件、数量上限同锁核对。内部保存点与调用方事务保证失败回滚，原响应丢失可查回同结果，不重复效果。
- 任意精度整数比例换算，八位小数和最大合同量无损；零指标仍保留非空份额范围。数据库拒绝改写原件、重叠/错误比例及余额与事实不一致的提交。

合同与采用边界：[容量份额存储V1](../../capacity-occupancy-store-v1.md)。没有HTTP/DI注册，没有T_Stock/Selector/旧移动任务写入。结果固定RECORDED_FACTS_ONLY/admissionGrant=false；域初建零值不证明现场空库。

## 实际验证和审查

12 actual isolated PostgreSQL capacity behavior/migration tests PASS;0skip;no UI change。精确运行命令、TRX、日志及源码blob绑定在 `D:/CP6-local-test/wave213/foundation/final-validation.json`。包含实际数据库锁等待观察、第二事务读到已提交余额后拒绝超容、调用方业务行与转化共同回滚、重新创建上下文恢复原结果、别名重放/异义/前驱冲突、单位版本、非零区间、精确换算、最大数量/溢出、租户环境隔离、原件不可改与余额提交约束。

首轮编译暴露测试Space类型与已有命名空间冲突，改为明确类型别名；原失败日志保留。集中作者SPEC/QUALITY检查确认正数来源份额允许对应零容量指标、零不是空库证明，并核对完整diff/相关链接/台账范围。11项行为用例通过后，仅追加并执行1项中途写入失败/净增量专项；运行时代码、迁移及原11项测试未改，复用其原始通过记录。后台构建测试期间未编辑后台输入。没有前端变更，不运行前端或无关全仓验证，不用用例数计算目标百分比。

## 仍未完成

真实源/Assignment/KNOWN_NONE解析、独占委托与完整Owner覆盖、实际Stock Root/Claim/来源预算及Journal/Outbox共同事务接线、跨组件统一锁计划、ScopeGate/PrepareProtection、08E生效与正式Transfer/Stock仍在后续范围。这里的原件/换算/关系/当前条件引用只作内部存储输入，尚未由真实Owner解析，不可包装成生产准入。Sibling Main行回滚验证不冒充已采用Stock数量写入。

D1/D4已完成；D2/D3/D5/D6/D7原出口继续保留。当前七天持续目标ACTIVE，未声称完整MASTER/Stock或企业验收完成。无GitHub Actions、既有环境迁移、生产部署或保护规则修改。

## 交付与资源快照

Handoff已正常合并 [本轮PR](https://github.com/GTX537/CP6.AI-Handoff/pull/189)，源 `4dda4884760c2c1581f5f97e8841723a5a28ec67`，通信目标 `8cc0f942ddd5fa7740c58af8bd83b3db024b834c`；准确任务分支已删除，DEV-2已finish释放。Main在此报告提交时待正常PR/finish；后续最终证明将记录远端采用、条件分支删除、固定席位干净detached及释放，不能提前把自身收尾写成已完成。

登记实际已用 11.37 分钟，预计50–75分钟；完整Main START到finish时长以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave213-final-verification.json` 为准，Handoff不重复加时。分支处置、工作区状态与席位释放在最终回复和检查点报告。
