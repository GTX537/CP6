# C# / ASP.NET Core / EF Core 速查

## C#

- 值类型变量含值；引用类型变量含引用；默认参数都按值。
- decimal 适合十进制金额，但仍有精度、范围、舍入和 DB p/s。
- class 有身份；struct 小型值；record 结构相等，`with` 浅复制。
- 相等对象必须同 hash；可变 key 危险。
- 泛型约束给编译期能力；开放泛型是构造规则。
- 接口是能力契约；抽象类共享状态/实现；不机械造接口。
- `throw;` 保堆栈；`throw ex;` 重置。
- `string?` 是编译器契约；`!` 只压警告。

## 异步

- async ≠ 线程；I/O await 释放等待线程，提高吞吐。
- 独立任务先启动再 WhenAll；同一 DbContext 不并发。
- token 一路透传；取消 ≠ 超时 ≠ 失败。
- `.Result` 阻塞/饥饿；`async void` 只留事件边界。
- fire-and-forget 用队列/outbox/作业，不裸 `_ =`。
- BackgroundService 为 singleton，scoped 服务用 scope factory。
- Kafka 手动 commit：至少一次窗口，消费者幂等。

## LINQ

- IEnumerable：委托/本地枚举；IQueryable：表达式/provider。
- 延迟执行到 ToList/First/Count/Any。
- 过滤/投影/分页在物化前。
- 只读 AsNoTracking。
- N+1：投影、Include、批量、拆分，权衡往返/膨胀。
- 稳定排序后分页；深分页考虑 keyset。

## DI

- Transient 每解析；Scoped 每 scope；Singleton 每宿主。
- singleton 不持有 scoped；需要时创建 scope。
- 多实现解析 `IEnumerable<T>`。
- Program 注册按基础、横切、业务域、基础设施、管道读。

## 管道和安全

```text
Metrics → AuthN → Tenant → Localization → Exception
→ CSRF → MustChangePwd → AuthZ → Endpoint
```

- AuthN 是谁；AuthZ 能做什么。
- JWT 签名不加密。
- 当前 httpOnly Cookie + CSRF header。
- 401 刷新；403 无权限。
- 前端隐藏是 UX，后端 403 是边界。
- CORS 不是认证。

## EF

- DbContext = unit of work + query + tracking；不线程安全。
- 五态：Detached/Unchanged/Added/Modified/Deleted。
- 单次 SaveChanges 关系库通常事务；外部消息不在内。
- RowVersion 乐观并发 → 409/重读/有界重试。
- 全局租户过滤绕过：Ignore、Dapper、特殊表、后台、写入错误租户。
- ExecuteUpdate 绕过 tracking/SaveChanges 审计。
- InMemory 不证明 SQL/约束/事务/并发。

## CP6 三个限制

1. `MoveAsync` 两腿两次提交，非原子。
2. `StampTenant` 仅 Added 且空租户时盖章，非空不一致需更严防线。
3. commit 后桥接/通知空 catch，需日志/outbox/补偿。

