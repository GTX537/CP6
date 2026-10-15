# Issue #216：畸形配置的结构化拒绝

基线：Main `81c28d264ef89916fb4d419b1572c73c8896d5a5`。
[Issue #216](https://github.com/GTX537/CP6/issues/216) 的根因是配置 JSON 解码后直接调用
`.get()`；合法 JSON 数组或标量使工具抛出 AttributeError，无法正常输出拒绝原因。

最小修复：读取已核哈希的配置后，先验证其为 JSON 对象，再检查 `state=CONFIGURED`。
非对象返回 `CONFIGURATION_OBJECT_REQUIRED`，目标运行仍为 false；保留正常配置路径、
采纳/覆盖/运行分离和原信任校验，不扩大异常捕获范围。

2026-10-15，Windows / Python 3.13 本地验证（从 `docs/development/tools` 执行）：

- 修复前：新增回归的数组、null、布尔、整数、小数、字符串七种输入均重现异常。
  `python -X utf8 -m unittest test_acceptance_basis.AcceptanceBasisTests.test_non_object_configuration_returns_structured_runtime_rejection`；[red.txt](red.txt)。
- 修复后：`python -X utf8 -m unittest test_acceptance_basis`，23/23 PASS；包含上述回归和既有正常配置路径。
  [green.txt](green.txt)。这是隔离算法测试，不是真实采纳或业务验收。
- Issue 原复现步骤重新执行，数组配置返回正常 JSON 结果和准确拒绝原因；
  [original-repro.txt](original-repro.txt)。原失败记录保留。

本任务仅需上述相关验证、一次集中审查和差异检查；不运行全仓测试。
审查与远端 main 核对后关闭 Issue。正式采纳、发布及业务状态不变；
没有 Actions 运行/取消/触发配置变更、签名发包、数据库操作或部署。
