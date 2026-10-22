# W45 / BUG-401-MES-REALTIME：MES实时通道边界

七天一期D3安全前置，COORD本聊天单人。[Issue401](https://github.com/GTX537/CP6/issues/401)为唯一BUG记录。Main基线`c0a720c5905c7dcb58be9e5026445ee5e78162d3`，DEV-1 `codex/bug-401-mes-realtime`、DEV-2 `codex/w45-handoff`；07:10:38Z实际START，初估45–90分钟，按最终finish报告真实耗时。W37–W44已收尾不重复。当前本地候选已验证，正常采用/Issue关闭与finish待执行。

## 原步骤、根因与修复

真实API匿名negotiate曾返回200；实际SignalRMesNotifier的5类通知在两个租户合计10次Clients.All业务广播，MesHub按客户端编码加入无租户组。ControlTower直接显示这些工单/数量/设备载荷；身份切换迟到响应覆盖新页面，403未清旧数据，加载期间卸载仍可能启动连接/轮询。失败证据原样保留。

MesHub现在要求认证，并用既有MesDashboard当前Owner核对原登录会话/查询动作/数据范围来源与实际权限根。真实WebSocket测试又发现SignalR独立DI作用域未经过TenantMiddleware，合法连接被拒；已从经过认证的连接租户绑定当前Hub作用域，仍由实际Owner重新核对当前状态，未跳过会话/权限校验。客户编码不能选租户或组。通知仅向当前PG租户组发送无参数MesInvalidatedV1，不含单号、数量、设备、人员、事件类别或业务时间戳；不支持可信PG上下文的路径拒绝。

页面只将空提示用于HTTP重取，原事件载荷不再显示。身份变化/401/403清空旧值并停止连接；卸载/迟到请求/旧连接完成均不能恢复旧身份数据或轮询。连接工具先解除旧实例引用，避免晚到stop覆盖新连接；被释放实例晚到start后再次停止。测试宿主保留实际API的runtime.log，便于验证真实连接故障而非猜测。

一次作者SPEC/QUALITY集中审查覆盖全部产品diff、原身份/租户绑定、无业务推送、动作撤销、页面代次与连接竞态、依赖/工作流范围。真实宿主发现的租户作用域问题在同窗修正并定向复验；无剩余合并阻断，无审查代理。

## 实际验证与失败保留

31项去重相关.NET通过（4项新增PG/真实HTTP与WebSocket，其余既有库存看板/CSRF边界），8项Vitest及完整vue-tsc -b通过。明细和原始来源`D:/CP6-local-test/wave45/foundation/final-validation.json`。

- realtime-red：3/3真实行为失败，匿名200、10次全广播、外部编码无租户组；修复后真实宿主最终4/4通过。
- websocket与websocket-diagnostic：各1次真实失败，连接type7关闭；runtime.log定位CurrentPermissionContext租户不匹配。修正作用域后合法连接与撤权订阅验证通过。
- regression-final：补验同一连接查询权恢复、原用户停用后的会话拒绝，相关库存/看板Owner及CSRF边界通过；产品不变的其他新测试复用前批。
- frontend-red最初因测试vue-i18n缺createI18n未执行；补齐夹具后frontend-red-behavior的4项均复现实际问题。首次green剩1项把框架定时器也计入；改为验证本页面setInterval未启动、11秒后无额外查询。hub-lifecycle-red另有2项真实竞态失败；最终6页面+2工具=8项通过。
- 完整vue-tsc -b exit0；未做全仓、浏览器E2E或真实环境验收，未把夹具冒充生产。

## 剩余与收尾

既有连接撤权后仍可能收到无参数失效提示，不承诺即时断开或无流量；业务数据仅来自HTTP读。现有ControlTower的machineApi.search仍是旧设备接口，没有本窗新增的独立Machine/OEE Owner；三个MesDashboard读已有Owner。此修复只关闭实时业务载荷泄漏，不能当作整设备/看板HTTP权限完成。提示非可靠Outbox/业务原结果，不证明投递或业务成功。旧事件消费者须采用新提示；本仓已确认唯一实际消费者为ControlTower。

W45未改WmsHub；其独立SignalR作用域是否同样需要显式租户绑定尚未实测，作为下一窗口定向核对项。D3设备/OEE/计划及其他正式合同继续按直接前置推进；125目标/247运行及整卡不提升，七天目标ACTIVE。

自有PG/API已停止；生成data在ownership、绝对路径、无进程与无重解析点核验后回收686906587字节，失败/成功日志、TRX和ownership保留。DEV-1/2待正常PR后finish，INTEGRATION-1空闲，无新增目录。无Actions触发/取消/触发配置变更、既有数据库迁移或生产；根用户资料保留。
