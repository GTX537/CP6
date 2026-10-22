# Main MES 不良记录独立 Owner v1（W40）

2026-10-22；PostgreSQL；补充[Operations v1](main-operations-owner-v1.md)与[库存/看板 v1](main-stocktake-dashboard-owner-v1.md)。DefectRecordService、实际DefectRecordController及DefectManagementView采用；MES品质检验父入口自身未采用，不因其自动生成不良而宣称业务闭环。

## 动作、字段与关联范围

mes-defect/菜单308：query（分页/单票/分类引用）、add、edit、del。复用已存在的query目录、字段空集合与scope定义，不新增角色授权、模型或迁移。每次读取/写入必须有当前原会话Owner、可信tenant、当前有效角色、启用动作目录及完整字段源。主资源和mes-work-order query来源各自验证当前scope和最终版本；分类引用只消费主资源query。

不良主体self只含当前Creator；关联工单按相同scope校验工单、全部工程/材料Creator（含软删除）。部门scope只使用实际工单→订单→可信部门映射；缺绑定不推测，主资源与来源范围取交集。分页、总数、产品名称补全之前过滤完整对象。可选InspectionNo必须关联同一租户/工单的实际有效检验头，其已指定工程必须有效；self包括检验头及全部含软删除明细Creator。Defect ProcessCd仍可空，与检验ProcessCd无需人为逐字相等；两者各自必须是实际工单下合法关联。

新建核对新关联；修改在保存前核对旧本体/旧图、新图，最终再核对旧图和新本体，不能通过换工单/检验清洗旧越权记录。删除在软删除后仍核对包含已删除本体的完整图。读不到对象返回空/404，越权写403。MES看板的不良来源同时采用该可选检验图，保留既有更严格的看板完整工单约束，不借汇总获得不可读单票信息。

所有关系和产品/分类补全使用PG原生键比较，不能用CLR大小写字典缩小合法键；补全按实际页内Guid归并。检验头/明细加入现有有序业务表围栏，覆盖raw SQL写入；不另造一套锁顺序。短表锁仍跨租户串行化，性能与持久细粒度Owner迁移另项。

## 事务、合法输入与页面

DTO/查询列表在首次await前冻结；拒绝缺Owner、外来/ambient事务、脏上下文、已跟踪业务图或不同上下文序号器。序号累计和不良写入/审计在同一ReadCommitted事务，最终重新验证授权、身份与图；确认丢失返回OPERATIONS_OUTCOME_UNKNOWN，服务不自动重放。通知只在确认提交和事务释放后best-effort执行，通知失败不把已确认业务改成失败；无outbox和持久幂等承诺。

Creator由真实Actor创建，修改保留原Creator、Modifier取真实Actor；合法decimal(21,8)、原状态/完成日默认规则保留。旧非PG路径不扩展、不新加SQL Server验收。HTTP保留CSRF；Owner的401/403/409/503不降为业务400，所有响应no-store。

页面冻结创建/更新/删除意图和确认目标，重复提交合并，UNKNOWN禁止重发；身份/目标/异步响应隔离，读失败清旧数据，旧身份CSV快照不可导出。创建成功绑定确认编号，即便随后读回失败或表单内容已改也不误发第二次创建；保留最后确认编号。迟到的详情/关闭回调不能覆盖新草稿。前端数值控件保留8位小数输入，接口精度由原生PG验证；未改变既有JavaScript number存储类型。
