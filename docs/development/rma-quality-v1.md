# 独立退货质量与 RMA 采用 v1

W231 在实际退货入库之后提供独立 `Main Quality / QA-NCR-01` 退货检验 Owner。它建立检验义务、签署四态精确分区、更正或撤回判定；RMA 从服务端读取该 Owner 原结果并保存观察。生产验收的数据可以按用户决定全部模拟，判定、权限、事务和恢复由实际代码执行。

本合同是 `QA-NCR-01.RMA-QUALITY/1` 范围适配，不宣称完整 QA-NCR R5 设计稿 API 已实现。原 RMA canonical `QaInspectionRequest` 和 `QualityDecisionRef` 是输出合同，实测报文已按原 schema 核验。[实物入库合同](rma-inbound-v1.md)是前置，[本轮交付](evidence/W231-01/DELIVERY.md)记录验证和剩余范围。

## 来源和权限

- 检验来源只包括当前租户真实 `SUCCEEDED` 退货入库结果。服务端读取原生入库单权限、不可变实收行、Stock 计划中的质量依据及实际出库关联；请求不能自报库存效果、实收量或已签署质量结果。
- 独立资源 `quality-return-inspection` 提供 `query/start/decide/withdraw`，默认数据范围为仅本人。它与 `wms-rma`、原生 `wms-inbound-receipt` 的当前读取范围分别生效，保留租户、部门及仓库约束。跨人员审核须明确配置相应数据范围，新增迁移不会自动授予角色权限。
- 建立检验同时要求 `wms-rma:inspection-start`；RMA 观察要求 `wms-rma:quality-observe` 和 Quality 当前读取权限。需要独立签署的政策禁止实物登记人本人判定，使用真实认证 actor，无法通过请求字段更换签署身份。
- 内部已验证的原记录使用类型合同直接读取。验证集中在配置及请求边界、权限、版本与精确分区；错误立即返回，不用回退值制造成功。

## 模拟输入

`ReturnQualitySimulation:Enabled=true` 后，从 `CasesJson` 读取配置数组，每个元素包含 `tenantId`、`siteId`、`qualityPolicyRef`、`version`、`requireIndependentRegistrar`、`evidenceRefs`。`qualityPolicyRef` 必须等于该实收行保存的质量依据引用；模拟证据以完整 `{owner,id,version,digest}` 引用配置。页面提供这些可选证据，判定人选择并签署。

配置按租户、站点和准确质量依据匹配；内容与版本形成 `Simulation.Quality.ReturnPolicy.v1` 引用。请求须带当前政策引用，政策变化返回冲突；不存在配置时可以查看已存结果，不能创建或签署新判定。显式模拟政策和检验证据可用于本目标的生产验收，不再等待真实企业数据。

实际可复现场景见 `CP6.Tests/Erp/ReturnQualityPostgreSqlTests.cs` 的 `QualityReturnSetup`：创建客户出库与退货申请、独立授权、实物登记、原生入库和 Stock 流水，再配置模拟政策与检验证据、建立检验。HTTP 场景启动实际 `Program`，通过登录 Cookie 和 CSRF 执行判定及 RMA 观察；既有环境不需要部署或导入资料。

## 业务和存储

一个实际实收行对应一个检验义务。检验保存该行原始范围、数量、入库及移动引用，初始没有签署判定。每次判定必须恰好包含 `ACCEPTED/REJECTED/HOLD/PENDING` 四个分区，允许某分区为空；非空范围之间不可重叠，其并集必须完整等于实收范围。服务端按 decimal(21,8) 支持的精度计算数量，前端使用字符串与 BigInt 避免浮点截断。合格、不合格和保持分区须引用当前政策中提供的检验证据；待判仍是待判。

新判定以当前 head 为 CAS 前置，保存签署人、政策、原因、前一判定引用及不可变 canonical 结果。更正继续同一接口并产生下一版本；撤回产生 `WITHDRAWN` 新版本，保留之前的数量和分区。历史结果保留原签署事实，当前生效状态以最新 head 为准；旧版本不能作为当前结果使用。

RMA 观察只接收预期判定及 RMA 实物 head 引用，在同一事务内读取 Quality 原结果。`observationCurrent` 仅在观察等于最新 ACTIVE 判定时为真。更正、撤回使旧观察失效；观察撤回结果可保留审计，但不恢复可处置资格。

新增 PostgreSQL schema `cp6_return_quality_v1` 保存检验、判定、观察及待发事实，所有业务主键与查询包含 tenant。它不是全库租户 schema 迁移。与现有 RMA 写入共用事务锁，判定 head、不可变结果、outbox 和原命令日志共同提交；失败全部回滚。迁移只前向，检验原始 body 和签署历史不可覆盖或删除。

## HTTP 和原请求恢复

基础路径 `/api/wms/rma/v1`，所有接口要求认证，响应 `no-store`；POST 使用现有 CSRF 管线。写入携带 GUID `Idempotency-Key`。输入 DTO 拒绝未知成员，合法命令完整绑定应用、操作、检验和原始 body。

| 方法 / 路径 | 用途 |
| --- | --- |
| GET `applications/{app}/quality/context` | 实收行、检验现状、模拟政策和当前能力 |
| POST `applications/{app}/quality/inspections` | 依据准确实收行建立检验 |
| GET `applications/{app}/quality/inspections/{id}?revision=N` | 当前或历史检验判定 |
| POST `applications/{app}/quality/inspections/{id}/decisions` | 签署或更正完整四态分区 |
| POST `applications/{app}/quality/inspections/{id}/withdrawals` | 撤回当前 ACTIVE 判定 |
| POST `applications/{app}/quality/inspections/{id}/observations` | RMA 读取和记录 Quality 原结果 |
| POST `quality/commands/query` | 按 applicationId、commandId 查原命令 |

日志使用 `Main.Quality.ReturnCommand.v1`。同键同请求返回不可变原结果；同键改请求拒绝。已提交但应答丢失时，在写权限撤销后仍可按当前读取权限找回；原操作人、应用及当前 RMA/IN/Quality 可见范围继续核对。查无记录返回 `NOT_OBSERVED`，不宣称没有效果。

实际退货页面的质量面板支持分区编辑、证据选择、签署、撤回、RMA 采用及恢复。原请求先存租户/actor 专属 IndexedDB，以 Web Lock 串行处理；先查询，仍未观察到时可重派准确原请求。页面/身份改变时丢弃迟到响应，保留待恢复记录；读取拒绝时清除已显示数据。

## 后续实现

质量判定本身不改变库存、商业或财务。五种最终处置 Owner、实物来源冲正与 VOID、更正后的下游效果处置仍待实现；当前没有来源冲正入口，`voidRanges` 为空，不提供任意减少有效实收量的接口。outbox 已实际落库，本轮未实现外部派发。后续按原七阶段范围继续实现，并使用完整模拟场景验收；这些是待写功能，不是等待企业数据。
