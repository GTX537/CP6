# W229 退货额度与独立授权验收

正式退货申请已接入同来源坐标额度、独立批准/拒绝/撤销、不可变批准份额和原命令恢复，实际页面已采用。

明确模拟签收/发票/历史占用供生产验收，实际PostgreSQL事务、当前权限、政策和独立审批逻辑运行。来源版本不重置占量，撤销保留已收货/未知段；没有真实数据不再构成等待。特批、正式实物入库、下游处置与商业/财务仍待实现。授权待发事件已同事务保存，尚未接入发送器。

[合同与范围](../../rma-authorization-v1.md)。

验证：10 distinct PostgreSQL/API scenarios PASS (9 authorization plus existing formal application HTTP), Vue22 PASS (7 authorization plus15 parent), vue-tsc PASS. Initial authorization final8/9 retained: expiry fixture incorrectly equated source-wide remaining1.62345678 with this application available0.62345678; corrected both assertions. Task review changed absent-decision HTTP from implicit204 to explicit200/null, then only expiry and actualHTTP reran2/2. Final provenance UI7 and typecheck passed; unchanged successful tests reused. Complete simulated source cuts, simulated actors/policy and explicit downstream receipt/unknown states used; actual Owner/API/UI executed. No Actions/deployment/existing-environment migration.

审查：One complete task diff review: exact immutable sources and coordinate identity; decimal interval arithmetic and PostgreSQL serialization; tenant/current scope/fields, independent actor and policy CAS; approval/share/claim/outbox/original journal atomicity and concurrency; revoke/expiry retained consumed/unknown ranges; read-only original recovery, no stale UI or private localStorage data. Absent-decision HTTP corrected and focused regression passed. Internal typed contracts trusted; no added qualification wrapper or repeated broad gate. Formal IN, exceptions and outbox dispatcher explicitly remain open.

证据：D:/CP6-local-test/wave229/foundation/ 下 related-20261029T095518Z.json, authorization-final-20261029T100943Z.json, corrected-boundaries-20261029T101259Z.json, web-related-20261029T100828Z.json, web-provenance-20261029T101259Z.json, web-typecheck-20261029T101259Z.json及对应日志/TRX。模拟业务数据可按用户决定用于生产验收；本报告验收本轮额度/批准/撤销与页面恢复，不将尚未实现的实物入库写为完成。

预计150–240分钟，截至快照登记43.61分钟。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/205)已合并，分支删除且DEV-2释放；Main待正常PR、远端采用和finish。最终生命周期见wave229-final-verification.json。固定目录、缓存及用户资料保留。

下一步：Adopt approved return shares into formal inbound in the same PostgreSQL transaction using complete simulated receipt/source/stock scenarios, then close remaining original D2/D3/D5/D6 exits and D7 integration.
