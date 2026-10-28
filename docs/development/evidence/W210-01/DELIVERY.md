# W210-01 — 库位规则字典与准确来源绑定

COORD 单人。本轮限定交付原生版本化规则字典、真实 UOM/基准采用、候选不可变来源绑定与独立页面；原 WP10-01/WP10-10、125目标/247正式验收及七天完整出口没有关闭。[合同](../../location-rule-references-v1.md)，[执行范围](../../dispatch/20261028-wave210/README.md)。

## 交付

`Main.Wms.LocationRuleReferences.v1` 在真实仓库范围内以独立 rule-policy 动作维护明确指标、精度和适用域。当前 UOM query/范围、准确版本/原件、启用状态及实际 BASE 共同核对；同名量纲不推定可换算。字典数据只由有权人员配置，无自动测试码、角色授权或生产许可。

W209 候选增加显式 resolve。原生字典与单位来源绑定在新候选版本中持久保存，旧原件/摘要不变，变化草稿重新 UNRESOLVED。准确代码/版本、容量精度/包装倍数、空集合与无限期政策、If-Match/NO_CHANGE、完整字段权限、最终源撤权与同事务回滚均覆盖。RESOLVED 不是当前批准或库位可用凭据。

PostgreSQL 不可变版本、真实租户/仓库身份、连续前驱、当前原件和同事务 journal/audit；两套原操作 UUID 的查询、封闭、并发与回放。仓库字典独立页面及候选来源入口，选择真实原生单位、历史读取、原结果恢复、当前身份与晚回调清理。旧库位/仓库隔离与数量不变。

## 实际验证与复用

46 distinct PG18/Program HTTP; 0 ancillary checks; 44 distinct Vue;types/build PASS;0skip。实际隔离 PostgreSQL 18 和 Program HTTP；相关 Vue、types/build，通过一次完整作者 SPEC/QUALITY 审查。精确日志和源码绑定：`D:/CP6-local-test/wave210/foundation/final-validation.json`。

首轮 backend43：42通过、1夹具失败。PostgreSQL 原日志明确 WAREHOUSE_DRAFT_CREATE_REQUIRED：并发用例末尾 SeedMaster 旧直接插入被既有仓库草稿屏障正确拒绝；改用正式 WarehouseDraft Owner 创建第二仓库，保留屏障。前端首轮44：43通过、1非响应式权限 mock 场景错误；改为在 mount 前设置拒绝权限。原始失败日志完整保留。

中间两次编译失败分别为夹具使用不存在的 DTO 类型、补充断言括号错误；分别改取原 WarehouseType、拆开断言语句，没有测试运行结果或虚报通过。其中16项来源复测15通过、1项揭示旧共用 Text 校验器对不透明标识符额外禁止首尾空格；按原规格4.1.1移除此限制，保持非空白、UTF-16长度、控制字符和Unicode合法性检查，不裁剪、不改写身份。原失败均保留。

集中检查补齐同一指标单位须共享准确原生 BASE（不能仅靠同名量纲）、真实来源错误边界与整仓写权限。最终仅复测受共用标识符校验影响的候选和字典路径；未受影响的原生 UOM 成功案例按准确 case 复用，不把旧执行改称新执行。最终来源与计数以 final-validation.json 为准，不能当正式验收或完成百分比。

## 合并与生命周期快照

预计75–120实际分钟；本报告登记快照 26.69 分钟（Main START起，不叠加Handoff）。Handoff 已正常合并 https://github.com/GTX537/CP6.AI-Handoff/pull/186，source `a1e5c9f64cc096068cd03196bfa03994dabb32a2`，target `334e0254b4277b0014616db8b9be3a1a0ce354b0`；精确采用、分支删除和 DEV-2 finish 已核实。

此 Main 提交自身尚待正常 PR、精确远端采用和官方 finish；不提前写成已完成。完整完成证明在 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave210-final-verification.json`（实际收尾后生成），下一轮真实准入只归档 W210 一次并补记限定生命周期。W209已准确收尾归档；原全任务状态保持。

没有 Actions 启动/取消、触发器调整、依赖/构建配置更改、既有环境迁移、生产部署、新源码树或跨仓重绑。仅新隔离本地测试数据库；原 root 用户资料和可复用缓存保留。

## 仍需完成

真实规则送审/批准、当前时钟与非重叠占用、效果 Owner 保护、Selector 优先与实际应用/激活、正式 Transfer Request/Draft/Stock 共同事务；原 D3/D5/D6/D7 前置和完整出口继续有效。字典配置、保存时来源绑定及本地夹具不能替代这些交付或企业真实验收。
