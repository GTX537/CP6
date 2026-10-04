# BUG163 集中事务审查

审查者：同一任务的只读 code reviewer；基线 ce49012d37da4b1b01f074d5b5b7fd227780e656。审查覆盖未提交完整功能/测试 diff（含新 partial 文件），根执行者核对以下源码哈希的适用性。

未发现 P0/P1/P2 阻断。六个命令独立调用时使用同一 DbContext 的 ReadCommitted 本地事务，两个 SaveChanges 与查询均位于事务内；事件与幂等回执保存后才提交，await using 在异常或取消时释放未提交事务。调用方本地事务保留提交/回滚权，预打开连接的 ambient Required 参与与失败 abort 语义保留。Pack/Move、公开接口、DTO、数据库配置和连接池设置未改动。

审查者已读取双库原 TRX：各25P/0F/0skip，四个独立命令 trace 无 ambient 且事务命令只有一条物理连接。有效种子 RED SQL17=3P/14F、PG17=10P/7F；2492项输入的 RED/GREEN 仅两份生产服务不同，当前全部匹配 GREEN，两库源码/运行时清单一致，七个冻结模块哈希一致。真实写入后的故障/取消控制与原生 SQL DTC 复现分开陈述。未运行构建、.NET、API 或数据库进程。

本文记录一次任务级审查，后续仅核对新增文档/归档及变更部分。远端交付、合并后冒烟、临时库清理及完整 WP6 尚未完成。

## 被审查源码的适用哈希（根执行者记录）
CP6.Core/Services/Wms/LpnService.cs SHA256 0C634B9D2A42FEEBF1D5DE27C2EDA77E9210DBAEBC86D95DE83E0DBED9E18BF6
CP6.Core/Services/Wms/LabelJobService.cs SHA256 1EA36FC9D4BC2F3890F93F2458593DFD4BDD9A893D41C84F02DCD521004B9366
CP6.Tests/WmsProductionSqlServerTests.cs SHA256 75A263CF2883045529F4CB85993864F0AB65EBD3A3E74595BEDFC875AD83883B
CP6.Tests/WmsProductionLocalTransactionTests.cs SHA256 1A5A2D0225EC411A5011A75BBB674BEA254F31AED14F1FB7C754ACDC41ADE39D
