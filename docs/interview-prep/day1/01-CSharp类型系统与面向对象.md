# 第 1 章　C# 类型系统与面向对象（从零到精通）

> 面向对象：本教程把你当成 C# 新手，从最底层的概念讲起，但每一个知识点都用 **C:\CP6** 这个真实生产项目（.NET 8 多租户制造业 ERP/MES/WMS 系统）里的代码当标本。学完这一章，你不仅懂 C#，还能在面试里张口就讲"我们项目里是这么用的"。
>
> **CP6 是什么**：一套给日本瓦楞纸箱制造企业做的生产管理系统，包含 ERP（企业资源计划）、MES（制造执行系统）、WMS（仓库管理系统）、FIN（财务）、OA/WF（办公/工作流）等模块，多租户（一套系统服务多家公司，数据互相隔离）。后端 .NET 8 + Entity Framework Core + SQL Server，前端 Vue。
>
> **本章阅读方式**：每个知识点固定五段式 —— ①概念讲解（新手友好，配类比）→ ②CP6 真实代码（标注文件路径）→ ③逐行解析 → ④常见坑与踩坑实录 → ⑤面试怎么问 + 参考答案。看不懂就慢下来，画图。章末有 15 道面试题、自测清单和动手练习。

---

## 目录

1. [.NET 生态全景：从 CLR 到机器码](#1-net-生态全景从-clr-到机器码)
2. [程序结构：解决方案 / 项目 / 命名空间 / 类 —— CP6 四项目分层](#2-程序结构解决方案--项目--命名空间--类-cp6-四项目分层)
3. [类型系统深讲：值类型 vs 引用类型、内置类型全表、金额为什么必须 decimal](#3-类型系统深讲值类型-vs-引用类型内置类型全表金额为什么必须-decimal)
4. [类与对象：字段、属性、构造函数、静态与实例](#4-类与对象字段属性构造函数静态与实例)
5. [特性 Attribute：声明式元数据与反射](#5-特性-attribute声明式元数据与反射)
6. [继承与多态：virtual / override / abstract / sealed](#6-继承与多态virtual--override--abstract--sealed)
7. [接口：契约、显式实现、默认实现、标记接口](#7-接口契约显式实现默认实现标记接口)
8. [封装与访问修饰符全表](#8-封装与访问修饰符全表)
9. [object 基类：Equals / GetHashCode / ToString](#9-object-基类equals--gethashcode--tostring)
10. [record 与 struct：值语义、with 表达式、如何选型](#10-record-与-struct值语义with-表达式如何选型)
11. [本章面试题 15 问（含参考答案）](#11-本章面试题-15-问含参考答案)
12. [自测清单](#12-自测清单)
13. [动手小练习（在 CP6 里做）](#13-动手小练习在-cp6-里做)

---

## 1. .NET 生态全景：从 CLR 到机器码

### 1.1 概念讲解

很多新手一上来就被 ".NET Framework"、".NET Core"、".NET 5/6/7/8" 搞晕。先给你一句话拉直历史：

> **.NET 是一个"运行 C# 程序的平台"，它经历了三个时代：老 Framework（只跑 Windows）→ 重写的 Core（跨平台）→ 合并后统一叫 .NET（去掉 Core 二字，从 5 开始）。**

用**汽车品牌换代**打个比方：

| 时代 | 名字 | 类比 | 关键特征 |
|------|------|------|----------|
| 2002–2019 | **.NET Framework** 1.0 ~ 4.8 | 老款燃油车 | 只能在 Windows 上跑；和 Windows 深度绑定；4.8 是最后一版，只修 Bug 不加新功能 |
| 2016–2019 | **.NET Core** 1.0 ~ 3.1 | 全新平台的电车 | 跨平台（Windows/Linux/macOS）、开源、高性能、可以放进 Docker 容器 |
| 2020 至今 | **.NET**（5、6、7、8、9…） | 电车成为主线，燃油车停产 | 微软把 Core 改名为 ".NET"，**跳过 4**（避免和 Framework 4.x 混淆），此后每年一版 |

**CP6 用的是 .NET 8**（2023 年 11 月发布的 LTS 长期支持版）。你在面试里说"我们项目是 .NET 8"，等于说"我们用的是当下企业主流的、跨平台的、能进 Docker 的现代版本"。CP6 的 `docker` 部署（记忆里反复提到"重建 cp6-api 镜像"）正是靠 .NET Core 之后的跨平台能力才可能。

**证据**：CP6.Entity.csproj 第 4 行明确写着目标框架：

```xml
<!-- 文件：C:\CP6\CP6.Entity\CP6.Entity.csproj -->
<TargetFramework>net8.0</TargetFramework>
```

### 1.2 C# 代码是怎么变成"能运行的东西"的？

这是面试高频概念题。C# **不是**直接编译成 CPU 能懂的机器码（machine code），而是走**两段式**：

```
   你写的 C# 源码 (.cs)
         │
         │  ① C# 编译器 (Roslyn) 编译
         ▼
   IL 中间语言 (Intermediate Language) + 元数据      ← 存进 .dll / .exe
         │
         │  ② 运行时，CLR 里的 JIT 即时编译
         ▼
   本机机器码 (x64 / ARM 指令)                        ← CPU 真正执行
```

**几个必须会背的名词**（首次出现附英文）：

- **CLR（Common Language Runtime，公共语言运行时）**：.NET 的"虚拟机 / 发动机"。它负责加载程序、管理内存、执行 IL。Java 有 JVM，.NET 有 CLR，地位相同。
- **IL（Intermediate Language，中间语言）**：一种介于 C# 和机器码之间的"通用汇编"。C#、F#、VB.NET 编译后都变成 IL，所以它们能互相调用。IL 存在 `.dll`（类库）或 `.exe`（可执行）文件里。
- **JIT（Just-In-Time，即时编译）**：CLR 里的一个组件。程序**运行时**，当某个方法**第一次**被调用，JIT 才把它的 IL 翻译成机器码并缓存起来，第二次调用就直接用缓存。这叫"即时"——用到才编。
- **GC（Garbage Collector，垃圾回收器）**：CLR 的自动内存管家。C 语言要你手动 `malloc/free`，C# 里你只管 `new`，不用的对象由 GC 自动回收。这是 C# 号称"托管（managed）"语言的核心。

**类比**：把 IL 想成"世界语"。中国人、法国人、日本人（C#、F#、VB）都先把话翻译成世界语写下来（编译成 IL），到了目的地（具体这台机器是 Intel 还是 ARM）再由当地翻译（JIT）临场翻成本地方言（机器码）。好处：源码写一次，能在任何装了 CLR 的机器上跑。

### 1.3 托管 vs 非托管、GC 三代

- **托管代码（managed code）**：跑在 CLR 里、由 GC 管内存的代码，就是你写的普通 C#。
- **非托管代码（unmanaged code）**：绕过 CLR 直接操作内存，比如调用 Windows API、C++ 库。CP6 作为业务系统几乎全是托管代码。
- **GC 分代（generational GC）**：GC 把对象按"存活时间"分成 Gen 0（新生）、Gen 1、Gen 2（老年）。新对象放 Gen 0，回收最频繁；活过几轮的"晋升"到更高代，回收频率低。原理：大多数对象都"朝生暮死"（比如一个方法里的临时变量），所以频繁扫描新生区、少扫老区，效率最高。

### 1.4 常见坑与踩坑实录

- **坑 1：把 .NET Framework 和 .NET 混为一谈**。面试官问"你们用 .NET 几"，答"4.8"和答"8"是两个完全不同的技术栈。CP6 是 **net8.0**，能跑 Linux/Docker；Framework 4.8 只能跑 Windows。
- **坑 2：以为 C# 编译后就是机器码**。不是，是 IL。机器码在运行时由 JIT 生成。反编译工具（ILSpy、dnSpy）能把 dll 里的 IL 还原成近似 C#，所以**纯 .dll 不是安全边界**（面试延伸点：需要混淆或服务端隔离）。
- **坑 3：以为有了 GC 就不会内存泄漏**。会。事件订阅不取消、静态集合只增不减、`IDisposable` 不释放，都会让对象一直被引用而无法回收。GC 只回收"没人引用"的对象。

### 1.5 面试怎么问 + 参考答案

**Q：说一下 C# 从源码到执行的完整过程。**

> A：C# 源码先由 Roslyn 编译器编译成 IL 中间语言，连同元数据一起打包进 dll 或 exe。运行时 CLR 加载它，其中的 JIT 即时编译器在方法首次被调用时，把该方法的 IL 翻译成当前 CPU 架构的机器码并缓存，之后直接执行缓存。内存由 GC 分代自动回收。这种两段式设计让同一份 C# 能跨平台运行——我们 CP6 项目就是 .NET 8，编译出的镜像既能在开发的 Windows 上跑，也能打包进 Docker 在 Linux 上部署。

**Q：.NET Framework、.NET Core、.NET 5+ 什么关系？**

> A：Framework 是 2002 年的初代，只支持 Windows，4.8 是终点站；Core 是 2016 年跨平台重写，开源、高性能、支持容器；到 2020 年微软把 Core 改名为 .NET，从 5 开始（跳过 4 避免和 Framework 4.x 撞号），此后一年一版，8 是当前 LTS。CP6 用的就是 .NET 8。

---

## 2. 程序结构：解决方案 / 项目 / 命名空间 / 类 —— CP6 四项目分层

### 2.1 概念讲解（四层容器）

C# 代码的组织从大到小是一套俄罗斯套娃：

```
解决方案 Solution (.sln)          ← 一个"工作区"，装若干项目
   └── 项目 Project (.csproj)      ← 编译产出一个 .dll 或 .exe，是复用/依赖的最小单位
         └── 命名空间 Namespace     ← 给类型分组、防重名的"文件夹路径"
               └── 类 Class          ← 真正写字段和方法的地方
                     └── 成员 Member  ← 字段、属性、方法……
```

**类比**：
- **解决方案** = 一栋写字楼（`CP6.sln`）。
- **项目** = 楼里的一家公司（编译成一个 dll）。公司之间可以"外包"（引用）。
- **命名空间** = 公司里的部门（`CP6.Entity.DomainModels.Wms`）。
- **类** = 部门里的一名员工（`Stock`）。
- **成员** = 员工的技能和资料（`PhysicalQty` 属性）。

### 2.2 CP6 的四项目分层与依赖方向

CP6 后端是经典的**分层架构（layered architecture）**，四个项目：

| 项目 | 职责 | 类比 | 引用谁 |
|------|------|------|--------|
| **CP6.Entity** | 实体 / DTO / 特性 / 接口——纯数据结构，几乎无逻辑 | 仓库里的"原材料和图纸" | 谁都不引（只依赖 EF Core） |
| **CP6.Core** | 业务逻辑、Service、EF DbContext、数据库访问 | 工厂车间，干活的地方 | → CP6.Entity |
| **CP6.WebApi** | 控制器 Controller、接收 HTTP、认证、返回 JSON | 公司前台/门店，对外接口 | → CP6.Core |
| **CP6.Tests** | 单元/集成测试 | 质检部门 | → 三者全引 |

**真实证据**（我实际读了各 csproj 的 `<ProjectReference>`）：

```
CP6.Entity  →  （无项目引用，仅 PackageReference: Microsoft.EntityFrameworkCore 8.0.12）
CP6.Core    →  CP6.Entity
CP6.WebApi  →  CP6.Core
CP6.Tests   →  CP6.WebApi + CP6.Core + CP6.Entity
```

画成依赖箭头（箭头 = "依赖 / 认识"）：

```
        ┌─────────────┐
        │ CP6.WebApi  │  控制器层：收 HTTP 请求
        └──────┬──────┘
               │ 依赖
               ▼
        ┌─────────────┐
        │  CP6.Core   │  业务层：Service + DbContext
        └──────┬──────┘
               │ 依赖
               ▼
        ┌─────────────┐
        │ CP6.Entity  │  数据层：实体/DTO，谁都不依赖
        └─────────────┘

     CP6.Tests ──依赖──► 上面三个全部
```

### 2.3 逐点解析：为什么依赖方向"只能向下"？

**依赖方向的意义 = 稳定的东西不该依赖易变的东西。**

1. **Entity 谁都不依赖** → 它是"稳定核心"。实体类 `Stock`、`OrderDto` 只描述"数据长什么样"，不写业务规则。因为它不认识 Core 和 WebApi，所以改业务逻辑（Core）或改接口（WebApi）**永远不会**逼你改 Entity。这叫**依赖倒置**的直观体现。

2. **WebApi 依赖 Core，Core 不依赖 WebApi** → 业务逻辑不应该知道"我是被 HTTP 调用还是被定时任务调用"。CP6 记忆里提到的"对账 worker"、"清理 worker" 就是非 HTTP 的后台调用者，它们也调 Core 的 Service。如果 Core 反过来依赖了 WebApi，worker 就没法复用了。

3. **循环依赖是大忌**：如果 Entity 又反过来引用 Core，两个项目就绑死了，编译器直接报错（C# 不允许项目间循环引用）。分层架构天然避免这一点。

**为什么单独拆一个 Entity 项目？** 因为实体和 DTO 是"公共货币"：WebApi 要用（序列化成 JSON 返给前端）、Core 要用（存数据库）、Tests 要用（造测试数据）。放在最底层，三方都能拿到，且互不干扰。

### 2.4 命名空间与"文件夹即命名空间"约定

看 CP6.Entity 的 `GlobalUsings.cs`（我实际读到的原文注释）：

```csharp
// 文件：C:\CP6\CP6.Entity\GlobalUsings.cs
// 物理フォルダ = 名前空間（Erp / Sys / Integration / Common / Mes / Wms）。
global using CP6.Entity.DomainModels.Erp;
global using CP6.Entity.DomainModels.Sys;
global using CP6.Entity.DomainModels.Integration;
global using CP6.Entity.DomainModels.Common;
global using CP6.Entity.DTOs.Erp;
```

逐点解析：

- **"物理文件夹 = 命名空间"**：`Stock.cs` 放在 `DomainModels\Wms\` 文件夹，它的命名空间就是 `CP6.Entity.DomainModels.Wms`（见 Stock.cs 第 4 行 `namespace CP6.Entity.DomainModels.Wms;`）。这是团队约定，让"文件在哪 = 类型归哪个命名空间"一目了然。
- **`global using`（C# 10 引入的全局 using）**：普通 `using` 只在当前文件生效；`global using` 写一次，**整个项目所有文件**都默认导入。这里把常用命名空间集中在一个文件全局引入，省得每个文件顶部都堆一排 `using`。
- **`namespace CP6.Entity;`（文件范围命名空间，file-scoped namespace，C# 10）**：注意 BaseEntity.cs 第 4 行是 `namespace CP6.Entity;`（**带分号，不带大括号**），下面所有代码都属于这个命名空间，少一层缩进。老写法是 `namespace CP6.Entity { ... }` 用大括号包住。

### 2.5 常见坑

- **坑：`ImplicitUsings` 让你以为不用 using**。csproj 里 `<ImplicitUsings>enable</ImplicitUsings>`（CP6.Entity.csproj 第 5 行）会自动帮你 `using System;`、`using System.Linq;` 等一堆常用命名空间，所以你在 Stock.cs 里没写 `using System;` 也能用 `Guid`、`DateTime`。但**不是所有**命名空间都隐式导入，`System.ComponentModel.DataAnnotations`（放特性的）还得手写——看 Stock.cs 第 1-2 行确实手动 `using` 了。
- **坑：项目引用方向搞反**。新手常想"WebApi 要用实体，那让 Entity 引用 WebApi 吧"——大错。永远是"上层引用下层"，Entity 是最底层，谁都不引它以外的项目。

### 2.6 面试怎么问 + 参考答案

**Q：介绍一下你们项目的分层结构。**

> A：我们后端分四个项目：CP6.Entity 放实体和 DTO，是最底层、不依赖任何业务项目；CP6.Core 放业务逻辑、Service 和 EF Core 的 DbContext，依赖 Entity；CP6.WebApi 是控制器层，收 HTTP 请求，依赖 Core；CP6.Tests 引用三者做测试。依赖方向严格向下，保证核心数据结构稳定、业务逻辑能被 HTTP 和后台 worker 同时复用，也避免项目循环依赖。我们还约定"物理文件夹等于命名空间"，比如 WMS 的实体都在 DomainModels\Wms 文件夹、命名空间是 CP6.Entity.DomainModels.Wms。

---

## 3. 类型系统深讲：值类型 vs 引用类型、内置类型全表、金额为什么必须 decimal

这是本章**最重要**的一节，也是面试必考。

### 3.1 概念讲解：值类型 vs 引用类型

C# 所有类型分两大阵营：

- **值类型（value type）**：变量**直接装着值本身**。赋值、传参会**复制一份**。包括 `int`、`double`、`decimal`、`bool`、`char`、`DateTime`、`Guid`、所有 `struct`、所有 `enum`。
- **引用类型（reference type）**：变量装的是**指向堆上对象的地址（引用）**。赋值、传参复制的是**地址**，两个变量指向同一个对象。包括 `class`、`string`、数组、`List<T>`、接口、委托。

**内存模型：栈（stack）与堆（heap）**

- **栈**：方法调用时的"临时工作台"，存局部变量、方法参数，速度极快，方法结束自动清空。值类型的局部变量一般住栈上。
- **堆**：一大片"仓库"，存 `new` 出来的对象，由 GC 管理。引用类型的对象住堆上，栈上只放一个指向它的地址。

**ASCII 内存图：值类型 vs 引用类型**

```
值类型： int a = 5;  int b = a;   (b 复制了一份值)
┌───────── 栈 ─────────┐
│  a │  5              │
│  b │  5   ← 独立的另一个 5，改 b 不影响 a
└──────────────────────┘

引用类型： Stock s1 = new Stock();  Stock s2 = s1;  (s2 复制的是地址)
┌───────── 栈 ─────────┐        ┌──────── 堆 ────────┐
│  s1 │ 0x1000 ────────┼───────►│ 0x1000: Stock 对象 │
│  s2 │ 0x1000 ────────┼───────►│  PhysicalQty=100   │
└──────────────────────┘        └────────────────────┘
       s1、s2 指向同一个对象，改 s2.PhysicalQty，s1 看到的也变了！
```

**这直接决定了 CP6 里的一个陷阱**：`Stock` 是 `class`（引用类型），所以如果你写 `var copy = stock;` 再改 `copy`，原来的 `stock` 也会变——它俩是同一个对象。要真正复制，得手动 new 一个新 Stock 逐字段拷贝。

### 3.2 内置类型全表（配制造业用途）

| 类型 | .NET 全名 | 阵营 | 大小 | 范围/精度 | CP6 制造业典型用途 |
|------|-----------|------|------|-----------|-------------------|
| `bool` | `System.Boolean` | 值 | 1 字节 | true/false | `IsDeleted` 逻辑删除、`RecallFlag` 召回标记（Stock.cs） |
| `byte` | `System.Byte` | 值 | 1 字节 | 0~255 | 原始二进制、`RowVersion`（`byte[]` 数组，乐观锁） |
| `char` | `System.Char` | 值 | 2 字节 | 单个 Unicode 字符 | 单字符标志位，制造业少用 |
| `int` | `System.Int32` | 值 | 4 字节 | ±21 亿 | `SortOrder` 显示顺序、`PageIndex` 分页、`Status` 状态码 |
| `long` | `System.Int64` | 值 | 8 字节 | ±922 京 | 自增大 ID、时间戳毫秒数、超大计数 |
| `float` | `System.Single` | 值 | 4 字节 | ~7 位有效数字 | **金额禁用**；科学计算/传感器近似值 |
| `double` | `System.Double` | 值 | 8 字节 | ~15-17 位有效数字 | **金额禁用**；IoT 传感器读数、几何计算 |
| `decimal` | `System.Decimal` | 值 | 16 字节 | 28-29 位精确十进制 | **金额、数量、单价、税额**——CP6 里 `PhysicalQty`、`UnitPrice`、`Amount` 全是它 |
| `string` | `System.String` | **引用** | 变长 | 不可变字符序列 | 各种编码/名称：`WarehouseCd`、`ProductCd`、`Creator` |
| `DateTime` | `System.DateTime` | 值 | 8 字节 | 年月日时分秒 | `CreateDate`、`ReceiveDate`、`ExpiryDate`（賞味期限） |
| `DateOnly` | `System.DateOnly` | 值 | 4 字节 | 仅日期无时间（.NET 6+） | 生产日期、纯日期字段（避免时区误差） |
| `TimeSpan` | `System.TimeSpan` | 值 | 8 字节 | 时间间隔 | 工时、超时时长、生产节拍 |
| `Guid` | `System.Guid` | 值 | 16 字节 | 全局唯一标识 | **所有实体主键** `Id`、`TenantId` 租户 ID |

> **注**：`string` 虽然是引用类型，但它**不可变（immutable）**且有值相等语义，用起来像值类型，是个特殊生物，后面第 9、10 节还会讲。

### 3.3 CP6 真实代码：类型选择的教科书

看 Stock.cs（我实读原文，标注行号）：

```csharp
// 文件：C:\CP6\CP6.Entity\DomainModels\Wms\Stock.cs
[Table("T_Stock")]
public class Stock : BaseBizEntity
{
    [Required, MaxLength(10)]
    public string WarehouseCd { get; set; } = string.Empty;   // 仓库编码：string

    [Column(TypeName = "decimal(21,8)")]
    public decimal PhysicalQty { get; set; } = 0m;             // 物理库存数：decimal！

    [Column(TypeName = "decimal(21,8)")]
    public decimal AllocatedQty { get; set; } = 0m;            // 引当中数量：decimal

    [Column(TypeName = "decimal(18,4)")]
    public decimal? UnitPrice { get; set; }                    // 单价：可空 decimal

    public DateTime? ExpiryDate { get; set; }                  // 賞味期限：可空 DateTime

    public bool RecallFlag { get; set; } = false;             // 召回标志：bool
}
```

逐行解析：

- `string WarehouseCd = string.Empty`：编码是文本，用 `string`。默认给 `string.Empty`（空字符串 `""`）而非 `null`，避免下游空引用。
- `decimal PhysicalQty = 0m`：**库存数量用 decimal**。后缀 `m`（或 `M`）表示"这是 decimal 字面量"。`decimal(21,8)` 表示总共 21 位、小数点后 8 位——瓦楞纸计量到 8 位小数，精度要求极高。
- `decimal? UnitPrice`：**问号 `?` 表示可空**（Nullable），"这个货可能还没定价"，用 `null` 表达"没有值"，比用 0 表达更准确（0 元和"未定价"是两回事）。
- `DateTime? ExpiryDate`：可空日期，"可能没有保质期"。
- `bool RecallFlag = false`：是/否标志，默认 false。

### 3.4 【重点】金额为什么**必须** decimal —— 二进制浮点误差的数学原因

这是面试**最爱问**的点，也是制造业/金融系统的血泪教训。

**核心结论**：`float`/`double` 用**二进制**存小数，无法精确表示大多数十进制小数（比如 0.1），会产生**舍入误差**；`decimal` 用**十进制**存，能精确表示，所以**一切金额、数量、单价、税额都必须 decimal**。

**数学原因（讲透）**：

计算机底层是二进制。整数没问题（5 = 101）。但小数用二进制表示，是各个 2 的负次幂相加：

```
二进制小数 = a·(1/2) + b·(1/4) + c·(1/8) + d·(1/16) + ...
```

十进制的 **0.1** 换成二进制是**无限循环小数**：

```
0.1(十) = 0.0001100110011001100110011...(二)  ← 1001 无限循环，存不下！
```

就像十进制里 1/3 = 0.3333… 永远除不尽一样，二进制里 0.1 也除不尽。`double` 只有 64 位，只能截断存一个**最接近的近似值**，于是：

```csharp
double a = 0.1 + 0.2;
Console.WriteLine(a);            // 输出 0.30000000000000004  ← 多出来的尾巴！
Console.WriteLine(a == 0.3);     // 输出 False               ← 惊悚

decimal b = 0.1m + 0.2m;
Console.WriteLine(b);            // 输出 0.3                  ← 精确
Console.WriteLine(b == 0.3m);    // 输出 True                ← 正确
```

`decimal` 内部是"十进制的科学计数法"（一个 96 位整数 + 一个小数位标度），十进制小数直接精确存储，**不经过二进制小数转换**，所以 0.1 就是 0.1。代价是慢一点、占 16 字节，但金额场景精度压倒性能。

**对账事故场景（真实会发生的血案）**：

假设 CP6 用 `double` 存单价和数量，某仓库出库 3 次，每次 0.1 吨纸：

```
用 double：0.1 + 0.1 + 0.1 = 0.30000000000000004 吨
对账时和纸厂系统（用 decimal 的）比对：0.30000000000000004 ≠ 0.3
→ 系统判定"库存对不上"，触发差异告警，财务花一整天查账，
  最后发现是浮点误差，白忙一场。累积到几万条交易，差异会滚成"分"级别的钱，
  月末总账和明细账差几分钱，审计过不了。
```

CP6 的记忆里就有"对账 worker / 发布链"的模块，对账系统对精度零容忍。所以你看 Stock.cs 里**所有数量、单价、金额清一色 decimal + `[Column(TypeName="decimal(21,8)")]` 精确指定数据库列精度**。MasterBase.cs 的 `DiscountThreshold` 折扣阈值也是 `decimal?`。

**一句话记住**：**money、quantity、price、tax → 永远 decimal，永远别 double。** 面试官听到你能讲出"二进制无法精确表示 0.1"这个数学原因，会立刻高看你。

### 3.5 装箱（boxing）与拆箱（unboxing）

**概念**：把一个**值类型**塞进 `object`（或接口）这种引用类型的"盒子"，叫**装箱**；反过来取出来叫**拆箱**。装箱会在**堆上 new 一个盒子**把值复制进去，有性能开销。

**内存图**：

```
int n = 42;              栈：  n │ 42
object box = n;   ← 装箱

┌──── 栈 ────┐         ┌──────── 堆 ────────┐
│  n  │ 42   │         │ 0x2000: [盒子]     │
│ box │0x2000├────────►│   里面装着 int 42   │  ← 新 new 的对象！
└────────────┘         └────────────────────┘

int m = (int)box;  ← 拆箱：从堆里把 42 复制回栈上的 m
```

- 装箱：值 → 堆上的对象，产生 GC 压力。
- 拆箱：必须转回**完全相同的类型**，`(int)box` 可以，`(long)box` 会抛 `InvalidCastException`。

**CP6 关联**：`PagedResultDto<T>`（下面会讲的泛型）之所以用泛型 `<T>` 而不是 `List<object>`，一大原因就是**避免装箱**——泛型让 `List<Stock>` 直接存 Stock，`List<int>` 直接存 int，不用装箱成 object。泛型是 C# 消除装箱的主力。

**坑**：老式非泛型集合 `ArrayList` 存 int 会疯狂装箱，性能差，现代代码一律用 `List<int>`。

### 3.6 Nullable<T>（可空值类型）原理

**问题**：`int` 是值类型，天生不能是 `null`（它总有个数值，默认 0）。但数据库里一个数字列可以是 NULL（表示"没填"）。怎么让 `int` 也能表达"空"？

**答案**：`Nullable<T>`，语法糖写作 `int?`、`decimal?`、`DateTime?`。

**原理**：`Nullable<T>` 是一个 `struct`，内部藏两个字段：

```csharp
public struct Nullable<T> where T : struct
{
    private bool hasValue;   // 有没有值
    private T value;         // 值本身（如果有）
}
```

- `decimal? UnitPrice = null` → `hasValue = false`。
- `decimal? UnitPrice = 5m` → `hasValue = true, value = 5`。

**常用操作**：

```csharp
decimal? price = stock.UnitPrice;
if (price.HasValue) { var v = price.Value; }   // 显式判断和取值
decimal safe = price ?? 0m;                    // ?? 空合并：没值就给 0
decimal x = price.GetValueOrDefault();         // 没值返回 default(decimal)=0
```

**CP6 证据**：Stock.cs 里 `decimal? UnitPrice`、`DateTime? ExpiryDate`、`DateTime? ReceiveDate`，BaseEntity.cs 里 `DateTime? ModifyDate`——凡是"可能没有"的字段都用 `?`。对比 `CreateDate`（`DateTime`，不可空，创建时必有值）。

**注意区分两种 `?`**：
- **值类型后的 `?`** = `Nullable<T>`，真的改变了类型（`int?` ≠ `int`）。
- **引用类型后的 `?`**（如 `string?`）= **可空引用类型注解**（C# 8，配合 `<Nullable>enable`），它**不改变运行时类型**，只是给编译器一个提示"这里可能为 null，帮我检查"。BaseEntity.cs 的 `string? Creator` 就是这种——csproj 里 `<Nullable>enable` 开了，所以编译器会警告你没判空就用它。

### 3.7 常见坑与踩坑实录

- **坑 1（最致命）：金额用了 double**。前面讲透了，对账必翻车。CP6 全程 decimal 就是防这个。
- **坑 2：`decimal` 字面量忘了 `m` 后缀**。写 `decimal d = 0.1;` 编译报错，因为 `0.1` 默认是 double，得写 `0.1m`。
- **坑 3：给引用类型做"复制"却只复制了引用**。`var b = a;`（a 是 class）后改 b 会连累 a。
- **坑 4：`int?` 直接参与运算不判空**。`int? a = null; int b = a + 1;` 编译不过（`a+1` 是 `int?`）；而 `a.Value` 在 null 时抛 `InvalidOperationException`。
- **坑 5：装箱在热路径上**。循环里反复把 int 装箱成 object，性能悄悄下降，用泛型避免。

### 3.8 面试怎么问 + 参考答案

**Q：值类型和引用类型的区别？各举例。**

> A：值类型变量直接存值，赋值和传参是复制值本身，一般在栈上，包括 int、decimal、bool、DateTime、Guid、struct、enum。引用类型变量存的是堆上对象的地址，赋值复制的是地址、两个变量指向同一对象，包括 class、string、数组、List。在 CP6 里 Stock 是 class 引用类型，所以两个变量指向同一个 Stock 时改一个另一个也变；而 PhysicalQty 是 decimal 值类型，复制就是独立一份。

**Q：为什么金额一定要用 decimal 不用 double？**

> A：double 用二进制浮点存小数，而十进制的 0.1 换成二进制是无限循环小数，只能存近似值，所以 0.1+0.2 会得到 0.30000000000000004，比较还不相等。金额、数量累积多次误差会放大，对账时和外部系统对不上，触发假差异告警甚至审计问题。decimal 用十进制精确存储，0.1 就是 0.1。我们 CP6 的库存数量、单价、金额全用 decimal，还用 `[Column(TypeName="decimal(21,8)")]` 精确指定数据库精度。

**Q：什么是装箱拆箱？有什么代价？**

> A：装箱是把值类型放进 object 引用类型的过程，会在堆上 new 一个盒子复制值，产生 GC 开销；拆箱是取回来，且必须转成原类型否则抛异常。热路径上频繁装箱会拖慢性能。C# 用泛型避免装箱——比如 List&lt;int&gt; 直接存 int 而不装箱成 object，CP6 的 PagedResultDto&lt;T&gt; 用泛型正是出于类型安全和避免装箱。

---

## 4. 类与对象：字段、属性、构造函数、静态与实例

### 4.1 概念讲解

- **类（class）** = 图纸；**对象（object / instance）** = 按图纸造出来的实物。`class Stock` 是图纸，`new Stock()` 造出一件库存对象。
- **字段（field）** = 类里直接声明的变量，存数据。
- **属性（property）** = 带 `get`/`set` 的"受控字段"，外部看着像字段，内部可以插逻辑（校验、通知）。C# 里**对外一律用属性，不裸露字段**。
- **方法（method）** = 类能做的动作。
- **构造函数（constructor）** = new 对象时自动跑的初始化方法。

### 4.2 属性：自动属性、表达式属性、init、required

**（a）自动属性（auto-property）**

CP6 里绝大多数是这种：

```csharp
// 文件：C:\CP6\CP6.Entity\BaseEntity.cs
public Guid Id { get; set; }
public string? Creator { get; set; }
```

`{ get; set; }` 是**自动属性**语法。编译器**背后自动帮你生成一个隐藏的私有字段**（叫"后备字段 backing field"，名字类似 `<Id>k__BackingField`）和标准的读写逻辑。你写一行，编译器展开成：

```csharp
// 编译器实际生成的等价代码（示意）：
private Guid <Id>k__BackingField;
public Guid Id
{
    get { return <Id>k__BackingField; }
    set { <Id>k__BackingField = value; }   // value 是 set 的隐含参数
}
```

**为什么不直接用 public 字段？** 因为属性给了你"未来插逻辑而不改调用方"的自由：哪天要在 set 里加校验，直接改属性体，外部 `stock.PhysicalQty = 5` 的写法一个字都不用动。字段做不到。

**（b）默认值初始化器**

```csharp
public DateTime CreateDate { get; set; } = DateTime.Now;      // BaseEntity.cs
public string WarehouseCd { get; set; } = string.Empty;      // Stock.cs
public bool RecallFlag { get; set; } = false;                // Stock.cs
public string OwnerType { get; set; } = StockOwnerType.Self; // Stock.cs
```

`= xxx` 在**对象创建时**赋初值。`CreateDate` 默认当前时间；`WarehouseCd` 默认空串（防 null）。

**（c）表达式属性（expression-bodied，只读计算属性）**

CP6 的 Stock 把 `AvailableQty` 物化成了真字段（因为 DB 要存），但如果只算不存，会写成表达式属性：

```csharp
// 假想写法（若不物化到 DB）：
public decimal AvailableQty => PhysicalQty - AllocatedQty;
```

`=>` 表示"每次读取时现算"，没有 set，是只读计算属性。CP6 的注释也点明了这个语义："PhysicalQty - AllocatedQty = AvailableQty（DB に物化、Service で都度算出）"——它选择两者都做。

**（d）`init` 访问器（C# 9）**

`init` 是"只在初始化时能写，之后只读"的 set：

```csharp
// 文件：C:\CP6\CP6.Entity\PiiFieldAttribute.cs
public PiiErase Mode { get; init; } = PiiErase.Placeholder;
```

`Mode` 可以在 new 时通过对象初始化器赋值（`new PiiFieldAttribute { Mode = PiiErase.Null }`），一旦对象建好就**不能再改**。它实现了"不可变对象"又保留了初始化便利，比 `readonly` 字段更灵活。

**（e）`required` 修饰符（C# 11）**

`required` 强制"创建对象时必须给这个属性赋值，否则编译报错"。CP6 的实体主要靠 `[Required]` 特性（那是 EF/校验层面的）和默认值来保证非空，`required` 关键字是纯 C# 编译期强制。示例：

```csharp
public class Demo
{
    public required string Code { get; set; }   // new Demo() 会报错，必须 new Demo { Code = "x" }
}
```

> **区分 `[Required]` 特性 vs `required` 关键字**：`[Required]`（Stock.cs 里到处是）是**运行时**给 EF Core / 模型校验看的元数据（"数据库这列 NOT NULL"、"表单这项必填"）；`required` 是 **C# 编译器**在你 new 对象时强制的语法。名字像，层次完全不同，面试爱挖这个。

### 4.3 构造函数（含 C# 12 主构造函数）

**默认无参构造**：CP6 的实体大多没写构造函数，C# 会自动送一个隐式的无参构造（`new Stock()` 能用就靠它）。EF Core 也要求实体有无参构造才能从数据库反序列化。

**传统构造函数**：

```csharp
public class Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency)   // 构造函数：类名同名、无返回类型
    {
        if (amount < 0) throw new ArgumentException("金额不能为负");
        Amount = amount;
        Currency = currency;
    }
}
```

**主构造函数（primary constructor，C# 12）**——参数直接写在类名后：

```csharp
public class StockService(CP6Context db, ILogger<StockService> logger)
{
    public void Foo() => logger.LogInformation("db has {n}", db.Stocks.Count());
    // db、logger 在整个类体内直接可用，无需手写字段赋值
}
```

主构造函数把"接收依赖 → 赋值给字段"这套样板浓缩成一行，Service 类做依赖注入（DI）时特别常用。CP6 是 .NET 8 项目，具备用主构造函数的条件（新写的 Service 常这么写）。

### 4.4 方法重载、静态 vs 实例、this、对象初始化器

**（a）静态成员——看 CP6 真代码**

```csharp
// 文件：C:\CP6\CP6.Entity\DomainModels\Wms\Stock.cs
public static class StockQcStatus
{
    public const string Pending = "PENDING";
    public const string Passed  = "PASSED";
    public const string Failed  = "FAILED";
    public const string Hold    = "HOLD";

    public static bool IsAllocatable(string status) =>
        status == Pending || status == Passed;
}
```

逐点解析：

- **`static class`**：静态类，**不能 new**，只当"工具箱 / 常量集合"用。`StockQcStatus` 就是把 QC 状态的字符串常量和一个判断方法集中收纳。
- **`const string Pending = "PENDING"`**：编译期常量，全局唯一、不可变。业务代码里到处 `if (s.QcStatus == StockQcStatus.Passed)`，比裸写字符串 `"PASSED"` 强太多——**防拼写错误**（写错常量名编译报错，写错字符串神不知鬼不觉）、**改一处生效全局**。
- **`static bool IsAllocatable(...)`**：静态方法，直接 `StockQcStatus.IsAllocatable(s.QcStatus)` 调用，不需要对象。这里封装了"什么状态的库存能被引当"的规则：PENDING 和 PASSED 能，FAILED 和 HOLD 不能。**把业务规则收敛到一处**，避免散落各地写重复的 `||` 判断。

**静态 vs 实例对比表**：

| | 实例成员（instance） | 静态成员（static） |
|--|--------------------|-------------------|
| 归属 | 每个对象各一份 | 全类共享一份 |
| 访问 | `stock.PhysicalQty` | `StockQcStatus.Pending` |
| 能否用 this | 能 | 不能（没有具体对象） |
| 典型用途 | 对象自己的数据/行为 | 工具方法、常量、计数器 |

**（b）`this` 关键字**：指"当前这个对象自己"。常用于构造函数里区分同名参数：`this.Amount = amount;`（左边是字段，右边是参数）。

**（c）对象初始化器（object initializer）**——CP6 测试和 seed 数据里高频：

```csharp
var stock = new Stock
{
    WarehouseCd = "WH01",
    LocationCd  = "A-01-01",
    ProductCd   = "P1000",
    LotNo       = "L20260715",
    PhysicalQty = 100m,
    QcStatus    = StockQcStatus.Passed
};
```

`new 类型 { 属性 = 值, ... }` 让你在 new 的同一句里给一堆属性赋值，无需写一个包含所有参数的构造函数。前提是这些属性有 `set` 或 `init`。

**（d）方法重载（overload）**：同名方法、参数列表不同（个数或类型），编译器按你传的实参选对应版本：

```csharp
decimal Round(decimal v) => Math.Round(v, 2);
decimal Round(decimal v, int digits) => Math.Round(v, digits);
// Round(1.234m) 走第一个，Round(1.234m, 3) 走第二个
```

注意：**只有返回类型不同不算重载**（编译报错），必须参数列表不同。

### 4.5 常见坑

- **坑：`const` 用来放会变的值**。`const` 是编译期烧死的，且**跨程序集有"版本陷阱"**——如果 A.dll 引用了 B.dll 的 const，改了 B 的 const 值但只重编 B、没重编 A，A 里还是旧值。要"可能变"的共享常量用 `static readonly`。
- **坑：给实体加了有参构造函数却没保留无参构造**。EF Core 物化实体需要无参构造，一旦你只写了有参构造，C# 就不再送隐式无参构造，EF 报错。
- **坑：属性 set 里写重逻辑/抛异常**。属性访问应"廉价无副作用"，复杂逻辑放方法。

### 4.6 面试怎么问 + 参考答案

**Q：C# 自动属性 `{ get; set; }` 编译器背后做了什么？**

> A：编译器会自动生成一个隐藏的私有后备字段，以及读它、写它的 get/set 逻辑，set 里有个隐含的 value 参数代表被赋的值。好处是对外暴露的是属性而非字段，将来要在 set 加校验或通知，改属性体即可，调用方代码不用动。CP6 实体如 BaseEntity 的 Id、Creator 全是自动属性。

**Q：`init` 和 `set` 有什么区别？`required` 又是什么？**

> A：set 随时能改；init 只允许在对象初始化阶段（构造函数或对象初始化器）赋值，之后只读，用来做不可变对象又不失初始化便利，CP6 的 PiiFieldAttribute.Mode 就用 init。required 是 C# 11 的编译期强制，标了它的属性 new 对象时必须赋值否则编译报错。注意 required 关键字和 [Required] 特性不是一回事，后者是给 EF/校验的运行时元数据。

**Q：静态类和普通类的区别？举个你项目里的例子。**

> A：静态类不能 new、不能有实例成员，只做工具箱和常量集合。CP6 的 StockQcStatus 就是静态类，用 const 定义 PENDING/PASSED/FAILED/HOLD 四个状态常量，还有静态方法 IsAllocatable 封装"哪些状态可引当"的规则，把业务规则收敛到一处、避免各处硬编码字符串出错。

---

## 5. 特性 Attribute：声明式元数据与反射

### 5.1 概念讲解

**特性（attribute）** 是"贴在代码上的标签"，本身**不执行任何逻辑**，只是把**元数据（metadata）**附加到类、属性、方法上，供别人（框架、你自己的代码）在运行时用**反射（reflection）** 读出来据此行事。

**类比**：特性就像贴在行李箱上的标签——"易碎"、"优先"、"重量 20kg"。行李箱（你的类/属性）本身不会因为贴了标签就变，但**分拣系统（框架）扫到"易碎"标签就轻拿轻放**。EF Core 扫到 `[Key]` 就知道"这是主键"，ASP.NET Core 扫到 `[HttpGet]` 就知道"这个方法处理 GET 请求"。

语法：写在 `[]` 里，放在目标上面或前面。多个可以合并 `[Required, MaxLength(10)]` 或分开写。

### 5.2 CP6 真实代码：特性的密集使用

Stock.cs 是特性教学的绝佳标本：

```csharp
// 文件：C:\CP6\CP6.Entity\DomainModels\Wms\Stock.cs
[Table("T_Stock")]                              // ① 类级：映射到数据库表 T_Stock
public class Stock : BaseBizEntity
{
    [Required, MaxLength(10)]                    // ② 非空 + 最长 10 字符
    public string WarehouseCd { get; set; } = string.Empty;

    [Column(TypeName = "decimal(21,8)")]         // ③ 指定数据库列精度
    public decimal PhysicalQty { get; set; } = 0m;
}
```

BaseEntity.cs 的主键定义：

```csharp
// 文件：C:\CP6\CP6.Entity\BaseEntity.cs
[Key]                                            // ④ 这是主键
[DatabaseGenerated(DatabaseGeneratedOption.Identity)]  // ⑤ 值由数据库自动生成
public Guid Id { get; set; }

[MaxLength(100)]                                 // ⑥ 最长 100
public string? Creator { get; set; }
```

BaseBizEntity.cs 的乐观锁：

```csharp
// 文件：C:\CP6\CP6.Entity\BaseBizEntity.cs
[Timestamp]                                      // ⑦ 乐观锁行版本列
public byte[]? RowVersion { get; set; }
```

### 5.3 常用特性全表

| 特性 | 命名空间/来源 | 贴在哪 | 作用 | CP6 出处 |
|------|--------------|--------|------|----------|
| `[Table("名")]` | DataAnnotations.Schema | 类 | 指定映射的数据库表名 | Stock→`T_Stock`、MasterBase→`M_Base` |
| `[Key]` | DataAnnotations | 属性 | 声明主键 | BaseEntity.Id |
| `[DatabaseGenerated(...)]` | DataAnnotations.Schema | 属性 | 值由 DB 生成（Identity/Computed/None） | BaseEntity.Id |
| `[Required]` | DataAnnotations | 属性 | 非空/必填（DB NOT NULL + 校验） | Stock.WarehouseCd |
| `[MaxLength(n)]` | DataAnnotations | 属性 | 最大长度（DB 列长 + 校验） | Creator=100、WarehouseCd=10 |
| `[Column(TypeName=...)]` | DataAnnotations.Schema | 属性 | 指定 DB 列类型/精度/名 | PhysicalQty=decimal(21,8) |
| `[Timestamp]` | DataAnnotations | 属性 | 乐观并发的行版本列 | BaseBizEntity.RowVersion |
| `[AttributeUsage(...)]` | System | 特性类 | 限定自定义特性能贴在哪 | PiiFieldAttribute |
| `[ApiController]` | AspNetCore.Mvc | 控制器类 | 标记 Web API 控制器（自动模型校验等） | WebApi 层控制器 |
| `[Route("api/[controller]")]` | AspNetCore.Mvc | 控制器/方法 | 指定 URL 路由 | WebApi 层 |
| `[HttpGet]`/`[HttpPost]`/`[HttpPut]`/`[HttpDelete]` | AspNetCore.Mvc | 方法 | 绑定 HTTP 动词 | WebApi 层 |
| `[Authorize]` | AspNetCore.Authorization | 控制器/方法 | 要求认证/授权 | WebApi 层 |
| `[AllowAnonymous]` | AspNetCore.Authorization | 方法 | 豁免认证 | 登录端点 |

> 记忆里提到的 `RequirePermission`（授权粒度收口）、`[Authorize]`、CSRF 豁免等都属于 WebApi 层的特性用法——控制器靠特性声明"谁能调、走哪个 URL、什么动词"。

### 5.4 自定义特性 + 反射：CP6 的 PII 标记

CP6 自己定义了一个特性，是学习自定义特性的完美标本：

```csharp
// 文件：C:\CP6\CP6.Entity\PiiFieldAttribute.cs
[AttributeUsage(AttributeTargets.Property)]      // 元特性：限定只能贴在"属性"上
public sealed class PiiFieldAttribute : Attribute // 自定义特性必须继承 Attribute
{
    public PiiErase Mode { get; init; } = PiiErase.Placeholder;
}

public enum PiiErase { Placeholder, Null }
```

以及配套的排除特性：

```csharp
// 文件：C:\CP6\CP6.Entity\AuditIgnoreAttribute.cs
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute { }
```

逐点解析：

- **`: Attribute`**：所有自定义特性都要继承 `System.Attribute`。
- **`[AttributeUsage(AttributeTargets.Property)]`**：这是"贴在特性上的特性"（元特性），限定 `PiiFieldAttribute` **只能贴在属性上**，贴到类上会编译报错。
- **`sealed`**：密封，不许别人继承这个特性（特性一般都 sealed，反射查找更快）。
- **`Mode { get; init; }`**：特性可以带**具名参数**，用的时候写 `[PiiField(Mode = PiiErase.Null)]`。
- **命名约定**：类名是 `PiiFieldAttribute`，但使用时可省略 `Attribute` 后缀，写 `[PiiField]` 即可——C# 编译器自动补全。

**它怎么被"读出来"用？靠反射。** 特性贴上去只是标签，真正干活的是别处的反射代码（在 CP6.Core 的 SaveChanges 管道里）。示意：

```csharp
// 概念示意（真实逻辑在 CP6Context 的写入管道）：
foreach (var prop in entity.GetType().GetProperties())
{
    // 反射：这个属性上有没有贴 [PiiField]？
    var pii = prop.GetCustomAttribute<PiiFieldAttribute>();
    if (pii is not null)
    {
        // 有！按 GDPR"被遗忘权"擦除：占位符替换或置 null
        if (pii.Mode == PiiErase.Null) prop.SetValue(entity, null);
        else prop.SetValue(entity, "***");
    }
}
```

- `entity.GetType()` 拿到运行时类型；`.GetProperties()` 列出所有属性；`.GetCustomAttribute<T>()` 查某属性上有没有贴 `T` 特性。**这就是反射**——运行时"读代码结构"。
- PiiFieldAttribute.cs 的注释也印证："数据主体擦除据此匿名化/置空、导出据此剔除密钥"。特性是"惰性标记"，消费逻辑在别处。

同理 `AuditIgnoreAttribute` 贴在 `IAuditable` 实体的敏感属性（如密码）上，字段级审计的反射管道扫到它就**跳过不记录**（见 AuditIgnoreAttribute.cs 原注释）。

### 5.5 常见坑

- **坑：以为特性会"自动生效"**。特性只是数据，**必须有反射代码去读它才有意义**。你自己写个特性贴上去，不写消费逻辑，等于贴了张没人看的标签。
- **坑：反射慢却在热路径反复调**。`GetCustomAttribute` 有开销，成熟框架会缓存反射结果（扫一次记下来）。
- **坑：`[MaxLength]` 只在 EF 建表和模型校验时生效**，不是运行时给字符串自动截断——超长会在存库时报错。

### 5.6 面试怎么问 + 参考答案

**Q：什么是特性（Attribute）？它和反射什么关系？**

> A：特性是贴在类、属性、方法上的声明式元数据，本身不执行逻辑，只是附加信息。真正让它起作用的是反射——运行时通过 GetType、GetProperties、GetCustomAttribute 读出这些标签再据此行事。比如 EF Core 靠 [Key]/[Table]/[Column] 反射出表结构，ASP.NET Core 靠 [HttpGet]/[Route] 反射出路由。CP6 里我们自定义了 [PiiField] 特性标记个人信息字段，SaveChanges 管道用反射扫到它就按 GDPR 被遗忘权擦除数据。

**Q：怎么自定义一个特性？**

> A：定义一个继承自 System.Attribute 的类，通常用 sealed，并用 [AttributeUsage] 元特性限定它能贴在哪（类/属性/方法）。可以加带 get/init 的属性作为具名参数。CP6 的 PiiFieldAttribute 就是继承 Attribute、sealed、[AttributeUsage(AttributeTargets.Property)]，带一个 Mode 属性控制擦除方式。定义完还要写反射代码去消费它才有实际效果。

---

## 6. 继承与多态：virtual / override / abstract / sealed

### 6.1 概念讲解

- **继承（inheritance）**：子类"白拿"父类的字段、属性、方法，还能加自己的。表达"是一个（is-a）"关系。`Stock is a BaseBizEntity`。
- **多态（polymorphism）**：同一个方法调用，运行时按对象**实际类型**执行不同版本。"同一句话，不同对象各自表演"。
- **抽象类（abstract class）**：不能 new、专门给人继承的"半成品"。
- **`virtual`/`override`**：父类方法标 `virtual`（允许被重写），子类用 `override` 提供自己的版本。
- **`sealed`**：封死，不许再继承/重写。

### 6.2 CP6 真实的三层继承链（教科书级标本）

CP6 的实体基类是一条清晰的继承链，我把三个文件串起来看：

```csharp
// 第 1 层 —— 文件：C:\CP6\CP6.Entity\BaseEntity.cs
public abstract class BaseEntity          // abstract：不能 new，只当基类
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }              // 每张表都有的主键
    [MaxLength(100)] public string? Creator { get; set; }    // 创建人
    public DateTime CreateDate { get; set; } = DateTime.Now; // 创建时间
    [MaxLength(100)] public string? Modifier { get; set; }   // 修改人
    public DateTime? ModifyDate { get; set; }                // 修改时间
}

// 第 2 层 —— 文件：C:\CP6\CP6.Entity\BaseTenantEntity.cs
public abstract class BaseTenantEntity : BaseEntity   // 继承 BaseEntity
{
    public Guid TenantId { get; set; }        // 多加一个：租户 ID（多租户隔离）
}

// 第 3 层 —— 文件：C:\CP6\CP6.Entity\BaseBizEntity.cs
public abstract class BaseBizEntity : BaseTenantEntity  // 继承 BaseTenantEntity
{
    public bool IsDeleted { get; set; } = false;    // 逻辑删除
    [Timestamp] public byte[]? RowVersion { get; set; }  // 乐观锁
}

// 第 4 层（具体业务实体）—— 文件：C:\CP6\CP6.Entity\DomainModels\Wms\Stock.cs
[Table("T_Stock")]
public class Stock : BaseBizEntity        // 继承整条链，白拿以上全部字段
{
    public string WarehouseCd { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; } = 0m;
    // ... 只写库存自己独有的字段
}
```

**继承链示意图**：

```
BaseEntity        (abstract)   →  Id, Creator, CreateDate, Modifier, ModifyDate
    ▲  继承并追加
BaseTenantEntity  (abstract)   →  + TenantId
    ▲  继承并追加
BaseBizEntity     (abstract)   →  + IsDeleted, RowVersion
    ▲  继承并追加
Stock  /  MasterBase  (具体)    →  + 各自业务字段
```

### 6.3 逐点解析：这套设计的精妙

- **`abstract class`（抽象类）**：BaseEntity/BaseTenantEntity/BaseBizEntity 全是 `abstract`——**你不能 `new BaseEntity()`**（没意义，它只是"公共字段收纳架"）。抽象类专门用来被继承。具体的 Stock、MasterBase 才是 `class`（可 new）。
- **字段"白拿"**：`Stock` 一行继承代码 `: BaseBizEntity`，就自动拥有了 Id、Creator、CreateDate、Modifier、ModifyDate（来自 BaseEntity）、TenantId（BaseTenantEntity）、IsDeleted、RowVersion（BaseBizEntity）——**8 个公共字段一个没写却全有**。这就是继承的威力：公共字段集中定义一次，几百张业务表全继承，改一处全生效。
- **为什么分三层而不是一层全塞？** 因为**不是每张表都需要全部字段**（这是 DRY 与精确建模的平衡）。BaseTenantEntity.cs 的注释讲得明明白白："纯字典/语言包/菜单结构等系统级共享表保持继承 BaseEntity（不带 TenantId）"。也就是：
  - 系统级共享表（如 `Sys_DictData`）→ 继承 `BaseEntity`（无需租户隔离，甚至不继承——见下）。
  - 需要按公司隔离的表 → 继承 `BaseTenantEntity`（多一个 TenantId）。
  - 业务表（ERP/MES/WMS）→ 继承 `BaseBizEntity`（还要逻辑删除 + 乐观锁）。
- **反例印证**：`Sys_DictData`（数据字典）我实读过，它**根本没继承任何基类**，自己定义 `int Id` 主键（不是 Guid）。因为字典是全系统共享的小表，不需要租户隔离、不需要逻辑删除，硬套基类反而累赘。这说明 CP6 团队**按需选基类**，不是无脑继承。

### 6.4 virtual / override / new / sealed / abstract 全套语义

CP6 实体类里方法重写用得少（实体是数据容器），但这是面试必考语法，用通用例子讲透：

| 关键字 | 用在 | 含义 | 一句话 |
|--------|------|------|--------|
| `virtual` | 父类方法 | 允许子类重写 | "这个方法可以被改写" |
| `override` | 子类方法 | 重写父类的 virtual/abstract 方法 | "我提供我的版本，且支持多态" |
| `abstract` | 父类方法/类 | 只声明不实现，强制子类实现 | "我只画了框，你必须填肉" |
| `new` | 子类方法 | 隐藏（而非重写）父类同名方法 | "重名但不是多态，按变量声明类型走" |
| `sealed` | 类/override 方法 | 禁止继续继承/重写 | "到此为止" |

**多态 vs 隐藏的关键区别（面试爱挖）**：

```csharp
public class Animal { public virtual string Speak() => "..."; }
public class Dog : Animal { public override string Speak() => "汪"; }  // override = 多态
public class Cat : Animal { public new string Speak() => "喵"; }       // new = 隐藏

Animal a = new Dog();
Animal c = new Cat();
a.Speak();  // "汪"  ← override：看【实际对象】是 Dog，多态生效
c.Speak();  // "..." ← new：看【变量声明类型】是 Animal，隐藏不多态！
((Cat)c).Speak();  // "喵" ← 变量类型变成 Cat 才走 new 的版本
```

`override` 看**对象实际类型**（运行时决定），`new` 看**变量声明类型**（编译时决定）。这就是多态与隐藏的分水岭。

**abstract 方法**：抽象类里可以有抽象方法，"只声明签名，子类必须 override 实现"：

```csharp
public abstract class Shape { public abstract decimal Area(); }  // 无方法体
public class Circle : Shape
{
    public decimal R { get; set; }
    public override decimal Area() => 3.14m * R * R;  // 必须实现，否则编译报错
}
```

### 6.5 多态的运行时机制：虚方法表（vtable）

**概念**：为什么 `override` 能在运行时按实际类型分派？靠**虚方法表（virtual method table，vtable）**。

- 每个有虚方法的类，编译器都建一张 vtable —— 一个"方法名 → 实际函数地址"的跳转表。
- `Dog` 的 vtable 里，`Speak` 那格指向 `Dog.Speak` 的代码；`Cat` 的 vtable 里指向 `Cat.Speak`。
- 每个对象头部藏一个指针指向它这个类的 vtable。
- 当你调 `a.Speak()`，运行时先看 `a` 指向的对象、找到它的 vtable、查 `Speak` 那格、跳过去执行。所以哪怕变量声明是 `Animal`，实际对象是 Dog 就走 Dog 的版本。

```
对象 a (实际是 Dog)                Dog 的 vtable
┌──────────────┐                 ┌────────────────────┐
│ vtable指针 ──┼────────────────►│ Speak → Dog.Speak() │
│ 其他字段...  │                 └────────────────────┘
└──────────────┘
调 a.Speak() → 顺着指针查表 → 跳 Dog.Speak → 输出"汪"
```

非虚方法（普通方法）没这层间接，编译时就绑死地址（"静态绑定"），所以更快但不多态。

### 6.6 里氏替换原则（LSP）与 is/as/模式匹配

**里氏替换原则（Liskov Substitution Principle）**：**任何用父类的地方，都能换成子类而不出错**。CP6 里 EF Core 的 `DbSet<BaseBizEntity>` 相关的过滤逻辑能统一处理所有子类实体，正是因为 Stock、MasterBase 都能安全"替换"BaseBizEntity 的位置——它们都保证有 TenantId、IsDeleted，全局查询过滤器可以一视同仁地加 `WHERE TenantId = @t AND IsDeleted = 0`。

**类型判断三件套**：

```csharp
object o = GetSomething();

// ① is —— 是不是某类型（返回 bool）
if (o is Stock) { ... }

// ② as —— 尝试转，失败返回 null（不抛异常）
Stock? s = o as Stock;
if (s != null) { ... }

// ③ 模式匹配（pattern matching，C# 7+）—— is + 声明变量，最常用
if (o is Stock stock)                 // 是 Stock 就转好赋给 stock
{
    Console.WriteLine(stock.PhysicalQty);
}

// switch 模式匹配（C# 8+）
string desc = o switch
{
    Stock s2      => $"库存 {s2.PhysicalQty}",
    MasterBase m  => $"据点 {m.BaseName}",
    null          => "空",
    _             => "未知"            // _ 是"其余情况"
};
```

对比 `(Stock)o` 强制转换：转失败会**抛 InvalidCastException**；`as` 转失败返回 null。要么用 `is` 先判断，要么用 `as` + 判空，别裸强转。

### 6.7 常见坑

- **坑：想 override 却漏写 virtual**。父类方法不加 `virtual`，子类没法 `override`（编译报错）。
- **坑：用 `new` 隐藏当成 override**。多态失效，通过基类引用调用还是父类版本，隐蔽 bug。
- **坑：抽象类当实例用**。`new BaseEntity()` 编译报错——它是 abstract。
- **坑：深继承链**。CP6 三层已是上限，继承太深难维护。现代倾向"组合优于继承"，但公共字段抽基类这种是继承的正当用法。

### 6.8 面试怎么问 + 参考答案

**Q：讲讲你项目里的继承体系。**

> A：CP6 实体有一条三层抽象基类链：BaseEntity 放每张表都有的 Id、创建人、创建时间等审计字段，是抽象类；BaseTenantEntity 继承它再加 TenantId 做多租户行级隔离；BaseBizEntity 再加 IsDeleted 逻辑删除和 RowVersion 乐观锁。具体业务实体如 Stock、MasterBase 继承 BaseBizEntity，一行继承就白拿八个公共字段。分三层是因为不是每张表都要全部字段——系统级共享表如数据字典就只继承 BaseEntity 甚至不继承，避免多余的 TenantId。

**Q：override 和 new 修饰方法有什么区别？**

> A：override 是真正的重写，支持多态，运行时按对象实际类型走，靠虚方法表分派；new 是隐藏父类同名方法，不多态，按变量的声明类型走。比如 Animal a = new Cat()，如果 Cat 用 new 隐藏 Speak，那 a.Speak() 走的还是 Animal 的版本，只有把 a 转成 Cat 才走 Cat 的。用 new 常常是无意中的 bug，想多态必须 override，且父类方法要标 virtual。

**Q：多态在运行时是怎么实现的？**

> A：靠虚方法表 vtable。每个有虚方法的类有一张表，记录方法到实际函数地址的映射，子类重写就把对应格子指向自己的实现。每个对象头部有指针指向所属类的 vtable。调用虚方法时运行时顺着对象的 vtable 指针查表跳转，所以按实际类型执行。非虚方法编译期就绑定地址，没有这层间接。

---

## 7. 接口：契约、显式实现、默认实现、标记接口

### 7.1 概念讲解

**接口（interface）** 是"只有方法/属性签名、没有实现的纯契约"。它规定"能做什么"，不管"怎么做"。谁实现了接口，就承诺提供这些成员。

**类比**：接口像"电源插座标准"。国标插座规定了孔的形状（契约），任何电器只要做成国标插头（实现接口）就能插上用，插座不关心电器内部怎么工作。你的代码依赖"插座标准"（接口）而非具体某台电器（具体类），换电器不用改插座——**面向接口编程**的核心价值：解耦。

**接口 vs 抽象类**：

| | 接口 interface | 抽象类 abstract class |
|--|---------------|----------------------|
| 能否多实现/多继承 | 一个类可实现**多个**接口 | 只能继承**一个**类 |
| 有无字段 | 不能有实例字段 | 可以有字段 | 
| 成员默认可见性 | public | 可各种修饰符 |
| 表达关系 | "能做什么（can-do）" | "是什么（is-a）" |
| 构造函数 | 无 | 有 |

一句话：**接口描述能力，抽象类描述身份**。CP6 里 `Stock` **是一个** BaseBizEntity（继承），同时**能被**数据权限过滤（实现 IDataScoped 的话）。

### 7.2 CP6 真实代码：标记接口模式

CP6 有两个漂亮的**标记接口（marker interface）** 标本：

**（a）空标记接口 IAuditable**

```csharp
// 文件：C:\CP6\CP6.Entity\IAuditable.cs
public interface IAuditable
{
}
```

逐点解析：

- **它是空的！没有任何成员。** 这叫"标记接口"——存在本身就是信息，不定义行为。
- **作用**（见原注释）："实体实现本接口即被 CP6Context.SaveChanges 写入管道纳入字段级 before/after 变更捕获。不实现则完全不参与审计（默认不审计，按需开启）。"
- **怎么用**：某实体想被字段级审计，就 `class Xxx : BaseBizEntity, IAuditable`。SaveChanges 管道用 `entity is IAuditable` 一判断，是就记审计日志，不是就跳过。**用类型系统表达"要不要审计"这个开关**，比加一个 bool 字段更干净——它是编译期的、零运行时成本、零数据库列（注释明说"本身不映射任何列"）。

**（b）有成员的接口 IDataScoped**

```csharp
// 文件：C:\CP6\CP6.Entity\IDataScoped.cs
public interface IDataScoped
{
    /// <summary>创建人（登录名）—— "本人" 范围比对。</summary>
    string? Creator { get; }

    /// <summary>归属部门 → Sys_Dept.Id —— "本部门 / 及下级 / 自定义" 范围比对。</summary>
    Guid? DeptId { get; }
}
```

逐点解析：

- **只声明了两个属性的 `get`**，没有实现。任何实现 `IDataScoped` 的实体必须提供 `Creator` 和 `DeptId`。
- **注意只有 `get` 没有 `set`**：接口只要求"能读出这两个值"，不管你内部怎么存。
- **作用**（见原注释）："业务实体实现后即可被 IDataScopeFilter 注入范围过滤。"即：数据权限系统（"你只能看本人/本部门的数据"）靠这个接口统一识别"哪些实体能做范围过滤、从哪两个字段过滤"。
- **精妙点**：注释说 "Creator 来自 BaseEntity；DeptId 需业务实体自补"。因为 BaseEntity 已经有 `Creator` 属性，实体继承基类就自动满足了接口的 `Creator` 契约；而 `DeptId` 基类没有，实现接口的实体要自己加一个 DeptId 属性。**接口契约可以由继承链上任意位置提供的成员来满足**——这是接口和继承配合的巧妙之处。

### 7.3 显式接口实现（explicit interface implementation）

当一个类实现的两个接口有**同名成员**，或你想"隐藏"接口成员只在转成接口时才可见，用显式实现：

```csharp
public interface ILogger { void Log(string m); }
public interface IAuditor { void Log(string m); }

public class Service : ILogger, IAuditor
{
    void ILogger.Log(string m)  { /* 给 ILogger 的版本 */ }   // 显式实现
    void IAuditor.Log(string m) { /* 给 IAuditor 的版本 */ }  // 显式实现
}

var s = new Service();
// s.Log("x");                 // 编译错误！显式实现不能通过类实例直接调
((ILogger)s).Log("x");         // 必须转成对应接口才能调
```

特点：显式实现的成员**不能加访问修饰符**（隐含 public 但只对接口可见），必须通过接口引用调用。CP6 实体接口都是隐式实现（`Creator` 直接是 public 属性，同时满足接口），因为不存在冲突。

### 7.4 默认接口实现（default interface method，C# 8）

C# 8 起，接口的方法**可以带默认实现**（打破了"接口不能有实现"的老规矩）：

```csharp
public interface INotifier
{
    void Send(string msg);
    void SendUrgent(string msg) => Send("【紧急】" + msg);  // 默认实现！
}
```

实现类只需实现 `Send`，`SendUrgent` 可以不写、直接用默认版。好处：给老接口加新方法时**不破坏已有实现类**（不然所有实现类都得改）。CP6 的接口目前保持"纯契约"风格（IAuditable/IDataScoped 都不带实现），但你要知道这个能力，面试可能问"C# 8 接口有什么新特性"。

### 7.5 常见坑

- **坑：接口不能有实例字段**。想在接口里存数据不行，接口只有方法/属性/事件签名（属性背后没字段）。
- **坑：实现接口漏了成员**。类声明 `: IDataScoped` 却没提供 `DeptId`，编译报错。
- **坑：把标记接口当有行为的接口**。IAuditable 是空的，别指望它"自己会审计"——审计逻辑在 SaveChanges 管道的反射/类型判断里。
- **坑：接口成员默认 public，别手贱加 private**。接口里写修饰符（除 C# 8 默认实现相关）会报错。

### 7.6 面试怎么问 + 参考答案

**Q：接口和抽象类怎么选？**

> A：接口描述"能做什么"的能力契约，一个类能实现多个接口，没有字段和构造函数；抽象类描述"是什么"的身份，只能单继承，可以有字段、构造函数和部分实现。经验法则：想给不相关的类赋予共同能力用接口（比如 CP6 的 IDataScoped 让任意实体获得数据权限过滤能力），想共享公共状态和骨架用抽象类（比如 BaseBizEntity 抽公共字段）。CP6 里两者配合——Stock 继承 BaseBizEntity 获得身份和公共字段，可以再实现接口获得额外能力。

**Q：什么是标记接口？举个例子。**

> A：标记接口是没有任何成员的空接口，靠"实现了它"这个事实本身传递信息。CP6 的 IAuditable 就是空接口，实体实现它就被 SaveChanges 管道纳入字段级审计，不实现就默认不审计。好处是用类型系统表达一个开关，编译期确定、零运行时成本、不占数据库列，比加个 bool 字段更干净。判断时一句 entity is IAuditable 即可。

**Q：C# 8 接口有什么新能力？**

> A：默认接口实现，接口方法可以带默认方法体。主要价值是给已发布的接口加新方法时不破坏所有现有实现类——它们自动继承默认实现，需要时再各自重写。

---

## 8. 封装与访问修饰符全表

### 8.1 概念讲解

**封装（encapsulation）**：把数据和操作数据的逻辑包在类里，对外只暴露必要的、受控的接口，隐藏内部细节。**类比**：ATM 机——你只能通过按钮界面（public 方法）取钱，摸不到里面的现金盒和线路（private 实现）。好处：内部实现可以随便改，只要对外接口不变，用的人不受影响。

**访问修饰符（access modifier）** 就是控制"谁能看见这个成员"的门禁级别。

### 8.2 访问修饰符全表

| 修饰符 | 可见范围 | 类比 | 典型用途 |
|--------|----------|------|----------|
| `public` | 任何地方，任何项目 | 大门敞开 | 对外 API、实体属性（Stock.PhysicalQty） |
| `private` | **仅本类内部** | 私人保险箱 | 后备字段、内部辅助方法（默认修饰符） |
| `protected` | 本类 + 所有子类 | 家族传承 | 给子类用但不对外的成员 |
| `internal` | **仅同一程序集（项目）内** | 公司内部通行 | 项目内共享、不想暴露给引用方 |
| `protected internal` | 同程序集 **或** 任何子类（并集，更宽） | 内部人 + 家族 | 少见，跨项目子类扩展 |
| `private protected` | 同程序集 **且** 是子类（交集，更窄，C# 7.2） | 内部的家族成员 | 严格限制的子类扩展 |
| `file` | **仅同一源文件内**（C# 11） | 一个房间内 | 源生成器/避免同项目命名污染 |

**范围从宽到窄排序**：`public` > `protected internal` > `internal` / `protected`（两者不可比，管的维度不同）> `private protected` > `private` > `file`。

**记忆要点**：
- **不写修饰符时的默认值**：类的成员默认 `private`；顶层类默认 `internal`。所以 Stock 的属性如果都不写 `public` 就全是 private，外面读不到——CP6 实体属性全显式 `public` 是必须的。
- `internal` 管**项目边界**：CP6.Core 里标 `internal` 的类型，CP6.WebApi **看不见**（不同程序集），但 CP6.Core 内部随便用。这是控制"哪些是对外公开 API、哪些是项目内部实现"的关键。
- `protected` 管**继承边界**：给子类留的后门。

### 8.3 CP6 关联

CP6 实体属性清一色 `public`（Stock、BaseEntity 全是），因为：
1. EF Core 需要 public 属性来读写映射数据库列。
2. 要序列化成 JSON 返给前端（Vue），非 public 序列化不到。

而 `PiiFieldAttribute`、`AuditIgnoreAttribute` 是 `public sealed class`——public 因为要被各项目的实体引用，sealed 封死继承。

CP6 的 Service 类（在 CP6.Core）内部辅助方法常是 `private`，只暴露 public 的业务方法给控制器调。这就是分层 + 封装的实践：**WebApi 只能碰 Core 暴露的 public 契约，碰不到 private 实现细节**。

**`const` 的可见性**：Stock.cs 里 `public const string Pending = "PENDING"`——public 让业务代码到处引用；const 隐含 static。

### 8.4 常见坑

- **坑：该 private 的字段写成 public**。破坏封装，外部直接改内部状态，绕过校验。永远"字段 private、属性 public"。
- **坑：以为 `internal` 是 private**。internal 是"项目内公开"，同项目其他类都能用，只是跨项目不行。
- **坑：`protected` 成员以为外部能用**。只有子类能用，外部实例访问不到。
- **坑：混淆 `protected internal`（并集，更宽）和 `private protected`（交集，更窄）**。前者"同项目**或**子类都行"，后者"同项目**且**子类才行"。

### 8.5 面试怎么问 + 参考答案

**Q：C# 有哪些访问修饰符？internal 和 private 区别？**

> A：public 全局可见；private 仅本类内；protected 本类加子类；internal 仅同一程序集（项目）内；protected internal 是同项目或子类的并集；private protected 是同项目且子类的交集；file 是 C# 11 的仅本文件内。internal 管项目边界——同项目其他类都能访问，但引用了这个项目的别的项目看不见，用来区分对外公开 API 和项目内部实现；private 只有定义它的那个类自己能访问。CP6 里 Core 项目的内部辅助类型可以标 internal，对 WebApi 隐藏，实体属性则必须 public 供 EF 映射和 JSON 序列化。

**Q：什么是封装？为什么属性优于公共字段？**

> A：封装是把数据和逻辑包进类、对外只给受控接口、隐藏内部实现。属性优于公共字段是因为属性通过 get/set 提供了拦截点——将来要加校验、日志、变更通知，改属性体就行，外部 stock.PhysicalQty = 5 的调用方式一字不改；而公共字段是裸露的内存位置，一旦要加逻辑就得改所有调用方。所以 C# 惯例是字段 private 做存储、属性 public 做对外访问。

---

## 9. object 基类：Equals / GetHashCode / ToString

### 9.1 概念讲解

**`object`（System.Object）是所有类型的祖宗**——包括值类型（通过装箱）和引用类型，一切都最终继承自 object。它给每个对象白送四个虚方法：

| 方法 | 默认行为 | 你可能要重写它当 |
|------|----------|-----------------|
| `Equals(object)` | 引用类型默认比**引用地址**（是不是同一个对象） | 想按**内容**比相等 |
| `GetHashCode()` | 基于对象标识生成哈希码 | 重写了 Equals 就**必须**跟着重写它 |
| `ToString()` | 返回类型全名（如 `CP6.Entity.DomainModels.Wms.Stock`） | 想输出有意义的文本（调试/日志） |
| `GetType()` | 返回运行时类型（不可重写） | 反射用 |

**默认相等语义**：两个 `class` 对象，`Equals` 和 `==` 默认比的是**引用**——只有指向同一块堆内存才算相等。所以 `new Stock{...}` 和另一个字段完全一样的 `new Stock{...}`，默认 `Equals` 返回 **false**（不是同一个对象）。

```csharp
var s1 = new Stock { ProductCd = "P1", PhysicalQty = 10m };
var s2 = new Stock { ProductCd = "P1", PhysicalQty = 10m };
Console.WriteLine(s1.Equals(s2));   // False —— 内容一样，但是两个不同对象
Console.WriteLine(ReferenceEquals(s1, s2)); // False —— 明确比引用
var s3 = s1;
Console.WriteLine(s1.Equals(s3));   // True —— 同一个对象
Console.WriteLine(ReferenceEquals(s1, s3)); // True
```

`ReferenceEquals(a, b)` 是 object 的静态方法，**永远比引用地址**，不受 Equals 重写影响，用来明确问"是不是同一个对象"。

### 9.2 为什么重写 Equals 必须重写 GetHashCode？（面试必考）

**核心规约**：**两个对象若 Equals 相等，它们的 GetHashCode 必须相等。**

**为什么？** 因为 `Dictionary`、`HashSet` 这些基于哈希的集合，查找分两步：
1. 先用 `GetHashCode()` 算哈希，定位到某个"桶（bucket）"。
2. 再在桶内用 `Equals` 逐个精确比对。

如果你只重写了 `Equals`（让内容相同的对象相等）却没重写 `GetHashCode`（还用默认的按引用哈希），会发生**灾难**：

```
两个内容相同的 key，Equals 说它们相等（你以为是同一个 key），
但 GetHashCode 不同（默认按引用），
→ 它们被分到不同的桶里
→ 你用 key2 去 Dictionary 查 key1 存的值，哈希算出去了另一个桶，根本找不到！
→ 明明"相等"的 key 却查不到，甚至同一个逻辑 key 存进去两份。
```

反过来，两个不相等的对象**允许**哈希相同（叫"哈希冲突"，靠桶内 Equals 兜底），但相等的对象**绝不能**哈希不同。所以规约是单向强制：**Equals 相等 ⟹ 哈希必相等**。

**正确的成对重写**：

```csharp
public class ProductKey
{
    public string ProductCd { get; }
    public string LotNo { get; }
    public ProductKey(string p, string l) { ProductCd = p; LotNo = l; }

    public override bool Equals(object? obj) =>
        obj is ProductKey o && o.ProductCd == ProductCd && o.LotNo == LotNo;

    public override int GetHashCode() =>
        HashCode.Combine(ProductCd, LotNo);   // .NET 提供的组合哈希工具，最省事

    public override string ToString() => $"{ProductCd}/{LotNo}";
}
```

`HashCode.Combine(...)` 是 .NET 内置的、把多个字段揉成一个哈希码的标准工具，别自己手写 `x * 31 + y` 那套容易错。

### 9.3 CP6 关联

CP6 的实体（Stock 等）**没有重写 Equals/GetHashCode**——这是**正确的选择**。因为：
- 实体的相等性由**数据库主键 Id（Guid）** 决定，不是内存内容。EF Core 用主键跟踪实体，靠的是它自己的变更跟踪器（change tracker）和主键，不依赖你重写 Equals。
- 实体是"可变的、有身份的对象"，天然是"引用相等"语义（两条不同的库存记录即使字段暂时一样，也是两个不同实体）。

**那什么时候在 CP6 里需要重写？** 当你要拿"业务键"当字典 key 或去重时。比如 Stock 的业务唯一键是 `(WarehouseCd, LocationCd, ProductCd, LotNo)`（见 Stock.cs 注释 "業務 UK"），如果要在内存里按这个四元组做 `HashSet` 去重，就该封装一个重写了 Equals/GetHashCode 的 key 类型（像上面 ProductKey）。而 `record`（第 10 节）能自动帮你干这事，正是 DTO/值对象的首选。

**ToString 的价值**：CP6 调试和日志里，如果实体重写了 ToString（或用 record 自动生成），日志能打印 `库存 P1000/L20260715` 而非 `CP6.Entity.DomainModels.Wms.Stock`，排查问题事半功倍。

### 9.4 常见坑

- **坑（经典）：只重写 Equals 不重写 GetHashCode**。放进 HashSet/Dictionary 就诡异丢失、重复。编译器会警告，别忽略。
- **坑：GetHashCode 用了可变字段**。对象进了 HashSet 后又改了参与哈希的字段，哈希变了，永远找不回来。**参与哈希的字段应不可变**（所以 record/值对象常用 init/只读）。
- **坑：用 `==` 比较对象内容**。引用类型 `==` 默认比引用（string 例外，它重载了 ==）。要比内容用重写的 Equals 或 record。
- **坑：拿实体当字典 key**。实体是引用相等，不同实例查不到，要用主键或业务键。

### 9.5 面试怎么问 + 参考答案

**Q：为什么重写 Equals 必须重写 GetHashCode？**

> A：因为 Dictionary、HashSet 靠哈希码定位桶、再用 Equals 在桶内精确比对。规约是 Equals 相等的对象哈希码必须相等。如果只重写 Equals 让内容相同的对象相等，却不重写 GetHashCode，默认还是按引用算哈希，两个"相等"的对象会被分到不同桶，导致用一个去查另一个存的值时定位到错误的桶而查不到，甚至同一逻辑 key 存进去两份。反过来不相等的对象允许哈希冲突，靠 Equals 兜底。所以是单向强制：相等就必须同哈希。

**Q：你项目里的实体重写 Equals 了吗？为什么？**

> A：没有，这是对的。CP6 实体的身份由数据库主键 Id 决定，EF Core 用自己的变更跟踪器和主键跟踪实体，不依赖 Equals；而且实体是可变、有身份的对象，天然就该是引用相等——两条库存记录哪怕字段暂时一样也是两个不同实体。只有当我要用业务键去重或当字典 key 时，才会封装一个重写了 Equals/GetHashCode 的 key 类型，或者直接用 record 自动获得值相等。

**Q：ReferenceEquals 和 == 有什么区别？**

> A：ReferenceEquals 是 object 的静态方法，永远比较两个引用是不是指向同一对象，不受任何重写或运算符重载影响；== 对引用类型默认也比引用，但可以被重载——比如 string 重载了 == 比较字符内容，record 也生成了值比较的 ==。所以想明确判断"是不是同一个对象"，用 ReferenceEquals 最稳。

---

## 10. record 与 struct：值语义、with 表达式、如何选型

### 10.1 概念讲解

C# 里"自定义类型"其实有三种模具：

- **`class`**（引用类型）：默认选择，堆上分配，引用相等，可变。CP6 的实体、Service、DTO 大多是 class。
- **`struct`**（值类型）：栈上/内联分配，值相等，复制传递。小而不可变的数据（坐标、货币值）用它。
- **`record`**（C# 9，可以是 record class 或 record struct）：**为"值语义数据载体"量身定做**，编译器自动生成一堆样板（值相等、ToString、解构、with）。

### 10.2 record：编译器帮你生成什么

看这个简洁定义：

```csharp
public record Money(decimal Amount, string Currency);
```

**一行**背后，编译器自动生成了：

1. 两个 `init` 只读属性 `Amount`、`Currency`（位置参数自动变属性）。
2. **值相等的 Equals 和 GetHashCode**——两个 record 所有字段都相等就 `Equals` 返回 true（不再是引用相等！）。
3. **漂亮的 ToString**——输出 `Money { Amount = 100, Currency = JPY }`，调试神器。
4. **解构器 Deconstruct**——`var (amt, cur) = money;` 一把拆出来。
5. **`with` 表达式支持**——复制并改几个字段。
6. `==` / `!=` 运算符（值比较）。

**值相等演示**：

```csharp
var a = new Money(100m, "JPY");
var b = new Money(100m, "JPY");
Console.WriteLine(a == b);        // True！ record 是值相等，字段全同即相等
Console.WriteLine(a.Equals(b));   // True
// 对比 class：同样内容的两个 class 对象 == 是 False（引用相等）
```

这正好解决第 9 节的痛点——record **自动**正确成对生成 Equals + GetHashCode，你不用手写、不会漏。

### 10.3 with 表达式（非破坏性修改）

record 默认不可变（init 属性）。想"改"一个字段怎么办？不是改原对象，而是**复制一个新的、只改指定字段**：

```csharp
var jpy = new Money(100m, "JPY");
var usd = jpy with { Currency = "USD" };   // 复制 jpy，只把 Currency 换成 USD
// jpy 仍是 (100, JPY) 没变；usd 是 (100, USD)
```

`with` 是"非破坏性变更（non-destructive mutation）"——原对象纹丝不动，得到一个新对象。这对**不可变数据流**极友好：多线程共享不用担心被改、状态可追溯。CP6 里做汇率快照、价格版本、审计前后值对比这类"拍快照"场景，record + with 是理想工具。

### 10.4 struct 与 readonly struct

**`struct`** 是值类型，赋值/传参**整体复制**，无堆分配、无 GC 压力，适合**小的、不可变的**数据。CP6 里你天天用的 `decimal`、`DateTime`、`Guid`、`int` 全是 struct（.NET 内置 struct）。`Nullable<T>`（第 3.6 节）也是 struct。

**`readonly struct`**（C# 7.2）：整个 struct 不可变，所有字段只读，编译器能做更多优化（避免防御性复制）：

```csharp
public readonly struct Ratio
{
    public decimal Numerator { get; }
    public decimal Denominator { get; }
    public Ratio(decimal n, decimal d) { Numerator = n; Denominator = d; }
    public decimal Value => Numerator / Denominator;
}
```

**`record struct`**（C# 10）：值类型 + record 的自动值语义。`public record struct Point(int X, int Y);` ——既是 struct（栈上、复制），又白拿 Equals/ToString/解构。

### 10.5 CP6 关联：DTO、实体、值对象的选型

CP6 的 DTO 目前多是 `class`（如 `OrderDto`、`PagedResultDto<T>`）。看 PagedResultDto：

```csharp
// 文件：C:\CP6\CP6.Entity\DTOs\Mes\PagedResultDto.cs
public class PagedResultDto<T>
{
    public int Total { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}
```

它用 `class` + `{ get; set; }`（可变）是合理的：DTO 要被 JSON 反序列化器逐属性填充，需要可写的 set。**如果**这个 DTO 是"传进来就不该改的只读快照"，用 `record` 会更贴切（自动值相等、不可变、ToString 友好）。这是团队的风格取舍，两者都对。

**它还是个泛型 `<T>`**：`PagedResultDto<Stock>`、`PagedResultDto<OrderListItemDto>` 复用同一套分页结构，`Items` 直接存 `List<T>` 不装箱（呼应第 3.5 节）。这就是**泛型（generics）** 的价值——一套代码适配多种类型，且类型安全、无装箱。

**选型决策表**：

| 场景 | 选什么 | 理由 | CP6 例子 |
|------|--------|------|----------|
| EF 实体（有身份、可变、要跟踪） | `class` | 引用相等、EF 用主键跟踪 | Stock、MasterBase |
| 可变 DTO（要被反序列化填充） | `class` + get/set | 需要可写 | PagedResultDto、OrderDto |
| 不可变数据载体、值相等、要去重/当 key | `record`（class） | 自动值语义 + with | 价格快照、汇率、事件 |
| 小的不可变值（≤16 字节左右）、高频、避免 GC | `struct` / `readonly struct` | 栈上、无堆分配 | 坐标、比率、货币值对象 |
| 小值 + 想要值语义样板 | `record struct` | 两者兼得 | 二维点、区间 |

**经验法则**：
- 有"身份"、要跟踪、会变 → **class**（实体）。
- "值本身就是身份"、不该变、要比较/去重 → **record**（DTO/值对象）。
- 小、不可变、性能敏感、量大 → **struct**。
- 拿不准 → 先 class，有明确值语义或性能需求再换。

### 10.6 常见坑

- **坑：大 struct 到处复制**。struct 传参是整体复制，字段一多（比如 20 个字段）复制成本超过 class 的引用传递。经验：struct 控制在约 16 字节内、字段少。
- **坑：可变 struct**。struct 可变会有反直觉的复制陷阱（改的是副本不是原件），所以 struct 尽量 `readonly`。
- **坑：record 里放可变引用字段**。`record Foo(List<int> Items)`，两个 record 的 Items 指向不同 list 时值相等判断可能不如预期（list 的 Equals 是引用相等）。record 的自动相等对引用字段是浅比较。
- **坑：把 record 当实体用于 EF**。record 的 init 不可变性和 EF 的可变跟踪有摩擦，实体一般还是用 class。
- **坑：以为 record 一定是引用类型**。`record class`（默认）是引用类型，`record struct` 是值类型，别混。

### 10.7 面试怎么问 + 参考答案

**Q：record 和 class 有什么区别？什么时候用 record？**

> A：record 默认是引用类型（record class），但编译器自动生成值相等的 Equals/GetHashCode、友好的 ToString、解构器和 with 表达式，默认属性是 init 只读，语义上是"不可变的值载体"。class 默认引用相等、可变。当我需要一个不可变的、按内容比较相等的数据对象——比如价格快照、汇率记录、领域事件、要拿来当字典 key 或去重的值对象——就用 record，它自动正确成对生成 Equals 和 GetHashCode，避免手写出错。而有身份、要被 EF 跟踪、会变的实体，仍然用 class，CP6 的 Stock、OrderDto 就是 class。

**Q：什么是 with 表达式？**

> A：with 是 record 的非破坏性修改语法，jpy with { Currency = "USD" } 会复制原 record、只替换指定字段、返回一个新对象，原对象不变。适合不可变数据流——比如拍快照、生成价格的新版本，既保留旧值又得到改动后的新值，多线程共享也安全。

**Q：struct 和 class 怎么选？**

> A：class 是引用类型、堆分配、引用相等，用于有身份、可变、较大的对象；struct 是值类型、复制传递、无堆分配无 GC 压力、值相等，用于小的、不可变的数据。经验是 struct 控制在 16 字节左右、字段少、尽量 readonly，否则频繁复制反而比 class 慢。CP6 里 decimal、DateTime、Guid 这些内置类型都是 struct，我们的实体和 DTO 是 class。

---

## 11. 本章面试题 15 问（含参考答案）

**1. C# 代码从源码到运行经历了哪些步骤？**
> Roslyn 编译成 IL 中间语言 + 元数据存进 dll/exe；运行时 CLR 加载，JIT 在方法首次调用时把 IL 编译成本机机器码并缓存；内存由 GC 分代自动回收。两段式编译让同一份 C# 跨平台运行。CP6 是 .NET 8，能打包 Docker 跑 Linux。

**2. .NET Framework、Core、5+ 的关系？**
> Framework（2002，仅 Windows，4.8 终点）→ Core（2016，跨平台开源高性能，支持容器）→ .NET 5+（2020 起 Core 改名，跳过 4，一年一版，8 是当前 LTS）。CP6 用 net8.0。

**3. 值类型和引用类型的区别？内存上如何体现？**
> 值类型直接存值、赋值复制值本身、一般在栈；引用类型存堆对象的地址、赋值复制地址、两变量共享同一对象。int/decimal/DateTime/Guid/struct 是值类型，class/string/数组/List 是引用类型。CP6 的 Stock 是 class，复制变量会共享同一对象；PhysicalQty 是 decimal，复制是独立一份。

**4. 为什么金额必须用 decimal？给数学解释。**
> double 用二进制浮点，十进制 0.1 换成二进制是无限循环小数只能存近似值，导致 0.1+0.2=0.30000000000000004 且不等于 0.3；累积误差在对账时和外部系统对不上，触发假差异告警和审计问题。decimal 十进制精确存储。CP6 所有数量单价金额都是 decimal，还用 [Column(TypeName="decimal(21,8)")] 定精度。

**5. 装箱拆箱是什么？如何避免？**
> 装箱是把值类型放进 object 引用类型，会在堆 new 盒子复制值，有 GC 开销；拆箱取回且必须转原类型否则抛异常。用泛型避免——List&lt;int&gt; 直接存 int 不装箱。CP6 的 PagedResultDto&lt;T&gt; 用泛型正是类型安全 + 避免装箱。

**6. 自动属性背后编译器生成了什么？init 和 required 呢？**
> 自动属性生成隐藏的私有后备字段和标准 get/set。init 只允许初始化阶段赋值之后只读（CP6 的 PiiFieldAttribute.Mode 用 init）；required 是 C# 11 编译期强制 new 时必须赋值。注意 required 关键字 ≠ [Required] 特性，后者是运行时给 EF/校验的元数据。

**7. 什么是特性？和反射什么关系？**
> 特性是贴在代码上的声明式元数据，本身不执行逻辑，靠反射（GetType/GetProperties/GetCustomAttribute）在运行时读出来据此行事。EF 靠 [Key]/[Table]/[Column] 反射表结构。CP6 自定义 [PiiField] 标记个人信息字段，SaveChanges 管道反射扫到就按 GDPR 擦除。

**8. 讲讲 CP6 的实体继承链。为什么分三层？**
> BaseEntity（抽象，Id + 审计字段）→ BaseTenantEntity（+ TenantId 多租户隔离）→ BaseBizEntity（+ IsDeleted 逻辑删除 + RowVersion 乐观锁）→ 具体实体如 Stock。分三层是因为不是每张表都要全部字段——系统级共享表如数据字典只继承 BaseEntity 或不继承，避免多余 TenantId。三个基类都 abstract 不能 new，只做公共字段收纳。

**9. override 和 new 的区别？多态如何实现？**
> override 真正重写、支持多态、按对象实际类型走、靠虚方法表 vtable 分派；new 是隐藏、不多态、按变量声明类型走。父类方法要 virtual 才能 override。vtable 是类的"方法名→函数地址"表，对象头有指针指向它，调虚方法时查表跳转。

**10. 接口和抽象类怎么选？**
> 接口描述"能做什么"能力、可多实现、无字段无构造；抽象类描述"是什么"身份、单继承、可有字段和部分实现。CP6 的 IDataScoped 让任意实体获得数据权限过滤能力（接口），BaseBizEntity 抽公共字段（抽象类）。

**11. 什么是标记接口？CP6 哪里用了？**
> 空接口，靠"实现了它"本身传递信息。CP6 的 IAuditable 是空接口，实体实现它就被 SaveChanges 纳入字段级审计，不实现默认不审计。零运行时成本、不占数据库列，判断只需 entity is IAuditable。

**12. 访问修饰符全讲一遍，internal 有什么用？**
> public 全局 / private 仅本类 / protected 本类加子类 / internal 仅同项目 / protected internal 同项目或子类（并集）/ private protected 同项目且子类（交集）/ file 仅本文件。internal 管项目边界，区分对外 API 和项目内部实现——CP6.Core 的内部类型可标 internal 对 WebApi 隐藏，实体属性必须 public 供 EF 映射和 JSON 序列化。

**13. 为什么重写 Equals 必须重写 GetHashCode？**
> Dictionary/HashSet 先用哈希码定位桶再用 Equals 精确比对。规约是 Equals 相等则哈希必相等。只重写 Equals 不重写 GetHashCode，内容相同的对象会被分到不同桶，导致查不到或重复存。用 HashCode.Combine 成对正确实现。CP6 实体不重写（身份靠主键 Id），要业务键去重时用 record 或封装 key 类型。

**14. record 和 class 区别？with 表达式是什么？**
> record 默认引用类型但自动生成值相等 Equals/GetHashCode、ToString、解构、with，属性默认 init 不可变，是"不可变值载体"。用于快照、汇率、事件、值对象。with 是非破坏性修改，复制并改指定字段返回新对象、原对象不变。实体仍用 class（有身份、可变、EF 跟踪）。

**15. struct 和 class 怎么选？readonly struct 有什么好处？**
> class 引用类型堆分配引用相等用于有身份可变较大对象；struct 值类型复制传递无堆分配无 GC 压力值相等用于小的不可变数据（控制 16 字节内、字段少、尽量 readonly）。readonly struct 整体不可变，编译器能省去防御性复制、优化更多。decimal/DateTime/Guid 都是内置 struct。

---

## 12. 自测清单

在纸上/嘴上过一遍，卡住的回去重读对应小节：

- [ ] 能画出 C# 源码 → IL → 机器码的两段式编译图，并说清 CLR/JIT/GC 各是什么。
- [ ] 能说清 .NET Framework / Core / 5+ 的演进和 CP6 用的版本。
- [ ] 能画出值类型 vs 引用类型的栈/堆内存图，并解释"复制变量"对两者的不同后果。
- [ ] 能背出内置类型全表，尤其知道哪些是值类型、哪些场景用哪个。
- [ ] 能用二进制无限循环小数解释"金额为什么必须 decimal"，并讲一个对账事故场景。
- [ ] 能画装箱内存图，说清代价，并知道用泛型避免。
- [ ] 能说清 Nullable&lt;T&gt; 原理（hasValue + value），区分值类型 ? 和引用类型 ? 。
- [ ] 能说出自动属性背后生成了后备字段，并区分 set/init/required 和 [Required]。
- [ ] 能说清 const/static/实例成员的区别，并举 CP6 的 StockQcStatus 例子。
- [ ] 能讲特性 + 反射的配合，并复述 CP6 的 PiiField/AuditIgnore 用法。
- [ ] 能背 CP6 三层继承链的每层字段和为什么分层。
- [ ] 能区分 override（多态、看实际类型、vtable）和 new（隐藏、看声明类型）。
- [ ] 能区分接口和抽象类，并解释 IAuditable（标记接口）和 IDataScoped（能力接口）。
- [ ] 能背访问修饰符全表，说清 internal 和 protected 的边界。
- [ ] 能解释"重写 Equals 必须重写 GetHashCode"的哈希桶原理。
- [ ] 能说清 record/struct/class 三者选型，并解释 with 表达式和值相等。

---

## 13. 动手小练习（在 CP6 里做）

> 目标：把本章知识落到真实项目里。做完你就能在面试里说"我读过/改过项目代码"。

### 练习 1：追踪继承链，写一张"字段来源表"

打开这四个文件：
- `C:\CP6\CP6.Entity\BaseEntity.cs`
- `C:\CP6\CP6.Entity\BaseTenantEntity.cs`
- `C:\CP6\CP6.Entity\BaseBizEntity.cs`
- `C:\CP6\CP6.Entity\DomainModels\Wms\Stock.cs`

列出 `Stock` 一个实例**总共有哪些属性**，并标注每个属性来自继承链的哪一层（BaseEntity / BaseTenantEntity / BaseBizEntity / Stock 自己）。数一数：Stock 自己写了几个属性，白拿了几个。

**进阶**：找出 `Sys_DictData.cs`（`C:\CP6\CP6.Entity\DomainModels\Sys\`），说明它为什么**不继承**任何基类、主键为什么是 `int` 而不是 `Guid`（提示：看 BaseTenantEntity.cs 的注释）。

### 练习 2：给一个实体加"标记接口"，理解按需 opt-in

阅读 `C:\CP6\CP6.Entity\IAuditable.cs` 和 `AuditIgnoreAttribute.cs`。

在纸上设计：如果要让 `Stock` 参与字段级审计，你要怎么改它的类声明？（答案方向：`public class Stock : BaseBizEntity, IAuditable`）。再想：Stock 里如果有个敏感字段不想被审计记录，该给它贴什么特性？（答案：`[AuditIgnore]`）。写出改完的类头两行代码。

**思考题**：为什么用"实现空接口"来开启审计，比在 BaseBizEntity 里加一个 `bool EnableAudit` 字段更好？（提示：数据库列、默认行为、编译期 vs 运行时）。

### 练习 3：金额精度实验 + record 值语义实验

新建一个控制台小程序（或在 CP6.Tests 里写个测试方法），亲手验证：

```csharp
// (a) 浮点误差
Console.WriteLine(0.1 + 0.2);          // 观察 double 的诡异输出
Console.WriteLine(0.1 + 0.2 == 0.3);   // 观察 False
Console.WriteLine(0.1m + 0.2m);        // 观察 decimal 精确
Console.WriteLine(0.1m + 0.2m == 0.3m);// 观察 True

// (b) record 值相等 vs class 引用相等
public record MoneyR(decimal Amount, string Ccy);
public class MoneyC { public decimal Amount; public string Ccy = ""; }

var r1 = new MoneyR(100m, "JPY");
var r2 = new MoneyR(100m, "JPY");
Console.WriteLine(r1 == r2);           // record：True
var r3 = r1 with { Ccy = "USD" };      // with 表达式
Console.WriteLine($"{r1} | {r3}");     // 观察 r1 不变、r3 是新的

var c1 = new MoneyC { Amount = 100m, Ccy = "JPY" };
var c2 = new MoneyC { Amount = 100m, Ccy = "JPY" };
Console.WriteLine(c1 == c2);           // class：False（引用相等）
```

跑一遍，把每行的输出记下来，对照第 3.4、9、10 节的讲解。**能亲手复现这些输出，你就真正懂了值语义、浮点误差和 record。**

---

> **本章小结**：你从 .NET 生态、CLR/JIT/GC，一路走到值/引用类型、内置类型（尤其金额必 decimal 的数学原理）、类与属性、特性与反射、继承多态、接口、封装、object 契约、record/struct 选型。每个点都有 CP6 真实代码撑腰——BaseEntity 三层继承链、Stock 的 decimal 精度、IAuditable/IDataScoped 接口、PiiField 自定义特性、StockQcStatus 静态常量。面试时你不只是背概念，而是能指着真实项目讲"我们为什么这么设计"。这，就是 5 年经验和背书之间的差距。
>
> 下一章预告：泛型深入、集合、LINQ、委托与事件、异步 async/await——CP6 的 Service 层和 EF 查询将成为主战场。
