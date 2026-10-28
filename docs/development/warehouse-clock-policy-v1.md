# 原生仓库时钟政策与规则时间评估 v1

W212 对 WMS-MASTER-01 v1.1.1 §2.3、5.5、7.3、13.3.2 的时钟直接前置实现。Owner `Main.Wms.WarehouseClockPolicy.v1`，命令合同 `WAREHOUSE-CLOCK-POLICY/1`；原生候选 Owner 和原审批合同保持原身份。该合同是时间子评估，不替代完整 Eligibility/AssessOccupancy 或08E规则应用。

## 信任与配置

`WarehouseClock:Enabled` 默认false，`Authority`及`Tenants`无业务默认。Authority必须显式包含EnvironmentId、Id、Version（正int64字符串）、Provider=`POSTGRESQL_CLOCK_TIMESTAMP_V1`、EvidenceMode=`REAL|DEMO`、AttestationRef、带偏移ValidFrom/ValidTo、MaxErrorMilliseconds（1..86400000）、MaxReadMilliseconds（1..30000）。Tenants明确允许租户，环境必须等于实际ScopeAuthorization环境；DEMO仅Development宿主可用。未配置、错环境、错模式或未注册租户不提供可信来源。

配置由受信部署侧提供，普通HTTP请求不能登记来源、指定“当前时刻”或声明已同步。AttestationRef和最大误差是运营者认可的来源信任声明，应用不声称仅凭PostgreSQL取时就证明NTP健康、UTC准确或企业时钟验收。正式环境需有真实、有效的来源证明；本地夹具明确DEMO。Authority全内容SHA-256参与来源引用，修改相同ID/Version内容也会使旧政策不匹配。只有批准的配置可以更新来源。

每仓库显式ClockPolicy绑定该准确引用与自己的有效区间；区间必须包含在来源区间内。保存配置可保留已过期政策，使用时返回未知；停用保存的准确内容无需来源仍在线。首次绑定数据库环境不可变。当前仓库权限、数据范围、层级控制、`clock-policy`动作与ExpectedVersion CAS；不自动授予角色。政策原件和Main journal同一事务，SQL不可变/前驱/同事务provenance及ready检查；历史版本保留原时区偏移及七位小数。

## 观察与时间判断

实际调用数据库`clock_timestamp()`，不使用事务固定时间、本机UtcNow或客户端时间作为效力时刻。单调计时器测读取耗时；保守区间为`[sample-declaredError, sample+declaredError+elapsed]`。超耗时、区间越过来源/政策有效期、来源变化或停用均UNKNOWN，不将失败变零或许可。

本地同事务持久保存 `Main.Wms.WarehouseClockObservation.v1` 不可变原件/摘要、政策/源/租户/环境/仓库身份与真实观察时刻。高水位取同租户同环境全部成功观察，跨仓和来源修订；检测已观测回拨时UNKNOWN，修改政策不会重置高水位。它检测已观察回拨，不宣称能检测未采样期间的任意跳时或验证外部时钟精度。PostgreSQL原始时刻精度为微秒；政策/候选原始偏移和七位输入独立保留，不伪造更高的测量精度。

`POST /api/location-rule/owner-v1/locations/{location}/proposals/{id}/time-evaluation`，If-Match与准确Proposal引用。正文 `{proposal,kind:"CURRENT"|"PLANNED",at:null|OffsetInstant}`；CURRENT禁止at，PLANNED必须带偏移at。读取当前真实候选、字段/仓库范围，最终核当前授权、来源/政策/候选；当前时间按可信观察区间判断，指定时间只预测有效期且仍要求本次来源观察有效。

规则有效期半开`[ValidFrom,ValidTo)`。整个观察区间在开始前→NOT_YET_VALID；在结束后或恰好结束→EXPIRED；跨边界→UNKNOWN；完全位于有效区间→TIME_SATISFIED。PLANNED保留指定原字符串，永远isPrediction=true。响应qualification=RULE_TIME_ONLY，admissionGrant=false，requiresFinalEffectRecheck=true；旧观察只是历史证据，后续实际效果必须重新取得当前依据，不能复用本响应取得预授权。

## HTTP、页面与恢复

`/api/wms/warehouses/{warehouse}/clock-policy-v1` 提供当前上下文、`versions/{version}`、`commands`、`operations/{uuid}`及`operations/{uuid}/close`，`observations/{id}`准确历史观察回查。封闭schema/no-store/既有CSRF与认证中间件。原操作主体、租户、仓库、字节意图一致；撤销写权限后可以在当前读权限下查询原结果，关闭未提交键不影响已提交结果，UNKNOWN不自动重发新键。

仓库查询入口 `/wms/warehouse/{id}/clock-policy`，显式来源/日期/启停/原因、准确历史及UUID恢复。规则审批页增加时间评估面板。浏览器原操作只保存租户/用户/仓库下UUID，Web Locks互斥；重新核身份/来源/版本后提交，身份、路由、权限变化或迟到响应不保留旧敏感内容。

## 边界

本轮不改变Selector、Proposal EFFECTIVE、原生库位Capacity/Blocked/Quarantine或Stock。实际08E仍需原审批当前适用、真实非重叠占用、效果Owner保护及完整ExecutionIntent/Selector/Receipt原子事务。来源观察不授予这些能力。新增仅PostgreSQL前向迁移；本地验证与正式时钟/业务验收分别记录。
