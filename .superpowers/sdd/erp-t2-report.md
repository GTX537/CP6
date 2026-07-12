# M-ERP T2 实现者报告(注: 上会话实现后中断,详细报告散失;以下为 commit 70f99e3 正文原文,作为实现者主张对待)

ErpMenuSeed 启动幂等种子(置于 Program.cs RoutePath 回填块之前):
- 既有 201-215 九个权限键各锚定登録/主页显式设 erp-* MenuKey,一覧页留 null
  (MenuKey 有 IS NOT NULL 唯一索引,禁两行共键——否决真相源§六.2两行同赋建议)
- 头号命门修复: 裸路径 /order 等被回填成无 erp- 前缀键→全 ERP 403,防御矫正块就地纠回
- 五孤儿路由收编 216-220: order-trace/credit-note/backorder/otd-report/fx-rate
  补 Sys_Menu 行含 erp-* MenuKey + RoleMenu 授管理员
- docs/seeds/erp-key-menu-anchor.md(14 键锚定表,T3b 输入)+ erp-menu-seed.sql 对照
- ErpMenuSeedTests 6 测试(锚定/孤儿/唯一键/幂等/矫正/一覧null)

全量 1689 绿(1683 基线 + 6 新)

变更文件:
- CP6.Tests/ErpMenuSeedTests.cs     | 140 +
- CP6.WebApi/Program.cs             |   7 +
- CP6.WebApi/Seed/ErpMenuSeed.cs    | 126 +
- docs/seeds/erp-key-menu-anchor.md |  62 +
- docs/seeds/erp-menu-seed.sql     |  60 +
