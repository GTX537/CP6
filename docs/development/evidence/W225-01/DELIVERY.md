# W225 正式退货申请页面采用

正式退货申请页面已接入真实来源、精确数量、草稿编辑、提交/撤回、稳定列表及原请求恢复；发货页面提供显式正式来源保存，早期申请入口保留。

原请求正文在发送前保存至按身份隔离的IndexedDB；未知结果先查询，只显式重派原命令和正文，不创建替代单。列表冻结成员与版本，编辑保留原备注/证据，当前权限、CAS和字段改写在Owner边界检查。Issue #649修复清空只读备注漏检，同时允许保留原值编辑其他获准字段；本次页面采用直接涉及该入口，按AGENTS一任务一分支及集中审查规则在W225内完成。来源/权益覆盖仍为PARTIAL/UNKNOWN；未提供发货行/发票行筛选、完整Q03、附件上传或跨设备恢复，不宣称权益、审批、收货和完整D2完成。

[接口与限定范围](../../rma-formal-application-v1.md)。[字段权限问题](https://github.com/GTX537/CP6/issues/649)。

验证：Vue related suite 26 PASS; focused formal page suite 10 PASS after review fixes; final vue-tsc PASS. PostgreSQL entry/actual HTTP 3 PASS; focused remarks/lifecycle 3 PASS. Original remarks check reproduced one expected failure before the fix; original-condition failure retained. Actual HTTP ApplicationPage and RmaApplication pass accepted canonical schema. Unchanged W224 shipment/transaction evidence reused; no Actions, production or existing-environment migration.

One complete task review covers current tenant/object/field authority, frozen listing/filter cursors, exact decimal quantities, original-command persistence/query/replay, identity changes, legacy entry preservation and explicit shipment opt-in. Focused corrections cover read-only remarks, recovery warehouse selection and frozen source keyword. Internal references/facts are trusted, no added qualification/hash layers or repeated whole-repository gates.

本地证据：D:/CP6-local-test/wave225/foundation/ 下 entry-20261029T064659Z.json, focused-20261029T065540Z.json, web-related-20261029T064804Z.json, web-focused-20261029T065425Z.json, web-typecheck-20261029T065425Z.json及对应log/TRX；remarks-reproduction-20261029T065316Z.json保留旧判定失败。C:/Users/tt/CP6-planning-reports/20261017-foundation/wave225-schema.json引用原实际HTTP响应；首次验证脚本把camelCase样本名误作schema定义名，修正映射后通过，未改响应或规范。前端IndexedDB模块在页面测试中使用存储替身；本轮未做真实浏览器、目标环境或跨设备验收。

预计90–150分钟，截至快照登记37.81分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/201)已合并，准确任务分支已删除、DEV-2已释放；Main待正常PR、远端采用和finish。最终生命周期见wave225-final-verification.json；用户资料、固定目录和缓存保留。

下一步：Continue actual entitlement and independent authorization, then formal inbound consumption, preserving every original seven-day exit.
