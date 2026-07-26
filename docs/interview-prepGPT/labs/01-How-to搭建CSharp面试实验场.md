# How to 搭建 C# 面试实验场

你将创建一个独立 .NET 8 控制台项目，用它验证类型复制、闭包、异步并发和取消。

## 前置

- .NET 8 SDK。
- PowerShell。
- 不需要连接 CP6 数据库。

## 步骤 1：创建临时项目

在仓库外或系统临时目录执行：

```powershell
$lab = Join-Path $env:TEMP 'cp6-interview-csharp-lab'
New-Item -ItemType Directory -Force -Path $lab | Out-Null
dotnet new console --framework net8.0 --force --output $lab
Set-Location $lab
dotnet run
```

看到 `Hello, World!` 即第一结果完成。

## 步骤 2：验证值与引用

把 `Program.cs` 替换为：

```csharp
var a = 1;
var b = a;
b++;

var x = new Box { Value = 1 };
var y = x;
Demo.Mutate(y);
Demo.Reassign(y);

Console.WriteLine($"values: a={a}, b={b}");
Console.WriteLine($"refs: x={x.Value}, y={y.Value}, same={ReferenceEquals(x,y)}");

Demo.Reassign(ref y);
Console.WriteLine($"by-ref: x={x.Value}, y={y.Value}, same={ReferenceEquals(x,y)}");

sealed class Box
{
    public int Value { get; set; }
}

static class Demo
{
    public static void Mutate(Box box) => box.Value++;
    public static void Reassign(Box box) => box = new Box { Value = 999 };
    public static void Reassign(ref Box box) => box = new Box { Value = 888 };
}
```

运行：

```powershell
dotnet run
```

先预测，再解释每行。上面的排列可以直接编译：顶级语句在前，`Box` 和 `Demo` 类型声明在文件末尾。若自己改写时把类型移到顶级语句前，编译器会报告位置错误。

## 步骤 3：验证异步三种策略

```csharp
using System.Diagnostics;

static async Task WorkAsync(int id, CancellationToken ct)
{
    await Task.Delay(200, ct);
    Console.WriteLine($"done {id}");
}

static async Task MeasureAsync(string name, Func<Task> action)
{
    var sw = Stopwatch.StartNew();
    await action();
    Console.WriteLine($"{name}: {sw.ElapsedMilliseconds}ms");
}

await MeasureAsync("serial", async () =>
{
    for (var i = 0; i < 20; i++)
        await WorkAsync(i, CancellationToken.None);
});

await MeasureAsync("all", async () =>
{
    await Task.WhenAll(Enumerable.Range(0, 20)
        .Select(i => WorkAsync(i, CancellationToken.None)));
});

await MeasureAsync("limited", async () =>
{
    using var gate = new SemaphoreSlim(4);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
    {
        await gate.WaitAsync();
        try { await WorkAsync(i, CancellationToken.None); }
        finally { gate.Release(); }
    }));
});
```

预期大致：串行约 4 秒，全并发约 0.2 秒，限 4 并发约 1 秒。机器调度会有差异。

## 步骤 4：增加取消

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));

try
{
    for (var i = 0; i < 10; i++)
        await WorkAsync(i, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    Console.WriteLine("cancelled as expected");
}
```

把最内层 `Task.Delay(200, ct)` 改成不传 token，再运行，观察取消响应为何变慢/失效。

## 步骤 5：闭包

```csharp
var actions = new List<Action>();
for (var i = 0; i < 3; i++)
{
    var copy = i;
    actions.Add(() => Console.WriteLine(copy));
}
actions.ForEach(a => a());
```

再故意让三个 Action 捕获同一个外部变量，解释输出。

## 验证

```powershell
dotnet build
dotnet run
```

实验记录应包含：三种耗时、取消行为、值/引用输出、闭包输出和自己的解释。

## 排错

- “Only one compilation unit can have top-level statements”：检查是否有多个带顶级语句的 `.cs`。
- 顶级语句位置错误：把类型声明移到文件末尾。
- SDK 不支持 net8.0：运行 `dotnet --list-sdks`，安装 .NET 8 SDK。
- 耗时差异小：确认模拟的是 `Task.Delay`，循环次数和延迟足够观察。

## 完成后

删除临时目录是可选的；若保留，确保它不在 CP6 仓库中。把实验结论写入学习记录，不只保留代码。
