# M-MES T2 任务简报：菜单 MenuKey 锚定(MesMenuSeed)

## 背景与位置
M-MES 横切接线波第二任务。T1 真相源 `docs/seeds/mes-permission-keys.md` 已过审(10 个 mes-* menu-key)。本任务为 T3b 种子提供「键→菜单行」锚定。同型先例=ErpMenuSeed(M-ERP T2, CP6.WebApi/Seed/ErpMenuSeed.cs)——结构、幂等、防御矫正块、测试形状照抄。MES 无孤儿路由(T1 已证 15 前端页全映射),故本任务纯锚定,无收编。

## 需求(真相源 §二/§六 为准)
1. **MesMenuSeed 启动幂等种子**,接入 Program.cs 且**必须先于 RoutePath 回填块(:894-901)执行**(与 ErpMenuSeed/ErpPermissionSeed 相邻位置)。
   - 🔴硬前置①(T1 审查坐实): 既有 MES 菜单 Add 块在 :1519-1608 且全部未设 MenuKey、位于回填块**之后**→洁净部署首启 MenuKey=null 全 403。MesMenuSeed 须在回填前把 10 键锚定行(含缺行补建,照 ErpMenuSeed 兼顾缺行的做法)显式赋 MenuKey。
   - 🔴硬前置②: 菜单 310 RoutePath=/mes/machine-list 回填派生 mes-machine-list ≠ 真相源 mes-machine——防御矫正块须把此类已被错误回填的行就地纠回(作用域严限 10 锚定行)。
2. **10 个 menu-key 各定唯一锚定菜单行**(MenuKey IS NOT NULL 唯一索引禁两行共键;若存在一覧/登録双行,择一为锚另一行留 null,照 ERP 先例并在锚定表记裁决)。
3. **交付**: `docs/seeds/mes-key-menu-anchor.md`(10 键→MenuId 映射,T3b 输入)+ `docs/seeds/mes-menu-seed.sql` 对照(文件头声明 C# 为正本)。
4. **测试**: MesMenuSeedTests 照 ErpMenuSeedTests 形状(锚定/唯一键/幂等/矫正/非锚定行留 null),真实断言删实现会红。
5. 键一律连字符;MenuId 段位=既有 300-315,新建缺行(如有)段位先查占用再定,写进锚定表。

## Global Constraints
- 基线不许跌(当前 1716 绿);每 commit 立即 push。
- MenuKey 命名与 MenuId 段位先登记再播种。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t2-report.md`。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。