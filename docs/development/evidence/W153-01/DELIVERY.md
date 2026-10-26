# W153-01 嵌套来源最终对象复核

本聊天COORD单人，Main基线42b7582507e1b6daa1f65b3b817b9f701f97cb79，固定DEV-1/DEV-2。[BUG572](https://github.com/GTX537/CP6/issues/572)为唯一活动缺陷记录。预计35–55分钟，Main START至本登记12.62分钟；最终以finish/proof计，不叠加Handoff。原七天目标ACTIVE。

## 根因、修复与实际行为

核对D3真实资源/物料前置时，发现OperationsAccessV1.ActionAsync只共享Authorities，而新来源拥有独立finals。OperationsOwnerV1.ExecuteAsync执行根ValidateFinalAsync及所有Authority复核，未执行来源对象检查。UOM准确来源读取会把RequireUomProfile登记在来源上，实际商品单位绑定/原结果查询因而遗漏这项提交前检查；原有权限版本、租户、身份与数据库锁仍存在，不能据此声称生产越权。

最小修复在新来源加入父访问上下文时登记其ValidateFinalAsync；缓存命中沿用一次登记，子来源同样连接其后代。回调在业务体完成后执行，因此包含来源创建后补登记的检查。SourceAsync与ActionAsync共用该入口；失败沿既有Owner异常/回滚路径处理，读取不返回结果，写入业务、原文、审计与原操作同事务回滚。移除ConfigurationRuleOwnerV1.Source原先的重复手工连接，保留准确对象的显式复核和所有原身份/规则/版本/权限判断。

没有新数据库表、迁移、Owner、API合同、依赖或页面。没有假造资源/材料数据，没有移除REP/RMA/WO/SCHED尚缺正式合同的拒绝门；此交付只是原目标D7必要集成缺陷修复。

## 实际验证与审查

先运行新测试，最初把编辑DTO属性ExpectedBindingVersion误写为BindingVersion导致编译失败，修正测试后才进行行为复现；失败日志保留。修复前4个实际PG用例均失败：商品单位绑定及原结果披露未触发最终来源故障，多层来源拒绝没有传播，缓存来源检查未执行。随后加入最小生产修复。

最终65个不同本地PostgreSQL18/实际HTTP用例通过，无跳过，具体名称/命令/结果在D:/CP6-local-test/wave153/foundation/final-validation.json，绑定最终源码和测试blob。新用例验证真实绑定头/不可变原文/含审计原操作均回滚、UNKNOWN可关闭并禁止旧键重放；读取拒绝披露而不损坏已有原结果，多层来源拒绝传播，缓存及晚登记检查提交前执行一次。受影响UOM/CFG及WMS/MES接缝按实际风险验证，未扩大成全仓回归。测试内故障注入属于本地恢复证据，不是生产故障记录。

UI/type/package未改变，不重跑页面或Vite；W152的50个不同页面/恢复/IAM和type/Vite成功只按历史未变输入引用，不计本轮执行数。无Actions/Docker、生产部署或既有环境迁移。

一次作者SPEC/QUALITY审查完整diff：来源树及缓存注册、延后回调捕获、异常回滚/披露边界、CFG准确来源检查保留、完整权限/锁门、真实测试红绿、台账/链接与范围。没有未解决实质阻断。共享入口改变因此额外覆盖实际WMS/MES来源消费，未重复全仓验证。

## 交付状态与原目标

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/129，source 3c4efb859e92fa774cd30b03c2b12be7c097f8a9，远端目标 86b61a7d75463cfb594bc2ec2423742146241fc7；准确分支已删除、DEV-2官方finish释放。Main此提交保留真实收尾前快照；待正常PR、远端采用、精确分支清理、官方finish及Issue关闭后，最终proof核实。首次BUG形式分支被官方canonical规则拒绝且未改动席位，按2026-10-20统一规则使用codex/w153-01，未绕过执行器。

W152仅归档一次，WP05-03/WP05-39两个wave152_configuration_rules记录均按最终proof关闭；本轮只新增WP02-13.wave153_nested_source_fence限定记录。375整卡仍24DONE/2IN_PROGRESS/349PLANNED；125完整目标0，247运行NOT_RUN，41历史静态保持。七天仍仅D1/D4整阶段完成，2/7=28.6%是阶段比例，不是工时/代码量完成率。Resume actual resource/material source prerequisites before engineering routing/BOM and original formal WO/SCHED writes; preserve all original D2/D5/D6/D7 exits and real external prerequisites.
