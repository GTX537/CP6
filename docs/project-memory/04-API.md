# API 与集成边界

## 组织方式

Controller 位于 `CP6.WebApi/Controllers/<Domain>`，2026-07-18 实扫 145 个。前端调用封装位于 `cp6.web/src/api`，类型位于 `src/types`。

| 域 | Controller 数 |
|---|---:|
| Sys | 15 |
| ERP | 15 |
| MES | 11 |
| WMS | 32 |
| FIN | 23 |
| PUR | 8 |
| WF | 5 |
| OA | 14 |
| PUB | 3 |
| PLAN | 2 |
| Space | 9 |
| Integration | 3 |

## HTTP 契约

- Controller 通常继承本地化基类，返回统一 code/message/data 结构。
- GET 用于读取；存在少量有明确豁免记录的只读 POST。
- 写端点必须有认证、租户边界及业务权限；权限键由 `RequirePermission` 声明。
- 错误码通过数据库 i18n 键传到前端，由 `http.ts` 翻译。
- CSRF、Cookie/JWT、强制改密、异常本地化及操作日志由中间件/过滤器处理。

## 权限 API 约束

- 各模块反射测试扫描 POST/PUT/PATCH/DELETE，防止写端点漏贴权限。
- “贴点 ⊆ 种子”互锁测试保证后端声明的键已进入权限目录。
- 只读 POST 豁免必须显式登记；不得通过改方法名或继承反射盲区逃逸扫描。
- 前端按钮键必须逐字来自 `docs/seeds` 权威表，不自行发明键名。

## 实时与异步

- SignalR 用于工作流通知、MES/WMS 看板等实时反馈。
- Kafka/RabbitMQ/Outbox 用于日志流或业务异步边界，具体启用情况以配置和服务注册为准。
- 工作流通知在事务内只写 outbox；提交后由 `WfNotificationDispatchWorker` 使用 `Clients.User(userId)` 定向推送，目标用户 ID 与 JWT `NameIdentifier` 保持一致。禁止退回 `Clients.All` 后由客户端过滤。

## API 变更检查

1. 查同域 Controller/Service/DTO/前端 API 先例。
2. 明确认证、租户、权限键、错误码和幂等语义。
3. 同步 DTO 与 TypeScript 类型。
4. 补服务测试、权限反射测试及必要的 401/403 验证。
5. 更新 `docs/seeds`、i18n 种子和项目记忆状态。
