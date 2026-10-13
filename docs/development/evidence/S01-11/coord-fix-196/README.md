# Issue196：原始字节所有权修复

原候选 `b27fbe54` 的 TypeScript Uint8Array 参数接受 Node Buffer，但 `.slice()` 对 Buffer 返回共享视图。COORD 在接收 S01-11 时确认外部修改会使存档字节、已记录摘要和冻结重放不一致；[Issue196](https://github.com/GTX537/CP6/issues/196)作为唯一活动缺陷记录。[初审及原复现](S01-11-original-validation.json)保留。

修复在 schema bundle、解码、摘要输入、原字节捕获、冻结请求/响应捕获和返回值边界统一 `new Uint8Array(raw)` 独立复制，继续接受合法 Buffer，不改变 wire 域、身份、C01/C02 原正文或 Owner 权限边界。

新增12项覆盖 current/frozen、验证期间/之后修改、返回值修改、schema 固定源、原字节捕获和C01/C02重放。完整RED为原416通过、新12全部失败；修复后目标TS **428/428、零跳过**，类型检查通过。Node TextEncoder 与 jsdom Uint8Array 分属不同 realm，五处旧字节断言改为比较逐字节值；向量、fixture内容和合法输入预期不改。[原步骤复测](S01-11-probe-recheck.json)确认摘要和存档一致、原生边界保持、修改后的重放请求被拒绝；第二个历史观察脚本的退出1是预期拒绝，完整正反重放由新增自动回归证明。

[验证记录](validation.json)固定实际运行命令、环境、未提交工作字节、结果和原执行基线，最终提交只是后续绑定。第一次测试编写问题和首次GREEN的跨realm断言失败均保留，不掩盖历史失败。原C#417、设计审计5项及其固定输入未改，按项目规则复用准确证据；原TS416保留为历史。本轮只复查受影响代码及测试，不重启完整93文件审查。

真实 Owner/Store/数据库/HTTP 业务仍 NOT_RUN。无 Actions运行/取消/触发配置改动，未迁库或部署。COORD 会在远端main确认修复后关闭Issue并更新唯一台账；原候选PR195保留为被替代的交付记录。
