# W115 Main 实际 H01 服务权限生产入口

本聊天COORD单人；七天目标ACTIVE。本轮Main功能与Handoff交付，CRM未改。W114已完整收尾，不重复。
Main候选从709c64e539e0f6252d2d298c75f7626b202336b8开始；本文件进入同一个W115-01功能提交。最终PR、远端采用及finish以本轮官方记录核对。

## 实际行为

新增默认关闭的InquiryServicePrincipals配置和POST /connect/inquiry-service-permission。服务端登记以ClientId绑定已有CrmOidc机密客户端、稳定SubjectId UUIDv4、真实DepartmentId与RoleIds；Version标识受控配置版本。角色ID保留完整int范围。未登记、已禁用或凭据错误的客户端不能生产投影；cp6.services令牌本身不能替代此入口的显式Basic凭据。请求仅接受现有InquiryPermissionRequestV1三字段，purpose仅intake:release；JSON多余身份字段拒绝。只在既有可信HTTPS/代理规则下工作，无缓存；精确CSRF例外仅此不使用环境Cookie的端点，不放宽人类入口。

单例登记复制所有值和角色数组，避免请求期间可变配置改变身份。修改ClientId/UUID/角色/部门/密钥/启用状态必须按现有服务宿主规则停止入口、排空全部实例、审查新Version并重启；不宣称即时热撤权。SubjectId须长期稳定、属于独立服务身份；同租户现有Sys_User（即使禁用）与其冲突时拒绝，不创建用户或浏览器会话。登记本身是Main受控身份/角色分配配置，不能交给请求者提交。生产没有新增或启用任何真实登记。

实际签发仅支持PostgreSQL：从认证后的登记选租户，持Main认证根锁，读取实际当前启用租户及策略版本、真实启用角色和版本、动作、完整健康部门树、字段源和精确site/form对。query/view-pii/add加assign主管语义、crm-lead数据范围MAX/自定义有效部门并集；不借用当前浏览器用户角色或配置权限。复用FieldPermissionSourceV1/Evaluator的实际存行MIN后补已注册默认，保留未知字段、非法Access和隐藏字段的拒绝行为；需要六个crm-intake字段可读。范围超冻结256/非UUIDv4不截断或重映射。SQL值参数化，表标识来自EF模型；不新增SQL Server或DDL。

签名前再次使用数据库时钟核对租户，最长5分钟且不超过实际租户到期。既有Ed25519/冻结crm-iam-projection-v1合同原样签发，purpose为原设计H01使用的intake:release。事务提交成功后才返回，失败/取消恢复原租户上下文。签名是Main时点权限事实；不是CRM业务提交授权，也没有虚构ServiceToken jti或浏览器sid。已有服务令牌、C02事件以及人类签名入口语义保持。

## 实际验证

84项唯一相关本地检查通过、0跳过。第一批84项包括真实新服务HTTP、实际人类登录/OIDC/HTTP配置和intake回归、服务注册冻结/禁用/凭据与完整int角色配置、当前IAM授权/字段MIN/站点表单/健康部门/到期、人类UUID冲突、提交丢失回执/取消恢复，以及实际pg_blocking_pids确认的角色/字段/部门撤权竞争。随后仅补跑同一个服务HTTP检查，证明真实有效浏览器Cookie不能认证、伪造tenantid/X-Subject-Id不能覆盖服务登记；其余83项成功证据复用。共85次成功执行不冒充85个不同检查。

证据：D:/CP6-local-test/wave115/foundation/final-validation.json；原始TRX/日志在同目录results和两份运行JSON引用处。最初编译因新增测试缺少CrmIdentity命名空间失败，修复后通过，失败日志service-producer-20261024T152725Z保留。现有无关编译警告未混入修复。不因提交/SHA再重复测试。两个新建隔离PostgreSQL测试实例均已停止，日志和数据保留；无既有库迁移。未运行Web、跨仓宿主或全仓业务回归。

## 采用与剩余范围

启用需受控配置InquiryServicePrincipals.Enabled=true、非空Version和Registrations，每条提供ClientId、SubjectId、DepartmentId、RoleIds；同一宿主CrmOidc服务客户端、映射启用租户及InquiryPermissionSigning必须已经配置。角色、动作、数据范围、字段和site/form对仍通过实际Main IAM管理，不在此复制一份可随意授予的权限清单。配置更新按上述停流/排空/重启规则执行。

尚未实现：服务主体及登记变更在CRM的当前身份来源/撤权采用、CRM最终当前资源/授权持锁门禁、H01原图转换和编号、IssuedAttempt消费/稳定重放、完整Submission/Attempt/Lead及审计/outbox同事务、公开submit路由和页面。没有把服务配置或签名当作这些步骤已完成；完整目标/真实验收仍24/375、0/125、247NOT_RUN，D6 PARTIALLY_ADOPTED。

本任务集中审查完整差异并按Main/Handoff正常PR合并、准确引用条件删除和官方finish收尾。没有Actions、分支保护调整或生产部署。最终分支处置、工作区与席位状态由本轮final-verification及检查点记录；本报告提交时Main最终合并/finish尚待。
