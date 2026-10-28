# W193：工程来源固定与完整性检查

COORD 单人推进七天 D3 的 WP05-11。W192 已核实远端采用、准确分支删除及席位释放，本轮只归档 W192 和 WP09-11.wave192_quality_specification；不重复实现。

## 原要求与交付

依据 ENG-REV-01 原文 REQ006/007/008/013、PROC005、DATA006/007、CD01–12，原 AC005–008/013/016/028/031，以及 R1 PE-ENG-TOOL-OWNER-PARTICIPANT。采用真实 Main PostgreSQL Owner，不登记 SQLServer WP0 原型，不以顺序 HTTP 或本地静态签名冒充最终共同提交。

1. 真实产品类别与已批准配置政策中的明确工程检查规则，准确候选/配置/来源原文、水位和当前资格；完整 CheckRun 与逐项 PASS/BLOCK/UNKNOWN/NOT_APPLICABLE 持久保存。新增政策范围明确版本化，旧 OPTIONS_ONLY 不自动升级。预计120–210分钟。
2. 各 native 来源参加能力与完整依赖，明确源不可达、权限/配置变化及旧检查失效；准确 API/页面和同键原结果恢复。技术检查不创建基线或授予许可，冻结消费者必须同事务重验。预计60–90分钟。
3. 定向 PostgreSQL/实际 HTTP/页面及并发验证，一次任务级 SPEC/QUALITY 审查，正常 PR、Handoff、台账与四状态文件、远端采用核对、准确分支清理及 finish。预计60–120分钟。

总计240–420分钟（4–7小时）。单人固定 DEV-1/Main、DEV-2/Handoff，不新增目录。PostgreSQL 唯一新增数据库，保留缓存，不运行 Actions、既有环境迁移或生产部署。

## 完成边界

- 检查输入、准确政策版本、每个发生位置、当前 source/control 水位及原始结果不可覆盖；源无法观察时保存本次 UNKNOWN 并保留既有记录。
- NOT_APPLICABLE 必须由当前产品类别/批准政策给出依据；未知、空政策、不可达或缺少 native 参加者都不得自动放行。
- 全部声明来源和依赖参与同一 UoW 的当前核验；最终消费者只可重验准确 CheckRun，不允许重用过时绿灯。真实源权限撤销仍拒绝泄露历史正文。
- 技术内容摘要与检查审计/评审/发布事实分离，保持发生位置和语义顺序。
- 工程评审/正式冻结/WO/SCHED 仍由后续直接任务采用；本轮不关闭整个 ENG-REV、125目标或247正式验收。原七天 D2/D3/D5/D6/D7 全部剩余出口保留。

## 实际交付

[报告](../../evidence/W193-01/DELIVERY.md)，登记49.4分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
