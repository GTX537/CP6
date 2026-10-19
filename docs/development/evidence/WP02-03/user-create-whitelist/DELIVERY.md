# W23-02 / BUG311 用户创建白名单交付

## 结论与范围

基线 `a68d285a31a47ba497909591ef9c5063e117300a`，分支 `codex/bug-311-user-create-whitelist-20261019`。已复现并修正[P0 #311](https://github.com/GTX537/CP6/issues/311)，**本地可合并 Yes**。17项去重相关验证通过：11原生PG/HTTP、5既有密码/控制器单测、1实际生成请求schema；0失败/跳过。一次作者集中SPEC/QUALITY无阻断，待正常合并/远端核对后关闭Issue。

这是本轮已接受SYS-IAM-01第47行创建白名单的实际采用。专用UserCreateRequest只接受UserName、Password、NickName、RoleId、Enable、DeptId、ManagerId、Email八项。START消息的“九项”是计数笔误，字段范围始终以上述实际列表/登记设计为准。

## 根因、修正与兼容

旧Add把请求直接绑定Sys_User并保存；只有user:add的普通账号可提交IsPlatformAdmin、其他TenantId、指定Id/审计元数据及外部认证字段。三项实际HTTP均返回200并保存越界值：平台标志true、外部issuer、其他租户归属。复现仅在自有fixture中进行，未使用真实账号或外部身份提供商。

现在用专用DTO构建全新实体：服务端生成GUID并绑定当前可信上下文TenantId，平台权保持false，认证/外部身份/2FA/PIN/徽章/锁定等不从请求复制，Creator来自当前已认证请求、时间由服务器设置。BCrypt、首次强制改密、合法字段长度/可空性、Enable以及原返回结构保留。按现有JSON兼容策略，额外属性被忽略，不暗中落入实体；没有新授权能力。

用户页UserView和userApi只发送这些合法字段及通用表单元数据，仍适用；本次无前端改动或构建。生成请求schema不再暴露身份/租户/平台/认证/审计字段，用户名与密码必填/原长度保持。实体本身保留供专用可信流程使用，不给实体加全局JsonIgnore来改变其他合法接口。没有新增数据库迁移、SDK发包或SQL Server验收。

## 验证与复用

证据根 `D:/CP6-local-test/wave23/platform-admin`，原始JSON/log/TRX保留：

| 前缀 | 结果与内容 |
|---|---|
| red-user-create-20261019T075241Z | 3失败/0跳过，实际普通身份HTTP分别保存平台权、外租户身份和外部认证字段，真实产品RED |
| green-user-create-20261019T075454Z | 8通过，原三路径转绿及5项原密码/修改合同测试 |
| user-create-contracts-20261019T075639Z | 9通过，合法全字段创建、哈希/强制改密/服务端身份和审计、六项模型绑定/长度边界、当前权限撤销及无认证拒绝、实际Swashbuckle请求schema |

HTTP使用W23现有原生PG已迁移fixture，产品JWT、Tenant/CSRF/当前权限/RequirePermission与真实UserController。角色和租户事实真实存储，测试Owner只用于准备/核验；不称全Main已采用普通写账号。未用login/UI/完整Program冒充，此次创建无需refresh/2FA服务，复用fixture的strict未调用依赖。原三条成功证据输入未变直接复用，没有仅为新增其他测试重跑。

W23-01平台管理员防自锁死及审计65项证据按[原DELIVERY](../platform-admin-transaction/DELIVERY.md)复用，本次仅修改Add入参与映射，不改更新/删除、数据库保护或事务服务。原5项密码/控制器在当前候选重新执行；全轮去重统计不重复累加这5项。

## 集中审查和边界

一次作者SPEC/QUALITY覆盖完整diff、准确S5白名单、服务端字段无批量复制、当前租户绑定、原合法字段/密码合同、JSON额外属性兼容、前端调用点和生成schema；无独立审查代理。没有额外反射/字段镜像测试，生成schema检查针对外部请求合同。

本窗口3自有PG全部停止；连同W23-01共10根，历史失败与数据库保留。唯一工作目录/构建/NuGet缓存复用，无新树/代理/Actions、既有库操作或部署。编译器/NuGet既有告警不作通过消除声明。

本次没有完成角色/组织/经理完整引用CAS、所有创建入口、委派与完整IAM权威向量、请求人撤权和Owner新效果最终门，也不是DEV/业务/生产验收。完整WP02/247运行NOT_RUN保持原状态。既有环境推广W23-01前向迁移仍是独立安装任务。
