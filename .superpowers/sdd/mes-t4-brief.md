# M-MES T4 任务简报：fail-closed 反射测试

## 背景与位置
M-MES 横切接线波第五任务。T3a 已给 9 控制器 28 写端点贴 [RequirePermission]。本任务写反射测试锁死「新写端点漏贴即红」。同型先例=**ErpPermissionAttributeTests**(CP6.Tests/ErpPermissionAttributeTests.cs,含终审后注释纠偏)——先读它学三件套+豁免防腐结构,再 MES 化。

## 需求
1. 新建 `MesPermissionAttributeTests`:
   - **discovery 守卫**: 扫描面=Mes 命名空间 11 控制器,断言计数(防空扫假绿)。注意基类问题: ERP 版类头注释已写明 DeclaredOnly 安全前提(基类无 HTTP action)——MES 控制器的基类形态自查后照写准确注释。
   - **fail-closed 核心闸**: 全部非 GET 端点 ∈ 贴点 ∪ 豁免,且 Assert.Equal(28, taggedCount)+Assert.Equal(2, exemptHit)精确计数+offenders==0。
   - **键约定**: `^mes-[a-z0-9-]+$` + action 集与真相源实际使用词汇逐词相等(HashSet)。
2. **显式豁免清单**: PlanAchievement Summary/ExportCsv 两条,带真相源 §四 编号理由注释;既贴键又在豁免的冲突场景显式捕获(照 ERP 版)。
3. 反向验证: 临时删一处贴点跑测试确认红,恢复后绿,证据写报告,工作树干净。
4. 仅新增测试文件,零生产改动。

## 验证与提交
- `dotnet test --filter MesPermissionAttributeTests` 全绿;全量(基线 1727)不许跌。
- 单 commit 即 push。

## 报告契约
详细报告写入 `C:\CP6\.superpowers\sdd\mes-t4-report.md`(豁免对账+反向验证证据)。回复只返回: 状态、commit sha、一行测试结论、concerns、报告路径(15 行内)。