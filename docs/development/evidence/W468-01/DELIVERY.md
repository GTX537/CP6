# W468-01 — v11 候选文件原字节落盘

基线 `758c74803324a8b9d5aeae7d12ed47aedb068082`；WP02-02。沿用 W467 实际 PostgreSQL 暂存及原回执，原 W429 v11 同源归档，实测Windows文件根。只新增 v11 格式识别与暂存/manifest 同历史元数据的绑定；原 pin、存储键、文件校验、排他落盘、恢复及旧格式合同保持。

- 同一实际暂存回执的 SHA-256 为 `ecdd7f71acf874c5a2053b3c4591a5f9b16c6808a7bb0e818292d94d56726dae`。原入口真实退出1、拒绝v11、无目标目录；最终以同一回执、原manifest和原LocalFileStore，按实际原键落盘45字节，文件SHA-256 `540842f6e6966d8f33243ad24bf8a88b74a408553d229e233a856d55c959fb76`。候选只包含所选租户的原文件，另一租户和所有源文件/归档保持。
- 持久 `ready/receipt.json`、同键重送及无源参数的 `recover` 原样回读，实际SHA-256 `5041381d6a540bb6c05d4dbb0778e627aaeddd2556168de5ef6eed7fc1a04d3a`。两个真实进程竞争退出1/0；只有一份ready，原回执和文件可读。错暂存pin、历史元数据不匹配和同长度错误源文件均拒绝，未产生目标。
- 原W412真实v10的41字节按原键写入并原样恢复回执。保持 `MATERIALIZED`、`recoveryReady=false`；候选根仅在私有隔离目录，未连接应用或Owner数据库。

实际证据 `D:/CP6-local-test/CP6-72H-20261109/W468-01/`，保留原/最终Windows进程退出、原文件、回执、并发及错误。只执行相关Python文件验证，无数据库/CLR/Actions/部署；本窗进程已退出。一次集中SPEC/QUALITY覆盖原文件根、实际竞态及旧协议。

仅补WP02-02文件候选段；FileContent Owner三表和原操作、真实Engineering父版本、宿主消费、ERP/Stock、权限、消息对账和正式PGT-021/022仍分别验收。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN。W467经#935采用且释放，16.71–16.80分钟；本窗12–24估计按原生边界校准。44h158458与48h172800保持。
