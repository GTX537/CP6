# 16 · 把 CP6 完整跑一遍：从下订单到出货

## 🌱 你将学到

- 看到 CP6 一次完整业务流时，前面 15 章的概念怎么协同工作
- 找到自己还没搞懂的环节（这就是下一步学习的方向）
- 知道每个模块的角色，能在真实代码里指出"这是第 X 章讲的 Y 模式"

---

## 🍳 比喻：拍一部电影

前 15 章你认识了每个角色：

- 摄影师（前端）
- 演员（API Controller）
- 导演（Service）
- 编剧（Entity / 数据形状）
- 制片（DI 容器）
- 录音师（SignalR）
- 剪辑（缓存）
- 配乐（i18n）
- 灯光（中间件）
- 群演（BackgroundService）

这一章是看一遍**完整的电影**，看所有人怎么配合。

---

## 🎬 完整剧本：王太太下订单到收到货

### 第 1 幕：登录

**1.1 王太太打开浏览器到 cp6.uk**

浏览器请求 → Cloudflare 边缘节点 → cloudflared 隧道 → CP6 容器 → cp6-web Nginx → 返回 HTML/JS

**1.2 前端启动**（[第 09 章]）

```typescript
// main.ts
await initI18n()   // 先加载翻译
createApp(App).use(pinia).use(router).use(i18n).use(ElementPlus).mount('#app')
```

i18n 调 `/api/lang/zh-CN` 拿翻译字典（[第 10 章]）。

**1.3 王太太输入用户名密码点登录**

```typescript
const res = await http.post('/auth/login', { userName, password })
// res = { token, menus, userName }
localStorage.setItem('token', res.token)
localStorage.setItem('menus', JSON.stringify(res.menus))
addDynamicRoutes(res.menus)   // [第 09 章] 动态路由
router.push('/dashboard')
```

**1.4 后端在干什么**

```
请求到 ASP.NET Core
↓
中间件管道（[第 02 章]）：UseCors → UseAuthentication → UseAuthorization
↓
路由匹配到 AuthController.Login
↓
[AllowAnonymous] 标记 → 不需要 token
↓
Service 校验用户密码（[第 15 章]：hash 验证）
↓
JwtHelper.GenerateToken（[第 07 章]）签 JWT
↓
查 Sys_RoleMenu + Sys_Menu 拿用户能进的菜单（[第 10 章]）
↓
返回 { code: 200, data: { token, menus, userName } }
```

**1.5 OperLogFilter 想记日志吗？**

不记。因为 `/api/auth/*` 被跳过（[第 07 章]：防密码泄露）。

---

### 第 2 幕：浏览到受注入力页

**2.1 王太太点左侧菜单 "ERP → 受注入力"**

```typescript
// router/index.ts 守卫
router.beforeEach((to, _from, next) => {
  // 已登录 + 路由已加载 → 放行
  next()
})
```

Vue Router 切到 `/order` 路径，懒加载 `views/erp/OrderEntryView.vue`（[第 09 章]）。

**2.2 OrderEntryView 加载初始数据**

```vue
<script setup>
onMounted(async () => {
  customers.value = await http.get('/business-partner/list?role=customer')
  products.value = await http.get('/product/list')
})
</script>
```

两个请求并行：

- 请求 1：axios 拦截器加 `Authorization: Bearer <token>`（[第 09 章]）
- 后端 OperLogFilter 检查 → GET 默认不记日志（[第 07 章]）
- ASP.NET Core 路由 → BusinessPartnerController.GetList → BusinessPartnerService → IRepository<BusinessPartner> 或直接 _context.BusinessPartners（[第 04 章]）
- EF Core 翻译 LINQ → SQL → SQL Server 返回数据
- AsNoTracking 让 EF 不追踪（[第 03 章]）
- 返回 JSON
- 前端拦截器解 `{ code, data }` → 直接返回 `data` 数组

---

### 第 3 幕：填表 + 提交

**3.1 王太太选客户、加 3 行明细、点保存**

```typescript
async function submit() {
  const order = await http.post('/order/create', formData)
  ElMessage.success('受注作成成功')
  router.push('/order-list')
}
```

**3.2 后端 OrderService.CreateAsync**

```csharp
public async Task<Order> CreateAsync(OrderCreateDto dto, string user)
{
    // 1. 校验
    if (await _context.Orders.AnyAsync(o => o.WebOrderNo == dto.WebOrderNo))
        throw new InvalidOperationException("受注号重复");

    // 2. 采番（[第 06 章]统一采番）
    var orderNo = await _docNumber.NextAsync("ORD");

    // 3. 创建实体（[第 01 章]Entity 项目里定义的 POCO）
    var order = new Order
    {
        WebOrderNo = orderNo,
        CustomerCd = dto.CustomerCd,
        Details = dto.Details.Select(d => new OrderDetail { /* ... */ }).ToList()
    };

    // 4. 写 DB（[第 03 章]EF Core 自动事务）
    _context.Orders.Add(order);
    await _context.SaveChangesAsync();

    // 5. Bridge Hook 通知 MES 和 WMS（[第 06 章] best-effort）
    try { await _mesBridge.OnOrderCreatedAsync(order.WebOrderNo, user); }
    catch (Exception ex) { _logger.LogWarning(ex, "MES bridge failed"); }
    
    try { await _wmsBridge.OnOrderCreatedAsync(order.WebOrderNo, user); }
    catch (Exception ex) { _logger.LogWarning(ex, "WMS bridge failed"); }

    return order;
}
```

**3.3 IMesBridgeHook 干什么**

```csharp
public class MesBridgeHook : BridgeHookBase, IMesBridgeHook
{
    public async Task OnOrderCreatedAsync(string webOrderNo, string user)
    {
        try
        {
            var wo = await _workOrderService.ExpandFromOrderAsync(webOrderNo, user);
            // 写 T_IntegrationEvent 记录成功
            await PersistEventAsync(..., IntegrationEventStatus.Success, targetNo: wo.WorkOrderNo);
        }
        catch (InvalidOperationException) { /* 已展开过，SKIPPED */ }
        catch (Exception ex)
        {
            await PersistEventAsync(..., IntegrationEventStatus.Failed);
            throw;
        }
    }
}
```

**3.4 IWmsBridgeHook 干什么**

类似，调 `InboundService` 或 `OutboundService` 自动生成出货指示。

**3.5 OperLogFilter 现在记日志**

POST 请求，路径 `/api/order/create`，记录入参、用户、耗时（[第 07 章]）。投递到 Kafka topic。

**3.6 Kafka Consumer**

```csharp
// KafkaOperLogConsumer
while (...)
{
    var log = await ConsumeOneAsync();
    using var scope = _factory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
    db.Sys_OperLogs.Add(log);
    await db.SaveChangesAsync();

    // 推 SignalR 通知所有连接的客户端（[第 08 章]）
    await _hub.Clients.All.SendAsync("NewOperLog", log);
}
```

**3.7 前端 Dashboard 收到推送**

任何打开 Dashboard 的人立刻看到一条新操作日志（[第 08 章]：SignalR）。

---

### 第 4 幕：工厂车间生产

**4.1 工厂经理在 MES 看到刚展开的工单**

```
前端 → GET /api/mes/work-order/list
→ WorkOrderService → 查 DB 返回
```

经理点"发行"按钮：

```
POST /api/mes/work-order/issue
→ WorkOrderService.IssueAsync
→ 工单状态变 Issued
→ IWmsBridgeHook.OnWorkOrderIssuedAsync（自动生成材料出库单）
→ OutboundService.CreateMaterialOutbound（[第 05 章]：库存铁律）
→ IStockMovementService.ApplyAsync(WmsTxnType.Allocate, ...) 引当材料
→ 写 T_StockTransaction
→ T_Stock.AllocatedQty +=, AvailableQty -=
```

如果引当不足：

```csharp
// OutboundService
if (header.OutboundType == OutboundType.Material && shortage > 0)
{
    // [第 06 章] Phase 9：不抛异常，写 T_MaterialShortage + SignalR 推送
    _context.MaterialShortages.Add(new MaterialShortage { ... });
    await _hub.Clients.All.SendAsync("MaterialShortageDetected", ...);
}
```

**4.2 拣货员去拣材料**

```
WMS 拣货操作 → IStockMovementService.ApplyAsync(WmsTxnType.Outbound, qty: -X)
→ T_Stock.PhysicalQty -=, AllocatedQty -=
→ 写 T_StockTransaction
```

**4.3 工人做完工序**

```
POST /api/mes/production-result/create
→ ProductionResultService.CreateAsync
→ 全工程完了时：
  → IWmsBridgeHook.OnProductionCompletedAsync
  → InboundService.CreateFinishedGoodsFromWorkOrderAsync
  → IStockMovementService.ApplyAsync(WmsTxnType.Inbound, qty: +X) 完成品入库
```

---

### 第 5 幕：出货

**5.1 WMS 拣完成品 + 包装**

```
WMS 出库操作 → IStockMovementService.ApplyAsync(WmsTxnType.Outbound, qty: -X)
WMS 包装、出货确定
```

**5.2 IErpBridgeHook 回写**

```csharp
public class ErpBridgeHook : BridgeHookBase, IErpBridgeHook
{
    public async Task OnShipmentConfirmedAsync(string outboundNo, string user)
    {
        // 找对应的 ERP 受注明细
        var outbound = await _ctx.OutboundOrders.SingleAsync(o => o.OutboundNo == outboundNo);
        var orderDetail = await _ctx.OrderDetails
            .FirstAsync(d => d.WebOrderNo == outbound.WebOrderNo && d.ProductCd == outbound.ProductCd);

        // 回写出货数量
        orderDetail.ShippedQty += outbound.ConfirmedQty;
        orderDetail.ShipDate = DateTime.Now;

        // 更新受注头状态
        var header = await _ctx.Orders.SingleAsync(o => o.WebOrderNo == outbound.WebOrderNo);
        var allDetails = await _ctx.OrderDetails.Where(d => d.WebOrderNo == header.WebOrderNo).ToListAsync();
        if (allDetails.All(d => d.ShippedQty >= d.OrderQty))
            header.ShipStatus = 9;   // 全出货

        await _ctx.SaveChangesAsync();
        await PersistEventAsync(..., IntegrationEventStatus.Success);
    }
}
```

王太太刷新订单列表 → 看到状态变 "已出货"。

---

### 第 6 幕：监控

**整个流程中，运维大屏看到什么**

- `/wms/bridge-health` 自动刷新（[第 13 章]：Bridge Health Monitor）
  - ERP→MES OnOrderCreated: +1 success
  - MES→WMS OnWorkOrderIssued: +1 success
  - MES→WMS OnProductionCompleted: +1 success
  - WMS→ERP OnShipmentConfirmed: +1 success
- 24h 成功率显示 100%
- DLQ 为空

**Prometheus** 抓取 /metrics 看到指标变化：

```
cp6_bridge_success_total{source="ERP",target="MES",hook="OnOrderCreatedAsync"} 1234
```

如果哪个 Hook 失败 → IntegrationEventRetryWorker 每 60 秒重试。5 次失败 → 转 Dead → DeadLetterNotifier 推 SignalR + 写 Sys_OperLog(IsAlert=true)。

---

## 🤔 你应该看到什么

整个流程涉及 15 个章节的内容：

| 章节 | 出现在哪 |
|---|---|
| 01 分层 | Controller 调 Service，Service 不知道 HTTP |
| 02 DI | OrderService 注入 IMesBridgeHook 等 |
| 03 EF/Dapper | _context.Orders.Add + SaveChanges 写库 |
| 04 仓储/Service | 简单 CRUD 用 _repo，复杂 LINQ on _ctx |
| 05 库存铁律 | 每次库存动经 IStockMovementService |
| 06 Bridge Hook | 4 个 Bridge Hook 自动联动 |
| 07 JWT/Filter | 每个请求经 OperLogFilter |
| 08 SignalR | Kafka Consumer 推 NewOperLog |
| 09 Vue3 | 前端 Composition API + 拦截器 |
| 10 i18n/RBAC | 翻译 + 菜单按角色加载 |
| 11 测试 | 测试覆盖每个 Service 关键路径 |
| 12 DevOps | 整套跑在 docker-compose / K8s |
| 13 可观测 | Bridge Health 看板 + Prometheus 指标 |
| 14 性能 | AsNoTracking + 缓存 + 异步 |
| 15 安全 | JWT + 参数化 SQL + 强制 secret |

---

## ✋ 动手试试：跑一遍完整流程

这是这套书最重要的练习。把整套跑一遍。

### 1. 准备

```bash
cd D:\CP6
# 创建 .env 文件，参考第 12 章
docker compose up -d --build
```

等所有容器 healthy。

### 2. 登录

打开 `http://localhost:8080`，用 CP6 默认账号登录（admin / 你的密码）。

打开 F12 → Network，留着看。

### 3. 创建一个受注

ERP → 受注入力，选客户、加明细、保存。

观察：

- Network 标签：看到 `POST /api/order/create` 的请求
- 浏览器右上角应该有"受注作成成功"提示
- 切到受注一览，看到刚创建的

### 4. 查数据库验证 Bridge Hook 发生了

打开数据库工具（SSMS / Azure Data Studio）连 `localhost:1433`：

```sql
-- 看刚创建的受注
SELECT TOP 5 * FROM T_Order ORDER BY CreateDate DESC;

-- 看是否触发了 hook
SELECT TOP 10 * FROM T_IntegrationEvent ORDER BY CreateDate DESC;

-- 看是否产生了对应的 WO（如果 MesBridge:Enabled = true）
SELECT TOP 5 * FROM T_WorkOrder ORDER BY CreateDate DESC;
```

### 5. 模拟生产

到 MES → 製造実績入力，给上面的工单录工序实绩。完整工程完了后查：

```sql
-- 看完成品入库
SELECT TOP 5 * FROM T_InboundReceipt ORDER BY CreateDate DESC;
SELECT TOP 5 * FROM T_StockTransaction WHERE TxnType = 'Inbound' ORDER BY CreateDate DESC;
```

### 6. 出货

WMS → 出库 / 出货确认。出货完了后查：

```sql
-- 看 ErpBridge 回写
SELECT WebOrderNo, ShippedQty, ShipDate, ShipStatus FROM T_OrderDetail WHERE ... ;
```

### 7. 看监控

访问 `/wms/bridge-health`，看 24h 统计有变化。

访问 `http://localhost:9991/metrics`，搜 `cp6_bridge_success_total`，看数字。

---

## 🎓 学完之后

如果你能跟着上面的流程跑通一次，**你已经超越 80% 的"一知半解" 的同行**了。CP6 涉及的所有核心概念你都摸过了。

下一步建议：

1. **重读高级版本**：现在再读 [`docs/learning/`](../learning/) 同名章节，能看出更多门道（取舍、对比、面试角度）
2. **看 git log**：通过 commit history 理解每次改动的理由
3. **改一个小功能**：找一个你觉得能改进的点（如加 ResponseCompression），实际改一下
4. **写文档**：自己复述本书的某一章给"假想读者"听，能讲清楚就真懂了

---

## 📚 想再学一点

- 高级版本第 16 章：[60 道模拟面试题](../learning/16-mock-interview.md)
- 项目内：[`docs/PROJECT_STRUCTURE.md`](../PROJECT_STRUCTURE.md) §三业务流程
- 项目内：[`docs/business-flow-walkthrough.md`](../business-flow-walkthrough.md)（如果存在）

---

**走到这里你已经辛苦了。读完不等于全懂，但你现在的基础比开始读时强 10 倍**。

剩下的功夫在反复看 CP6 真实代码 + 自己动手改。

慢慢来，从一知半解到豁然开朗，往往就在某个深夜读懂某段代码的瞬间。
