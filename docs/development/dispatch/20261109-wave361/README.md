# W361 RMA 原操作日志的原生权限

W361 实际于 2026-11-09 09:04:56 UTC 领取 DEV-2，RMA-NATIVE-JOURNAL，`codex/w361-01`，起始主线 `baff5fc20dceee932c805fbf309e6542b960cbeb`，隔离 PostgreSQL 55567。原估 26–45 分钟，实库与集中复核证据齐备 1,489 秒，远端收尾另计。W361 开发与 W363 归档并行，主线采用串行。

W363 随 #817 正常采用，实测完整 1,168 秒，准确本地和远端任务分支删除，固定 DEV-1 干净释放。W365-VERIFY 独立实测普通登录的命名空间隔离，完整 703 秒，INTEGRATION-1 干净 detached，无任务分支且正式释放。 [W363](../../evidence/W363-01/DELIVERY.md) · [#817](https://github.com/GTX537/CP6/pull/817)

原日志安装使用真实 W351 发布初始化和原 W353 commissioning。A/B 真实普通登录同键各自读写，原事务回滚及原认证根等待可复现。现有旧 CLI 对写入后的原 commissioning 返回明确错误，当前 CLI 恢复同一原字节。下一步按采用顺序推进普通宿主 draft 和归档的原日志片段。 [W361](../../evidence/W361-01/DELIVERY.md)
