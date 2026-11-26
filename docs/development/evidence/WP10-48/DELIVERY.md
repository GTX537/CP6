# WP10-48 实物版模工程候选交付

本记录供 [WP10-48 PR #1331](https://github.com/GTX537/CP6/pull/1331) 验收。受保护 main 正常合并且实际核对包含性后，同 PR 的卡级 DONE 生效。交付祖先和本机受测产品为 44aef14a239995a22488e902b6cbfbc7839bfd9f，基线为 204464f95bfffb067f11bd3ba9edc6224af8f7d7，实际合并 SHA 由 PR 回执给出。

## 本卡范围

在 LAPTOP-3QQ44FJS 的 DEV-1 由 CP6-EXEC 执行。现有 W160 实物稳定身份和当前来源、W161 实物登记与独立计量、W164 精确来源接入和 W165 双角色及同事务 HoldExact 复用。本次补齐 WMS 实物页按完整 WdPtnNo 和显式 WdRev 读取全部当前可选草稿。保留 BaseCd 和原 Owner 的 UUID、版本、摘要。按有资格的实际可见候选分页，兼容调用保留 data 数组。页面切换查询或继续读批次时清除上次选择。DRAFT / NOT_REQUESTED 保持，普通库存写入为零。

## 本机验证

- PostgreSQL 18.6 和 .NET 10.0.404 在回环隔离实例使用合成数据，Release 锁定还原和构建通过。现行架构首轮 87 PASS、1 FAIL、0 SKIP。唯一失败为旧迁移夹具用当前 EF 写旧表缺少的 AuditAtUtc。按旧表字段和类型准备记录后，同一用例定向 1 PASS、0 FAIL、0 SKIP。结果覆盖原生 JSON 和角色动作数保留，以及只回填一条存在的版本 1 原件。88 个独立用例均有通过证据。
- 隔离多 Base 压力 1 PASS、0 FAIL、0 SKIP。完全相同编号和业务版次的第 101 号 Base 合格草稿排在旧粗筛第 102 位，由实际 Main HTTP 第一批返回。HTTP 50／50／5 共 105 条，来源前进后重新查询 104 条且原件可回读。Chrome 在 /wms/plate-mold 实际登录后读取 50／50／4，选中末页准确版本。读前后库存余额、交易、StockMovement 原回执和关联提案为零。Vue 18 PASS，Web 类型检查及 Vite 构建通过。
- 正式数据库同时使用 IX_T_PlateMold_WdPtnNo_WdRev 的 TenantId/WdPtnNo/WdRev 唯一性和 UX_T_PlateMold_NoRev 的 WdPtnNo/WdRev 全局唯一性。一次性合成压力库专用 TenantId/BaseCd/WdPtnNo/WdRev 索引用于验证分页算法。候选无数据库架构迁移。现行架构回归覆盖精确单项、两 Owner 同轮确认、HoldExact 竞争、原键、旧计数及实物登记。
- 三份主要原件 SHA-256 依次为首轮 TRX 8cdee54c15dcd9c626a5d8a9c7bd4c27c6530d84234b9240f6d9b6b8d6d64eb8、旧数据迁移回放 TRX 5f73a0e0730e74ae9cf200480acc745964a53e33f3ca0d9e7d47af2398b5cd7b、多 Base HTTP/Chrome TRX fea9d4d760b11cf22a34be81b9a0694ce709731759ab3370881f249cd5f70ac1。产品 API SHA-256 8eae4ba6719bdc669646227ca48202833dd48589a7a5b05a37b1e81138463c1c。证据目录为 D:/CP6-test-artifacts/WP10-48/exec-20261126T042009Z。独立复核文件为目录内 HANDOFF.md。8 个隔离实例经读回均已停机。

## 正式验收和收尾边界

本卡只核销版模实例身份、当前头、来源及 HoldExact 提供者。严格前置 13 项。WMS-TOOL-01 保持 PLANNED，12 条原场景和 2 条必需补充场景仍为 NOT_RUN。真实外部原件 UNPROVEN，真实生产使用、退役、完整目标验收和路线资格列为原卡余项。

本卡 M 档记账上界 5653.555 秒，累计 232480.906 → 238134.461 秒，已剔除轮次间等待。最终采用、同树冒烟和 DEV-1 保管收尾的读回位于 D:/CP6-test-artifacts/WP10-48/publish-20261126T063305Z。OA 暂停，下一卡未指定。
