# BUG #721：字段缺省与null操作

[Issue #721](https://github.com/GTX537/CP6/issues/721)，精确基线094627927cb50e60f896a76d1d9a35216119020f，COORD在固定DEV1分支codex/bug-721-field-unset执行。W285已保存远端72282ff657871818b41363f662bba9bbb5921b80并官方暂缓释放席位，附件代码未混入本修复。

普通表单字段默认提供“移除填写”和“设为空值”两个明确动作；退回patch显式allowUnset=false仍仅提供设为空值。

Vue会将省略的Boolean prop转为false；此前allowUnset?: boolean没有默认值，模板allowUnset !== false因此隐藏移除按钮。withDefaults明确默认true，保留调用方false，递归明细仍传递同一策略。未改变数值精度、删除键与JSON null语义，也未放开只读字段。

实际Vue组件挂载的15项相关行为通过：既有精确数字/明细/缺省与null原回归恢复，新增明确false和只读无按钮检查；当前PersonalFormEditor与NativeTaskOperations相关行为同时通过。vue-tsc --build通过。

未修复原主线的原组件和原已有测试已先失败：D:/CP6-local-test/wave285/foundation/ui-baseline-unset.log，探针C:/Users/tt/CP6-planning-reports/20261017-foundation/wave285-baseline-field-probe.json。原步骤在本分支实际组件中重跑通过，绿测D:/CP6-local-test/bug721/foundation/ui-green.log，类型检查同目录typecheck.log。DOM实际点击数值输入、移除及置null；明确false保留键且赋null，只读无修改按钮。未将组件mock的API称为真实业务HTTP验收；该缺陷本身是Vue默认prop行为，无数据库/后端改动。

一次作者集中审查完整差异：一项默认值修正、行为回归与本报告/四状态，无W285文件或其他范围。没有Actions触发/取消、工作流修改、数据库迁移或部署。原375/59局部/125/247状态不变。

Issue保持Open至正常PR合并并核对远端main包含源码后关闭；准确任务分支删除、干净工作区及官方finish释放由C:/Users/tt/CP6-planning-reports/20261017-foundation/bug721-final-verification.json核销。然后官方resume原W285，正常merge最新main，继续24h有效执行目标。
