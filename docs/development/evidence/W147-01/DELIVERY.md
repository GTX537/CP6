# W147-01 原生单位 Owner 与原产品单位绑定

Main基线cbb2b17197349a3a8c202b137d3ffe8bbfba0dc6；COORD本对话单人，固定DEV-1/DEV-2复用，无新增工作树。服务于WP04-33和保留的原D3直接前置，不完成整张WP04-33或七天目标。Main真实START至当前登记72.28分钟，原完整范围估计180–240分钟；Handoff时间不叠加，最终以官方finish及本机wave147-final-verification.json为准。

## 实际交付

新增Main.Erp.UomProfile.v1独立入口/api/uom-profiles/owner-v1及页面/erp/uom-profiles。稳定Tenant+UUID与原样Code、创建者UUID、单调版本/原始UTF8/摘要；BASE/RATIO、量纲、0..1024精度、显式正整数比例、可选精确包装倍数和停用均为追加版本。BigInteger换算只在源/目标精度、同准确BASE及包装倍数内能无损表示时返回，不舍入、不默认1、不跨量纲。不从旧M_GenericCode Unit、名称、代码相等推导资格，停用不释放身份/代码。

独立erp-uom-profile query/add/edit定义，范围1按创建者UUID，2/3/4为原生uom-profile部门绑定，5当前租户。创建部门来自当前可信creation-context，实际一次性创建能力绑定同一DbContext/事务/Actor/Tenant；新增映射后只更新获准的组织版本并做最终重核。无自动角色授权，旧部门kind不丢失。原生存储必须持有实际Main事务及本存储锁；跨DbContext、错误资源或仅声称持有fence均拒绝。

配置/产品绑定版本、部门映射、审计及MainOwnerOperationJournalV1原结果同一Main事务。CAS竞争仅一个后继；提交失败全回滚；相同OperationId/准确输入返回原结果，异体409，UNKNOWN只查/关闭，关闭挡迟到。原历史/结果始终受当前权限控制。浏览器仅保存按可信Tenant/Actor/类型分隔的公开恢复引用，Web Locks不排队重发，身份切换不接收旧响应、不清他人引用。

原产品绑定Owner Main.Erp.ProductUnit.v1精确固定ProductMaster原UUID、实际身份源版本/摘要/QtyUnit和准确单位版本。要求当前erp-product edit与独立uom-profile query的两对象范围；无单位不回落EA，无缺源补造。产品/profile/BASE更新、停用或ABA后当前绑定拒绝，精确历史保留并声明当前适用性；Editor仅提供当前原产品源及旧绑定头的CAS修复观察。原CommercialProductIdentityV1响应保持，产品主账不复制。

## 实际验证与复用

- 真实红灯：red-20261026T145708Z.json中真实登录/Kestrel的creation-context为404，新增Owner后通过；scoped中尚缺产品绑定404及测试观察器SQL字面量FormatException均保留，后者改为decode('7b7d','hex')，未伪造成功。
- final-20261026T153958Z.json：43/43实际PostgreSQL18及Program/Kestrel/HTTP相关检查，0跳过；含单位隔离/创建者原UUID/部门2、3、4/撤权、原操作/关闭、实际预提交故障回滚、竞争CAS、BASE漂移、13/18/1024精度和巨大整数比例、原产品ABA及Editor/历史，以及27项受影响既有产品/计划/部门行为。
- migration-20261026T155438Z.json：最后菜单ID上限兼容改动的定向正向迁移1/1通过，保留旧菜单int.MaxValue和原Unit代码，不自动生成profile或角色授权。与上述43项无重叠，合计44项；未把它们写作一次新的44项运行。该最后改动只影响菜单新ID分配，未重跑未变化的成功Owner行为。
- ui-final.json中恢复6与IAM映射20成功结果直接复用；收尾扩展的身份切换用例在view-identity-red.json真实失败（旧数量仍显示），清空当前内存数量/搜索/选择/草稿后view-identity-final.json最终页面6/6通过，替代旧页面6项。共32项去重、0跳过，未冒称重新执行32项。覆盖原串精度、显式创建部门/准确选择引用、UNKNOWN查/关闭而不重发、跨标签/身份隔离、产品原UUID及刷新CAS、失效换算结果及旧输入清除。组件API mock只为页面边界，不冒充真实业务。
- 最后身份输入修正后，vue-tsc与Vite构建均exit0（typecheck-identity-final.log、build-identity-final.log及ui-closeout-execution.json记录实际命令/退出）。保留既有体积警告与此前构建记录。最终web-closeout构建是dev/unknown测试资产，无签名/发布身份。本地原始记录统一在D:/CP6-local-test/wave147/foundation，汇总final-validation.json记录真实来源与适用范围。

隔离数据库仅由已有PG fixture创建/迁移并停止自有进程；故障注入为真实提交前回滚验证，不声称真实网络未知提交。无W147真实浏览器端到端、生产/完整业务验收、既有环境迁移、Docker/socket或Actions运行。原产品wire及规划写503保持。

## 一次任务级 SPEC/QUALITY 与收尾

本COORD集中检查最终完整差异、租户与对象范围、实际事务绑定、不可变历史/FK/参数SQL、CAS/恢复、迁移兼容及无自动授权、精度与页面边界、原始成功/失败记录；无未解决实质合并阻塞。后续仅普通台账/链接/生成进度校验，不为文档重跑产品测试。完整最终diff及审查结论保存在本机wave147-main-review.diff/json。

Handoff已正常PR采用：https://github.com/GTX537/CP6.AI-Handoff/pull/123，原消息source 94d2406cd0754fd557b4388dbbf0819568701ddc，目标头1a939148968245096e833d4140cee80577975885，准确分支已删除、DEV-2已finish释放。Main在本报告提交时仍待正常PR、远端精确采用、分支清理和finish；快照如实保持未收尾，最终proof由后继准入一次归档，不在main补写自证提交。

本轮只写一项有限记录WP04-33.wave147_uom_owner；正式375卡状态、125完整目标、247运行验收及41历史静态范围不提升。原七天仍ACTIVE，仅D1/D4阶段闭合，这个2/7不是工作量完成百分比。

## 剩余原范围

币种原身份、双Owner工程映射、完整产品子图和最终HoldExact仍须实现；本单位绑定不授予制造、Transfer/REP/RMA或SCHED资格。后继按原合同继续币种/工程→冻结工程基线→WP08-17意图→正式WO Root/Snapshot/Occurrence五域及SCHED同一UoW/UI。D2/D5/D6/D7的原出口全部保留，不因本次提供者完成而缩减。
