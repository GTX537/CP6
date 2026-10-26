# Main 补货／退货当前 Owner 分页 v1

W141，2026-10-26，仅 PostgreSQL。此增量读取现有历史业务对象，不实现正式 REP SourceDemand/Transfer 或 RMA 实收／处置合同。

|入口|当前权限|返回|
|---|---|---|
|GET `/api/wms/replenish/pages/v1`|`wms-replenish:query`|`{code,message,data:{items,total,page,pageSize}}`|
|GET `/api/wms/rma/pages/v1`|`wms-rma:query`|同上|

查询字段沿各自现有 `SearchQuery`；状态 `0` 是有效筛选，未传才表示全部。冻结调用方输入，pageSize 归一到 1–500，page 归一到 1–floor(Int32.MaxValue/pageSize)。默认 1/50。旧根 GET 仍返回数组，单票路径不变；新两段路径不占用任何旧单号。

Owner 先核当前租户、真人、query 动作、本人／健康部门及仓区，形成同一个有权集合。补货要求源与目标真实库位均可见；退货保留原出货／订单／客户和各明细的范围约束。筛选后的集合才参与计数与分页；原列表隐藏的客户自由文本、操作者、备注等仍隐藏，响应 `no-store`。

计数和页项用一条 SQL 的聚合与 LEFT JOIN 取得，同一语句观察时点。非空集合的越界页返回空 items 和准确 total；完全空集返回 total=0。补货按 priority、CreateDate 降序、UUID，退货按 AppliedDate 降序、UUID 排序；分页子查询和最终结果均明确排序。每次翻页重新授权、重新读取，这不是跨多次请求的不可变快照、导出证明或完整任务族关闭证明。

最终组织／授权版本与字段来源复核仍由原 OperationsOwner 执行；失联、撤权或上下文不成立时拒绝，不转成成功空页。普通 SQL Server 旧 API 代码保留历史范围；新分页入口不增加非 PostgreSQL 实现。

ReplenishView 和 RmaView 直接发送当前页号、页大小并显示服务器有权 total，不下载前500条后截断。返回页身份、非负整数总数与页项数量不一致时显示读取失败。身份切换、较新请求、当前 query 撤权都使迟到结果失效；写入能力继续由现有合同控制，不因分页成功打开任何命令。

验证与真实剩余范围见 [W141 单份交付报告](../../development/evidence/W141-01/DELIVERY.md)。正式 REP/RMA 实际采用及完整业务验收不因本增量改变。
