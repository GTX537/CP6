# W294-01 FLOW发布预览实际引用影响

W294：FLOW发布预览复用真实当前引用影响统计，纠正旧未采用标记并在既有页面展示；当前租户/权限、原pin/启停和发布原事务保持。

基线 `16da9575339494871e18f7641410d537293ec44f`，COORD单人固定DEV1/Main与DEV2/Handoff，沿[24小时实际执行计划](../../planning/20261102-codex-24h-oa.md)。

## 产品与验证

FLOW发布预览在原当前授权和WF_AUTH事务内复用已有DefinitionRuntimeImpactReader，返回当前租户/环境下实际运行/挂起、根/子实例、表单入口/APP绑定和固定上游版本数量及观察时间；旧NOT_ADOPTED标记改为当前准确运行采用状态。响应只含数量，不含主体、实例或其他定义标识。统计是观察摘要，不纳入发布内容摘要，不因运行数量变化造成虚假预览冲突；正式发布仍按准确schema/依赖/current权限及到期核对。FLOW本身没有个人表单草稿，未将统计中的0冒称关联表单没有待重基草稿。

发布页复用现有引用统计组件，明确统计仅限当前已采用运行、不包含旧历史流程，绑定数量不意味着所有入口立即改版，启停和在途pin保持独立。草稿修改导致原预览失效时一并清统计。没有复制统计SQL、增加新审计层或修改发布命令。

3项实际PostgreSQL18行为通过，含新增真实Main HTTP/跨租户影响统计、原pin/activation保持与当前撤权，以及既有提交未知原结果和最终预览到期回滚；8项相关Vue行为及应用vue-tsc --build通过，0跳过。未执行新浏览器到数据库全链。

证据 `D:/CP6-local-test/wave294/foundation/final-validation.json`。新PG测试实际创建两个不同租户的同Key运行，真实Main HTTP只返回本租户数量；随后发布新版本仍保持原activation、旧实例pin和原任务，撤销发布权限后HTTP403无统计披露。业务/IAM输入模拟，数据库、Owner、登录会话和HTTP实际执行。前端组件API模拟，不冒充新浏览器到数据库全链。

先红后绿：impact-red实际收到NOT_ADOPTED；ui-red无引用统计；原失败保留。集中审查完整产品/测试/报告diff，额外观察只接既有授权查询，非发布准入真相；原预览过期最终回滚和提交未知后原结果/当前读权定向回归。没有新迁移/依赖/工作流。

## 原WP12-02范围核对

| 原卡要求 | 当前有效来源 |
|---|---|
| 准确draft/schemaDigest、完整FORM/子FLOW依赖，提交同WF_AUTH重核 | [W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)：预览固定GUID/token/摘要，递归循环/深度/当前资格，原pin仍合法不换latest，最终权限/期限再核。 |
| 发布版/依赖pin/receipt/audit原子；独立activation历史/唯一form-flow CAS | W270/W272与[W273](../W273-01/DELIVERY.md)：原结果、未知提交、故障整笔回滚，PG条件唯一约束及两连接竞争，不暗停另一flow。 |
| 停用只阻新根、在途保持pin；旧publish/enable同门 | [W274](../W274-01/DELIVERY.md)、[W275](../W275-01/DELIVERY.md)、[W276](../W276-01/DELIVERY.md)、[W278](../W278-01/DELIVERY.md)实际根/子流程及页面；[W286](../W286-01/DELIVERY.md)完整旧caller处置。 |
| 发布不自动enabled/业务发起；切head不改旧pin | 上述PG证据和W278真实Chrome链，本次HTTP再次核对原activation/实例/任务未改。 |
| 原命令失响应恢复同发布版，竞争只有合法唯一binding | 服务端已有真实commit未知恢复；W273唯一绑定竞争已证。FLOW页面当前存在独立BUG736，因此本卡仍未完成。 |
| 过期/漂移/失权不得半发布，停用不称取消在途 | W270/W272最终门/故障回滚，W274–276实际运行pin与停新；本次到期/原结果回归。 |

来源提交、远端采用与适用证据已核对为当前基线祖先，见 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave294-card-audit.json`。这些是复用原执行，不称本轮重跑全部。WP12-01已由[W288](../W288-01/DELIVERY.md)完整核销；WP02-10/13的原限定TASK_DELIVERY已有Main实际fence/同事务原件审计采用，后续正式核销时按原绑定登记，不改前置定义。

实际服务节点执行属于其运行/业务Owner范围，不能因它未完就将原卡三项已交付范围反复重做；本轮也未称整OA-DEF目标完成。原125/247与其他卡分别保持。

## 余项与生命周期

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-02待独立BUG736原命令恢复修复与最终整卡核销。

[BUG #736](https://github.com/GTX537/CP6/issues/736)的未知后403/409、无关storage和损坏缓存丢原意图问题已登记，下一独立BUG分支先红后绿修复；未混入当前功能提交。修复远端采用前WP12-02不标DONE。

本窗估0.25–0.5h低置信，实际用时按START至最终两仓finish记录校准。当前是合并前快照，最终远端采用、准确分支处置、工作区干净和席位释放见 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave294-final-verification.json`。固定目录和用户原资料保留；24h目标继续。无Actions触发/取消、生产部署或既有环境迁移。
