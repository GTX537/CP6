# OA P1 持续优化执行记录：SFS 子表

日期：2026-07-23  
状态：本地实现与自动化验收完成  
前置：OA P0 Foundation 保持原有 `CONDITIONAL` 发布结论；本切片不改变其外部 staging/旧二进制门禁状态。

## 1. 本轮决策

P1 第一切片选择 SFS 子表，而不是直接开放附件：

- 子表直接覆盖采购明细、费用明细、物料清单等日常 OA 表单，是“可实际使用”的高频基础能力。
- 子表数据嵌入现有 `DataJson` 并随 `FormDefVersion` 固化，不新增数据库结构，也不影响旧表单数据。
- 当前通用附件接口仍以登录授权为主，缺少稳定的业务归属/实例参与者级校验；在该边界补齐前，不把附件控件接入 OA 运行态。

## 2. 已交付范围

| 能力 | 结果 |
|---|---|
| Schema | 新增 `table` 字段、`columns`、`minRows`、`maxRows` |
| 子表列 | 支持单行文本、多行文本、数字、下拉、日期、日期时间 |
| 表单设计器 | 控件库新增“子表”；可配置列标识、标题、类型、必填、长度、正则、选项和行数范围 |
| 设计时校验 | 阻止空/重复列、无选项下拉、不支持类型、非法正则、非法行数范围和超限配置发布 |
| 运行态 | 表格化编辑、添加/删除明细、最大行数提示；readonly/hidden 字段权限继续更严优先 |
| 草稿 | 允许缺少必填行/单元格，便于中途保存；仍拒绝未知列、错误类型、嵌套对象和超限行数 |
| 正式提交 | 后端复核数组形态、行数、未知列、列必填、类型、长度和正则，失败不落库 |
| 版本兼容 | 子表定义与数据均沿用表单版本固定机制，旧版本数据不迁移、不重写 |

## 3. 数据契约

```json
{
  "name": "items",
  "label": "采购明细",
  "type": "table",
  "required": true,
  "minRows": 1,
  "maxRows": 20,
  "columns": [
    { "name": "material", "label": "物料", "type": "input", "required": true, "maxLength": 50 },
    { "name": "qty", "label": "数量", "type": "number", "required": true },
    {
      "name": "unit",
      "label": "单位",
      "type": "select",
      "options": [{ "label": "个", "value": "pc" }]
    }
  ]
}
```

对应数据保持扁平行对象：

```json
{
  "items": [
    { "material": "A-01", "qty": 2, "unit": "pc" }
  ]
}
```

禁止人员、部门、附件或子表嵌套，避免 P1 阶段出现无法稳定授权或查询的复杂对象。

## 4. 验收标准

- [x] 设计器生成的子表 schema 能保存、发布并由运行态直接消费。
- [x] 正常明细可在新建、草稿重开和提交链路中保持 JSON 一致。
- [x] 草稿允许未完成的必填单元格；正式提交必须完整。
- [x] 未知列、非对象行、错误数字类型、超长文本和超限行数均由服务端拒绝。
- [x] readonly 掩码下不显示增删操作，列输入控件不可编辑。
- [x] 非子表旧 schema/旧数据不要求迁移。
- [x] 前端全量测试、类型检查、生产构建和后端全量测试通过。

## 5. CLI 证据

```text
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --filter
  "FullyQualifiedName~FormServiceTests|FullyQualifiedName~DraftServiceTests|FullyQualifiedName~FormSubmissionServiceTests"
结果：25 passed / 0 failed

bun run test -- src/views/wf/subtable.spec.ts src/views/wf/designer/designValidate.spec.ts
结果：11 passed / 0 failed

bun run test -- src/views/wf/DynamicForm.subtable.spec.ts
结果：2 passed / 0 failed

dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --nologo
结果：2285 passed / 0 failed / 7 skipped（既有环境门禁）

bun run test
结果：81 files passed / 515 tests passed / 0 failed

bun run build
结果：vue-tsc 与 Vite production build 均成功
```

OA/WF/PUR 专项回归另为 `784 passed / 0 failed / 2 skipped`；跳过项是既有 SQL Server 环境门禁，不属于本切片。

## 6. 下一优先级

1. 附件安全底座：业务归属 token、实例参与者/字段权限校验、上传暂存与提交绑定、删除审计。
2. SFS 附件控件：在安全底座之上接入设计器、草稿、提交和详情。
3. SFS 布局与可视化规则配置。
4. 字段查询/导出和 FlowOps 运维驾驶舱。
