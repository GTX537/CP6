# M-OA/WF T4 任务简报：fail-closed 反射测试

## 背景与位置
M-OA/WF 横切接线波第五任务。T3a 已给 14 控制器 31 写端点贴 [RequirePermission](2 控制器全豁免)。同型先例=**MesPermissionAttributeTests**(最新版)——先读学三件套+豁免防腐结构。真相源=docs/seeds/oawf-permission-keys.md(合一后)。

## 需求
1. 新建 `OawfPermissionAttributeTests`:
   - **discovery 守卫**: 扫描面=Oa 命名空间 11 + Wf 命名空间 5 = 16 控制器(两命名空间,谓词覆盖两个;计数断言 16 防空扫)。基类形态自查后写准确注释(DeclaredOnly 安全前提)。
   - **fail-closed 核心闸**: 全部非 GET 端点 ∈ 贴点 ∪ 豁免;Assert.Equal(31, taggedCount)+Assert.Equal(2, exemptHit)+offenders==0 精确计数。
   - **键约定**: 键匹配 `^oa-[a-z0-9-]+$`(本波全部键为 oa-* 前缀,含 Wf 控制器上的贴点——真相源如此);action 集与真相源实际词汇逐词相等(HashSet)。
2. **显式豁免清单**: Forecast.Preview/Query.Search 两条,带真相源编号理由注释;贴键+豁免冲突场景显式捕获。
3. 反向验证: 临时删一处贴点(建议 Inbox 审批族)确认红→恢复绿,证据写报告,工作树干净。
4. 仅新增测试文件,零生产改动。

## 验证与提交
- `dotnet test --filter OawfPermissionAttributeTests` 全绿;全量(基线 1771)不许跌。
- 单 commit 即 push。

## 报告契约
报告写入 `C:\CP6\.superpowers\sdd\oawf-t4-report.md`。回复只返回: 状态、commit、一行测试结论、concerns、报告路径(15 行内)。