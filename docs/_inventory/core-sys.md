### 五、CP6.Core/Services/Sys + Wf + Pub + 基础设施 + Migrations

> 范围内逐文件一句话功能说明（排除 bin/obj）。Migrations 仅汇总。

#### Services/Sys

权限四粒度引擎（PUB 章00~05）+ S 类认证加固（BCrypt/密码策略/锁定/安全审计）。

- `CP6.Core/Services/Sys/ICurrentPermissionContext.cs` — 请求级当前用户权限上下文接口：取/预热/失效（按用户、按角色）已合并权限结论。
- `CP6.Core/Services/Sys/CurrentPermissionContext.cs` — 上述实现，用 IMemoryCache（30 分钟滑动）缓存权限上下文，未登录抛异常；角色变更可按用户/角色失效。
- `CP6.Core/Services/Sys/IPermissionAggregator.cs` — 权限聚合器接口：把某用户全部角色的四粒度权限合并为一个 UserPermissionContext。
- `CP6.Core/Services/Sys/PermissionAggregator.cs` — 聚合器实现：菜单/操作取并集、数据范围取最宽(MAX)、字段权限取最可见(MIN)，主角色∪附加角色去重。
- `CP6.Core/Services/Sys/UserPermissionContext.cs` — 会话级权限聚合结果 POCO：菜单键/操作键/数据范围/自定义部门/字段权限的"已合并结论"载体。
- `CP6.Core/Services/Sys/IPermissionService.cs` — 功能权限查询接口：HasAction / HasMenu（读已聚合上下文）。
- `CP6.Core/Services/Sys/PermissionService.cs` — 功能权限查询实现：零额外查库，直接读会话缓存的上下文判定。
- `CP6.Core/Services/Sys/IRolePermService.cs` — 角色授权配置接口：菜单操作点 + 角色功能/数据/字段权限的读写 + my-actions / my-readonly。
- `CP6.Core/Services/Sys/RolePermService.cs` — 角色授权配置实现：diff 增删 RoleMenu/RoleAction/RoleDataScope/RoleFieldPerm，保存后按角色失效缓存，含 E-PUB-021/031/032/041 校验。
- `CP6.Core/Services/Sys/IUserRoleService.cs` — 用户-角色分配接口：取/存某用户角色集合+主角色 + 历史数据迁移。
- `CP6.Core/Services/Sys/UserRoleService.cs` — 用户-角色分配实现：diff 中间表写主角色、失效该用户缓存，MigrateAsync 把旧 RoleId 幂等并入中间表。
- `CP6.Core/Services/Sys/IDataScopeFilter.cs` — 数据权限查询注入接口：把用户对某资源的数据范围翻译成 IQueryable 过滤。
- `CP6.Core/Services/Sys/DataScopeFilter.cs` — 数据权限过滤实现：五范围（本人/本部门/及下级/自定义/全部），未配置回落"本人"最严。
- `CP6.Core/Services/Sys/DataScopeRegistry.cs` — 数据范围资源注册表（静态）：各模块注册可做数据权限的资源、支持范围、默认范围。
- `CP6.Core/Services/Sys/IFieldPermService.cs` — 字段权限执行接口：MaskHidden（隐藏字段掩码）+ StripReadOnly（只读/隐藏字段拒写）。
- `CP6.Core/Services/Sys/FieldPermService.cs` — 字段权限执行实现：反射把隐藏字段置空、把只读/隐藏字段用 DB 原值覆盖。
- `CP6.Core/Services/Sys/FieldRegistry.cs` — 字段权限资源注册表（静态）：各模块注册可控字段供配置页渲染与保存校验。
- `CP6.Core/Services/Sys/IDeptService.cs` — 部门（组织树）服务接口：树查询 + 增改移删 + 设负责人/用户组织字段。
- `CP6.Core/Services/Sys/DeptService.cs` — 部门服务实现：物化路径维护、移动重算子孙路径并防成环、删除前查子部门/在职用户护栏。
- `CP6.Core/Services/Sys/IDictService.cs` — 字典服务接口：按 TypeCode 取启用项 + 值→标签翻译 + 失效缓存。
- `CP6.Core/Services/Sys/DictService.cs` — 字典服务实现：IMemoryCache 按类型缓存，翻译未命中回原值。
- `CP6.Core/Services/Sys/IPasswordHasher.cs` — 密码哈希接口：Hash / Verify / IsHashed（判是否已为哈希）。
- `CP6.Core/Services/Sys/BCryptPasswordHasher.cs` — BCrypt 哈希实现：工作因子可配（默认 11），前缀+长度判定 BCrypt 变体。
- `CP6.Core/Services/Sys/SecurityOptions.cs` — 安全配置 Options：密码策略/锁定/令牌/认证 Cookie 各子项默认值。
- `CP6.Core/Services/Sys/IPasswordPolicyService.cs` — 密码策略接口：复杂度校验 + 历史不可重用 + 记录裁剪历史 + 有效期判定。
- `CP6.Core/Services/Sys/PasswordPolicyService.cs` — 密码策略实现：长度/大小写/数字/符号校验、最近 N 条历史比对（E-SEC-004/005）、历史裁剪不自提交（与改密合并原子保存）。
- `CP6.Core/Services/Sys/ILoginSecurityService.cs` — 登录安全接口：账户锁定校验 + 记登录失败/成功（维护登录画像）。
- `CP6.Core/Services/Sys/LoginSecurityService.cs` — 登录安全实现：滑动窗口累计失败计数达阈值锁定（E-SEC-002），成功清零并刷新 LastLogin。
- `CP6.Core/Services/Sys/ISecurityAuditService.cs` — 安全事件审计接口：把认证类事件写入 Sys_SecurityLog，审计失败不阻断主流程。
- `CP6.Core/Services/Sys/SecurityAuditService.cs` — 安全审计实现：字段防御式截断后写入，写失败仅记日志（仿 OperLog 安全写入），绝不阻断登录/改密。

#### Services/Wf

OA 审批/工作流引擎（OA 章01~08）。引擎对业务单向依赖，业务经回调契约接入。

- `CP6.Core/Services/Wf/IApproverResolver.cs` — 审批人解析接口 + 策略枚举/规则/上下文/结果（直属上级/部门负责人/角色/指定/发起人）。
- `CP6.Core/Services/Wf/ApproverResolver.cs` — 审批人解析实现：消费 PUB 组织模型纯查询，缺位返回原因不抛异常（由引擎挂起待指派）。
- `CP6.Core/Services/Wf/IFlowDefService.cs` — 流程定义服务接口 + 实例详情 DTO（实例+痕迹+任务）。
- `CP6.Core/Services/Wf/FlowDefService.cs` — 流程定义实现：按 FlowKey upsert（schema 变更才升版）+ 实例详情聚合查询。
- `CP6.Core/Services/Wf/IWfNotifier.cs` — 流程实时通知接口 + NullWfNotifier 空实现（无 SignalR/单测用）；新待办推送处理人。
- `CP6.Core/Services/Wf/ITaskCenterService.cs` — 待办中心接口 + 待办项/我的申请项 record：我的待办/我的申请/撤回。
- `CP6.Core/Services/Wf/TaskCenterService.cs` — 待办中心实现：查待办、查申请、发起人撤回（置 Withdrawn + 清在途待办 + 记痕迹）。
- `CP6.Core/Services/Wf/IApprovalCallback.cs` — 业务侧审批回调契约 + 回调上下文：业务模块实现并注册，OA 终态时反向直调（共享 DbContext 保证原子，失败抛异常触发整体回滚）。
- `CP6.Core/Services/Wf/ApprovalDispatcher.cs` — 终态分发器：按实例 BizType 在注册的回调集合中找对应业务回调同步直调（无 BizType 不回调，配错抛异常）。
- `CP6.Core/Services/Wf/IApprovalService.cs` — 审批服务接口 + ApprovalStatus 枚举：业务接入 OA 唯一入口（Submit 起审批 / GetStatus 查状态）。
- `CP6.Core/Services/Wf/ApprovalService.cs` — 审批服务实现：防重 + 按 Wf_ApprovalBinding 选流程 + 把 formSnapshot 序列化进实例变量后委托引擎起流程。
- `CP6.Core/Services/Wf/ExpressionEvaluator.cs` — 安全表达式求值器（手写递归下降）：白名单字段+比较/逻辑/算术+内置函数，绝不 eval、任何错误安全失败（条件流转与表单规则后端复算共用）。
- `CP6.Core/Services/Wf/ConditionEvaluator.cs` — 条件求值器向后兼容门面：转发到 ExpressionEvaluator，供既有流程 condition 调用点无缝沿用。
- `CP6.Core/Services/Wf/FormSchema.cs` — 表单 schema 模型：字段（类型/必填/长度/正则）+ 规则（When/Then 显隐/计算/联动）后端复核所需结构。
- `CP6.Core/Services/Wf/IFormService.cs` — 表单引擎接口：表单定义 upsert + 提交数据（服务端复核）+ 纯校验 + 规则复算并校验。
- `CP6.Core/Services/Wf/FormService.cs` — 表单引擎实现：SchemaJson/DataJson 直存 JSON 列，提交走服务端 schema 复核（前端校验不可信），改版只升版本。
- `CP6.Core/Services/Wf/WfStatus.cs` — 流程实例/任务状态常量：实例(进行中/通过/驳回/撤回/挂起) + 任务(待办/同意/驳回/作废/挂起)。
- `CP6.Core/Services/Wf/FlowSchema.cs` — 流程 schema 模型：节点（类型/审批人规则/会签规则/字段权限/超时）+ 条件边，驱动引擎状态机。
- `CP6.Core/Services/Wf/IFlowEngine.cs` — 流程引擎接口：起流程/办理(幂等)/退回/加签/登记委派。
- `CP6.Core/Services/Wf/FlowEngine.cs` — 流程引擎状态机实现（partial）：建实例进首节点、办理+会签三规则判定+条件流转，全状态落库可重放。
- `CP6.Core/Services/Wf/AdvancedFlow.cs` — 流程引擎高级动作（FlowEngine partial）：退回/加签/委派，难点在动作后的在途待办作废与前加签挂起清理。
- `CP6.Core/Services/Wf/WfTimeoutService.cs` — 超时扫描服务（接口+实现）：周期扫到期待办按 TimeoutAction 处理（催办/自动通过驳回/升级），双重幂等。

#### Services/Pub

PUB 公共平台服务（采番/附件/Excel/代码生成 + 通用 CRUD 泛型基座，PUB 章05~08）。

- `CP6.Core/Services/Pub/ISeqService.cs` — 富采番服务接口：按业务键生成号码（前缀+日期段+周期重置流水）。
- `CP6.Core/Services/Pub/SeqService.cs` — 采番实现：跨周期重置流水、即时提交（号码允许跳号），未配置抛 E-PUB-051。
- `CP6.Core/Services/Pub/IFileStore.cs` — 文件存储抽象接口：保存/读取/删除/存在判定（本地/OSS/MinIO 可切换）。
- `CP6.Core/Services/Pub/LocalFileStore.cs` — 本地磁盘文件存储实现（根目录可配），DB 存相对路径，云存储留接口另实现。
- `CP6.Core/Services/Pub/IAttachmentService.cs` — 统一附件服务接口：上传(秒传)/列表/下载/删除(引用计数)/草稿转正。
- `CP6.Core/Services/Pub/AttachmentService.cs` — 附件服务实现：MD5 秒传复用 + 大小/类型校验 + 按 StorePath 引用计数物理删除。
- `CP6.Core/Services/Pub/ExcelModels.cs` — Excel 列配置/导入结果模型：一份列配置（字段/标题/字典/格式/必填）驱动导出/模板/导入。
- `CP6.Core/Services/Pub/IExcelService.cs` — 通用 Excel 导入导出接口：导出(过字典翻译)/生成模板/导入(逐行校验)。
- `CP6.Core/Services/Pub/ExcelService.cs` — Excel 实现（ClosedXML）：列配置驱动导出/模板/导入，导入逐行校验并产出标红错误文件。
- `CP6.Core/Services/Pub/CodeGenService.cs` — 代码生成器：GenTable/GenColumn 经 Scriban 渲染 Controller/Service/Entity 等产物（开箱装配四粒度），含 custom 区块二次生成保护。
- `CP6.Core/Services/Pub/BaseCrudService.cs` — 通用 CRUD 泛型服务基座：把数据权限注入/自动采番/部门归属/拒写只读字段固化进基类，子类只给资源键。

#### Auth

权限的两个 MVC 过滤器特性（贴控制器/动作即生效）。

- `CP6.Core/Auth/RequirePermissionAttribute.cs` — 功能权限强校验特性（IAsyncAuthorizationFilter）：命中 "menuKey:action" 才放行，否则 403。
- `CP6.Core/Auth/FieldMaskAttribute.cs` — 字段权限掩码特性（IAsyncResultFilter）：序列化前反射把当前用户的隐藏字段置空（信封返回不生效，需服务内手动掩码）。

#### BaseProvider

早期通用 CRUD 泛型基类（仓储+服务），按 Guid 主键的简单增删改查地基。

- `CP6.Core/BaseProvider/IRepository.cs` — 泛型仓储接口：FindById/分页/增/改/批量删。
- `CP6.Core/BaseProvider/RepositoryBase.cs` — 泛型仓储实现：基于 CP6Context.Set<T> 的通用增删改查，新业务继承免重复写。
- `CP6.Core/BaseProvider/IService.cs` — 泛型服务接口：取单条/分页/增改删。
- `CP6.Core/BaseProvider/ServiceBase.cs` — 泛型服务基类：封装通用业务（创建时盖 CreateDate），子类可重写加自定义逻辑。

#### EFDbContext

- `CP6.Core/EFDbContext/CP6Context.cs` — 全局核心 DbContext（约 1938 行）：聚合 Sys/Pub/Wf/Fin/Mes/Plan/Pur/Wms 全模块 DbSet，OnModelCreating 配置实体映射 + 多租户全局查询过滤(HasQueryFilter)与写入盖章；可选注入 ITenantContext（无注入回退默认租户，故既有单测无需改造）。

#### Options

- `CP6.Core/Options/IntegrationEventOptions.cs` — 集成事件重试 worker 配置：最大尝试数/指数退避秒数/轮询间隔/开关 + 按尝试次数取退避秒数的助手方法。

#### Utilities

跨模块基础设施（缓存/消息中间件/JWT）。

- `CP6.Core/Utilities/CacheService.cs` — 缓存服务：封装 IDistributedCache 提供强类型读写（开发内存、生产 Redis 一行切换，Cache-Aside）。
- `CP6.Core/Utilities/IOperLogTransport.cs` — 操作日志传输中间件抽象：把"用什么消息中间件投递操作日志"解耦，Kafka/RabbitMQ 可并存按配置选用。
- `CP6.Core/Utilities/KafkaProducerService.cs` — Kafka 操作日志生产者（IOperLogTransport 实现）：Singleton 复用 IProducer，未配置则安全退出跳过本通道，按 UserName 分区保序。
- `CP6.Core/Utilities/INotificationPublisher.cs` — 业务事件通知契约 + BusinessNotification 模型：低频确实配信的业务告警发行口（与高吞吐操作日志区分，走 RabbitMQ）。
- `CP6.Core/Utilities/RabbitMQService.cs` — RabbitMQ 业务通知/告警生产者（INotificationPublisher 实现）：长连接 Singleton、按发行建销 Channel，best-effort 不阻断业务。
- `CP6.Core/Utilities/JwtHelper.cs` — JWT Token 生成工具（静态）：按用户Id/名/密钥/签发者/受众/过期时长签发 Token。

#### Pub(Core顶层)

- `CP6.Core/Pub/BaseCrudController.cs` — 通用 CRUD 泛型控制器基座：提供 query/add/edit/del 的 virtual 方法体（权限/掩码常量特性由代码生成的具体子类 override 时贴上，基类只兜逻辑，本身不被路由）。

#### Migrations(汇总)

`CP6.Core/Migrations/` 共 **175 个 .cs 文件**，是 EF Core 数据库迁移（每个迁移=一次 schema 变更快照，自 2026-04-08 `Init` 起按时间戳累积，覆盖 MSBBPA 报价/产品、Mes、Wms 多阶段、Pub 组织与四粒度权限、OA 表单/流程引擎、Fin 财务、I18n、多租户等演进）：

- **87 个**迁移定义文件（`{时间戳}_{名称}.cs`，含 Up/Down）。
- **87 个**配对的 `.Designer.cs`（该迁移时点的模型快照，EF 自动生成，不手改）。
- **1 个** `CP6ContextModelSnapshot.cs` —— 当前数据库模型的总快照，EF 据此与下一次模型 diff 出新迁移（核心存在物，全局唯一）。

绝大多数为 EF 自动脚手架；少数含手写 SQL/数据回填的代表性迁移（值得点名）：

- `20260518115050_AddMesStoredProcedures.cs` —— 手写 `migrationBuilder.Sql` 创建 MES 仪表盘存储过程 + 复合索引（性能调优）。
- `20260615153849_FinJournalLineNoMutateTrigger.cs` —— 手写 DB 触发器：已过账/红冲凭证分录被 UPDATE/DELETE 时 ROLLBACK+THROW（防绕过应用层篡改的兜底）。
- `20260616125141_MultiTenantOperLog.cs` —— 加 TenantId 列 + 手写 `UPDATE` 把存量操作日志回填默认租户（章10 多租户存量数据迁移代表）。
