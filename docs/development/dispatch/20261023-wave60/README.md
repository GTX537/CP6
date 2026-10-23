# W60 / 七天一期 D6：PostgreSQL 不可变日历快照存储

COORD本聊天单人，2026-10-23T09:15:15.079287+00:00。Main `a31c4c180d8ac1101eb6102e6b7c7cedddc65d7a`，CRM `611e95cfc83928c192070c5b101f2c6b9f202183`，Handoff `4e6cb8a25ae06931ec590df95384c3abbbfa1f57`。W59 CRM104/Handoff35/Main428全部采用，204项相关本地用例通过，登记实际26.70min、前置约4min；准确任务分支已删除，固定树干净detached，席位全部释放，任务Actions为0。W37–W59不重复，七天持续目标ACTIVE。

## 前置与明确边界

最新AGENTS/POLICY、台账、工作区status/capacity、固定目录origin和三仓重复PR已核对。WP03-01依赖S01-08/S02-00/WP02-09；前两项已完成，WP02-09只有局部基础，完整字段Owner映射与共同最终门未完成，因此本窗只领取独立日历存储支撑，不领取公开表单、激活、外部提交或整个WP03-01。

复用既有BusinessCalendar、CrmCalendarStorageCodec及Publication：保存调用方已构造的原生已发布快照（ProvenanceKind20/Status10），不是发布授权入口。由可信宿主传入tenant、同一NpgsqlConnection/Transaction；保存/读取都验证原始出版事实摘要、源身份及完整索引一致性。摘要是事实承诺，不等于用户签名或环境审批；调用方仍必须在真实Owner/audit事务中完成授权和发布。无HTTP/DI/Program或旧业务工厂切换。

新独立版本schema仅安装到明确空的命名空间；不修改现有库/原SQL Server映射或历史迁移。不引入虚构rowversion、tzdb版本或生产配置；不实施草稿更新、当前有效指针、定时替换、隐私政策、FormBundle或发布ETag。保留原全局Id、tenant/Id、tenant/CalendarKey/technical ContractVersion、tenant/SourceVersionHash唯一性保存不可变快照，技术映射版本不冒充业务版本合同。采用完整二进制payload与严格UTF-16 key字节，精确保留既有别名、Unicode/NUL、时区、周时段、节假日与100ns UTC ticks；来源版本按原严格UTF-16 SHA256保留租户内唯一身份；CalendarKey保留原80个UTF-16代码单元范围。数据库原生修改/删除/清空保护及实际catalog核对，漂移拒绝，不用配置hash假装守卫有效。并发同事实重复返回原结果，异事实冲突且不覆盖；内部savepoint保留调用方事务控制，不自动commit/replay。

## 任务与实际耗时估计

预计60–90min实际执行：25–40min原生存储/DDL/事务；15–25min隔离PG行为验证；20–25min一次作者SPEC/QUALITY和三仓收尾。Main start至finish只计一次，不叠加子任务。D6本窗完成不提前宣称完整D5/D6业务采用。

验证实际PG独有集群：租户/ID绑定、源/索引篡改拒绝、Unicode/NUL及100ns无损、同事实并发重复与冲突、外层commit/rollback和savepoint恢复、原生UPDATE/DELETE/TRUNCATE拒绝、守卫禁用/改写拒绝、最小权限runtime；运行相关既有日历codec/publication/domain回归。冻结读取不重算当前时区规则或历史deadline；新计算仍须原AssertCurrentRules。未变W59验签/W58身份证据直接引用，不重跑无关全仓。

DEV-1总台账和四状态；DEV-2先Handoff START/finish deferred，bind CRM实现和PR采用/准确分支删除/finish，再返回Handoff交付/finish，最后Main收尾。固定席位复用，不派代理、不新建工作树；无Actions、发包、既有库迁移或生产部署。完整24/375整卡、125目标0完整交付、247运行NOT_RUN保持。W51准确data清理曾被自动审批拒绝（blocked by policy），停止保留，不重试/绕过。

## W60实际交付

CRM [PR105](https://github.com/GTX537/CP6.CRM/pull/105)已采用，source `2df15fbb46290b1f5f06c80e52251aca6eee746b`，远端main `8be1a856b50aaa68a733b8ca47df1ecd25fbc8a9`，9任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/8be1a856b50aaa68a733b8ca47df1ecd25fbc8a9/docs/development/evidence/W60-01/DELIVERY.md)。92项相关本地用例通过（31项真实PG、61项既有日历）；真实Owner/公开表单验收NOT_RUN，无Actions。

新增显式PostgreSQL不可变日历快照存储，复用原日历codec/publication事实校验；保存原全局ID、租户来源版本、CalendarKey/technical version唯一性及100ns/Unicode/NUL。初始START拟省略来源唯一性，经原运行时schema核对已纠正，不采用错误草案。调用方持有可信tenant和实际外层事务；相同事实复用，冲突拒绝，savepoint恢复，两个事务实际transactionid等待已观察。

原生ENABLE ALWAYS修改/删除/清空保护，核实际catalog/索引/guard/RLS漂移；未知schema不自动采用或迁移。原Domain/codec/生成合同及八份身份SQL、Program不变。它只保存构造好的发布事实，hash不是审批；实际Owner/audit/最终门、激活和公开表单仍待完成。技术版本不冒充业务版本，schema不冒充租户权限隔离。

一次作者完整SPEC/QUALITY通过；最终31项真实PG及未变61项日历回归有效，初始红灯和中间成功均保留不重复累计。两个独有PG集群均正常停止，data/log保留、无PID。CRM准确本地/远端分支删除、固定树干净detached、finish释放。

七天目标ACTIVE，继续D6可独立公共支撑并保留真实业务前置。24/375整卡、125目标0完整交付、247 NOT_RUN不提升。无Actions、既有库迁移或部署。W51准确data删除此前被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR36](https://github.com/GTX537/CP6.AI-Handoff/pull/36)已采用 `d1419def25aad4939e1418956462729364163ff8`，准确本地/远端分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约33.37min，原预算60–90min，最终实际以finish为准，不重复累计子任务；此前只读前置及接续约10min。
