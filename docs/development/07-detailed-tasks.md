# 细分任务进度

台账日期：2026-10-11。由 roadmap.json 生成；S01 已交付：0/12。另列 S02-00 多租户设计前置。

这是任务交付计数，不是总工时百分比。设计任务完成不代表业务代码或运行验收通过；全部目标/场景的完成门保持独立。

| 任务 | 仓库 | 状态 | 直接前置 | 相对规模 | 类型 |
|---|---|---|---|---|---|
| S01-01 冻结采用输入和共用向量 | Main | 待安排 | 无 | S | DESIGN |
| S01-02 严格 JSON、NullOnly 与结构相等 | Main | 待安排 | S01-01 | M | CODE |
| S01-03 FIN-CREDIT 精确金额 | Main | 待安排 | S01-02 | M | CODE |
| S01-04 FIN-INV 数量、金额与分数 | Main | 待安排 | S01-02 | M | CODE |
| S01-05 Main TypeScript 精确值 | Main | 待安排 | S01-01 | M | CODE |
| S01-06 ATP StableId | Main | 待安排 | S01-02 | M | CODE |
| S01-07 原生 Ref/Scope 与 NCR/IPQC 类型 | Main | 待安排 | S01-02 | M | CODE |
| S01-08 CRM INQ/LEAD 合同采用 | CRM | 待安排 | S01-02 | L | CODE |
| S01-09 Platform 准确合同路由与原字节 | Platform | 待安排 | S01-01 | M | CODE |
| S01-10 PostgreSQL 精确存储适配 | Main | 待安排 | S01-03, S01-04, S01-06 | L | CODE |
| S01-11 ERP-CREDIT 原生金额与冻结重放 | Main | 待安排 | S01-02 | M | CODE |
| S01-12 采用证据与消费者交接 | Main | 待安排 | S01-02, S01-03, S01-04, S01-05, S01-06, S01-07, S01-08, S01-09, S01-10, S01-11 | M | DESIGN |
| S02-00 PostgreSQL 多租户存储与事务布局设计 | Main | 待安排 | 无 | M | DESIGN |

任务卡、文件和验收命令见[执行计划](../superpowers/plans/2026-10-11-s01-task-breakdown.md)。状态、阻塞和验证/交付证据只在 JSON 中更新；每次启动从届时确认的 main 建分支。

[PostgreSQL 基线与多租户方向](08-postgresql-baseline.md)适用于后续新增开发。S01-10 独立测试库不采用正式租户布局；真实业务布局/迁移须先完成 S02-00 设计及对应实现验收。

最快 CRM 路径是 S01-01 → S01-02 → S01-08；消息接线另需 S01-09，真实 Owner 写入另需 S02-00 与 S02 事务/授权准入。无需等待全部财务精度任务。
