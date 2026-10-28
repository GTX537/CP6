# 库位规则候选 v1

Owner `Main.Wms.LocationRuleProposal.v1`，原操作合同 `LOCATION-RULE-PROPOSAL/1`。落实 WMS-MASTER-01 v1.1.1 §4.1.2、4.3、7.1 与 08D/08W 的候选维护。真实字典来源、送审、批准绑定、应用与生命周期仍须独立采用；本合同不提供生产使用许可。

## 独立入口和当前权限

API `/api/wms/locations/{locationId}/rules-v1`：GET scope/context/分页列表；GET `/{proposalId}?version=...`；POST 创建；PATCH `/{proposalId}`；POST `/{proposalId}/withdraw`；GET `operations/{uuid}` 和 POST `operations/{uuid}/close`。

资源 `wms-location-rule` 有 query/draft/edit/withdraw/abandon 独立动作，目录登记不自动授予任何角色。字段为 Capacity、Selector、AllowedProductTypes、AllowedOperations、IsPickable、Validity。当前规则资源的范围1–5按原生 Location 的 Creator 或真实 location 部门映射解释，另核当前 wms-location query、wms-warehouse query、当前仓库/区域权限。没有候选创建者或客户端提交的 scope 替换原库位授权。

历史原件和分页列表都按当前目标/字段权读取；隐藏字段不进入 candidate，任一字段隐藏时不返回完整 OriginalJson。原操作回执只含准确身份、版本和效果摘要。写权撤销后仍可按当前 query 恢复原结果；读权撤销则不披露。最后授权、来源和前驱检查失败时整个事务回滚。

支持现有原生 Location 和 W208 隔离库位。旧库位仍显示 LEGACY_UNVERIFIED，不由旧容量0推导不限或 ACTIVE；隔离草稿仍是 QUARANTINED。对新草稿部门的测试使用真实创建事务，不能从旧 IAM 入口事后重绑隔离对象。

## 类型化候选

创建正文为 operationId 与 draft；draft 含 selectorKey、capacity、两个允许集合、必传 isPickable 及显式可空 validFrom/validTo。selectorKey 使用 ruleFamily、metricId、applicabilityDomainRef(id/version)。外部编号保持字符串，不转成 Guid 或显示名。

- LIMITED 必须带普通十进制字符串及 metric/uom/precision 三个版本化引用；最大13位整数和8位小数，拒绝负数、指数、千分位和超精度，不舍入。保存规范8位小数，有限0保持 LIMITED。
- UNLIMITED 只有 mode，无 value 或隐式0，仅是候选对该指标的不限声明。
- UNKNOWN 必须有待核原因，不能带数值或单位假定。
- LEGACY_UNLIMITED 不可由创建请求伪造；本轮不提供未经核定的旧来源适配。

容量采用独立有标签原件存储，不回写旧 CapacityQty。允许集合空数组明确保留；代码按 ordinal 排序做同值比较，禁止重复/未知结构。时刻必须显式时区、最多7位小数，保存原偏移，区间比较按 UTC 且 From<To；null 表示候选待补，不表示已允许无限期。

当前没有规则字段到实际 metric/precision/applicability/操作字典的权威注册，候选引用以准确原字符串和版本保存，ReferenceResolution=UNRESOLVED、ReferenceAuthorities=UNAVAILABLE。现有 ERP 单位档案不会仅因名称相同就被冒认为该引用的 Owner。将来解析必须绑定真实 Owner/Registry 与新候选版本，不能重解释历史原件。本窗口没有送审或应用端点，也没有伪造批准人/ClockPolicy/库存占用证据。

## 原件、前驱和撤回

`cp6_location_rule_v1.selectors` 按 Tenant+LocationId+ruleFamily+metricId+domainId 唯一。domain 政策版本不加入唯一域；同域多个候选共享选择器。新建的选择器仅 Revision=0/CurrentRule=null，数据库禁止当前版本在本合同下被改成生效规则。

proposals 分开维护候选 version 与对象 revision/token；versions 保存不可变准确候选和摘要。修改必须持当前强 ETag；typed patch 的遗漏是 KEEP，SET(null)、顶层null、非法CLEAR、未知字段均拒绝。容量 SET 替换整个有类型对象；两个集合、布尔仅SET，时间可CLEAR回待补。selectorKey 不可 PATCH。整体规范同值返回 NO_CHANGE，候选版/ETag/选择器都不变化。

实质修改新增候选原件，版本+1、对象 revision/token 更新；撤回仅 DRAFT→WITHDRAWN，保留原候选版本及原件，单独推进对象前驱。修改与撤回争同一前驱，落败者返回412；WITHDRAWN 不得编辑或重开。未来送审/批准/应用状态须由后续正式合同扩展，不能靠本端点伪造。

选择器/候选原件/对象前驱/Journal及审计同 PostgreSQL 事务。外键、真实原生租户身份校验、不可变触发器、连续版本与延迟原操作证明约束一起保全；运行时核查实际启用触发器。Raw SQL 不能把草稿选择器改成已应用，也不能删除原件。没有创建 Stock、释放预占、解除仓库或库位隔离、改变旧名称/数量/层级。

原操作 UUID 按租户、Owner、原actor和库位绑定；重复载荷回原结果，冲突载荷拒绝。UNKNOWN 不代表未提交；close 和 create 在同一原生授权/事务序列中竞争，已关闭 UUID 不能复活。回查旧回执不覆盖客户端当前详情，页面重新 GET 当前候选。

## 页面和验证范围

独立页面 `/wms/location/{id}/rules` 从旧库位主数据列表和 W208 草稿列表进入。提供明确模式、字符串数量、空集合和含时区的有效期，当前候选差分修改、准确版本历史与撤回。规则域隐藏而容量可读写时，从可读容量自己的 metricRef 保留身份，不恢复隐藏 selector 字段。字段无读取权时保持遗漏，不能用默认值覆盖。

浏览器仅按 origin+tenant+actor+location 保存 UUID，使用 Web Locks 互斥并重核当前身份；未知提交只查询原键。身份/路由变化及401/403/404清理私有显示，拒绝晚响应和错误身份回执。无应用按钮或虚构默认字典。

验证为隔离 PostgreSQL18、实际 Program HTTP 和相关 Vue/类型/构建。没有执行既有环境迁移、Actions 或生产部署。正式 MASTER 全流程、规则参考来源/审批/应用、READY_TYPED/激活、Stock/RSV/Transfer 效果及原七天完整出口保留。
