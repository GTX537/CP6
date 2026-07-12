# M-OA/WF T3a 任务简报：16 控制器 31 写端点贴 [RequirePermission]

## 背景与位置
M-OA/WF 横切接线波第三任务。T1 真相源(已含 T2 委派合一裁决)+T2 菜单锚定已过审。本任务只做贴点;种子=T3b。

## 必读(按顺序)
1. `docs/00-横切接线规范.md`
2. `docs/seeds/oawf-permission-keys.md`(T1 真相源合一后版本——键值逐字使用: 31 真写端点×键 + 2 只读 POST 豁免[Forecast.Preview/Query.Search])
3. 样板: M-MES T3a 先例 commit 35e90a7(git show 学贴法)

## 需求
1. 真相源列出的 **16 控制器 31 写端点**逐方法贴 `[RequirePermission("key","action")]`,键值逐字一致(连字符;8 高危不得降级;三个 delegate 端点统一贴 `("oa-settings","delegate")` 照 §六注4)。
2. **2 豁免不贴**(Forecast.Preview/Query.Search)。
3. **双栈端点照贴**: 旧栈 Flow/Form.SaveDef 等已在真相源定键(oa-designer:*)——退役裁决未出前照真相源贴点(不裸奔);若日后退役端点连属性一起删,无冲突。本任务不删不改任何路由/端点。
4. 纯注解叠加: 零方法体改动,类级 [Authorize] 保留。
5. 交付自查表: 逐控制器 1:1 对账(31=31)写入报告。
6. 顺手项(审查处方): Program.cs OawfMenuSeed 接线注释的漂移行号(:857/:908)改为内容描述(「紧随 MesPermissionSeed 之后」「下方全局回填块之前」),去行号化。

## 验证与提交
- dotnet build 0 Error;全量测试(基线 1764 绿)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\oawf-t3a-report.md`。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。