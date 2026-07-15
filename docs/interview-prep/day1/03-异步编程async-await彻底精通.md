# 第 3 章　异步编程 async/await 彻底精通

> 面试官的第一道刀，八成落在这里。
>
> 「你说你写了 5 年 C#——那你告诉我，`await` 之后代码在哪个线程上跑？」
> 「`async void` 为什么是罪恶？」
> 「`.Result` 什么时候死锁，为什么？」
>
> 这一章我把 async/await 从**操作系统的线程**一直讲到**编译器生成的状态机**，再到 **CP6 真实生产代码**里每一处异步是怎么写、为什么这么写。读完这一章，上面三个问题你能反过来给面试官上课。
>
> 本章所有代码标本来自 `C:\CP6`——一套 .NET 8 多租户制造业 ERP/MES/WMS 系统。凡引用真实代码，都标了文件路径，你可以自己打开对照。

---

## 目录

1. [为什么需要异步——从线程和线程池讲起](#1-为什么需要异步从线程和线程池讲起)
2. [异步 ≠ 多线程——这句话讲透](#2-异步--多线程这句话讲透)
3. [Task 体系：Task / Task&lt;T&gt; / ValueTask](#3-task-体系task--tasktt--valuetask)
4. [async/await 语法与语义：编译器状态机](#4-asyncawait-语法与语义编译器状态机)
5. [SynchronizationContext 与 ConfigureAwait(false)](#5-synchronizationcontext-与-configureawaitfalse)
6. [组合与并发：WhenAll / WhenAny / 限流 / 超时](#6-组合与并发whenall--whenany--限流--超时)
7. [CancellationToken 完整篇](#7-cancellationtoken-完整篇)
8. [必坑全集（错误代码→现象→原理→正确写法）](#8-必坑全集)
9. [异步流：IAsyncEnumerable 与 await foreach](#9-异步流iasyncenumerable-与-await-foreach)
10. [后台服务里的异步：BackgroundService + Scope](#10-后台服务里的异步backgroundservice--scope)
11. [面试深水区问答](#11-面试深水区问答)
12. [本章面试题 15 问（详细答案）](#12-本章面试题-15-问)
13. [自测清单](#13-自测清单)
14. [动手练习：把坏异步代码改对](#14-动手练习把坏异步代码改对)

---

## 1. 为什么需要异步——从线程和线程池讲起

### 1.1 先搞清楚：线程（Thread）是什么

**类比。** 把你的服务器进程想象成一家餐厅。**线程就是服务员**。每个服务员（线程）能同时只干一件事：要么带位、要么点单、要么上菜。CPU 核心就是「后厨的灶台」——真正干活的地方。服务员多，能同时招待的桌子就多；但服务员本身要占位置（内存）、要发工资（调度开销）。

技术事实：

- 一个 .NET 线程，**默认栈空间 1 MB**（Windows）。开 1000 个线程 ≈ 1 GB 内存只是栈，还没算别的。
- 线程由**操作系统调度**。CPU 在众多线程之间来回切换，每次切换要保存/恢复寄存器、刷新缓存，这叫**上下文切换（context switch）**，是有成本的（微秒级，但量大了很可观）。
- 线程数远大于 CPU 核心数时，大量时间浪费在「切换」而非「干活」上——这叫**线程颠簸（thrashing）**。

### 1.2 线程池（ThreadPool）：不要现招现雇服务员

每次来一个请求就 `new Thread()`，等于每来一桌客人就去人才市场现招一个服务员，用完解雇——招聘/解雇成本极高。

.NET 的解法是**线程池**：进程启动时养一批服务员待命，活儿来了从池子里领一个去干，干完还回池子里待命。

- ASP.NET Core 的每个 HTTP 请求，默认就是从**线程池**领一个线程来处理的。
- 线程池有**最小/最大线程数**。默认最小线程数 ≈ CPU 逻辑核心数。当活儿突然暴增、池里现成线程不够时，线程池会**慢慢**（默认每约 0.5 秒补一个）注入新线程——这个「慢慢」很关键，是后面「线程池饥饿」事故的伏笔。

### 1.3 同步 I/O 阻塞的代价——核心图

现在到了本章最重要的一张图。制造业系统 90% 的操作是 **I/O**：查数据库、调下游 HTTP 接口、读写文件、发 Kafka 消息。这些操作的共同特点是——**发出请求后，绝大部分时间在「等」**：等数据库磁盘寻道、等网络往返。

假设一个场景（很真实）：**100 个并发 HTTP 请求，每个请求要做一次数据库查询，DB 查询耗时 50ms。**

#### 模型 A：同步 I/O（阻塞）

```csharp
// 同步写法：查询期间线程被死死占住
public IActionResult GetStock()
{
    var list = _db.Stocks.ToList();   // ← 这一行阻塞 50ms，线程什么都干不了，就是干等
    return Ok(list);
}
```

时序图（每个 `T` 是一个被占用的线程）：

```
时间轴 ───────────────────────────────────────────────►
        0ms                    50ms
请求1   [T1 发SQL |■■■■■ 干等DB ■■■■■| 收结果]      T1 全程被占 50ms
请求2   [T2 发SQL |■■■■■ 干等DB ■■■■■| 收结果]      T2 全程被占 50ms
...
请求100 [T100...  |■■■■■ 干等DB ■■■■■| ...]         T100 全程被占 50ms

           ↑
   需要同时占用 100 个线程！
   而这 100 个线程 99% 的时间在「干等」，CPU 灶台其实是空的。
```

问题在哪？

- 100 个并发就要 **100 个线程同时被占住**。但线程池默认可能就几十个现成线程。
- 请求 51~100 只能排队，等线程池「每 0.5 秒补一个」慢慢注入——**延迟飙升，甚至请求超时**。
- 这些线程明明啥也没干（就在等网卡回数据），却白白占着 100 MB 内存和调度开销。这叫**线程池饥饿（thread pool starvation）**：不是 CPU 忙不过来，是**线程被阻塞操作全占光了，新活儿没线程接**。

#### 模型 B：异步 I/O（await）

```csharp
// 异步写法：查询期间线程「还」给线程池去干别的
public async Task<IActionResult> GetStock(CancellationToken ct)
{
    var list = await _db.Stocks.ToListAsync(ct);   // ← await 处线程被释放，DB 回数据后再继续
    return Ok(list);
}
```

时序图：

```
时间轴 ───────────────────────────────────────────────►
        0ms                    50ms
请求1   [T1 发SQL]→(线程还回池)···DB在等···→[任一线程 收结果]
请求2   [T1 发SQL]→(同一个T1去发请求2的SQL了!)···→[收结果]
请求3   [T1 发SQL]→ ...
...
                     ↑
   发 SQL 只花几微秒，发完线程立刻释放去发下一个请求的 SQL。
   50ms 的「干等」不占任何线程——由操作系统的 I/O 机制在背后等（下一节讲）。
   结果：几个线程就能「照看」100 个在途请求。
```

对比总结（背下来，面试直接用）：

| 维度 | 同步 I/O（阻塞） | 异步 I/O（await） |
|---|---|---|
| 100 并发 × 50ms 查询占用线程 | ~100 个 | 少数几个 |
| 线程在等 DB 时 | 被占死、干等 | 释放回池、去干别的 |
| 内存（仅栈） | ~100 MB | ~几 MB |
| 高并发下 | 线程池饥饿、请求排队 | 吞吐量线性扩展 |

### 1.4 吞吐量 vs 延迟——别搞混

面试常问「异步能让单个请求更快吗？」——**不能，甚至可能略慢一点点**（多了状态机调度开销）。

- **延迟（latency）**：单个请求从发出到拿到结果的时间。一次 50ms 的 DB 查询，同步和异步都是 ~50ms。异步**不会让单个查询变快**。
- **吞吐量（throughput）**：单位时间系统能处理多少请求。异步的价值全在这里——**用同样的线程，照看多得多的在途请求**，所以整个系统的吞吐量（QPS）大幅提升。

一句话面试答法：**异步不优化延迟，优化吞吐量与可伸缩性（scalability）。它让服务器在 I/O 密集负载下，用极少的线程扛住极高的并发。**

---

## 2. 异步 ≠ 多线程——这句话讲透

这是全章**最容易被面 5 年经验候选人卡住**的点。很多人以为 `await` = 「另起一个线程去干活」。**错。**

### 2.1 核心命题

> **异步 I/O 在等待期间，不占用任何线程。**

再回到餐厅类比。同步模型里，服务员点完单**站在后厨门口死等菜做好**——一个服务员被一桌客人占死。

异步模型里，服务员点完单，把订单**贴到后厨的挂单栏上**，然后回去招待别的桌。菜做好了，后厨**按铃**，任何一个空闲服务员听到铃就去端菜。**「等菜」这段时间没有任何服务员被占用**——等菜的是后厨（操作系统 + 硬件），不是服务员（线程）。

那个「后厨的挂单栏 + 按铃」，在操作系统层面就是 **I/O 完成端口（IOCP，I/O Completion Port）**。

### 2.2 异步 I/O 在操作系统层面到底怎么回事

一次异步数据库查询的真实生命周期：

```
① 应用线程 T1 调用 ToListAsync()
        │
        ▼
② 把 SQL 通过 socket 发给数据库，向操作系统注册一个「异步 I/O 请求」
   —— 关键：告诉网卡驱动「数据回来时，请通知 IOCP」
        │
        ▼
③ T1 立即返回（await 处），T1 被还回线程池 —— 现在 0 个线程在等这次查询
        │
        │   ......（50ms 过去，DB 在磁盘上找数据、网络往返）......
        │       这 50ms 里，没有任何线程为这次查询而阻塞！
        │       真正在「等」的是：网卡硬件 + 操作系统内核的 IOCP
        ▼
④ DB 结果通过网卡回到本机，网卡触发中断，操作系统把「完成事件」投递到 IOCP 队列
        │
        ▼
⑤ 线程池里一个空闲线程（可能是 T1，也可能是 T7——不保证同一个）
   从 IOCP 拿到完成事件，恢复状态机，继续执行 await 之后的代码
```

**结论：** I/O 的等待由**硬件和操作系统内核**负责，不消耗线程。线程只在「发起 I/O」（几微秒）和「处理结果」（几微秒）这两个瞬间被占用。中间几十毫秒的等待，线程是自由的。

这就是「异步 ≠ 多线程」的本质：**异步 I/O 压根不需要一个线程去「等」。**

### 2.3 那什么时候异步「真的」用了线程？

区分两种「异步」：

| 类型 | 例子 | 等待期间占线程吗？ |
|---|---|---|
| **I/O-bound 异步** | DB 查询、HTTP 调用、读文件、Kafka | **不占线程**（IOCP 机制） |
| **CPU-bound「异步」** | `Task.Run(() => 算一个大矩阵)` | **占一个线程**（真的在算） |

`Task.Run` 是把一段**计算**扔到线程池的另一个线程上跑——这才是「多线程」。而 `await db.ToListAsync()` 根本没有「另一个线程在算」，它只是注册了个回调等 IOCP。

面试金句：

> **`await` 一个 I/O 操作，不会创建线程、不会占用线程去等待——等待由操作系统的 I/O 完成端口负责。只有 `Task.Run` 这类把 CPU 计算派到线程池的场景，才真正用到了额外线程。所以「异步」和「多线程」是正交的两件事：异步是关于「不阻塞地等」，多线程是关于「同时算」。**

### 2.4 CP6 里的真实印证：为什么阻塞式 Kafka 消费要 `Task.Run`

看 `C:\CP6\CP6.WebApi\BackgroundServices\KafkaOperLogConsumer.cs`——这里正好有一个「异步 vs 占线程」的教科书对照：

```csharp
protected override Task ExecuteAsync(CancellationToken stoppingToken)
{
    // ...
    // 阻塞式拉取循环 → 独立长线程，避免占用线程池
    return Task.Factory.StartNew(
        () => ConsumeLoop(bootstrap, topic, groupId, stoppingToken),
        stoppingToken,
        TaskCreationOptions.LongRunning,   // ← 关键
        TaskScheduler.Default);
}
```

注释写得很明白：**`Confluent.Kafka` 的 `Consume()` 是一个同步阻塞调用**——它不是 IOCP 式异步 I/O，它会**死占住调用它的线程**去等消息。

如果直接在 `await` 链里跑这个阻塞循环，就会**长期占住一个线程池线程**，是线程池饥饿的隐患。所以 CP6 用了 `TaskCreationOptions.LongRunning`——这个标志告诉运行时：「这个任务要跑很久，**别从线程池借线程，给它开一根专用的独立线程**」。这样就不会啃食线程池的容量。

这就完美诠释了本节主题：**面对天生阻塞（同步）的 API，你没法把它变成真异步，只能把它隔离到专用线程；而真正的异步 I/O（`ToListAsync`）根本不需要这么做。**

---

## 3. Task 体系：Task / Task&lt;T&gt; / ValueTask

### 3.1 Task 是什么——「一张取餐凭证」

**类比。** 你在快餐店点了餐，店员给你一张**取餐号码牌（Task）**。这张牌代表「一个将来会完成的操作」。你可以：

- 拿着牌等叫号（`await`）；
- 问「好了没？」（`task.IsCompleted`）；
- 叫到号时凭牌取餐，餐就是**结果**（`Task<T>` 的 `T`）。

`Task` = 一个**将来会完成的操作**的句柄。`Task<T>` = 一个**将来会产出一个 T 类型结果**的操作。

```csharp
Task        飞行中的操作，完成时无返回值（对应同步的 void）
Task<int>   飞行中的操作，完成时产出一个 int（对应同步的 int）
```

### 3.2 Task 的状态机（任务状态）

一个 Task 内部有状态。简化版：

```
   Created ──► WaitingToRun ──► Running ──┬──► RanToCompletion  (成功，有结果)
                                          ├──► Faulted          (抛异常了)
                                          └──► Canceled         (被取消了)
```

- `RanToCompletion`：正常完成。`await` 会返回结果。
- `Faulted`：里面抛了异常。**异常被「装」在 Task 里**，直到你 `await` 它（或访问 `.Result`）才「抛出来」——这是第 10 节和面试深水区的重点。
- `Canceled`：被 CancellationToken 取消，`await` 会抛 `OperationCanceledException`。

### 3.3 Task.Run vs 直接 await：CPU-bound 与 I/O-bound 的分界线

这是最高频的判断题。**判断依据只有一个：这个操作是在「等 I/O」还是在「烧 CPU」？**

```csharp
// ❌ 反面：I/O 操作不要包 Task.Run
public async Task<List<Stock>> GetStocksBad()
{
    return await Task.Run(() => _db.Stocks.ToListAsync());
    // 罪状：ToListAsync 本身就是异步 I/O，根本不需要额外线程。
    // 你却用 Task.Run 白白从线程池借了一个线程去「发起」这个查询——
    // 纯属浪费，在 ASP.NET Core 里还降低了吞吐量。
}

// ✅ 正面：I/O 操作直接 await
public async Task<List<Stock>> GetStocksGood(CancellationToken ct)
{
    return await _db.Stocks.ToListAsync(ct);   // 直接 await，不占线程去等
}

// ✅ 正面：真正的 CPU 密集计算，才用 Task.Run 挪出请求线程
public async Task<byte[]> RenderHeavyReport(ReportData data)
{
    // 假设这是一段纯 CPU 的重活（几百毫秒的图像渲染 / 大量数值计算）
    return await Task.Run(() => CpuHeavyRender(data));
    // 在 ASP.NET Core 里，把长 CPU 计算 Task.Run 到线程池，
    // 是为了尽快把「请求线程」还给服务器去接别的请求（避免长时间独占）。
}
```

一句话规则：

> **I/O-bound → 直接 `await` 那个 `...Async` 方法，绝不包 `Task.Run`。**
> **CPU-bound → 用 `Task.Run` 把计算挪到线程池，避免长时间占住调用线程。**

CP6 的所有服务方法（如上一节的 `StockMovementService.ApplyAsync`）都遵守前者：清一色 `await _db.XxxAsync(ct)`，全程**没有一个** `Task.Run` 去包 EF Core 调用。

### 3.4 Task.FromResult / Task.CompletedTask：给同步逻辑穿异步外衣

有时候你要实现一个**签名是异步**（返回 `Task`）的接口方法，但内部逻辑其实是同步的（没有真正的 I/O）。这时不要 `Task.Run`，用现成的已完成 Task：

```csharp
public Task<int> GetConfigValueAsync()
{
    int v = _cache["x"];              // 纯内存，同步就能算出来
    return Task.FromResult(v);        // 包成一个「已经完成、结果是 v」的 Task
}

public Task DoNothingAsync()
{
    return Task.CompletedTask;        // 一个「已经完成、无返回值」的 Task，全局单例，零分配
}
```

CP6 里的真实例子——`KafkaOperLogConsumer.ExecuteAsync`，当 Kafka 没配置时：

```csharp
if (string.IsNullOrWhiteSpace(bootstrap))
{
    _logger.LogWarning("Kafka 未配置，Kafka OperLog Consumer 不启动");
    return Task.CompletedTask;   // ← 什么都不做，直接返回一个「已完成」的 Task
}
```

注意这个方法签名是 `protected override Task ExecuteAsync(...)`——**没有 `async` 关键字**。因为它内部没有 `await`，直接返回 Task 即可。这是一个重要细节：**不是所有返回 Task 的方法都必须带 `async`**。带 `async` 会生成状态机（有开销）；如果你只是转发或返回一个现成 Task，就别加 `async`。

### 3.5 ValueTask：什么时候用，什么时候别乱用

**问题背景。** `Task` 是一个**引用类型（class）**，每次 `new` 一个 Task 都要在堆上分配、将来要 GC。对于**绝大多数会走真异步的方法**，这点分配无所谓。但如果一个方法**极高频调用**，且**大多数时候能同步就返回结果**（比如命中缓存），那每次都堆分配一个 Task 就浪费了。

`ValueTask<T>` 是一个**结构体（struct）**。当结果同步就绪时，它**不分配堆内存**，直接把值揣在栈上带走。

```csharp
public ValueTask<Product?> GetProductAsync(string code)
{
    // 90% 命中内存缓存 → 同步返回，ValueTask 不分配堆
    if (_cache.TryGetValue(code, out var p))
        return new ValueTask<Product?>(p);

    // 10% 未命中 → 走真异步 DB 查询
    return new ValueTask<Product?>(LoadFromDbAsync(code));
}
```

**ValueTask 的铁律（面试必答）：**

1. **一个 ValueTask 只能 `await` 一次。** 不能存起来 await 两遍，不能同时 `WhenAll` 多个来自同一 ValueTask 的东西。要多次消费，先 `.AsTask()` 转成 Task。
2. **不要在 ValueTask 上用 `.Result` / `.GetAwaiter().GetResult()` 除非它确定已完成。**
3. **默认还是用 `Task`。** 只有在**profiler 证明**某个高频方法的 Task 分配是热点，且它经常同步完成时，才换 ValueTask。**过早用 ValueTask 是新手炫技，反而容易踩「await 两次」的坑。**

一句话：**Task 是默认；ValueTask 是「高频 + 常同步完成」场景下、经测量后的性能优化，代价是使用约束更严。**

---

## 4. async/await 语法与语义：编译器状态机

这是「装懂」和「真懂」的分水岭。`async`/`await` **不是**「让代码在后台跑」的魔法关键字。它是**编译器的语法糖**——编译器会把你的 async 方法**改写成一个状态机（state machine）**。

### 4.1 先建立直觉：await 是「暂停点」

把一个 async 方法想成一段**可以暂停、之后从暂停处继续**的代码。每个 `await` 就是一个**潜在的暂停点**：

- 如果 await 的操作**已经完成**了（比如缓存命中的 ValueTask），就**不暂停**，直接往下跑（快路径）。
- 如果 await 的操作**还没完成**（比如 DB 查询在飞），方法就**在这里暂停、把控制权还给调用者**，等操作完成后再从这里继续。

「暂停后能从原地继续」——这在 C# 里靠**编译器生成的状态机**实现。

### 4.2 编译器状态机变换——简化的编译后伪代码

拿 CP6 的这个方法当原料（来自 `StockController.cs`，略微简化）：

```csharp
// 你写的源代码
public async Task<IActionResult> Search(string productCd)
{
    var q = _db.Stocks.Where(x => x.ProductCd == productCd);
    var total = await q.CountAsync();          // 暂停点 1
    var items = await q.Take(50).ToListAsync(); // 暂停点 2
    return Ok(new { total, items });
}
```

编译器大致把它改写成下面这样（**高度简化**的伪代码，帮助理解，不是逐字节精确）：

```csharp
// 编译器生成的状态机（伪代码）
public Task<IActionResult> Search(string productCd)
{
    var sm = new SearchStateMachine();
    sm._productCd = productCd;
    sm._builder = AsyncTaskMethodBuilder<IActionResult>.Create();
    sm._state = -1;                 // -1 = 初始状态
    sm._builder.Start(ref sm);      // 同步执行到第一个「未完成的 await」为止
    return sm._builder.Task;        // 立即把「取餐号码牌」还给调用者
}

struct SearchStateMachine : IAsyncStateMachine
{
    public int _state;
    public AsyncTaskMethodBuilder<IActionResult> _builder;
    public string _productCd;
    private IQueryable<Stock> _q;         // 跨 await 存活的局部变量，被「提升」为字段
    private int _total;
    private TaskAwaiter<int> _awaiter1;
    private TaskAwaiter<List<Stock>> _awaiter2;

    public void MoveNext()               // ← 状态机的心脏：每次「恢复」都调这个
    {
        try
        {
            switch (_state)
            {
                case -1:  // 第一次进入
                    _q = _db.Stocks.Where(x => x.ProductCd == _productCd);
                    _awaiter1 = _q.CountAsync().GetAwaiter();
                    if (!_awaiter1.IsCompleted)          // CountAsync 还没完成？
                    {
                        _state = 0;
                        // 注册回调：等 awaiter1 完成时，再调一次 MoveNext()（回到 case 0）
                        _builder.AwaitUnsafeOnCompleted(ref _awaiter1, ref this);
                        return;                           // ★暂停★ 控制权还给调用者，方法「退出」了
                    }
                    goto case 0;                          // 已完成 → 走快路径不暂停

                case 0:  // CountAsync 完成后从这里恢复
                    _total = _awaiter1.GetResult();       // 取出 count 结果（若 Faulted 在此重抛异常）
                    _awaiter2 = _q.Take(50).ToListAsync().GetAwaiter();
                    if (!_awaiter2.IsCompleted)
                    {
                        _state = 1;
                        _builder.AwaitUnsafeOnCompleted(ref _awaiter2, ref this);
                        return;                           // ★再次暂停★
                    }
                    goto case 1;

                case 1:  // ToListAsync 完成后从这里恢复
                    var items = _awaiter2.GetResult();
                    var result = Ok(new { _total, items });
                    _builder.SetResult(result);           // 把结果塞进 Task → Task 变 RanToCompletion
                    return;
            }
        }
        catch (Exception ex)
        {
            _builder.SetException(ex);   // 异常塞进 Task → Task 变 Faulted（不是当场抛！）
        }
    }
}
```

**从这段伪代码里你能读出全章一半的结论：**

1. **`async` 方法一进去就是同步执行的**，直到撞上第一个「未完成」的 await 才暂停。（`Start` → `MoveNext` case -1 是同步跑的。）
2. **暂停 = 方法真的 return 了**，把一个未完成的 Task 交给调用者。调用者继续干自己的事——这就是「非阻塞」的实现原理。
3. **恢复 = 那个 awaiter 完成时，回调再次调用 `MoveNext`**，靠 `_state` 字段跳回上次的 `case`。这就是「从暂停处继续」。
4. **跨 await 存活的局部变量（`_q`、`_total`）被提升为状态机的字段**——因为方法会退出再进入，普通栈局部变量活不过这次退出。
5. **异常被 `catch` 后 `SetException` 塞进 Task**，不是当场 throw——所以「异步方法的异常，要 await 时才观察到」（面试高频，见第 10 节）。
6. **如果 awaiter 已经完成（`IsCompleted == true`），根本不暂停、不注册回调**，直接 `goto` 下一个 case——这就是 ValueTask「同步快路径零分配」的舞台。

### 4.3 await 前后的执行线程——到底在哪个线程？

现在回答那道杀手面试题：**「`await` 之后，代码在哪个线程上跑？」**

精确答案分场景：

- **await 之前**：在**调用者的线程**上（同步执行到第一个未完成的 await）。
- **await 之后（恢复时）**：**取决于有没有 SynchronizationContext**（下一节详解）。
  - **ASP.NET Core（无 SynchronizationContext）**：恢复时从**线程池抓任一空闲线程**继续。**可能和 await 前是同一个线程，也可能不是——不保证**。
  - **UI 框架 / 老 ASP.NET（有 SynchronizationContext）**：默认会**回到原来的上下文线程**（UI 线程）继续。

面试标准答法：

> **`await` 之前的代码在调用线程上同步执行。`await` 之后（异步恢复）跑在哪个线程，取决于捕获的 SynchronizationContext：ASP.NET Core 没有同步上下文，恢复时用线程池的任意线程，不保证是原线程；WPF/WinForms 有同步上下文，默认恢复回 UI 线程。用 `ConfigureAwait(false)` 可以显式声明「不要回原上下文，就用线程池线程继续」。**

一个非常重要的推论，直接体现在 CP6 代码里：**因为 await 之后不保证同一个线程**，所以**跨 await 不能依赖线程本地状态（ThreadLocal / `[ThreadStatic]`）**。CP6 用 `ITenantContext`（作用域服务，DI scope 级别）而不是线程本地存储来传租户身份，正是因为异步会换线程——线程本地在 await 后可能就丢了。见 `TenantScopeRunner.cs` 的设计。

---

## 5. SynchronizationContext 与 ConfigureAwait(false)

### 5.1 SynchronizationContext 是什么

**类比。** SynchronizationContext（同步上下文）是一个「**把工作送回特定线程**」的信使。

- **WPF / WinForms（桌面客户端）**：有一个**唯一的 UI 线程**，所有控件（按钮、文本框）**只能被 UI 线程碰**。如果 await 之后在别的线程上执行 `label.Text = "done"`，直接抛异常（跨线程访问 UI）。所以 UI 框架安装了一个 SynchronizationContext，它的作用是：**await 完成后，把「继续执行的代码」重新排回 UI 线程去跑**。这样你 await 之后还能安全地改 UI。

- **ASP.NET Core**：**故意没有 SynchronizationContext**。因为 Web 请求没有「必须回到的唯一线程」这个概念——任何线程池线程都能接着处理这个请求。去掉同步上下文，省掉「排队回特定线程」的开销，吞吐量更高。

> 你面的是「制造业生产管理系统开发工程师（C# + SQL + Vue）」。虽然主体是 Web，但制造业现场常有**桌面客户端**（车间上位机、WinForms/WPF 的操作台、扫码终端管理端）。所以 SynchronizationContext 这个点，面试官很可能结合「桌面卡顿」来问——务必答得出「UI 线程 + 死锁」这条线。

### 5.2 经典死锁：`.Result` 在 UI 线程上为什么锁死

这是把「SynchronizationContext」和「`.Result` 死锁」两大考点串起来的经典题。

```csharp
// WPF 按钮点击事件（跑在 UI 线程上）
private void Button_Click(object sender, RoutedEventArgs e)
{
    // ❌ 在 UI 线程上同步等一个异步方法
    var data = LoadDataAsync().Result;   // 死锁！
    label.Text = data;
}

private async Task<string> LoadDataAsync()
{
    await Task.Delay(1000);   // 默认会「捕获 UI 同步上下文」，想恢复回 UI 线程
    return "done";
}
```

**死锁推演（背下来）：**

1. UI 线程调用 `LoadDataAsync().Result`——`.Result` 会**同步阻塞 UI 线程**，等这个 Task 完成。
2. `LoadDataAsync` 内部 `await Task.Delay` 时，**捕获了 UI 的 SynchronizationContext**，意味着「Delay 完成后，我要排回 UI 线程去继续执行 `return "done"`」。
3. 1 秒后 Delay 完成，状态机想恢复——它需要 **UI 线程**。
4. 但 UI 线程此刻正**卡在第 1 步的 `.Result` 上死等**，腾不出来。
5. **互相等：** `.Result` 等 Task 完成，Task 完成需要 UI 线程，UI 线程被 `.Result` 占死。→ **死锁。**

三种解法（理解每种为什么行）：

```csharp
// 解法 A（最优）：一路 async 到底，永远不要 .Result
private async void Button_Click(object sender, RoutedEventArgs e)  // 事件处理才允许 async void
{
    var data = await LoadDataAsync();   // await 不阻塞 UI 线程 → 无死锁
    label.Text = data;
}

// 解法 B：让被 await 的库方法「不捕获上下文」
private async Task<string> LoadDataAsync()
{
    await Task.Delay(1000).ConfigureAwait(false);  // ← 不回 UI 线程，用线程池线程继续
    return "done";
    // 这样恢复时不需要 UI 线程，.Result 不再死锁。但调用方仍不该用 .Result（见下）。
}
```

### 5.3 ConfigureAwait(false) 到底何时需要

`ConfigureAwait(false)` 的语义：**「这个 await 恢复时，我不需要回到原来的 SynchronizationContext，随便用个线程池线程继续就行。」**

**判断规则：**

| 你在写…… | 用 ConfigureAwait(false)？ | 为什么 |
|---|---|---|
| **通用类库 / NuGet 包 / 可被任何环境调用的底层代码** | ✅ **要用** | 你不知道调用者是不是 UI 线程。加上它，避免拖累 UI、避免制造死锁，还省一点点排队开销 |
| **ASP.NET Core 应用代码（Controller / Service）** | ⚪ **不必**（加了也无害） | ASP.NET Core 根本没有 SynchronizationContext，捕不捕获没区别。加了纯属噪音 |
| **WPF/WinForms 里 await 之后要改 UI** | ❌ **不能用** | 你**恰恰需要**回到 UI 线程，用了它会导致「在非 UI 线程改 UI」而崩溃 |

**这就是为什么 CP6 代码里几乎看不到 `ConfigureAwait(false)`**——CP6 是 ASP.NET Core 应用（`StockController`、`StockMovementService`、各 `BackgroundService`），跑在没有 SynchronizationContext 的环境里，加 `ConfigureAwait(false)` 对它毫无收益，只会污染每一行 await。CP6 的选择——**应用层不加、保持代码干净**——正是社区共识。

一句话面试答法：

> **`ConfigureAwait(false)` 的意思是「await 恢复时别回原同步上下文」。它主要给「不知道运行环境的通用库」用，避免死锁和不必要的上下文切换。在 ASP.NET Core 里因为没有同步上下文，加不加都一样，通常不加以保持整洁；在 UI 应用里，凡是 await 之后要碰控件的，绝对不能加，因为你需要回到 UI 线程。**

---

## 6. 组合与并发：WhenAll / WhenAny / 限流 / 超时

前面都是「一个异步操作」。真实系统里你常要**同时发起多个**、**等它们都好 / 等第一个好**、**别一次发太多把下游打爆**、**超时就放弃**。

### 6.1 Task.WhenAll：并发发起，等全部完成

**类比。** 你同时给三家供应商发询价邮件（不是发完一家等回复再发下一家），然后**等三封回信都到齐**再汇总比价。

```csharp
// ❌ 串行：三个「互不依赖」的查询排队跑，总耗时 = 三者之和
var warehouse = await _db.Warehouses.FirstAsync(w => w.Cd == cd, ct);   // 50ms
var stock     = await _db.Stocks.CountAsync(s => s.WarehouseCd == cd, ct); // 50ms
var pending   = await _db.Orders.CountAsync(o => o.WarehouseCd == cd, ct); // 50ms
// 总计 ~150ms，纯属浪费——它们之间没有依赖关系！

// ✅ 并发：三个查询同时在途，总耗时 ≈ 最慢的那个（~50ms）
var whTask      = _db.Warehouses.FirstAsync(w => w.Cd == cd, ct);
var stockTask   = _db.Stocks.CountAsync(s => s.WarehouseCd == cd, ct);
var pendingTask = _db.Orders.CountAsync(o => o.WarehouseCd == cd, ct);
await Task.WhenAll(whTask, stockTask, pendingTask);
var warehouse = whTask.Result;    // WhenAll 之后取 .Result 是安全的（已完成，不会阻塞）
var stock     = stockTask.Result;
var pending   = pendingTask.Result;
```

> **重大警告（第 8 节会再强调）：上面的三个并发查询能这么写，是因为它们各用一个 `Task`，但——它们不能共用同一个 `DbContext` 实例！** EF Core 的 `DbContext` **不是线程安全的，禁止并发操作**。上面的写法之所以危险，是因为 `_db` 是同一个 context。真要并发多个 DB 查询，得为每个查询开独立的 DbContext（`IDbContextFactory`）。**对同一个 DbContext，正确做法仍是串行 await。** WhenAll 并发最安全的用武之地是**并发调用多个下游 HTTP 服务 / 多个独立资源**，而不是同一个 DbContext 上的多查询。这个坑第 8.5 节专门讲。

`Task.WhenAll` 的异常语义（面试点）：

- 如果多个 Task 都失败，`await Task.WhenAll(...)` **只会抛出第一个异常**（其余异常被吞进返回的 Task 里）。
- 要拿到**全部**异常，得检查 `WhenAll` 返回的那个 Task 的 `.Exception`（一个 `AggregateException`）：

```csharp
var all = Task.WhenAll(t1, t2, t3);
try { await all; }
catch
{
    // all.Exception 是 AggregateException，含所有失败任务的异常
    foreach (var ex in all.Exception!.InnerExceptions)
        _logger.LogError(ex, "并发任务之一失败");
}
```

### 6.2 Task.WhenAny：等第一个完成（常用于超时/竞速）

```csharp
// 谁先回来用谁——比如同时问两个价格源，用先到的
var t1 = QuerySourceAAsync(ct);
var t2 = QuerySourceBAsync(ct);
var first = await Task.WhenAny(t1, t2);   // 返回「第一个完成的那个 Task」
var price = await first;                  // 再 await 它取结果（也会重抛它的异常）
```

`WhenAny` 最经典的用途是**超时**（下面 6.4）。

### 6.3 并发限流：SemaphoreSlim

**问题。** 你要给 500 个产品逐个调下游 API 补数据。`Task.WhenAll` 一次性把 500 个请求全发出去——下游服务会被瞬间打爆（或者你自己的 socket / 线程池被冲垮）。你需要**限流**：最多同时 10 个在途。

**类比。** 停车场只有 10 个车位（信号量初值 10）。车（任务）进场先领一张卡（`WaitAsync`——没车位就在门口排队等），出场还卡（`Release`——腾出车位，放下一辆进来）。永远最多 10 辆在里面。

`SemaphoreSlim` 就是这个「异步版停车场闸机」。**注意是 `SemaphoreSlim` 而不是 `lock`——因为它有 `WaitAsync`，能在 await 世界里非阻塞地等，而 `lock` 块里不能 await（见第 8.7 节）。**

```csharp
public async Task EnrichAllAsync(IReadOnlyList<string> productCodes, CancellationToken ct)
{
    using var gate = new SemaphoreSlim(10);   // 最多 10 个并发在途
    var tasks = productCodes.Select(async code =>
    {
        await gate.WaitAsync(ct);             // 领车位（满了就异步排队）
        try
        {
            await CallDownstreamAsync(code, ct);
        }
        finally
        {
            gate.Release();                   // 还车位——必须放 finally，异常了也要还！
        }
    });
    await Task.WhenAll(tasks);
}
```

**易错点：** `Release()` 必须放 `finally`。否则某个任务抛异常没还车位，信号量会慢慢漏光，最后所有任务卡在 `WaitAsync` 上永远等——这叫**信号量泄漏**。

CP6 的 `StockMovementService` 里对**同一个 DbContext** 的操作是严格串行的（`ApplyAsync` 里一个 await 接一个 await），正是因为不能并发用 DbContext；而需要限流并发的场景（批量调外部服务）才用 SemaphoreSlim。

### 6.4 超时模式：CancelAfter 与 WhenAny + Delay

**模式 A：`CancellationTokenSource.CancelAfter`（首选，最干净）**

```csharp
public async Task<Data> LoadWithTimeoutAsync(CancellationToken ct)
{
    // 把「调用者的取消」和「3 秒超时」合成一个 token
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(3));   // 3 秒后自动 Cancel
    try
    {
        return await _http.GetDataAsync(cts.Token);   // 超时 → 抛 OperationCanceledException
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        // 区分「是超时」还是「调用者主动取消」：这里 ct 没被取消，说明是超时
        throw new TimeoutException("下游 3 秒未响应");
    }
}
```

为什么首选它？因为它是**协作式取消**——超时信号真的传进了 `GetDataAsync`，让那个操作**真正停下来**（取消底层 HTTP 请求）。

**模式 B：`Task.WhenAny` + `Task.Delay`（当被调方法不吃 CancellationToken 时的退路）**

```csharp
var work  = SomeLegacyOpAsync();                 // 假设它不接受 CancellationToken
var delay = Task.Delay(TimeSpan.FromSeconds(3), ct);
var done  = await Task.WhenAny(work, delay);
if (done == delay)
    throw new TimeoutException("超时");           // ⚠ 注意：work 仍在后台继续跑，并没被停掉！
var result = await work;
```

模式 B 的**局限**：它只是「不再等」了，那个慢操作**并没有真的被取消**，还在后台耗资源。所以能用模式 A（协作式取消）就别用 B。

### 6.5 Parallel.ForEachAsync：.NET 6+ 的并发批处理利器

`Parallel.ForEachAsync` = 「WhenAll + 内置限流」的官方封装，还自带 CancellationToken 支持，是批量异步的现代首选：

```csharp
await Parallel.ForEachAsync(
    productCodes,
    new ParallelOptions
    {
        MaxDegreeOfParallelism = 10,   // 内置限流，不用自己搓 SemaphoreSlim
        CancellationToken = ct
    },
    async (code, token) =>
    {
        await CallDownstreamAsync(code, token);
    });
```

它比手搓 `SemaphoreSlim + WhenAll` 更简洁、更不易错（不会漏 Release）。**但同样注意：body 里若碰 DbContext，每个并发迭代必须用各自独立的 DbContext（`IDbContextFactory`），绝不能共用注入的那一个。**

---

## 7. CancellationToken 完整篇

### 7.1 为什么要有取消（Cancellation）

**类比。** 你让厨房做一份牛排（一个耗时异步操作）。客人突然走了（浏览器关了 / 请求断了 / 应用要关机）。如果没有「取消」机制，厨房还会把这份没人要的牛排做完——**白白浪费 CPU、DB 连接、下游调用**。`CancellationToken` 就是那句「**这单不用做了，停**」。

制造业系统里取消无处不在：

- 用户在前端点了「查询」又马上关页面 → 那个几秒的大报表查询应该被取消。
- 应用要优雅停机（部署、重启）→ 所有后台 worker 的循环该停下来。
- 一个操作超时了 → 用取消让它真的停（见 6.4）。

### 7.2 协作式取消模型：token 只是「信号」，停不停要靠代码配合

关键认知：**CancellationToken 不会「强行杀死」你的代码。** 它是**协作式（cooperative）**的——它只是一个「有人请求取消了」的**信号**，真正停下来靠：

1. 你把 token **透传**给下游的 `...Async(ct)` 方法，由它们内部检查（EF Core、HttpClient 都会检查）；
2. 或者在你自己的循环里**主动轮询**这个信号。

两种「响应取消」的方式：

```csharp
// 方式一：透传给会检查 token 的异步 API（最常见）
var list = await _db.Stocks.ToListAsync(ct);   // EF Core 内部会检查 ct，取消则抛 OperationCanceledException

// 方式二：自己的长循环里主动检查
foreach (var item in hugeList)
{
    ct.ThrowIfCancellationRequested();   // 若已请求取消，当场抛 OperationCanceledException
    HeavyProcess(item);
}
// 或轮询布尔（不想抛异常、想自己收尾时）：
while (!ct.IsCancellationRequested) { ... }
```

`ThrowIfCancellationRequested()` vs `IsCancellationRequested`：

- `ThrowIfCancellationRequested()`：检查到取消就**抛异常**，让调用栈自然回退。适合「取消 = 中止这次操作」。
- `IsCancellationRequested`（布尔）：只是问「取消了吗」，**不抛**，你自己决定怎么收尾（比如后台循环里 `break` 出去做清理）。CP6 的 worker 循环 `while (!stoppingToken.IsCancellationRequested)` 用的就是它。

### 7.3 ASP.NET Core 自动注入 RequestAborted

这是 Web 开发里最实用的一点：**ASP.NET Core 会自动把「本次 HTTP 请求的取消令牌」注入到你 action 方法的 `CancellationToken` 参数里**（它就是 `HttpContext.RequestAborted`）。当客户端断开连接（关页面、超时、取消），这个 token 就被触发。

你**什么都不用配**，只要在 action 签名里**加一个 `CancellationToken` 参数**，框架就自动填：

看 CP6 的 `StockController.Apply`（`C:\CP6\CP6.WebApi\Controllers\Wms\StockController.cs`）：

```csharp
[HttpPost("apply")]
[RequirePermission("wms-stock", "adjust")]
public async Task<IActionResult> Apply([FromBody] StockMovementRequest req, CancellationToken ct)
{                                                                        // ↑ 框架自动注入 RequestAborted
    req.OperatorCd ??= CurrentUser;
    try
    {
        var txnNo = await _mover.ApplyAsync(req, ct);   // ← 立刻把 ct 往下透传
        return Ok(new { code = 0, message = "WM-MSG-071", data = new { txnNo } });
    }
    catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    catch (ArgumentException ex)          { return BadRequest(new { code = 400, message = ex.Message }); }
}
```

客户端一断开，`ct` 触发 → `ApplyAsync(req, ct)` 里的 EF Core 查询抛 `OperationCanceledException` → 这次库存变动被中止、不再白占 DB。

### 7.4 一路透传（transitive propagation）——CP6 的透传约定

**核心纪律：CancellationToken 要像接力棒一样，从最外层（Controller）一路传到最底层（EF Core / HttpClient），中间任何一个异步方法都不许「把它吞掉」。**

看 CP6 里这根完整的透传链，从 Controller 一直到 DB：

```
StockController.Apply(..., CancellationToken ct)          ← 框架注入
        │  传 ct
        ▼
IStockMovementService.ApplyAsync(req, CancellationToken ct = default)   ← StockMovementService.cs
        │  传 ct 给每一个下游 await
        ├──► _db.Database.BeginTransactionAsync(ct)
        ├──► _db.Stocks.FirstOrDefaultAsync(..., ct)
        ├──► _db.Warehouses...FirstOrDefaultAsync(ct)
        ├──► _db.SaveChangesAsync(ct)
        ├──► tx.CommitAsync(ct)
        └──► IsLocationFrozenAsync(..., ct) ──► ...AnyAsync(ct)
```

`StockMovementService.ApplyAsync` 的签名与透传（真实代码，`C:\CP6\CP6.Core\Services\Wms\StockMovementService.cs`）：

```csharp
public async Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct = default)
{
    // ...
    if (... && await IsLocationFrozenAsync(req.WarehouseCd, req.LocationCd, ct)) { ... }  // 传 ct

    IDbContextTransaction? tx = null;
    if (_db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        tx = await _db.Database.BeginTransactionAsync(ct);                                 // 传 ct

    var stock = await _db.Stocks.FirstOrDefaultAsync(s => ..., ct);                        // 传 ct
    // ...
    await _db.SaveChangesAsync(ct);                                                        // 传 ct
    if (tx != null) await tx.CommitAsync(ct);                                              // 传 ct
    // ...
}
```

CP6 的**透传约定**（从代码里总结出来的、可以直接讲给面试官的团队规范）：

1. **每个异步服务方法的最后一个参数都是 `CancellationToken ct = default`。** 带默认值，方便测试/内部调用，但生产路径一定从 Controller 传真 token。
2. **拿到 ct 就往每个下游 `...Async(ct)` 里传，一个都不漏。**
3. **底层私有 helper 也接 ct 并透传**（如上面的 `IsLocationFrozenAsync(..., ct)`）。
4. **后台服务用 `stoppingToken` 作为顶层 token**，逐层往下传（见第 10 节）——把「应用停机」当作「取消所有后台工作」的信号。

### 7.5 取消异常的处理惯例

被取消时抛的是 `OperationCanceledException`（`TaskCanceledException` 是它的子类）。**惯例：取消不是「错误」，别当普通异常记 Error 日志。** 看 CP6 background service 里的标准处理：

```csharp
// IntegrationEventRetryWorker.cs
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
{
    // 应用正常停机导致的取消 —— 静默吞掉，不记 Error（这不是 bug，是预期行为）
}
```

那个 `when (stoppingToken.IsCancellationRequested)` 的**异常筛选器（exception filter）**很讲究：它保证「只吞**因停机而起**的取消」，而不是把所有 `OperationCanceledException` 一律吞掉（避免误吞了别处真正该处理的取消）。

---

## 8. 必坑全集

每个坑四段式：**错误代码 → 现象 → 原理 → 正确写法。** 这一节是事故档案馆，面试官最爱从这里挖「你踩过什么坑」。

### 8.1 `.Result` / `.Wait()`：死锁与线程池饥饿

**错误代码：**
```csharp
public IActionResult Get()
{
    var list = _db.Stocks.ToListAsync().Result;   // 同步阻塞等异步
    return Ok(list);
}
```

**现象：**
- 在有 SynchronizationContext 的环境（老 ASP.NET、WPF）→ **直接死锁，请求永远挂起**。
- 在 ASP.NET Core（无同步上下文）→ 不死锁，但**每个这样的请求都白占一个线程池线程去干等**，高并发下**线程池饥饿**，整个服务响应变慢甚至雪崩。

**原理：**
- 死锁机制见 5.2：`.Result` 阻塞线程 → 异步恢复又需要那个线程 → 互等。
- 线程池饥饿：`.Result` 把「本该释放去干别的活的线程」按住干等 I/O，等于退回到第 1 节的同步阻塞模型，把异步的全部好处抵消。

**正确写法：**
```csharp
public async Task<IActionResult> Get(CancellationToken ct)
{
    var list = await _db.Stocks.ToListAsync(ct);   // 一路 async 到底，永不 .Result
    return Ok(list);
}
```
> 铁律：**Async all the way（异步要一路到底）。** 一旦某处用了 `.Result`/`.Wait()`/`.GetAwaiter().GetResult()`，就在异步链上打了个「同步阻塞的结」，前功尽弃。CP6 全仓库的 Controller/Service 里**没有一处** `.Result`/`.Wait()`（并发块里 WhenAll 之后取 `.Result` 是唯一例外，那是安全的——任务已完成）。

### 8.2 `async void`：无法 await、异常直接崩进程

**错误代码：**
```csharp
public async void SaveStock(StockMovementRequest req)   // ❌ async void
{
    await _mover.ApplyAsync(req);   // 若这里抛异常……
}
```

**现象：**
- 调用者**无法 await 它、无法知道它何时完成、无法捕获它的异常**。
- 里面一旦抛异常，**没有 Task 去承接这个异常** → 异常直接冒泡到 SynchronizationContext / 线程池的顶层 → 在 ASP.NET Core 里**直接把整个进程干崩**。

**原理：**
`async` 方法的异常是「装进返回的 Task」里的（见 4.2 的 `SetException`）。`async void` **没有返回 Task**，异常无处可装，只能抛到最顶层——相当于在后台线程上 `throw`，无人能救。

**正确写法：**
```csharp
public async Task SaveStockAsync(StockMovementRequest req)   // ✅ 返回 Task
{
    await _mover.ApplyAsync(req);
}
```
> **`async void` 唯一合法的用途：UI 事件处理器**（如 WPF 的 `Button_Click`），因为事件签名要求返回 void。除此之外一律用 `async Task`。这是面试送分题——记住「async void 只给事件处理用」。

### 8.3 循环内串行 await 独立操作：本可并发却排队

**错误代码：**
```csharp
var results = new List<Data>();
foreach (var id in ids)                       // 100 个独立 id
    results.Add(await CallServiceAsync(id));  // 一个回来才发下一个 → 100×50ms = 5 秒
```

**现象：** 100 个互不依赖的调用被排成一队，总耗时 = 累加（5 秒），而不是 ≈ 单个（50ms 级）。

**原理：** `await` 在循环里意味着「这次完成才进入下次迭代」。如果这些操作**互相独立**（后一个不依赖前一个的结果），串行就是纯粹浪费。

**正确写法（带限流的并发）：**
```csharp
// 简单并发（数量可控且下游扛得住时）
var tasks = ids.Select(id => CallServiceAsync(id));
var results = await Task.WhenAll(tasks);

// 数量大 → 用 Parallel.ForEachAsync 限流，别把下游打爆
var bag = new ConcurrentBag<Data>();
await Parallel.ForEachAsync(ids,
    new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
    async (id, token) => bag.Add(await CallServiceAsync(id, token)));
```
> **注意分辨「独立」还是「有依赖」。** 如果每次循环依赖上一次的结果（比如翻页游标、顺序状态机），那**串行 await 是对的**，不能强行并发。CP6 的 `IntegrationEventRetryWorker` 里 `foreach (var evt in due)` 就是**故意串行**的——因为它们共用同一个 `DbContext`（不能并发用，见 8.5），而且逐个重试+改状态有顺序语义。

### 8.4 在 async 方法里吞异常（尤其 catch 后不 rethrow）

**错误代码：**
```csharp
public async Task ApplyAsync(Req req)
{
    try { await _db.SaveChangesAsync(); }
    catch { /* 吞了，啥也不干 */ }   // ❌ 异常被无声吃掉，调用方以为成功了
}
```

**现象：** 保存其实失败了，但方法「正常」返回，上层拿到「假成功」，数据不一致却毫无察觉——**最难查的那种 bug**。

**原理：** 空 `catch` 把 Task 从 Faulted「洗白」成 RanToCompletion，异常信息彻底丢失。

**正确写法——区分「必须成功」和「best-effort（尽力而为，失败可接受）」：**

CP6 的 `StockMovementService.ApplyAsync` 给了教科书级示范。**主业务（库存变动）绝不吞异常**——它靠最外层的 `catch { rollback; throw; }` 保证失败一定回滚并上抛：
```csharp
catch
{
    if (tx != null) await tx.RollbackAsync(ct);
    throw;   // ← 主业务异常必须 rethrow，绝不吞
}
```
而**真正的 best-effort 副作用**（发通知、点火下游桥），CP6 是**有意识、有注释地**吞——因为「通知失败不该让已成功的库存变动回滚」：
```csharp
// WMS→Fin 库存过账桥点火（best-effort、失敗しても在庫移動は成功扱い）
try { await _finBridge.OnStockMovedAsync(txn, req.RelatedType ?? "", req.OperatorCd); }
catch { /* fin bridge failure must not break stock movement */ }

// SignalR 实时通知（best-effort）
try { await _notifier.NotifyStockChangedAsync(...); }
catch { /* notifier failure must not break stock movement */ }
```
> 判据：**吞异常必须是「明确的业务决策 + 注释说明为什么可以吞」**，而不是「懒得处理就 catch 空」。而且理想情况下 best-effort 的吞也该记一条日志（CP6 这里为极致轻量省了日志，但注释交代了意图）。**面试答法：核心业务异常必须上抛或显式处理；只有明确的、非关键的副作用才允许 best-effort 吞，且必须注释+最好记日志。**

### 8.5 DbContext 并发使用翻车

**错误代码：**
```csharp
// ❌ 同一个 _db 上并发发两个查询
var t1 = _db.Stocks.CountAsync();
var t2 = _db.Orders.CountAsync();
await Task.WhenAll(t1, t2);   // 💥 抛 InvalidOperationException
```

**现象：** 运行时抛
`System.InvalidOperationException: A second operation was started on this context instance before a previous operation completed.`

**原理：** **EF Core 的 `DbContext` 不是线程安全的，同一时刻只允许一个操作在飞。** 它内部维护变更追踪、数据库连接状态，并发操作会把这些内部状态搅乱。而 ASP.NET Core 里 `DbContext` 默认是 **Scoped（每个请求一个实例）**——所以「一个请求内并发用同一个 context」是最常见的翻车姿势。

**正确写法：**
```csharp
// 方案 A：同一个 context 就老老实实串行 await（绝大多数情况够用）
var stockCount = await _db.Stocks.CountAsync(ct);
var orderCount = await _db.Orders.CountAsync(ct);

// 方案 B：真的要并发多个 DB 查询 → 用 IDbContextFactory 给每个查询开独立 context
public async Task<(int, int)> CountBothAsync(IDbContextFactory<CP6Context> factory, CancellationToken ct)
{
    async Task<int> CountStocks() { await using var db = await factory.CreateDbContextAsync(ct); return await db.Stocks.CountAsync(ct); }
    async Task<int> CountOrders() { await using var db = await factory.CreateDbContextAsync(ct); return await db.Orders.CountAsync(ct); }
    var s = CountStocks(); var o = CountOrders();
    await Task.WhenAll(s, o);
    return (s.Result, o.Result);   // 各用各的 context，互不干扰
}
```
> 这就是为什么第 6.1 节反复警告「WhenAll 并发多查询别共用 DbContext」。CP6 的所有服务对注入的那个 `_db`（Scoped）一律**串行 await**，从不并发；后台服务每个租户循环都**开独立 scope 拿新的 context**（见第 10 节），正是为了避免跨迭代/跨租户并发用同一个 context。

### 8.6 fire-and-forget 的正确姿势

**错误代码：**
```csharp
public IActionResult Post(Req req)
{
    _ = ProcessHeavyAsync(req);   // ❌「发射后不管」——丢弃 Task，不 await
    return Ok();
}
```

**现象：**
- 请求返回后，那个 Task 还在后台跑，但**请求作用域（scope）已经销毁** → 它用的 Scoped 服务（DbContext 等）已被 dispose → `ObjectDisposedException`。
- 里面抛的异常**无人观察**（没人 await 它），静默丢失。
- 应用停机时不等它 → 干到一半被硬切，数据可能损坏。

**原理：** ASP.NET Core 的 DI scope 和 DbContext 生命周期**绑定在请求上**。请求一结束就拆台，后台还在用台上的东西 → 崩。

**正确写法——把后台工作交给「有独立生命周期」的宿主：**

不要在请求里 fire-and-forget。要么用 `IHostedService`/`BackgroundService`（见第 10 节），要么用一个持久化队列 + worker 消费。CP6 就是这么做的——需要异步后台处理的活（重试集成事件、清理日志、对账、超时扫描）**全部落在 BackgroundService 里**，每个都：

1. 有自己独立于请求的生命周期；
2. 用 `IServiceScopeFactory` **自己开 scope、自己管 DbContext 生命周期**；
3. 用 `stoppingToken` 参与优雅停机。

如果一定要「请求里触发、后台完成」，正确管道是：**请求里只往一个持久队列（DB 表 / Kafka / Channel）写一条消息（这步是 await 的、在请求 scope 内安全完成），然后由 BackgroundService 消费。** CP6 的 `KafkaOperLogConsumer`（请求侧发 Kafka，消费侧后台落库）和 `IntegrationEventRetryWorker`（请求侧写 IntegrationEvent 表，worker 侧重试派发）就是这个模式的两个实例。

### 8.7 `lock` 里不能 await（以及 SemaphoreSlim 替代）

**错误代码：**
```csharp
private readonly object _gate = new();
public async Task UpdateAsync()
{
    lock (_gate)                       // ❌ 编译不过！
    {
        await _db.SaveChangesAsync();  // lock 块里不允许 await
    }
}
```

**现象：** **编译错误**——`CS1996: Cannot await in the body of a lock statement`。

**原理：** `lock` 底层是 `Monitor.Enter/Exit`，它有个铁律：**同一个线程进、同一个线程出**。但 `await` 之后可能换线程（见第 4.3 节）——那样就会「A 线程 Enter、B 线程 Exit」，破坏 Monitor 的线程归属，锁彻底坏掉。所以编译器**直接禁止**在 lock 里 await。（更别提锁被持有期间去 await 一个慢 I/O，会长时间堵死所有等锁的人。）

**正确写法——用 `SemaphoreSlim(1,1)` 作「异步锁」：**
```csharp
private readonly SemaphoreSlim _gate = new(1, 1);   // 初值1、上限1 = 互斥锁
public async Task UpdateAsync(CancellationToken ct)
{
    await _gate.WaitAsync(ct);         // 异步获取锁（不阻塞线程）
    try
    {
        await _db.SaveChangesAsync(ct);   // ✅ 现在可以 await 了
    }
    finally
    {
        _gate.Release();               // 必须 finally 释放
    }
}
```
> `SemaphoreSlim` 不依赖「同线程进出」，所以能安全跨 await 持有。这是「异步互斥」的标准解法。第 6.3 节讲的限流是它的多槽位版（初值 N）；这里是单槽位（初值 1）当互斥锁用。

---

## 9. 异步流：IAsyncEnumerable 与 await foreach

### 9.1 问题：一次性 ToList 一百万行会 OOM

制造业系统常要**导出/流式处理海量数据**（几十万条库存流水、整月的生产记录）。如果 `await _db.StockTransactions.ToListAsync()` 一次性把一百万行全拉进内存，直接 **OutOfMemoryException**。

你需要**一边从数据库拉、一边处理、拉一批处理一批**——这就是**异步流（async stream）**。

### 9.2 IAsyncEnumerable&lt;T&gt; + await foreach

**类比。** `IEnumerable<T>` 是「传送带」——你站在带子旁，来一个处理一个，不需要把整仓库的货堆你面前。`IAsyncEnumerable<T>` 是**异步传送带**：下一件货可能要「异步等一下」（等 DB 返回下一批），但你依然是「来一个处理一个」，内存里始终只有当下这一件（或一小批）。

```csharp
// 生产者：yield return + IAsyncEnumerable，一批批地异步产出，不占满内存
public async IAsyncEnumerable<StockTransaction> StreamTransactionsAsync(
    string productCd,
    [EnumeratorCancellation] CancellationToken ct = default)   // ← 注意这个特性
{
    // EF Core 的 AsAsyncEnumerable：流式拉取，不一次性 ToList
    await foreach (var txn in _db.StockTransactions
                       .AsNoTracking()
                       .Where(t => t.ProductCd == productCd)
                       .OrderBy(t => t.TxnDateTime)
                       .AsAsyncEnumerable()
                       .WithCancellation(ct))
    {
        yield return txn;   // 产出一条，消费方处理完再要下一条
    }
}

// 消费者：await foreach 逐条消费
public async Task ExportAsync(string productCd, CancellationToken ct)
{
    await foreach (var txn in StreamTransactionsAsync(productCd, ct).WithCancellation(ct))
    {
        await WriteToCsvAsync(txn, ct);   // 处理一条写一条，内存占用恒定
    }
}
```

**关键点（面试点）：**

1. **`[EnumeratorCancellation]` 特性**：`async IAsyncEnumerable` 方法的 CancellationToken 参数要加这个特性，才能让消费方的 `.WithCancellation(ct)` 正确把 token 传进迭代器内部。漏了它 token 就不生效——高频踩坑。
2. **惰性拉取（lazy）**：消费方 `await foreach` 每转一圈，才驱动生产方产出下一个。**背压（back-pressure）**天然存在——消费得慢，生产就等，内存不爆。
3. **对比 `Task<List<T>>`**：那是「等全部好了一次性给你一大坨」；`IAsyncEnumerable<T>` 是「好一个给你一个」。海量数据 / 流式管道用后者。

> CP6 主体用分页（`Skip/Take`，见 `StockController.Search`）来控内存——对「用户翻页看」的场景，分页比流式更合适（用户不会一次看百万行）。`IAsyncEnumerable` 的主场是**后台批处理/导出**：你要**处理完全部**、又不想一次性载入内存。两种技术解决的是不同形态的「大数据量」问题，面试时能说清「分页 vs 流式各自适用场景」是加分项。

---

## 10. 后台服务里的异步：BackgroundService + Scope

制造业系统离不开后台任务：定时对账、清理日志、扫描超时审批、消费消息队列、重试失败事件、计算 OEE。CP6 的 `C:\CP6\CP6.WebApi\BackgroundServices\` 目录下有 14 个这样的服务。这一节把它们的**通用模式**讲透——这几乎是面试「你怎么写定时任务/后台服务」的标准答案模板。

### 10.1 BackgroundService.ExecuteAsync 的标准骨架

`BackgroundService` 是 .NET 内置的长时后台任务基类。你只需重写 `ExecuteAsync(CancellationToken stoppingToken)`。**这个 `stoppingToken` 会在应用开始停机时被触发**——它就是你「优雅停机」的信号。

CP6 的 `FinReconciliationWorker`（`C:\CP6\CP6.WebApi\BackgroundServices\FinReconciliationWorker.cs`）是最干净的定时任务范本：

```csharp
public class FinReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;   // ← 关键依赖（见 10.2）
    private readonly ILogger<FinReconciliationWorker> _logger;

    public FinReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<FinReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("财务每日对账 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);          // ① 启动延迟，避开初始化高峰
            while (!stoppingToken.IsCancellationRequested)          // ② 主循环，看停机信号
            {
                await ProcessOnceAsync(stoppingToken);             // ③ 干一轮活（透传 token）
                await Task.Delay(Interval, stoppingToken);         // ④ 睡到下个周期（token 让睡眠可被打断）
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {                                                          // ⑤ 停机导致的取消 → 静默退出
        }
        finally
        {
            _logger.LogInformation("财务每日对账 worker 停止");    // ⑥ 收尾日志
        }
    }
}
```

逐点解析：

- **① `Task.Delay(startupDelay, stoppingToken)`**：应用刚起来时正忙着迁移/预热，后台任务缓一分钟再开工。传 `stoppingToken` 使这段睡眠**可被停机打断**（否则停机要干等它睡完）。
- **② `while (!stoppingToken.IsCancellationRequested)`**：轮询停机信号的标准循环（第 7.2 节的布尔轮询用法）。
- **④ `await Task.Delay(Interval, stoppingToken)`** 而不是 `Thread.Sleep(Interval)`：`Thread.Sleep` 会**死占线程**睡一整天（回到同步阻塞老路），且**无法被停机打断**；`await Task.Delay` 睡眠期间**不占线程**，且停机时立即抛 `OperationCanceledException` 醒过来。这是后台服务里最典型的「同步阻塞 vs 异步等待」对照。
- **⑤ 异常筛选器**：只吞「停机引发的取消」，把它当正常退出（第 7.5 节）。
- **优雅停机全景**：应用关闭 → host 触发 `stoppingToken` → 循环条件转 false / 睡眠被打断 / 正在跑的 `ProcessOnceAsync` 里的 `...Async(ct)` 抛取消 → 走到 `finally` 记日志、干净退出。host 默认还会给一段宽限时间等 `ExecuteAsync` 返回。

### 10.2 Scoped 服务在后台服务里怎么用——IServiceScopeFactory

**这是后台服务最大的坑，也是最高频的面试点。**

**问题：** `BackgroundService` 是**单例（Singleton）**——应用启动时创建一个，活到关机。但 `DbContext`、大多数业务服务是 **Scoped（作用域，每个 HTTP 请求一个）**。**你不能把 Scoped 服务直接注入到 Singleton 的构造函数里**——那会导致：

- 要么启动直接抛「Cannot consume scoped service from singleton」；
- 要么这个 DbContext 被一个单例长期持有，变成「事实上的单例 DbContext」，活一整年，变更追踪缓存无限膨胀、连接不释放、跨租户数据串味——灾难。

**解法：** 注入 **`IServiceScopeFactory`**（它是单例、可安全注入），然后在**每一轮工作时手动开一个 scope**，从 scope 里解析 Scoped 服务，用完 dispose scope。

CP6 的 `OperLogCleanupService`（`C:\CP6\CP6.WebApi\BackgroundServices\OperLogCleanupService.cs`）示范：

```csharp
private async Task CleanupOnceAsync(int retentionDays, CancellationToken stoppingToken)
{
    try
    {
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        using var scope = _scopeFactory.CreateScope();                     // ① 每轮开一个新 scope
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();   // ② 从 scope 里解析 DbContext

        var deleted = await db.Sys_OperLogs
            .IgnoreQueryFilters()
            .Where(l => l.CreateDate < cutoff)
            .ExecuteDeleteAsync(stoppingToken);                            // ③ EF Core 8 批量删（不载入内存）

        if (deleted > 0)
            _logger.LogInformation("OperLog 清理：删除 {Count} 条……", deleted, ...);
    }
    // ④ scope 在方法结束时 using 自动 dispose → DbContext 随之释放，绝不长命
    catch (OperationCanceledException) { /* 停止中，忽略 */ }
    catch (Exception ex) { _logger.LogError(ex, "OperLog 清理失败"); }
}
```

要点：

1. **`using var scope = _scopeFactory.CreateScope()`**：每一轮（每次清理/每次扫描）开**新** scope，用完即弃。**scope 的生命周期 = 一轮工作**，绝不跨轮复用。
2. **DbContext 从 scope 里解析**，随 scope 一起短命——每轮一个全新的、干净的 context，没有缓存膨胀、没有跨轮串味。
3. **注意 `ExecuteDeleteAsync`（EF Core 7/8）**：批量删除直接翻译成 `DELETE WHERE`，**不把百万行加载进内存**再逐个删——这是异步 + 大数据量清理的正确姿势（呼应第 9 节的「别 ToList 一大坨」）。
4. **`IgnoreQueryFilters()`**：CP6 是多租户系统，后台没有 HttpContext → 默认租户过滤器只会删到默认租户；运维级跨租户清理要显式忽略过滤器。（这是多租户 + 后台服务的交叉考点，能提一嘴是加分。）

### 10.3 多租户后台的进阶模式：TenantScopeRunner

CP6 把「后台每轮开 scope」这件事进一步抽象成了 `TenantScopeRunner`（`C:\CP6\CP6.WebApi\BackgroundServices\TenantScopeRunner.cs`），解决「后台任务要**为每个租户各跑一遍**、且各租户数据不能串」的问题。它是「IServiceScopeFactory 模式」的多租户升级版：

```csharp
public static async Task ForEachTenantAsync(
    IServiceScopeFactory scopeFactory,
    Func<IServiceProvider, Guid, CancellationToken, Task> body,   // 每租户要干的活
    ILogger? logger = null,
    CancellationToken ct = default)
{
    IReadOnlyList<Guid> tenants;
    using (var enumScope = scopeFactory.CreateScope())            // ① 先开一个 scope 拿租户清单
    {
        tenants = await enumScope.ServiceProvider
            .GetRequiredService<ITenantEnumerator>().ListActiveAsync(ct);
    }

    foreach (var tenantId in tenants)
    {
        if (ct.IsCancellationRequested) break;                   // ② 每租户前检查停机信号

        using var scope = scopeFactory.CreateScope();            // ③ 为每个租户开独立 scope
        scope.ServiceProvider.GetRequiredService<ITenantContext>()
             .CurrentTenantId = tenantId;                        // ④ 设当前租户（同 scope 内 context 会据此过滤）
        try
        {
            await body(scope.ServiceProvider, tenantId, ct);     // ⑤ 在该租户作用域内干活
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;                                               // ⑥ 停机取消 → 上抛，中止整个循环
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "后台任务在租户 {Tenant} 执行异常（跳过该租户，继续其余）", tenantId);
        }                                                        // ⑦ 单租户业务异常 → 记日志跳过，不拖垮其余租户
    }
}
```

用法（`FinReconciliationWorker.ProcessOnceAsync`）：
```csharp
public async Task ProcessOnceAsync(CancellationToken ct = default)
{
    await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
    {
        var svc = sp.GetRequiredService<IFinReconciliationService>();   // 从「本租户 scope」解析服务
        var r = await svc.ReconcileAsync();
        if (r.AllClear) _logger.LogInformation("[FinRecon] 租户 {Tenant} 全部一致", tenantId);
        else            _logger.LogError("[FinRecon] 租户 {Tenant} 发现 {Count} 项不一致……", tenantId, r.Issues.Count, ...);
    }, _logger, ct);
}
```

这段代码浓缩了本章多个考点：

- **每个租户独立 scope**（③）→ 每租户独立 DbContext → 不违反「DbContext 不能并发/串味」（第 8.5 节）。注意这里是**逐租户串行**，正因为要各开各的 scope、各租户 context 隔离。
- **异常隔离**（⑦）：一个租户炸了不影响别的租户——用 try/catch 包住每轮 body，记日志后 `continue`。
- **取消要区别对待**（⑥）：`OperationCanceledException` + `when 停机` → **上抛**（真的要停整个循环）；普通业务异常 → **吞掉记日志继续**。同一个 catch 结构里，两种异常两种命运，这个区分很见功力。
- **不用 ThreadLocal 传租户，用 Scoped 的 `ITenantContext`**（④）：正因为 await 会换线程（第 4.3 节），线程本地存储在异步里靠不住，所以租户身份挂在 DI scope 上而非线程上。

---

## 11. 面试深水区问答

这一节把最刁钻的几个「原理级」问题，用能直接背诵的话术给出。

### 11.1 「async/await 的状态机原理，讲一下」

> 编译器会把每个 async 方法改写成一个实现了 `IAsyncStateMachine` 的结构体。方法里每个 await 是一个状态（`_state` 字段记录），跨 await 存活的局部变量被提升为状态机的字段。核心是 `MoveNext()` 方法：第一次同步执行到第一个「未完成」的 await，就注册一个「完成时再调 MoveNext」的回调，然后**方法直接返回**一个未完成的 Task 给调用者——这就实现了非阻塞。被 await 的操作完成后，回调再次调用 `MoveNext()`，靠 `_state` 跳回上次暂停的位置继续。方法正常结束时 `SetResult` 把结果塞进 Task，异常时 `SetException` 把异常塞进 Task。所以本质上，async/await 是「把线性代码切成一段段、用状态机在回调间调度」的语法糖，运行时并不需要为等待专门占用线程。

### 11.2 「await 之后代码在哪个线程跑？」

> await 之前在调用线程上同步跑。await 之后（异步恢复时）跑在哪个线程，取决于捕获的 SynchronizationContext：ASP.NET Core 没有同步上下文，恢复用线程池任一空闲线程，**不保证**是原线程；WPF/WinForms 有同步上下文，默认恢复**回 UI 线程**。用 `ConfigureAwait(false)` 可显式声明「不回原上下文、用线程池线程继续」。推论：跨 await 不能依赖 `[ThreadStatic]`/ThreadLocal，因为线程可能变了——所以像租户身份这种要挂在 DI scope 上而不是线程上。

### 11.3 「Task 和 Thread 的区别？」

> Thread 是操作系统级的执行单元，真实占内存（默认 1MB 栈）和调度资源，是「一个真的在跑的执行流」。Task 是一个更高层的抽象——它代表「一个将来会完成的操作」，**不一定对应一个线程**：一个 I/O-bound 的 Task（`ToListAsync`）在等待期间**根本没有线程**，等待由操作系统 IOCP 负责；只有 CPU-bound 的 `Task.Run` 才真的借用一个线程池线程去算。所以 Task 是「工作的句柄/承诺」，Thread 是「干活的工人」。你通常操作 Task（await、组合、取消），很少直接碰 Thread。

### 11.4 「ValueTask 什么时候用？」

> 默认永远用 Task。ValueTask 是一个结构体，当结果能**同步就绪**时避免堆分配。仅在「**极高频调用** + **大多数时候同步完成**（如高命中率缓存）」且 **profiler 证明** Task 分配是热点时才换。代价是使用约束严：只能 await 一次、不能存起来重复消费、不能对未完成的它取 `.Result`。用错比不用更糟，所以不是热点别碰。

### 11.5 「异步方法的异常什么时候抛出？」

> 异常不在方法「被调用」时抛，而在你 **await 那个 Task 时**才被重新抛出（对 `Task<T>` 而言，访问 `.Result` 也会抛，包装成 AggregateException）。原因看状态机：异常被 `catch` 后 `SetException` **装进了返回的 Task**，让 Task 变 Faulted 状态；直到你 await 它，`GetResult()` 才把这个异常「解包重抛」。**推论一**：一个 async 方法即使第一行就会抛，只要你不 await 返回的 Task，异常就一直「潜伏」着——这正是 `async void`（无 Task 可装）和 fire-and-forget（无人 await）会「静默丢异常/崩进程」的根源。**推论二**：`Task.WhenAll` 里多个失败时，`await` 只重抛第一个，要拿全部得看那个 Task 的 `.Exception`（AggregateException）。

### 11.6 「为什么 ASP.NET Core 去掉了 SynchronizationContext？」

> 因为 Web 请求没有「必须回到的唯一线程」——任何线程池线程都能接着处理同一个请求。保留同步上下文只会带来「每次 await 恢复都排队回特定线程」的开销，还制造 `.Result` 死锁隐患。去掉它，await 恢复直接用线程池线程，吞吐量更高、也不会因为同步上下文而死锁。代价是「await 后不保证同一线程」，但 ASP.NET Core 应用本来就不该依赖线程亲和性，所以这是纯赚。

---

## 12. 本章面试题 15 问

**Q1. 异步到底优化了什么？延迟还是吞吐量？**
A：优化**吞吐量与可伸缩性**，**不优化单次延迟**。一次 50ms 的 DB 查询，同步异步都约 50ms；但异步让服务器用极少线程照看海量在途 I/O 请求，高并发下吞吐量（QPS）大幅提升、内存占用大幅降低。因为 I/O 等待期间线程被释放去干别的，而非阻塞干等。

**Q2. 「异步不等于多线程」怎么理解？**
A：I/O-bound 异步（DB/HTTP/文件）在等待期间**不占用任何线程**——等待由操作系统的 I/O 完成端口（IOCP）负责，线程只在「发起 I/O」和「处理结果」两个瞬间被占用。只有 `Task.Run` 这类 CPU-bound 场景才真的借线程池线程去算。所以「异步」（不阻塞地等）和「多线程」（同时算）是正交的。

**Q3. `Task.Run` 什么时候该用、什么时候是反模式？**
A：CPU-bound 重计算 → 用 `Task.Run` 把计算挪出调用线程（尤其别占死请求线程）。I/O-bound（`ToListAsync`/`GetAsync`）→ **绝不**包 `Task.Run`，直接 await 即可，包了纯属白借线程、降低吞吐。

**Q4. `.Result`/`.Wait()` 为什么危险？**
A：两宗罪。①在有 SynchronizationContext 的环境（WPF/老 ASP.NET）→ 死锁（阻塞的线程正是异步恢复要用的线程，互等）。②在 ASP.NET Core → 不死锁但**线程池饥饿**（把本可释放的线程按住干等 I/O，退化成同步阻塞模型）。铁律：async all the way，永不 `.Result`。

**Q5. `async void` 为什么是罪恶？唯一例外是什么？**
A：调用方无法 await、无法知道完成、无法捕获异常；里面抛异常没有 Task 承接，直接冒泡到顶层**崩进程**。唯一合法用途：**UI 事件处理器**（签名要求 void）。其余一律 `async Task`。

**Q6. `await` 之后在哪个线程？**
A：取决于 SynchronizationContext。ASP.NET Core 无上下文 → 线程池任意线程，不保证原线程；UI 框架有上下文 → 默认回 UI 线程。推论：跨 await 别依赖线程本地状态。

**Q7. `ConfigureAwait(false)` 是干嘛的？CP6 为什么几乎不用？**
A：声明「await 恢复时不回原同步上下文」。主要给**不知运行环境的通用库**用（防死锁、省上下文切换）。CP6 是 ASP.NET Core 应用，**本就没有同步上下文**，加不加等效，故不加以保持代码整洁。UI 应用里 await 后要碰控件的地方则**绝不能加**（需要回 UI 线程）。

**Q8. `Task.WhenAll` 里两个任务都抛异常，await 会拿到几个？**
A：`await` 只重抛**第一个**异常。要拿全部，得访问 `WhenAll` 返回的那个 Task 的 `.Exception`（`AggregateException`，含所有 InnerExceptions）。

**Q9. 为什么不能对同一个 DbContext 并发发查询？怎么正确并发？**
A：`DbContext` 非线程安全，同时刻只允许一个操作，并发会抛「A second operation was started...」。正确做法：同一 context 串行 await；真要并发多查询用 `IDbContextFactory` 给每个查询开独立 context。

**Q10. 循环里 `await` 一串独立操作有什么问题？**
A：本可并发的操作被排成串行队列，总耗时累加。若操作互相独立，改用 `Task.WhenAll`（数量小）或 `Parallel.ForEachAsync`/`SemaphoreSlim` 限流并发（数量大，防打爆下游）。但若后一个依赖前一个结果，串行 await 才是对的。

**Q11. CancellationToken 是「强制杀死」吗？ASP.NET Core 怎么用它？**
A：不是，是**协作式**——它只是信号，靠透传给会检查它的 `...Async(ct)` 或自己 `ThrowIfCancellationRequested()`/轮询 `IsCancellationRequested` 才生效。ASP.NET Core 自动把 `HttpContext.RequestAborted` 注入 action 的 `CancellationToken` 参数，客户端断开即触发。CP6 约定：Controller 收到就一路透传到 EF Core。

**Q12. `lock` 块里为什么不能 await？替代方案？**
A：编译错误。`lock`=`Monitor`，要求同线程 Enter/Exit，而 await 后可能换线程，会破坏锁的线程归属。替代：`SemaphoreSlim(1,1)` 当异步互斥锁，`await WaitAsync()` … `finally Release()`。

**Q13. `IAsyncEnumerable<T>` 解决什么？和 `Task<List<T>>` 区别？**
A：解决「海量数据流式处理不 OOM」。`Task<List<T>>` 是「等全部好了一次性给一大坨」；`IAsyncEnumerable<T>`+`await foreach` 是「异步地来一个处理一个」，内存占用恒定、天然背压。后台批处理/导出用它；用户翻页用分页。注意 async 迭代器的 token 参数要加 `[EnumeratorCancellation]`。

**Q14. BackgroundService 里为什么不能直接注入 DbContext？怎么办？**
A：BackgroundService 是单例，DbContext 是 Scoped，单例直接注入 Scoped 会报错或让 context 变「事实单例」长命膨胀。正确做法：注入 `IServiceScopeFactory`，每轮工作 `using var scope = _scopeFactory.CreateScope()`，从 scope 解析 DbContext，用完随 scope dispose。

**Q15. 后台循环里为什么用 `await Task.Delay` 而不是 `Thread.Sleep`？**
A：`Thread.Sleep` 死占一个线程睡整段时间（同步阻塞老路），且无法被停机打断；`await Task.Delay(interval, stoppingToken)` 睡眠期间**不占线程**，且停机时 token 触发立即抛 `OperationCanceledException` 醒来，实现优雅停机。

---

## 13. 自测清单

对着下面每一条，问自己「我能不看答案讲清楚吗」：

- [ ] 能画出「100 并发 × 50ms 查询」在同步/异步两种模型下的线程占用时序图。
- [ ] 能解释「异步 I/O 等待期间不占线程」，并说出 IOCP 是谁在等。
- [ ] 能区分 I/O-bound 和 CPU-bound，并说明各自该不该用 `Task.Run`。
- [ ] 能说清 `Task`/`Task<T>`/`ValueTask` 的定位，ValueTask 的三条使用约束。
- [ ] 能默写「不带 async 直接返回 `Task.CompletedTask`/`Task.FromResult`」的场景。
- [ ] 能讲编译器状态机：`_state`、`MoveNext`、局部变量提升、暂停=return、恢复=回调再调 MoveNext。
- [ ] 能回答「await 之后在哪个线程」并解释 SynchronizationContext 的作用。
- [ ] 能推演 `.Result` 在 UI 线程上的死锁全过程。
- [ ] 能判断 `ConfigureAwait(false)` 在库/ASP.NET Core/UI 三种场景下该不该用。
- [ ] 能写出 `WhenAll` 并发、`SemaphoreSlim` 限流、`CancelAfter` 超时、`Parallel.ForEachAsync` 四段代码。
- [ ] 能解释 CancellationToken 是协作式的，说清 ASP.NET Core RequestAborted 与透传约定。
- [ ] 能复述八大坑：`.Result` / `async void` / 串行独立 await / 吞异常 / DbContext 并发 / fire-and-forget / lock 里 await（+ 每个的正确写法）。
- [ ] 能解释 `IAsyncEnumerable` + `[EnumeratorCancellation]` 及其相对分页/ToList 的适用场景。
- [ ] 能默写 BackgroundService 骨架 + `IServiceScopeFactory` 开 scope 模式，并说清为什么。
- [ ] 能说清「异步方法的异常在 await 时才抛」以及它如何解释 async void/fire-and-forget 的丢异常。

---

## 14. 动手练习：把坏异步代码改对

下面是一段**塞满了本章几乎所有坑**的「坏代码」——一个虚构的「批量补货」Controller + Service + 后台服务。你的任务：**逐一找出问题、改对。** 参考答案在后面，先自己做。

### 14.1 坏代码（BAD）

```csharp
// ==================== BAD: ReplenishController.cs ====================
[ApiController]
[Route("api/wms/replenish")]
public class ReplenishController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IReplenishService _svc;
    public ReplenishController(CP6Context db, IReplenishService svc) { _db = db; _svc = svc; }

    // 坏点：没有 CancellationToken 参数
    [HttpPost("run")]
    public IActionResult Run([FromBody] ReplenishRequest req)
    {
        // 坏点：.Result 同步阻塞异步
        var count = _svc.ReplenishAllAsync(req).Result;
        return Ok(new { count });
    }

    // 坏点：async void
    [HttpPost("fire")]
    public async void Fire([FromBody] ReplenishRequest req)
    {
        // 坏点：fire-and-forget 用请求 scope 的服务，请求结束后 scope 就没了
        _ = _svc.ReplenishAllAsync(req);
        await Task.CompletedTask;
    }
}

// ==================== BAD: ReplenishService.cs ====================
public class ReplenishService : IReplenishService
{
    private readonly CP6Context _db;
    private readonly IDownstreamApi _api;
    private readonly object _gate = new();
    public ReplenishService(CP6Context db, IDownstreamApi api) { _db = db; _api = api; }

    public async Task<int> ReplenishAllAsync(ReplenishRequest req)
    {
        var products = await _db.Products.Where(p => p.NeedReplenish).ToListAsync();

        // 坏点：循环内串行 await 独立的下游调用（500 个产品排队）
        var quotes = new List<Quote>();
        foreach (var p in products)
            quotes.Add(await _api.GetQuoteAsync(p.Code));

        // 坏点：同一个 DbContext 上并发查询
        var t1 = _db.Warehouses.CountAsync();
        var t2 = _db.Suppliers.CountAsync();
        await Task.WhenAll(t1, t2);

        // 坏点：lock 里 await（编译都过不了）
        lock (_gate)
        {
            await _db.SaveChangesAsync();
        }

        // 坏点：I/O 包 Task.Run
        await Task.Run(() => _db.AuditLogs.AddAsync(new AuditLog { Action = "replenish" }));

        // 坏点：吞掉关键异常，假装成功
        try { await _api.CommitAsync(req.BatchNo); }
        catch { }

        return quotes.Count;
    }
}

// ==================== BAD: ReplenishCleanupService.cs ====================
public class ReplenishCleanupService : BackgroundService
{
    // 坏点：把 Scoped 的 DbContext 直接注入单例后台服务
    private readonly CP6Context _db;
    public ReplenishCleanupService(CP6Context db) { _db = db; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)   // 坏点：不看 stoppingToken，永远停不下来
        {
            var old = _db.ReplenishJobs.Where(j => j.Done).ToList();  // 坏点：ToList 全捞进内存 + 未透传 token
            _db.ReplenishJobs.RemoveRange(old);
            await _db.SaveChangesAsync();

            Thread.Sleep(60000);   // 坏点：Thread.Sleep 死占线程、无法优雅停机
        }
    }
}
```

**先自己列出所有坏点并写出修正版，再看下面的参考答案。**

### 14.2 参考答案（GOOD）

```csharp
// ==================== GOOD: ReplenishController.cs ====================
[ApiController]
[Route("api/wms/replenish")]
[Authorize]
public class ReplenishController : ControllerBase
{
    private readonly IReplenishService _svc;
    public ReplenishController(IReplenishService svc) { _svc = svc; }

    // 修正：async Task<IActionResult> + 注入 CancellationToken（框架自动填 RequestAborted）
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] ReplenishRequest req, CancellationToken ct)
    {
        // 修正：一路 await，不用 .Result
        var count = await _svc.ReplenishAllAsync(req, ct);
        return Ok(new { count });
    }

    // 修正：不在请求里 fire-and-forget。要「触发后台完成」→ 往持久队列写一条，由 BackgroundService 消费。
    // 这里只 await 入队（在请求 scope 内安全完成），后台处理见 ReplenishQueueWorker（略）。
    [HttpPost("fire")]
    public async Task<IActionResult> Fire([FromBody] ReplenishRequest req, CancellationToken ct)
    {
        await _svc.EnqueueAsync(req, ct);   // 只入队，真正的活交给后台 worker
        return Accepted();
    }
}

// ==================== GOOD: ReplenishService.cs ====================
public class ReplenishService : IReplenishService
{
    private readonly CP6Context _db;
    private readonly IDbContextFactory<CP6Context> _dbFactory;   // 需要并发多查询时用它开独立 context
    private readonly IDownstreamApi _api;
    private readonly SemaphoreSlim _gate = new(1, 1);            // 修正：异步互斥锁替代 lock

    public ReplenishService(CP6Context db, IDbContextFactory<CP6Context> dbFactory, IDownstreamApi api)
    {
        _db = db; _dbFactory = dbFactory; _api = api;
    }

    public async Task<int> ReplenishAllAsync(ReplenishRequest req, CancellationToken ct)
    {
        var products = await _db.Products.Where(p => p.NeedReplenish).ToListAsync(ct);

        // 修正：独立的下游调用 → 限流并发（最多 10 个在途，别打爆下游）
        var quotes = new ConcurrentBag<Quote>();
        await Parallel.ForEachAsync(products,
            new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
            async (p, token) => quotes.Add(await _api.GetQuoteAsync(p.Code, token)));

        // 修正：并发多查询用独立 context（各开各的，绝不共用注入的 _db）
        async Task<int> CountWarehouses()
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Warehouses.CountAsync(ct);
        }
        async Task<int> CountSuppliers()
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Suppliers.CountAsync(ct);
        }
        var wTask = CountWarehouses();
        var sTask = CountSuppliers();
        await Task.WhenAll(wTask, sTask);
        var warehouseCount = wTask.Result;   // WhenAll 后取 .Result 安全（已完成）
        var supplierCount  = sTask.Result;

        // 修正：SemaphoreSlim 当异步锁，可以在临界区 await
        await _gate.WaitAsync(ct);
        try
        {
            // 修正：直接 await，不用 Task.Run 包 I/O
            _db.AuditLogs.Add(new AuditLog { Action = "replenish" });   // Add 是同步的，无需 AddAsync/Task.Run
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();   // 必须 finally 释放
        }

        // 修正：关键操作异常不吞——上抛让调用方感知（或按业务显式处理并记日志）
        await _api.CommitAsync(req.BatchNo, ct);

        return quotes.Count;
    }

    public async Task EnqueueAsync(ReplenishRequest req, CancellationToken ct)
    {
        _db.ReplenishJobs.Add(new ReplenishJob { BatchNo = req.BatchNo, Done = false });
        await _db.SaveChangesAsync(ct);
    }
}

// ==================== GOOD: ReplenishCleanupService.cs ====================
public class ReplenishCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // 修正：注入 IServiceScopeFactory（单例安全），不直接注入 Scoped 的 DbContext
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReplenishCleanupService> _logger;

    public ReplenishCleanupService(IServiceScopeFactory scopeFactory, ILogger<ReplenishCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("补货清理 worker 启动");
        try
        {
            // 修正：循环看 stoppingToken，可优雅停机
            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanupOnceAsync(stoppingToken);
                // 修正：await Task.Delay 而非 Thread.Sleep（不占线程、可被停机打断）
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _logger.LogInformation("补货清理 worker 停止"); }
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        try
        {
            // 修正：每轮开独立 scope，从中解析短命的 DbContext
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();

            // 修正：ExecuteDeleteAsync 批量删，不 ToList 全捞进内存；透传 token
            var deleted = await db.ReplenishJobs
                .Where(j => j.Done)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0) _logger.LogInformation("补货清理：删除 {Count} 条已完成任务", deleted);
        }
        catch (OperationCanceledException) { /* 停机中，忽略 */ }
        catch (Exception ex) { _logger.LogError(ex, "补货清理失败"); }
    }
}
```

### 14.3 坏点对照表（自查用）

| # | 坏点 | 所属节 | 修正 |
|---|---|---|---|
| 1 | action 无 CancellationToken 参数 | §7.3 | 加 `CancellationToken ct` 参数并透传 |
| 2 | `.Result` 同步阻塞异步 | §8.1 | `await` 一路到底 |
| 3 | `async void` 端点 | §8.2 | `async Task<IActionResult>` |
| 4 | 请求里 fire-and-forget 用 Scoped 服务 | §8.6 | 只入队，交 BackgroundService 消费 |
| 5 | 循环内串行 await 独立调用 | §8.3 | `Parallel.ForEachAsync` 限流并发 |
| 6 | 同一 DbContext 并发查询 | §8.5 | `IDbContextFactory` 各开独立 context |
| 7 | `lock` 里 await | §8.7 | `SemaphoreSlim(1,1)` + `WaitAsync/Release` |
| 8 | I/O 包 `Task.Run` | §3.3 | 直接 await（且 `Add` 本是同步） |
| 9 | 空 catch 吞关键异常 | §8.4 | 上抛或显式处理 + 记日志 |
| 10 | 单例后台服务直接注入 Scoped DbContext | §10.2 | 注入 `IServiceScopeFactory`，每轮开 scope |
| 11 | `while(true)` 不看 stoppingToken | §10.1 | `while(!stoppingToken.IsCancellationRequested)` |
| 12 | `ToList` 全表捞进内存删除 | §9 / §10.2 | `ExecuteDeleteAsync` 批量删 |
| 13 | `Thread.Sleep` 死占线程 | §10.1 | `await Task.Delay(interval, stoppingToken)` |

---

## 结语

这一章你从**操作系统的线程**一路走到了**编译器的状态机**，中间用 CP6 的 `StockController`、`StockMovementService`、六七个 `BackgroundService` 做了全程标本。把握三条主线，async/await 就再也不神秘：

1. **异步的价值是吞吐量**——I/O 等待期间不占线程（IOCP），所以少数线程能扛海量并发。异步 ≠ 多线程。
2. **await 是编译器状态机的暂停点**——暂停=方法 return 未完成 Task，恢复=回调再调 MoveNext；异常装进 Task、await 时才抛。
3. **纪律高于技巧**——async all the way、CancellationToken 一路透传、DbContext 不并发、后台服务用 scope、核心异常不吞。CP6 的每一处异步都在示范这些纪律。

面试时，别只背结论，**讲原理 + 举 CP6 里的真实取舍**（为什么 Kafka 消费要 LongRunning 独立线程、为什么后台服务要 IServiceScopeFactory、为什么应用层不加 ConfigureAwait），你就是那个「真懂」的候选人。
