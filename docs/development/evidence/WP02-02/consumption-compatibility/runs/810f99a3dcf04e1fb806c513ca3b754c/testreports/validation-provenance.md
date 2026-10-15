# Graph 验证记录来源

本 owner 只保存 graph alias/junction 增量回归证据；没有执行 .NET、restore、宿主或实际依赖图检查。

`graph-alias-red.log` 与 `graph-alias-green.log` 是 Python subprocess 捕获 stdout 与 stderr 合并流后原字节写入的本次执行日志。相邻 JSON 保存实际 argv、退出码、日志 SHA-256 和当时 helper/test 文件 SHA-256。测试构造的 junction 及其目标全部位于另一个新测试 owner 内，不引用任务外路径；分别验证普通路径和 Windows 扩展路径下 contained 与 recorded_cache 拒绝原始 alias。

- RED：1 项测试的 4 个 subtest 实际失败，原因均为未抛出预期 ValueError；退出码 1。
- GREEN：同一项测试的全部 subtest 实际通过；退出码 0。
- 修复：canonical 在任何 resolve 前检查原始逻辑路径的 link/reparse 属性；预期 Platform 项目位置也走同一检查。

此前本子代理执行的初始 6 项 ERROR（实现缺失）、随后 6 项 GREEN、扩展到 8 项 GREEN、扩展到 10 项 GREEN，以及共用 snapshot 长路径工具后的 10 项 GREEN，仅保存在对话工具调用的控制台返回中，没有另存原始 stdout/stderr 日志文件。本说明不重建这些日志，不把当前重跑冒称此前执行；此前测试生成的隔离临时输入不等于原始控制台证据。主对话另行保存的整套测试日志按其实际 owner 和当时输入引用。

本次只重跑该新增回归，既有未受影响结果按 AGENTS 节奏保留其真实范围。
