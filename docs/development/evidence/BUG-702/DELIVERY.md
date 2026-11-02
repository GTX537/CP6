# BUG #702：受控文档文件绑定事务门

[Issue #702](https://github.com/GTX537/CP6/issues/702)，基线`32103247c36e3f44bef82494d550e7c98a1878f8`。COORD单人在DEV2独立分支`codex/bug-702-file-bound-source`修复；W274现场保留在DEV1，Handoff任务安全保存并暂缓释放DEV2后借用。

## 根因与修复

修复受控文档绑定可用文件时丢失存储事务门的问题：同一FileContentStore读取状态及验证原始字节，保留当前权限、暂扣状态和内容完整性检查。

BoundSource先用ReadOwnedAsync创建并取得事务门的store读取快照，requireAvailable分支却另建未取得事务门的store调用Row，造成合法文档创建在实际业务链上503 AUTHORITY_FENCE_UNAVAILABLE。现在通过私有重载传入同一个store，权限和Fence仍在原权威入口执行一次，Row直接复用已成立的内部契约。没有删除或放宽FileContentStore.Transaction保护，没有新增迁移或变更公开合同。

## 验证与一次集中审查

6项相关PG18行为通过、0跳过：新增回归先在未修复主线失败，再通过；原迭代撤回故障注入、文档创建/历史/原重放、错误引用/权限撤销及文件损坏隔离回归均通过。业务数据、身份及文件存储为模拟，数据库与Owner事务真实执行。

新增场景按同一原操作执行：暂扣拒绝且无文档；释放后篡改实际存储字节仍拒绝且无绑定；恢复原始字节后创建成功，原命令重放唯一结果，原字节下载成功。红测实际错误为AUTHORITY_FENCE_UNAVAILABLE而非预期完整性错误，证据`D:\CP6-local-test\bug702\foundation\red-20261102T172032Z.json`；绿测`D:\CP6-local-test\bug702\foundation\green-20261102T172244Z.json`。首轮W274复现失败另保留在D:/CP6-local-test/wave274/foundation/tree-withdrawal-20261102T170826Z.json，不更改其历史结果。

作者一次SPEC/QUALITY审查完整差异：仅同store复用及行为回归，检查权限/父来源/精确引用/字节校验不变、失败原子性及恢复原命令；相关编译随本地测试完成。无前端、Actions触发/取消或工作流调整、现有环境迁移和生产部署。W274未提交子流程代码未混入此修复。

## 采用与后续

原375/56局部卡统计及24小时计划不因BUG修复改变；总目标仍IN_PROGRESS，远端采用后继续W274子流程树撤回验收。Issue关闭以前必须核对远端main含本提交；分支处置、干净工作区和官方finish释放以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug702-final-verification.json`核销。
