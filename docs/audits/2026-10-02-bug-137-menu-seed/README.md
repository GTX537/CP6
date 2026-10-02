# BUG #137：采购对账菜单首次初始化资源键

[Issue #137](https://github.com/GTX537/CP6/issues/137) 的根因是 Program 在全局 `MenuKey` 回填之后插入菜单 708，插入时没有资源键。首次初始化留下 NULL；第二次初始化补键并新增字段审计。新插入直接写入 `pur-reconcile`，已有菜单配置和授权保留。

功能执行源为 `bab643750111beef610eba911bb530b422f9d99f`，基线 `d6074aaad3098b61adaeda902b92bf24b3eb0c04`。真实 compiled API 的 SHA-256 为 `20894153768F6AFA1F3D5B3307597B1E9CFBD55ACC52DC1D29AAB2D62CE625E2`；两次使用同一二进制。后续证据及文档提交不改变这三个功能源码文件，复用测试结果。

- [单元测试索引](verification.json)：旧逻辑 **2 Failed / 1 Passed / 0 Skipped**，修复与采购权限、字段审计相关测试 **20 Passed / 0 Failed / 0 Skipped**。原始 [RED TRX](bug-137-seed-red.trx) 和 [GREEN TRX](bug-137-seed-green.trx) 保留；实际 CP6Context/InMemory，不代替真库。
- [首次真实初始化](sql-first.json)：SQL Server `16.0.1000.6`、`Chinese_PRC_CI_AS`，准确命名且 owner 标记匹配的独立空库；执行原有 Core 136 / Space 47 条迁移和真实应用 seed。退出 0，完成标记存在，隔离端口没有 HTTP 监听。
- [首轮状态与保留夹具](sql-prepare.json)：直接验证菜单 708 已有 `pur-reconcile` 和 `/pur/reconcile`，随后统计 **351 张实际表**的行数，并设置全局翻译、租户翻译、管理员昵称保留夹具；密码只在本机摘要比较，不归档密码或摘要值。
- [第二次真实初始化](sql-repeat.json)及[原步骤复测](sql-verify.json)：同一 compiled API 再次退出 0，不启动 HTTP；**全部 351 张表行数及迁移历史完全一致**，包括字段审计。全局/租户翻译、管理员昵称和原密码哈希原样保留。

真实库为兼容任务所有的 `CP6Compat_WP2_20261002_36ef9cae`，凭据与可写连接均留在忽略目录，现有环境未变。真库检查使用兼容任务的原生探针；报告中的其他运行库摘要只标识探针加载输入，不把 WP2 模型或尚未执行的 generation 升级算作本 BUG 的验收。

任务级完整功能 diff 已审查；只读核对候选及受保护远端主线的 13 条 GitHub 工作流，12 条仅手动、1 条仅受保护版本 Tag。Azure 桥手动，DEV 只依赖桥完成；分支 push / PR 不触发它们。没有排队或执行中任务运行，取消 0 项，触发配置未修改，未运行 Actions / Azure / 部署。当前等待正常 PR 和远端 `main` 包含性核对，Issue 在此时仍 Open；业务与生产验收不在此最小修复范围。
