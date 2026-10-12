# S01-01 交付记录

本任务只交付 `CP6-S01-CONTRACT-INPUTS/1.0.0` 输入包，消费者实现留给 S01-02 至 S01-11。基线 `df5553fd04ec4fc516602a4e2843650c38ae5006`，分支 `codex/s01-01-exact-inputs-20261012`。用户确认对话 A 兼任本任务协调；原 193bb05c 准备提交及当时未领取记录保留。

包包含 33 份来源、16 个 profile、122 条向量；原 23 条向量的 wire_type/input/accepted/pair 保持，case ID 与来源 pointer 分开。每个 profile 明确 Owner、实际目标消费者、原/派生 hash、补丁集合和 Schema/类型身份。ERP current/frozen 注册表分开，ATP PostgreSQL DDL 保留 PLAN-IF-004/005，CRM-INQ 原 AC-INQ-009/048/053 与19条修订引用只覆盖合同子断言。

## 验证与审查范围

- [包检查](package-check.json)：61 次原/派生文件 SHA-256 核对、22 份本地引用 pin、原生 Schema 与预期数据一致、16 profile 覆盖、原23向量保真、来源引用、链接、业务及其他任务台账不变。命令：`python docs/development/evidence/S01-01/verify-package.py --out docs/development/evidence/S01-01/package-check.json`。
- 向量生成器 `--check --verify-native-fields`：现有产物与来源/具名预期一致，122条、9个source pin；3份准确Schema的67个原生字段指针全部解析并匹配scalar profile。此检查不执行待实现消费者。
- [准入台账检查](entry-check.json) 与收尾台账检查只核对唯一登记、依赖/资源和生成页，不运行业务用例。
- 实际解释器为本机 CPython 3.13；无编译业务/全仓测试/重建镜像，无跳过测试冒充成功。准备核验的58项结果留在 [input-check.json](input-check.json)，新增包核验是扩大到准确注册表、原字段Schema及最终向量的实际检查。
- 收尾审查覆盖分支对 main 的完整 diff；修复只复查受影响部分。审查发现的FIN-CREDIT内联字段绑定遗漏已补齐；仓库来源使用Git blob字节pin，新包/生成器固定LF，最终报告更新到实际字节。初次报告保留为 [package-check-initial.json](package-check-initial.json)。最终审查记录与远端包含性在实际产生后追加。

## 未验证范围与后续消费

125 业务目标、13,404 原验收关联、247 修订运行场景和41历史静态状态保持原值。无 C#/TS 消费者运行、真实数据库、权限/事务、业务写者切换或生产验收。单字段投影、类型接线和构造的字节样例不证明整个业务对象或完整流程通过。

S01-02、S01-05、S01-09 可在本任务远端交付核实后重新核对准入并领取；本任务没有自动启动它们。S01-03/04/06/07/08/10/11 继续遵守各自依赖与 Owner/数据库门。

GitHub 普通工作流手动触发；R2 candidate 只响应受保护版本 Tag。每次 push/PR/merge 前重新核对候选和远端主线，禁止启动远程编译测试。无取消项、触发变更、保护变更、数据库或部署。
