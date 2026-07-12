# M-OA/WF T2 任务简报：菜单 MenuKey 锚定(OawfMenuSeed)

## 背景与位置
M-OA/WF 横切接线波第二任务。T1 真相源 `docs/seeds/oawf-permission-keys.md` 已过审(7 menu-key/23 资源键)。同型先例=MesMenuSeed(最新版,含按 MenuId 定位的防御矫正块)——先读 CP6.WebApi/Seed/MesMenuSeed.cs+MesMenuSeedTests.cs 学样。

## 主控已拍板(T2 执行前提,同步更新真相源)
1. **委派双键合一**: `oa-inbox:delegate` 与 `oa-settings:delegate` 合一为 **`oa-settings:delegate`**(权限面统一,防一处授一处漏)。真相源相应行改写并留裁决记录(引用审查建议)。
2. **`oa-flow-admin:enable` 维持状态级**(可逆、不动在途实例)。
3. **双栈退役/收编未裁决**(用户回来拍板)——本任务不动 /wf/*-designer 路由与旧栈端点,仅做菜单锚定;旧栈写端点键(oa-designer:*)照真相源锚定到新设计器菜单行。

## 需求
1. **OawfMenuSeed 启动幂等种子**,接入 Program.cs 紧随 MesPermissionSeed 之后、**先于全局回填块(:908 附近)**。
   - 🔴硬前置(T1 审查坐实): OA 菜单 733-739 在 :1446 插入且未设 MenuKey,回填块更早→洁净首启 OA/WF 全 403。OawfMenuSeed 须显式赋 7 键(含缺行补建,照 MesMenuSeed 兼顾缺行做法)。
2. **7 个 menu-key 锚定 733-739**(照真相源 §二;740 父组行不锚定;唯一索引安全)。
3. **防御矫正块**按 MenuId 定位(照 MesMenuSeed :388 正解——按 Key 找会漏),严限 7 锚定行,纠回已被回填成 RoutePath 派生键的存量行。
4. **交付**: `docs/seeds/oawf-key-menu-anchor.md` + `docs/seeds/oawf-menu-seed.sql`(头声明 C# 正本)+ 真相源合一裁决更新。
5. **测试**: OawfMenuSeedTests 照 MesMenuSeedTests(锚定/唯一键/幂等/矫正/非锚定行 null)。

## Global Constraints
- 基线 1758 不许跌;每 commit 立即 push;键连字符。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\oawf-t2-report.md`。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。