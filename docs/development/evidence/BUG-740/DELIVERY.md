# BUG #740：APP业务Key原合法域

[Issue #740](https://github.com/GTX537/CP6/issues/740)，基线`a701259004bb1137db073cbe7a0a6e05277d77e4`。COORD单人固定DEV1，独立分支`codex/bug-740-app-key-domain`。

APP绑定与提交入口恢复原oa.app/2.0允许的100字符bizType/subjectKind，按Unicode字符计数，101明确拒绝，原字符串无截断或规范化。

原OA-APP-01 R2 §4规定Key1..100，Subject与Binding的bizType/subjectKind均Key。现绑定与提交入口误限制50；绑定还按UTF-16单元计数。新测试在旧实现先得到3个明确失败：实际HTTP BINDING_SHAPE_INVALID，普通/补充字符两种wire输入SUBMISSION_SHAPE_INVALID。修复不改既有原包、内容摘要或业务Owner身份。

6项相关本地行为通过、0跳过：真实Main登录HTTP→PostgreSQL保存100个补充字符业务Key及100个中文主体Key，原件/重放/历史完整；101分别拒绝且无新命令。两个wire边界与既有封闭输入、三层去重/alias、跨Scope/伪造冻结事实拒绝回归通过。

一次集中审查三个产品/测试文件：边界恢复原100字符域，使用现有Rune语义，不改DTO/摘要/Owner/UUID/原授权/事务；PG text列无需迁移。绑定原Key helper的既有flowKey/表达式上限数值不变，字符计数一致；既有闭集/规则行为已回归。业务Owner对冻结事实仍按其原合同核验；wire解析通过不等于实际业务受理。

完整证据`D:/CP6-local-test/bug740/foundation/final-validation.json`。本轮不是全仓回归；未改UI，不跑前端检查。新增真实HTTP从现有已登记Owner选择业务，读取与同命令重放返回完整Key；101边界之后仍只有一条配置命令/历史，无实例或新业务效果。入站解析100字符只证明wire合法，未知或未采用Owner仍按现有业务证据边界拒绝；不能把本轮测试包装成任意Owner端到端提交。原失败日志保留。

PostgreSQL既有biz_type/subject_kind为text，无数据迁移/转换，旧合法输入保持；不引入SQL Server要求。无Actions、生产部署、已有环境迁移、额外工作树或子代理。

原375保持29DONE/2IN_PROGRESS/344PLANNED；原59局部5闭环/54剩余。WP12-06仍待完整alias/身份适用及原卡核销，不能以本缺陷修复自动DONE。后续24h末盘点准确原59真实余项与未完成的整体业务目标。

Issue仅在远端main准确采用后关闭；分支定向删除、工作区干净、官方finish释放DEV1以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug740-final-verification.json`核销。固定目录/缓存、根用户归档保留。
