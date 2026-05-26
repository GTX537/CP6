# 华测检测「系统工程师」面试备战全记录

> **面试时间**：2026-05-28 上午（线上面试）
> **岗位**：系统工程师（.NET + AI 全栈）
> **薪资范围**：12-18K · 13薪
> **公司**：华测检测（CTI）· 深圳宝安
> **整理日期**：2026-05-27

---

## 目录

1. [岗位分析与技能匹配度](#一岗位分析与技能匹配度)
2. [P0 技能恶补（C#/.NET 核心）](#二p0-技能恶补c-net-核心)
3. [P0 技能恶补（ASP.NET Core Web API）](#三p0-技能恶补aspnet-core-web-api)
4. [P0 技能恶补（ORM：EF Core / SqlSugar）](#四p0-技能恶补ormef-core--sqlsugar)
5. [P0 技能恶补（SQL 数据库）](#五p0-技能恶补sql-数据库)
6. [初试战术总览](#六初试战术总览)
7. [板块 1：自我介绍话术](#七板块-1自我介绍话术)
8. [板块 2：CP6 项目深度讲解（ERP+MES+WMS）](#八板块-2cp6-项目深度讲解erpmeswms)
9. [板块 3：MES 工单状态机 + 并发/幂等设计](#九板块-3mes-工单状态机--并发幂等设计)
10. [板块 4：跨模块串联主线剧本](#十板块-4跨模块串联主线剧本)
11. [板块 5：AI 杀手锏话术（Dify/Coze/RAG）](#十一板块-5ai-杀手锏话术difycozerag)
12. [板块 6：HR 灵魂三问](#十二板块-6hr-灵魂三问)
13. [板块 7：反问环节 5 问](#十三板块-7反问环节-5-问)
14. [板块 8：最终备忘单](#十四板块-8最终备忘单)
15. [板块 9：线上面试专项补丁](#十五板块-9线上面试专项补丁)
16. [板块 10：模拟面试题库](#十六板块-10模拟面试题库)

---

## 一、岗位分析与技能匹配度

### 1.1 JD 核心要求拆解

```
岗位职责:
1. .NET Core/.NET 6+ 后端架构设计与开发
2. Vue/TypeScript/jQuery 前端开发
3. AI 技术引入(大模型API/Prompt工程/RAG/智能体)
4. 系统维护、性能优化、迭代

任职要求:
1. 计算机本科+ / 3年以上 C#/.NET / 环境检测/LIMS优先
2. .NET框架 / ASP.NET Core / EF Core/SqlSugar / MySQL/SQL Server
3. Vue 2/3 / TypeScript / jQuery / 组件化 / HTML5/CSS3
4. AI 平台 API (OpenAI/DeepSeek/文心/通义) / Dify/Coze / Agent/Workflow
5. Cursor/Claude Code/通义灵码 AI 辅助开发深度融合
```

### 1.2 技能优先级分层

| 优先级 | 模块 | 说明 |
|---|---|---|
| **P0** | C#/.NET 核心、ASP.NET Core、ORM、SQL | 必须熟练，面试必考 |
| **P1** | Vue+TS、AI 集成、AI 辅助开发 | 加分项，体现深度 |
| **P2** | HTML5/CSS3、Git、LIMS 领域知识 | 了解即可 |

### 1.3 简历匹配度分析

| JD 要求 | 候选人匹配 | 状态 |
|---|---|---|
| 3年以上 C#/.NET | 4 年（2022.9 至今）| ✅ 满足 |
| 本科+计算机相关 | 西安翻译学院 软件工程 本科 | ✅ 满足 |
| .NET Core / .NET 6+ | **.NET 8** 实战 | ✅ 超配 |
| ASP.NET Core Web API | CP6 项目主框架 | ✅ 强匹配 |
| EF Core + SqlSugar | **EF Core + Dapper 双 ORM** | ✅ 强匹配 |
| MySQL/SQL Server | SQL Server + MySQL + Oracle | ✅ 超配 |
| Vue 2/3 + TS | **Vue3 + TS + Element Plus + Pinia** | ✅ 强匹配 |
| jQuery | 没写 | ⚠️ 弱 |
| 环境检测/LIMS | 无，但有 ERP/MES 工业系统经验 | ⚠️ 可迁移 |
| **Dify/Coze/Agent/RAG** | **玩过 Dify/Coze，搭过 Agent/RAG** | ✅ **核心反差** |

**结论**：技术硬实力完全够格；CP6 项目是王牌；离职稳定性需准备说辞；AI 经验是杀手锏。

---

## 二、P0 技能恶补（C# / .NET 核心）

### 2.1 async / await 异步编程 ⭐⭐⭐⭐⭐

**原理一句话**：编译器把方法编译成状态机，遇 await 释放线程回线程池，完成后恢复执行。

**关键点**：
1. async 本身不创建线程
2. await 是"挂起当前方法，把线程让出去"
3. Task 类似 JS 的 Promise
4. ASP.NET Core 没有同步上下文，不需要 ConfigureAwait(false)

**高频问答**：

**Q：async void 为什么不能用？**
> 三个问题：① 异常无法被 catch 捕获，会直接抛到同步上下文导致进程崩溃；② 无法被 await；③ 不利于单元测试。唯一允许的场景是事件处理器。

**Q：什么时候用 Task.Run？**
> CPU 密集型操作丢到线程池后台执行。I/O 密集型操作不要用 Task.Run 包装，因为 I/O 本身就不占线程。

### 2.2 值类型 vs 引用类型 & GC ⭐⭐⭐⭐⭐

| 维度 | 值类型 | 引用类型 |
|---|---|---|
| 存储 | 栈（作为字段时跟随对象在堆） | 对象在堆，引用在栈 |
| 赋值 | 拷贝整个值 | 拷贝引用 |
| 默认值 | 0/false | null |
| 例 | int/struct/enum | class/string/array |

**string 是引用类型但不可变（immutable）**，表现出值类型特性。

**GC 分代回收**：堆分 Gen 0/1/2，新对象进 Gen 0，弱分代假设——大部分对象朝生夕死。大对象（≥85KB）进 LOH。

**装箱/拆箱**：值类型转引用类型会在堆上分配并复制，有性能开销。

### 2.3 委托、事件、Lambda ⭐⭐⭐⭐

- **Action**：无返回值
- **Func**：有返回值，**最后一个泛型参数是返回类型**
- **Predicate\<T\>**：返回 bool

**事件 vs 委托**：事件只允许 += 和 -=，只能在声明类内部触发；委托可被任何代码调用赋值。

### 2.4 LINQ & IEnumerable vs IQueryable ⭐⭐⭐⭐⭐

| 维度 | IEnumerable\<T\> | IQueryable\<T\> |
|---|---|---|
| 执行位置 | **内存中**（客户端） | **数据库**（服务端） |
| 参数类型 | `Func<T, bool>` | `Expression<Func<T, bool>>` |
| 适用 | List、Array | EF Core 等 |
| 性能 | 取全表回内存再过滤 | 翻译成 SQL 在 DB 过滤 |

**延迟执行**：LINQ 查询不立即执行，直到 ToList/foreach/Count 才执行。

### 2.5 依赖注入（DI）三种生命周期 ⭐⭐⭐⭐⭐

| 生命周期 | 注册方法 | 说明 |
|---|---|---|
| Singleton | AddSingleton | 全局唯一 |
| Scoped | AddScoped | 每个请求一个实例（**DbContext 必须用 Scoped**） |
| Transient | AddTransient | 每次注入都新建 |

**陷阱**：Singleton 里直接注入 Scoped 服务会导致 captive dependency，应注入 IServiceScopeFactory 手动创建 scope。

---

## 三、P0 技能恶补（ASP.NET Core Web API）

### 3.1 中间件管道（洋葱模型）

**标准顺序**：
```
UseExceptionHandler → UseHttpsRedirection → UseStaticFiles
→ UseRouting → UseCors → UseAuthentication → UseAuthorization
→ MapControllers
```

**关键规则**：
- UseAuthorization 必须在 UseAuthentication 之后
- UseCors 必须在 UseAuthentication 之前
- UseExceptionHandler 在最外层

### 3.2 五大过滤器

```
Authorization → Resource → Action(Before) → [Action] → Action(After) → Result
异常时: Exception Filter
```

**中间件 vs 过滤器**：中间件是 HTTP 管道层面，只有 HttpContext；过滤器是 MVC 层面，能访问 Controller/Action/ModelState。

### 3.3 JWT 鉴权 ⭐⭐⭐⭐⭐

**三段结构**：
```
Header.Payload.Signature
   ↓     ↓        ↓
算法   载荷(Base64,非加密)  签名(防篡改)
```

**JWT vs Session**：
- Session 有状态，服务端存储
- JWT 无状态，所有信息在 Token 里
- JWT 优势：易扩展、跨域友好
- JWT 劣势：签发后无法主动失效

**JWT 失效方案**：
1. 短期 Access Token + Refresh Token 机制
2. 服务端黑名单（Redis）
3. 用户登录版本号（jti）

### 3.4 CORS 跨域

**预检请求**：浏览器发非简单请求前先发 OPTIONS 询问服务端是否允许。
**生产**：不要用 AllowAnyOrigin()，要明确白名单。

---

## 四、P0 技能恶补（ORM：EF Core / SqlSugar）

### 4.1 EF Core 核心

- **DbContext** 不是线程安全，必须 **Scoped**
- 三种加载策略：**Eager (Include)** / Explicit / Lazy
- **N+1 问题**：查询 N 条主记录后访问导航属性各发一次 SQL → 用 Include 显式加载

### 4.2 性能优化关键点

```csharp
// ❌ 全表加载后过滤
db.Users.ToList().Where(u => u.Age > 18);

// ✅ 数据库过滤
db.Users.Where(u => u.Age > 18).ToListAsync();

// ❌ 默认跟踪
var users = await db.Users.ToListAsync();

// ✅ 只读用 AsNoTracking
var users = await db.Users.AsNoTracking().ToListAsync();

// 批量操作 (EF Core 7+)
await db.Users.Where(...).ExecuteUpdateAsync(...);
await db.Users.Where(...).ExecuteDeleteAsync();
```

### 4.3 EF Core vs SqlSugar 选型话术

> "EF Core 微软官方，强类型 LINQ，跨数据库能力强，适合大型项目。SqlSugar 国产轻量，API 更接近 SQL，上手快、性能好，国内中小项目常用。两者心智模型相通——都是用对象封装 SQL。"

### 4.4 EF + Dapper 双 ORM 分工（候选人使用）

- **EF**：复杂业务、CRUD、事务（导航属性 + ChangeTracker 省代码）
- **Dapper**：复杂报表、大数据量、动态 SQL（直接写 SQL 性能可控）

---

## 五、P0 技能恶补（SQL 数据库）

### 5.1 索引原理

**B+ 树特点**：
1. 多叉平衡树
2. 非叶子节点只存索引键
3. 叶子节点用双向链表连接（范围查询快）
4. 树高 3-4 层

**聚簇 vs 非聚簇**：
- 聚簇：叶子节点存整行数据，一张表只能一个（InnoDB 主键）
- 非聚簇：叶子节点存主键值，查询需要"回表"
- **覆盖索引**：索引包含 SELECT 所有字段，不回表

### 5.2 索引失效场景（10 个）

1. 函数/计算（YEAR(date)）
2. 隐式类型转换
3. 前导 % 模糊查询
4. OR 连接非索引列
5. != / NOT IN
6. IS NULL（部分场景）
7. 跳过联合索引最左列
8. 范围查询右侧失效
9. ORDER BY 不符合最左前缀
10. 优化器认为全表更快

### 5.3 事务 ACID + 隔离级别

| 级别 | 脏读 | 不可重复读 | 幻读 |
|---|---|---|---|
| Read Uncommitted | ❌有 | ❌有 | ❌有 |
| Read Committed | ✅无 | ❌有 | ❌有 |
| **Repeatable Read** (MySQL默认) | ✅无 | ✅无 | ⚠️ MVCC+间隙锁解决 |
| Serializable | ✅无 | ✅无 | ✅无 |

### 5.4 锁

- **乐观锁**（RowVersion/Timestamp）：冲突概率低场景，性能好
- **悲观锁**（SELECT FOR UPDATE）：强一致场景
- **行锁加在索引上**：没索引会升级表锁

### 5.5 EXPLAIN 关键列

| 列 | 关注 |
|---|---|
| type | 至少 range，避免 ALL |
| key | NULL 就是没走索引 |
| rows | 越小越好 |
| Extra | Using index 最好；filesort/temporary 警觉 |

### 5.6 慢 SQL 优化三步法

1. **定位**：慢查询日志 / EXPLAIN
2. **分析**：看 type/key/rows/Extra
3. **优化**：索引 → SQL 改写 → 表结构 → 缓存 → 架构

### 5.7 LIMIT 深分页优化

```sql
-- ❌ 慢
SELECT * FROM orders ORDER BY id LIMIT 1000000, 10;

-- ✅ 游标
SELECT * FROM orders WHERE id > 1000000 ORDER BY id LIMIT 10;
```

---

## 六、初试战术总览

### 6.1 初试流程

| 阶段 | 时长 | 重点 |
|---|---|---|
| 1. 自我介绍 | 3-5 min | 表达能力、节奏、亮点提炼 |
| 2. 项目深挖 | 10-15 min | 项目真实性、技术选型、解决问题能力 |
| 3. 基础技术问答 | 10-15 min | 广度 > 深度 |
| 4. 反问环节 | 3-5 min | 思考深度、对岗位的兴趣 |
| 5. HR 收尾 | 5-10 min | 薪资、到岗、稳定性、离职原因 |

### 6.2 初试胜负手

| # | 关键点 | 占比 |
|---|---|---|
| 1 | 自我介绍 + 项目讲解 | 50% |
| 2 | AI 工具实战故事 | 20% |
| 3 | 基础技术问答 | 15% |
| 4 | 软素质：表达、稳定性、薪资匹配 | 10% |
| 5 | 反问环节 | 5% |

---

## 七、板块 1：自我介绍话术

### 7.1 黄金 4 段式结构

```
1. 基本盘（10 秒）→ 我是谁 + 几年经验 + 主要方向
2. 技术栈（30 秒）→ 一句话提炼，重点突出 JD 命中的
3. 项目亮点（90 秒）→ 重点讲 CP6，主动抛 3 个钩子
4. 求职动机（20 秒）→ 为什么是这家公司 + 收尾
```

### 7.2 完整 3 分钟版（带钩子标注）

**【1. 基本盘 10s】**
> "面试官您好，我叫高步宝，27 岁，2022 年从西安翻译学院软件工程专业本科毕业，到现在差不多 4 年 C#/.NET 开发经验，目前在苏州的纬致芯创科技。"

**【2. 技术栈 30s】**
> "技术栈上，后端我比较熟 ASP.NET Core Web API，从 .NET Framework、.NET Core 一直到现在的 .NET 8 都做过完整项目；ORM 主要用 EF Core 配合 Dapper——复杂业务用 EF，高频查询和报表用 Dapper；前端用 Vue 3 + TypeScript + Element Plus + Pinia；中间件用过 Redis、RabbitMQ、SignalR；部署上接触过 Docker 和 K8S。"

> 🪝 钩子：EF + Dapper 双 ORM 主动抛出

**【3. 项目亮点 90s】**
> "我最有代表性的项目是过去一年做的『クラウンパッケージ ERP/MES 系统重构』，是给一家日本的包装制造企业做的核心业务系统升级，我独立负责生产管理这块核心模块，从需求分析、数据库设计、后端接口到前端页面全流程负责。
>
> 这个项目里我做过几件比较有挑战的事：
> 第一，性能优化——某些核心查询接口在数据量上去之后从 3 秒打到几百毫秒。
> 第二，做了一套通用的 RBAC 权限体系。
> 第三，做了 5 语言（中英日韩繁）动态国际化。
> 整个系统用 Docker Compose 做容器化部署，结合 K8S 多副本运行，过程里也踩过 SignalR 在集群下连接不稳定这种坑。"

> 🪝 钩子：性能优化 / SignalR 集群坑

**【4. 求职动机 30s】**
> "除了主业之外，最近这一年我也在主动学 AI 应用方向——用过 OpenAI、DeepSeek、通义千问几家的大模型 API，也基于 Dify 和 Coze 搭过几个智能体和 RAG 的小项目，所以我看到贵司 JD 里写到『AI 平台 API 调用、Agent 与工作流构建』，还有『Cursor、Claude Code 等 AI 辅助开发工具的深度融合』，和我现在的兴趣方向高度匹配。我希望能在一家愿意把 AI 真正落地到业务系统的团队里继续深耕『.NET + AI』这条路，所以特别想了解一下这个机会。以上是我的基本情况。"

> 🪝 致命杀手锏：精准命中 JD 的 AI 要求

### 7.3 5 个开场雷区

1. 流水账式介绍
2. 报菜名（堆砌技术词）
3. 太软的自我评价（"学习能力强、抗压"）
4. 超时（超过 5 分钟）
5. 求职动机说"听说贵公司不错"

---

## 八、板块 2：CP6 项目深度讲解（ERP+MES+WMS）

### 8.1 项目一句话定位

> "CP6 是给一家日本包装制造企业做的一体化业务管理系统，覆盖 ERP（订单+销售+采购）、MES（生产执行）、WMS（仓储管理）三大业务域，前端 Vue3 + TS，后端 .NET 8 + Web API，数据库 SQL Server，已上线运行。"

### 8.2 三大模块详解

**ERP（企业资源计划）**：
- 客户/供应商管理
- 销售管理（询价/报价/订单/合同/回款）
- 采购管理
- 基础数据（物料/BOM/币种）
- 关键技术：多级审批流、多币种、PDF/Excel 导出、5 国语言 i18n

**MES（制造执行系统）⭐ 主战场**：
- 生产排程
- 工单管理（创建/下发/暂停/关闭）
- 工序流转（裁切→印刷→装订→包装）
- 车间报工
- 设备/产能
- 质检
- 关键技术：状态机、SignalR 实时推送、复杂查询性能优化、配置化流程

**WMS（仓储管理系统）**：
- 入库（采购/成品/退货）
- 出库（销售/生产领料/退货）
- 库位管理（仓库→库区→货架→库位）
- 库存盘点
- 库存预警
- 批次/序列号
- 关键技术：强一致性扣减、高并发、条码扫码、库存快照

### 8.3 角色边界划分

| 模块 | 角色 | 演示话术 |
|---|---|---|
| MES - 生产管理 | ✅ 独立负责 | "我从 0 到 1 设计开发的" |
| ERP - 销售部分 | ⚠️ 部分参与 | "我做了 XX 部分" |
| WMS | ⚠️ 接口对接 | "代码同事做，我熟悉业务" |
| RBAC/i18n | ✅ 封装 | "这块我封装的" |

### 8.4 STAR 法则深度展开

**S - 背景**（20s）
> "生产管理模块核心场景：业务下了订单后要拆成生产工单、排产、车间领料、生产报工、入库等流程。老系统痛点：① 工单查询慢（3 秒+）；② 生产数据没实时反馈；③ 流程节点硬编码。"

**T - 任务**（10s）
> "重新设计这块模块——解决性能和实时性问题，把流程做得可配置。"

**A - 行动**（60s）—— 4 个维度
> "① 数据层：重新设计核心表结构，加联合索引，复杂报表用 Dapper，CRUD 用 EF。
> ② 接口层：ASP.NET Core Web API，全局异常中间件，JWT 鉴权，FluentValidation。
> ③ 缓存和异步：基础数据放 Redis，耗时操作丢 RabbitMQ。
> ④ 实时推送：车间报工、设备状态用 SignalR 推到前端。"

**R - 结果**（20s）
> "工单列表 3 秒→500ms 以内；实时数据延迟从手动刷新到秒级推送；流程节点配置化后业务调整不发版。"

### 8.5 5 个技术选型答辩

**Q1：为什么用 EF + Dapper 双 ORM？**
> "有意识的分工：EF 适合复杂业务、强类型查询、事务；Dapper 适合复杂报表场景，性能可控。分工原则：写 CRUD 和事务用 EF，读复杂报表用 Dapper。"

**Q2：Redis 缓存怎么用？一致性怎么保证？**
> "3 类数据：基础数据用 Cache Aside（先更 DB 再删 Redis）；用户登录态/Token 黑名单；接口限流计数。强一致场景不上缓存。"

**Q3：RabbitMQ 用来做什么？消息丢失怎么办？**
> "3 类用途：批量耗时操作、通知、跨模块事件。可靠性 3 层：① publisher confirm 重试；② 队列消息 durable；③ 手动 ack + 死信队列 + 消费幂等。"

**Q4：SignalR 在 K8S 多副本下问题？**
> "SignalR 默认有状态，客户端连 A 但消息从 B 推就丢。解决：Redis Backplane——`AddSignalR().AddStackExchangeRedis(...)`，所有副本通过 Redis 转发。注意监控 Redis 连接数和带宽。"

**Q5：5 语言国际化怎么做？**
> "前端 vue-i18n，翻译数据从后端 API 动态加载。难点：翻译缺失回退默认语言、富文本占位符、缓存版本号控制。"

### 8.6 性能优化故事（3 秒→500ms）

**1. 发现问题**：工单列表数据量上去后 3 秒+
**2. 定位原因**：SQL Profiler + EXPLAIN
- 关联 4 张表，关联字段没索引，type=ALL，扫几十万行
- SELECT * 带回大量无用字段，导致回表
- LIMIT skip,take 深分页性能差
**3. 解决方案 4 维度**：
- 索引：联合索引 + 覆盖索引
- SQL 改写：SELECT * → 指定字段；子查询先过滤主表
- ORM 切换：EF → Dapper 手写 SQL
- 缓存：维度数据放 Redis
**4. 结果**：3 秒 → 500ms 以内，性能提升 80%+
**5. 沉淀**：
- 任何上线接口先压测
- 列表查询永远不写 SELECT *
- 写复杂查询前先 EXPLAIN
- 高频查询和报表优先 Dapper

### 8.7 基本面追问应对

| 追问 | 应对 |
|---|---|
| 项目多少人？ | 8-10 人（前端 2 + 后端 3-4 含我 + 测试 2 + 产品 1） |
| 数据量？ | 工单表几十万，订单表百万级，日活几百 |
| 上线了吗？ | 已上线，持续迭代 |
| 协作工具？ | Git/GitLab + Jira/禅道 + Confluence/飞书 |
| 代码评审？ | 每个 PR ≥1 Reviewer |
| 怎么发版？ | CI/CD pipeline + GitLab Runner + Docker + K8S 滚动更新 |

### 8.8 5 大讲解雷区

1. 吹自己是"主负责人"但说不出架构决策
2. 技术词堆砌但讲不出落地
3. 数字虚高（"日活几万、QPS 几千"）
4. 甩锅团队
5. 不知道就硬编

**救命话术**：
> "这块当时主要是 X 同事负责的，我大致了解的思路是 XX，但具体实现细节没完全跟下来。如果让我做的话，我会考虑 XX 方向。"

---

## 九、板块 3：MES 工单状态机 + 并发/幂等设计

### 9.1 工单 7 个状态

```
PendingScheduling 待排产
     ↓ Schedule
Scheduled 已排产
     ↓ Release
Released 已下发
     ↓ StartWork
InProduction 生产中 (工序流转都在这里)
     ↓ ReportDone
PendingQC 待质检
     ↓ PassQC
Completed 已完工
     ↓ Stock
Stocked 已入库

异常分支: Cancelled / Paused
```

### 9.2 状态机三种实现

| 方案 | 评价 |
|---|---|
| ❌ if-else 散落 | 难维护，状态规则散落 |
| ⚠️ switch 集中 | 扩展性差 |
| ✅ **状态转移表 + 规则引擎** | 集中配置，扩展性强 |

### 9.3 状态变更服务 5 步

```csharp
public async Task<Result> ChangeStatusAsync(long woId, WorkOrderAction action, string operatorId)
{
    var wo = await _repo.GetByIdAsync(woId);
    
    // 1. 校验状态转移合法性
    if (!Transitions.TryGetValue((wo.Status, action), out var nextStatus))
        return Result.Fail($"不允许从 {wo.Status} 通过 {action} 转移");
    
    // 2. 前置业务校验
    await _validators[action].ValidateAsync(wo);
    
    // 3. 执行状态变更(带乐观锁)
    wo.Status = nextStatus;
    wo.Version++;
    await _repo.UpdateAsync(wo);
    
    // 4. 写状态变更历史
    await _historyRepo.AddAsync(new StatusHistory {
        WorkOrderId = woId, FromStatus = wo.Status, ToStatus = nextStatus,
        Operator = operatorId, Time = DateTime.Now
    });
    
    // 5. 发领域事件
    await _eventBus.PublishAsync(new WorkOrderStatusChanged(woId, nextStatus));
    
    return Result.Ok();
}
```

### 9.4 并发控制 3 类场景

| 场景 | 方案 |
|---|---|
| 状态变更冲突 | **乐观锁**（RowVersion） |
| 库存/领料强一致扣减 | **悲观锁**（UPDLOCK） |
| 跨副本并发 | **Redis 分布式锁**（SETNX + 过期时间 + lockToken） |
| 数量累加 | **数据库原子操作**（`SET Qty = Qty + @qty`） |

### 9.5 幂等 4 种实现

1. **幂等键 + Redis**（通用兜底）
2. **业务唯一键 + 唯一索引**（最可靠）
3. **状态机天然幂等**（重复请求被合法性校验挡掉）
4. **乐观锁版本号**（UPDATE WHERE Version 兜底）

### 9.6 幂等键示例

```csharp
[HttpPost("report")]
public async Task<Result> ReportAsync(
    [FromHeader] string idempotencyKey, 
    [FromBody] ReportDto dto)
{
    var cachedResult = await _redis.GetAsync($"idempotent:{idempotencyKey}");
    if (cachedResult != null) return cachedResult;
    
    if (!await _redis.SetNxAsync($"lock:{idempotencyKey}", "1", TimeSpan.FromSeconds(30))) 
        return Result.Fail("请勿重复提交");
    
    var result = await _service.ReportAsync(dto);
    await _redis.SetAsync($"idempotent:{idempotencyKey}", result, TimeSpan.FromHours(1));
    
    return result;
}
```

### 9.7 高频追问

**Q：乐观锁失败怎么处理？**
> 核心业务自动重试 2-3 次（带退避）；管理操作直接抛错让用户决策。不能无限重试。

**Q：分布式锁挂了怎么办？**
> ① 锁带过期时间；② 业务时间不超过锁过期时间；③ Redisson Watchdog 续期；④ 极端情况降级（接受短暂数据不一致或拒绝服务）。

**Q：RabbitMQ 消费幂等怎么做？**
> 消息 ID 全局唯一 + Redis/DB 查重 + 事务包裹"查重→处理→写标记"。

---

## 十、板块 4：跨模块串联主线剧本

### 10.1 全流程数据流

```
       ┌────── ERP 域 ──────┐
客户 → │ 询价→报价→合同→订单→审批 │
       └───────┬─────────────┘
               ↓ OrderConfirmed 事件
       ┌────── MES 域 ──────┐
       │ 拆单→排产→下发→领料 │ → WMS 出库
       │ →报工(SignalR)→质检→完工│
       └───────┬─────────────┘
               ↓ WorkOrderCompleted 事件
       ┌────── WMS 域 ──────┐
       │ 成品入库→发货出库   │ → 客户
       └─────────────────────┘

横向贯穿: RBAC / i18n / 基础数据 / 审批引擎 / 消息总线
```

### 10.2 5 段式主线讲解

**第 1 幕：ERP 接单（45s）**
> 询价 → 报价 → 销售订单 → 多级审批 → OrderConfirmed 事件通过 RabbitMQ 异步通知下游

**第 2 幕：MES 生产执行（90s）**—— 主战场
> 监听事件自动拆单 → 排产 → 下发车间 → 领料（跨模块调 WMS） → 工序流转（状态机） → 报工（SignalR 实时推送 + 幂等保护） → 质检 → 完工

**第 3 幕：WMS 仓储联动（45s）**
> 领料出库（FIFO + 悲观锁） → 成品入库 → 发货出库

**第 4 幕：横向贯穿能力（30s）**
> RBAC / i18n / 审批引擎 / 消息总线 / 基础数据中心

**第 5 幕：收尾引导（20s）**
> "如果您感兴趣，我可以挑里面任何一段展开讲深"

### 10.3 4 个跨模块高难度问题

**Q1：模块通信怎么做？为什么用 RabbitMQ 而不是直接 API？**
> 同步实时用 HTTP API（MES 调 WMS 领料）；异步解耦用 RabbitMQ + 领域事件。MQ 好处：解耦/削峰/可靠/异步。代价：最终一致性。

**Q2：跨模块数据一致性怎么保证？**
> 不追求强一致，追求最终一致。Saga 思想做补偿 + Outbox 模式（本地事务+消息表+独立发布器） + 死信队列 + 业务对账。

**Q3：MES 服务挂了，ERP 已确认订单怎么办？**
> RabbitMQ 持久化兜底，消息堆积在队列等 MES 恢复。前提：MES 消费逻辑必须幂等。

**Q4：3 大模块部署在一起还是分开？**
> 模块化单体倾向微服务的过渡形态——同一 solution 下三个项目但模块隔离，独立 Docker 镜像 K8S 部署，数据库共享但逻辑分组。演进式架构。

---

## 十一、板块 5：AI 杀手锏话术（Dify/Coze/RAG）

### 11.1 AI 经验定位

> "个人探索 + 小项目"，不是企业级生产。原则：**承认深度有限，但展示思考力 + 学习速度 + 落地意识**。

### 11.2 AI 辅助开发（Cursor/Claude Code）使用场景

1. 写脚手架代码（CRUD 整套）
2. 调试和排查 bug
3. 写单元测试
4. 重构和迁移
5. 写 SQL 和正则

**关键认知**：AI 是"审查者"不是"代码生成器"，生成的代码必须 Code Review。

### 11.3 大模型 API 集成关键点

- 调过：OpenAI、DeepSeek、通义千问、文心、豆包
- .NET SDK：OpenAI-DotNet / Azure.AI.OpenAI
- 流式输出（IAsyncEnumerable）
- Token 成本控制
- 重试和降级
- Prompt 模板管理
- 上下文管理（超长用 summary）

### 11.4 Dify RAG 实战项目（研发文档问答助手）

**背景**：项目文档散落，新人入职查询效率低

**技术方案**：
1. 数据导入：Confluence/飞书文档 → Markdown/PDF → Dify 知识库
2. 切片：按 Markdown 标题层级 + Token 数（每片 500 token）
3. 向量化：OpenAI text-embedding-3-small
4. 检索：向量召回 top-K → DeepSeek-V3 生成
5. 加 rerank：bge-reranker 二次排序
6. 接入飞书/钉钉机器人

**效果**：精度约 70%，"找文档定位"场景效率提升明显

**踩坑**：
- 切片粒度太大答非所问，太小丢上下文
- 中文 embedding 模型选择
- 混合检索（向量 + BM25）

### 11.5 RAG 7 步流程

```
【离线】① 文档加载 → ② 切片 → ③ Embedding → ④ 存向量库
【在线】⑤ 问题向量化 → ⑥ 向量检索 Top-K(+rerank) → ⑦ LLM 生成
```

### 11.6 Dify vs Coze 选型话术

> "Coze：上手快、可视化好、国内生态好，适合非技术团队、快速搭原型、to C 场景。
> Dify：开源可自部署、可控性高，适合企业自建、对数据隐私要求高、需要深度定制。
> 这家公司是检测行业，数据涉及客户检测报告、商业机密，私有化部署是刚需，生产场景下应该优先 Dify。"

### 11.7 Agent vs Workflow

- **Agent**：自主规划 + 调用工具 + 多步推理，灵活但可控性差
- **Workflow**：预定义步骤编排，可控但灵活性差
- **生产环境**：核心流程用 Workflow 保证稳定，边角场景让 Agent 处理

### 11.8 检测行业 AI 落地 5 方向（绝杀加分）

1. **检测报告智能问答**（RAG）：标准/报告/合同查询
2. **报告自动撰写助手**：基于检测数据生成报告初稿
3. **客户咨询智能体**（Agent + Workflow）：检测项/周期/费用 7×24 答疑
4. **异常数据 AI 识别**：检测数据初筛预警
5. **LIMS + AI 流程自动化**：意图识别 + Workflow 派发

### 11.9 5 大 AI 话术雷区

1. 吹企业级生产经验
2. 乱用 AI 术语（Transformer 微调）
3. 看不起 AI 工具（"调 API 没含量"）
4. 把 AI 当万能
5. 不知道 RAG 细节

---

## 十二、板块 6：HR 灵魂三问

### 12.1 Q1：为什么离开第一家公司？（2022.9-2023.12，1 年 3 个月就跳）

**话术模板 A（业务方向因素）**：
> "在第一家公司主要做传统 .NET 项目，技术栈相对老一些，业务也比较单一。我希望接触更新的技术栈和更复杂的业务场景，所以选择了纬致芯创——果然在那边接触到 .NET 8、容器化、微服务等更现代的技术栈。"

**话术模板 B（家庭/地域因素）**：
> "主要是地域原因——第一家在无锡，家人和发展规划都在苏州，所以选择了离家更近、平台更大的公司。"

**话术模板 C（成长瓶颈）**：
> "第一家公司团队偏小，独立成长空间到一定程度遇到瓶颈。我希望加入有更完善技术体系、更多技术挑战的团队。"

**绝对不能说**：
- ❌ 加班太多
- ❌ 钱给得少
- ❌ 和领导/同事不合
- ❌ 公司效益不好

### 12.2 Q2：期望薪资多少？

| 现状 | 建议报价 |
|---|---|
| 现薪 ≤ 12K | "14-16K" |
| 现薪 13-15K | "16-18K" |
| 现薪 ≥ 15K | "18K 及以上" |
| 不想说现薪 | "期望 15-18K，最终看面试综合评估" |

**追问回答**：
> "3 个考虑：① 现司薪资是 XK，希望适度提升；② 这个岗位要求 .NET 全栈 + AI 落地，和我能力匹配度高；③ 这个数也在贵司 JD 范围内。当然具体可以再沟通。"

### 12.3 Q3：职业规划是什么？

> "短期 1-2 年：在 .NET + AI 这个复合方向上做深，真正把 AI 能力落地到业务系统。
> 中期 3-5 年：成长为既懂工程又懂 AI 应用的技术骨干 / 架构师。
> 长期：希望在 'AI + 行业' 这个交叉领域有自己的积累——贵司是检测行业头部，AI 落地空间大。"

---

## 十三、板块 7：反问环节 5 问

### 13.1 高分反问

1. **"贵司在 .NET + AI 这块目前是什么阶段？是已经有落地的产品，还是处于探索/规划期？我入职后大概会从哪个具体场景切入？"**
2. **"团队目前的技术栈和工具链是什么样的？比如 AI 这块用 Dify 还是自研框架？代码评审、CI/CD、监控这些工程化做到什么程度？"**
3. **"我做过 MES/ERP 类业务系统，听贵司用 LIMS 系统，这块业务复杂度和工程实践有什么差异？我能从哪些方面快速衔接？"**

### 13.2 中分反问

4. **"团队的技术氛围怎么样？比如有没有定期技术分享、内部技术博客这种机制？"**
5. **"这个岗位的成长路径是什么？比如我未来 1-2 年能往哪些方向发展？"**

### 13.3 务实必问

6. **"工作时间和加班情况大致是怎样的？"**
7. **"面试流程大概是几轮？后续多久能收到反馈？"**

### 13.4 不要问的

- ❌ "公司福利怎么样？"
- ❌ "可以居家办公吗？"
- ❌ "你们公司主要做什么？"

---

## 十四、板块 8：最终备忘单

### 14.1 心态定调

```
你的定位:4年.NET全栈 + Vue3前端 + AI实战派
你的杀手锏:Dify/Coze/RAG 实战 + .NET+AI 复合人才
你的主项目:CP6(ERP+MES+WMS)一体化系统,独立负责MES
你的短板:1段经历1年3个月跳过、AI是个人探索非企业级
你的策略:技术稳基本盘,AI抛差异化,主动埋钩子掌控节奏
```

**3 条铁律**：节奏稳 / 诚实优先 / 数字落地

### 14.2 高频技术题速答卡

| 问题 | 速答 |
|---|---|
| async/await 原理 | 编译器生成状态机，遇 await 释放线程回线程池 |
| async void 为什么不能用 | 异常无法捕获、无法 await、难测试 |
| 值类型 vs 引用类型 | 栈+拷贝值 / 堆+拷贝引用；string 引用类型但不可变 |
| GC 怎么工作 | 分代 Gen0/1/2，弱分代假设 |
| DI 三种生命周期 | Singleton/Scoped/Transient；DbContext 必 Scoped |
| IEnumerable vs IQueryable | 内存执行 / 数据库执行（表达式树） |
| 中间件顺序 | Exception → Routing → CORS → AuthN → AuthZ → Endpoints |
| JWT 三段 | Header/Payload/Signature |
| JWT 失效 | 短期Token+Refresh / 黑名单 / 版本号 |
| 聚簇 vs 非聚簇 | 整行 / 主键值 + 回表 |
| 覆盖索引 | SELECT 字段全在索引里，不回表 |
| 索引失效 | 函数/类型转换/前导%/OR/!=/跳过最左 |
| 隔离级别 | RU/RC/RR(MySQL默认)/Serializable |
| EXPLAIN 看 | type/key/rows/Extra |

### 14.3 出门时间表

```
07:00  起床
07:15  早餐时过备忘单
07:30  对镜子讲 1 遍自我介绍
07:40  CP6 7 步主线默念
07:45  整理仪容
08:00  出门(提前 20-30 分到达)
进门前 深呼吸 3 次 / 微笑 / 主动问好
```

### 14.4 进门前 3 句口诀

```
口诀一: 慢半拍说话,声音稳
口诀二: 不会就老实说,诚实是加分
口诀三: 我是来"合作"的,不是来"被审"的
```

---

## 十五、板块 9：线上面试专项补丁

### 15.1 5 个线上优势

1. **可以放小抄**：桌面侧边贴备忘单（不能盯着看）
2. **演示更方便**：屏幕共享
3. **紧张感降低**：自己环境
4. **可以喝水调节**
5. **结束后不用尬聊**

### 15.2 5 个线上风险

1. **网络卡顿** → 有线网 + 5G 热点备用
2. **设备没准备好** → 提前 30 分钟测试
3. **背景乱/光线差** → 白墙/书柜 + 正面光
4. **盯屏幕不看摄像头** → 重点表态时看摄像头
5. **听不清** → 有线耳机 + 直接问"能再说一下吗"

### 15.3 备忘单贴在屏幕边框

```
┌─────── 钩子 ───────┐
│ • CP6: ERP+MES+WMS │
│ • 我:独立MES        │
│ • 性能:3s→500ms     │
│ • 双ORM:EF + Dapper │
│ • SignalR集群:Redis │
│ • AI: Dify/RAG      │
│ • 落地: 5方向       │
│                     │
│ HR:                 │
│ • 离职:成长方向     │
│ • 薪资:15-18K       │
│ • 反问:AI落地阶段   │
└────────────────────┘
```

### 15.4 眼神管理

```
你在说话时           → 看摄像头
你在听他说话时       → 可以看屏幕
你在思考时           → 自然往侧上方看
重点表态时           → 一定看摄像头
```

### 15.5 面试前 30 分钟 设备 Checklist

```
[ ] 网络:有线优先,Wi-Fi上行≥5Mbps
[ ] 电脑充电
[ ] 摄像头:腾讯会议自检
[ ] 麦克风:有线耳机优先
[ ] 屏幕:亮度调高,字体放大
[ ] 桌面:整理干净,关掉所有通知
[ ] 背景:白墙/书柜
[ ] 光线:正面光,不逆光
[ ] 着装:上半身整洁
[ ] 关掉:微信/QQ/邮件/钉钉/弹窗
[ ] 备忘单:贴在显示器侧边
[ ] 演示账号:提前登录测试
[ ] 喝水:杯子放在桌上
[ ] 上厕所:面试前 10 分钟
[ ] 通知家人/室友别打扰
```

### 15.6 5 个线上禁忌

1. ❌ 盯着备忘单不看摄像头
2. ❌ 背景出现家人/宠物/床/衣物
3. ❌ 吃东西、嚼口香糖
4. ❌ 手机响、电脑弹窗
5. ❌ 网络断了惊慌失措

---

## 十六、板块 10：模拟面试题库

### 16.1 10 题分布

| # | 类型 | 难度 |
|---|---|---|
| 1 | 自我介绍 | ⭐ |
| 2 | 项目背景 | ⭐⭐ |
| 3 | 技术选型答辩 | ⭐⭐⭐ |
| 4 | 项目深挖（性能） | ⭐⭐⭐ |
| 5 | 技术深挖（并发/幂等） | ⭐⭐⭐⭐ |
| 6 | 系统架构思维 | ⭐⭐⭐⭐ |
| 7 | AI 经验真实性 | ⭐⭐⭐ |
| 8 | AI 落地业务思考 | ⭐⭐⭐⭐ |
| 9 | HR 离职原因 | ⭐⭐⭐ |
| 10 | HR 薪资 + 反问 | ⭐⭐⭐ |

### 16.2 模拟面试场景设定

> 面试官：30 多岁，C#/.NET 老兵，做检测行业 ERP 多年，最近在推 AI 落地。
> 风格：不犀利但抓细节，听到模糊会追问，听到亮点会鼓励。

### 16.3 答题原则

- 想怎么说就怎么说，别完美主义
- 不会就说不会，比硬编更值钱
- 长度自己控制（1-3 分钟）
- 觉得难直接说"过"

---

## 附录：最终自检 10 件事

- [ ] 自我介绍 3 分钟版能脱稿讲 1 遍
- [ ] CP6 7 步主线能脱稿讲 1 遍
- [ ] AI 杀手锏 90 秒话术能脱稿讲 1 遍
- [ ] 5 个技术答辩（双ORM/性能/SignalR/状态机/并发）至少背 3 个
- [ ] 离职原因话术选定 1 个版本背熟
- [ ] 薪资报价想清楚说哪个区间
- [ ] 职业规划话术能讲
- [ ] 反问 3 个问题准备好
- [ ] 演示账号 + 网络 + 兜底 PPT 准备就位
- [ ] 华测检测官网看 5 分钟（业务范围/公司背景）

---

> **结语**：本文档为 2026-05-27 晚间面试备战会话整理稿。
> 候选人优势：4 年 .NET 实战 + CP6 全栈项目 + AI 工具实战派。
> 核心战术：技术稳基本盘 + AI 抛差异化 + 主动埋钩子掌控节奏。
> **明天加油！**
