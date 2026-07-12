# M-ERP T4 任务简报：fail-closed 反射测试

## 背景与位置
M-ERP 横切接线波第五任务。T3a 已给 10 控制器 35 写端点贴 [RequirePermission](commit bdbc532)。本任务写反射测试把「新写端点漏贴即红」锁死——与已合并的 WmsPermissionAttributeTests(commit 0efb717)同型,先 `git show 0efb717` 学其结构。

## 需求
1. 新建 `ErpPermissionAttributeTests`(CP6.Tests),照 WMS 先例三件套:
   - **discovery 守卫**: 断言扫描到的 ERP 控制器数=实际数(防命名空间移动后空扫假绿)。扫描面=T1 真相源 §七 的 15 控制器(以程序集反射按命名空间/路由前缀圈定,方式照 WMS 先例)。
   - **fail-closed 核心闸**: 全部非 GET 端点要么贴 [RequirePermission] 要么在显式豁免清单——贴点数 35=35 精确;新增写端点未贴且未豁免即红。
   - **键约定断言**: 键匹配 `^erp-[a-z0-9-]+$`(连字符,禁下划线),action 属于真相源实际使用的 action 集合(逐词相等)。
2. **显式豁免清单**(与真相源逐条对齐,每条带理由注释):
   - 11 个只读 POST 豁免(真相源 §一 逐条);
   - `EstimateCalcController.Calculate` 挂 [AllowAnonymous](主控裁决保留,已记终审票待用户裁处)——豁免清单中显式列出并断言其确实挂着 [AllowAnonymous](防止有人删了 AllowAnonymous 却忘贴权限)。
3. 反向验证写进报告: 任选一贴点临时删属性跑测试确认变红(验完恢复,工作树干净)。
4. 仅新增测试文件,零生产代码改动。

## 验证与提交
- `dotnet test --filter ErpPermissionAttributeTests` 全绿;全量测试(以 T3b 后基线为准)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\erp-t4-report.md`(豁免清单对账+反向验证证据)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。
