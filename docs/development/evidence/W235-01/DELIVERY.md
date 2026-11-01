# W235 调拨请求生命周期交付

原本调拨只有来源环境和本地需求预算组件，不能创建、编辑或审批业务请求。本轮增加独立 Owner、PostgreSQL 持久事务、真实 HTTP 入口及调拨请求页面，完成创建、编辑、送审、独立批准/驳回、撤回与原操作恢复。完整合同和使用边界见[原生入口 v1](../../transfer-native-requests-v1.md)。

本轮采用已有产品/UOM/站点、权限与 SourceDemand 组件，没有增加资格包装或重复审计前置。内部来源按不同引用在同一事务内复用，改变中的业务状态在当前决策边界核对。源/目标库位、数量精度、租户及部门范围保持必要校验。

## 实际验证

本地 PostgreSQL18、.NET8 与实际登录后的 HTTP：15个不同场景通过，未执行场景不计入。覆盖请求版本/强ETag、幂等与输入冲突、重复 draftKey、需求绑定替换、独立审批、撤回/拒绝、并发胜者、原操作未知/关闭、提交响应丢失、写权限撤销、日志失败和最终撤权整事务回滚、五类对象范围、跨租户、历史不可变、极值与最小数量、库位冻结；同时复用并定向重跑3项受影响的原生产品来源场景。

成功结果和原失败均保存在 `D:/CP6-local-test/wave235/foundation/`：

- `pg-first-20261101T081053Z.json`：首批4项通过。
- `pg-boundaries-20261101T081404Z.json`：并发和回滚2项通过；HTTP夹具错误地复用了硬编码创建人登录，因此自审被正确拒绝，此失败保留。
- `pg-final-additions-20261101T081743Z.json`：改用实际审批人登录后的HTTP、部门2/3/4、极值和历史5项通过。
- `pg-source-regression-20261101T082057Z.json`：单位来源缓存调整后的数量/预算、租户与创建人范围，以及3项旧来源回归共6项通过。重复场景不重复计数；无关成功输入直接复用。

页面交互7项通过，部门映射的新 `transfer-request` 场景1项通过。其余29项通用映射场景未选中，未计为通过。页面测试实际挂载 Vue 控件并操作创建、编辑、送审、失联恢复、身份切换、精度边界和保存成功后刷新失败；API以明确模拟响应提供。日志 `D:/CP6-local-test/wave235-web-final-tests.log`、`wave235-web-mapping.log`。应用 TS 项目检查通过；仅根引用配置的首次空检查不作为应用类型检查证据。未做浏览器布局或完整生产包构建，不据此宣称已验证这些范围。

复现：设置本机 `CP6_PERMISSION_PG_BIN`、隔离的 `CP6_PERMISSION_PG_ROOT` 后，对 `CP6.Tests` 运行 `dotnet test --filter "FullyQualifiedName~TransferRequest_|FullyQualifiedName~TransferSource_"`；Web运行 `vitest run src/views/wms/TransferRequestsView.spec.ts`，以及通用映射测试的 `-t transfer-request`；应用类型检查使用 `vue-tsc --noEmit -p tsconfig.app.json`。

## 生产业务验收范围

依据用户授权，租户、客户/产品、站点、仓库、库位、单位、部门及审批身份均可模拟。本轮模拟资料进入实际产品代码、权限、持久预算和数据库事务，执行的请求生命周期验收通过。批准后库存不发生移动，仍明确显示未派发。

本轮为独立版本的原生 DIRECT_MOVE 请求入口。完整TF v1.1路由、外部需求义务、正式工作流审批适配、Stage和实际Stock移动、补货执行、事件运输消费者仍未交付；WP10-10/WP10-31和七天D2不因此标为完成。未部署生产、未覆盖既有环境或数据库。CRM与Platform未修改。

## 审查及收尾

COORD单人完成一次任务级完整差异审查，覆盖契约版本、当前来源/审批/租户边界、原子预算释放、不可变历史、原操作恢复与页面身份失效。审查修复提交成功后刷新失败的页面状态，只复测影响的页面行为。无剩余阻止本轮范围合并的问题。

原估实际90–150分钟；实际用时在本轮最终工作区记录中核销。固定 DEV-1/DEV-2 复用，缓存保留。Handoff和Main正常PR、准确分支删除、停止进程和官方finish同轮收尾；本文件为合并前审查快照，最终采用和席位状态以PR和同轮 `wave235-final-verification.json` 为准。无Actions运行/取消/触发变更；普通工作流和依赖保持原内容。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/211)已完整采用并finish。Main交付快照为30.13分钟；同轮最终Main合并和finish结果单独核对。
