# M-OA/WF T2 追补任务简报：双栈收编(用户裁决 2026-07-12)

## 背景
用户裁决=**收编**: 补 Sys_Menu 行使旧设计器 `/wf/form-designer`、`/wf/flow-designer` 可达,双栈并存,不删旧栈端点。权限面不变(旧栈写端点已贴 oa-designer:* 锚 738)。

## 需求
1. **OawfMenuSeed 追加两条收编行**(照 ErpMenuSeed 五孤儿收编先例):
   - RoutePath=`/wf/form-designer`、`/wf/flow-designer`;MenuName 用日文照旧设计器页面语义(如 フォームデザイナー(旧)/フローデザイナー(旧),「(旧)」后缀区分新设计器);ParentId=740(OA工作流父组);Icon 照 738 新设计器同款或近似;Enable=true。
   - **MenuId 段位先查占用再定**(741/742 候选,grep Seed 目录+Program.cs 种子段+迁移确认无碰撞,查证结论写报告)。
   - **MenuKey 留 null**(权限锚在 738,不得共键;回填块会派生 wf-form-designer/wf-flow-designer 键,无 RoleAction 引用,无害——与 MES 非锚定行同型,注释说明)。
   - RoleMenu 授 admin(RoleId=1),照收编先例。
2. **前端可达性自查**: 两 RoutePath 与 router viewModules 既有映射(index.ts:46-47)逐字一致(菜单行 RoutePath 必须精确等于 viewModules 键,否则 addDynamicRoutes 仍不注册)——对照写进报告。
3. 更新 `docs/seeds/oawf-key-menu-anchor.md`(追记收编行与裁决)+ `oawf-menu-seed.sql` 对照。
4. 测试: OawfMenuSeedTests 追加 2 用例(收编行存在+RoutePath 精确/幂等含新行)。
5. 真相源 §六 双栈条目追记「2026-07-12 用户裁决=收编,已落地」。

## 验证与提交
- `dotnet test --filter OawfMenuSeedTests` 全绿;全量(基线 1769)不许跌。
- 单 commit 即 push。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\oawf-t2b-report.md`。回复只返回: 状态、commit、一行测试结论、段位查证一行、concerns(15 行内)。