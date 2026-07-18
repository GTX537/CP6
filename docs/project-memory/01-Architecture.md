# 架构与关键设计

## 分层

```text
cp6.web ──HTTP / SignalR──> CP6.WebApi → CP6.Core → CP6.Entity
                                  ↑          ↑
                                  └──── CP6.Tests
```

- `CP6.Entity`：实体和 DTO，不反向依赖业务层。
- `CP6.Core`：EF 上下文、迁移、服务、业务不变量和集成逻辑。
- `CP6.WebApi`：Controller、认证授权、中间件、后台任务、SignalR、种子和 DI 接线。
- `cp6.web`：Vue SPA，只通过 API/SignalR 与后端通信。
- 文件夹与命名空间按业务域对齐：Sys、Erp、Mes、Wms、Fin、Pur、Wf/Oa、Pub、Plan、Space、Integration。

## 请求链

典型写请求：页面 → `src/api` → Controller → Service → EF/Dapper → SQL Server。统一响应为 `{ code, message, data }`，前端 `http.ts` 负责解包、错误翻译和 401 处理。

## 跨域业务流

ERP 受注创建后通过 Bridge Hook / IntegrationEvent 驱动 MES/WMS；MES 指图与实绩继续驱动材料出库、成品入库；WMS 出荷回写销售状态。跨域联动不得绕过现有 Hook 直接在 Controller 拼接多域写入。

## 多租户

- 业务实体通常继承 `BaseTenantEntity` / `BaseBizEntity`。
- `TenantId` 由请求上下文、全局查询过滤和种子显式盖章共同保证。
- 种子必须逐租户执行、幂等、避免扰动管理员手工授权。
- 未经专项设计不得使用 `IgnoreQueryFilters()` 绕过租户边界。

## 权限模型

- 页面/菜单权限与动作权限分离。
- 后端 `[RequirePermission(menuKey, action)]` 是安全边界，fail-closed。
- 前端 `v-permission="'menu-key:action'"` 只负责 UX 隐显，不能替代后端校验。
- admin 前端全放行不等于引擎业务规则豁免；WF 审批归属仍由引擎 E-WF-029 校验。
- 权限键事实源：`docs/seeds/*permission-keys.md` + 各模块反射测试。

## 数据一致性原则

- EF 用于事务性 CRUD；Dapper 用于复杂查询和报表。
- WMS 库存只能走库存移动/台账服务，不直接改余额。
- 使用软删除、审计字段和 `RowVersion` 乐观锁的实体，不得绕过约束。
- 审批引擎状态迁移必须经引擎服务；Controller 保持薄壳。
- IntegrationEvent/Outbox、幂等种子和后台 worker 的现有语义不能随意改成 fire-and-forget。

## 前端原则

- API 类型位于 `src/types`，HTTP 封装位于 `src/api`，页面不直接创建散落 axios 实例。
- 动态路由来自后端菜单；i18n 由 DB 键驱动。
- 业务条件 `v-if` 与权限指令并列；权限铺设任务只加 template 指令，不改脚本和样式。
- 设计系统见 `docs/CP6_Design_System_v1.0.md`；响应式公共逻辑优先复用 composable。
