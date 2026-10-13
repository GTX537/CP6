# S01-10 正式 PostgreSQL 映射采纳

COORD 决定 `CP6-S01-10-SCALE-20261013-R1`：采纳 [CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0](../../../../eng/contracts/s01/postgresql-mappings.json)。这是有版本的 PostgreSQL 精确存储表示，原 `CP6-EXACT-NUMERIC-STORAGE/1`、原件、固定向量及[提案](postgresql-mappings.proposed.json)保留原字节。

20 项数值尺度由 0..8 扩至 0..18，依据已接受 Decimal18；存储保留完整系数、尺度和原字符串，不在入口舍入。Money.scale / trace.scale 政策仍为 0..8，calculationScale 仍 18。17 项有直接原生金额链；3 项 ValueLine 是字典派生量，不声称存在同名原生 wire 属性。逐字段旧/新表达、null/sign、指针链及实际存储用例见[采纳依据](coord-adoption/scale-adoption-review.md)和[机器记录](coord-adoption/scale-adoption-review.json)。其余46项不变。

正式文件只改变 adoption、scale_change.approval 并添加决定元数据。全部 fixture 消费语义与已测试提案一致，核对脚本 [verify_formal_mapping.py](coord-adoption/verify_formal_mapping.py) 和[结果](coord-adoption/verification.json)固定两份文件哈希、语义哈希、66行及20行集合。现有 TestInputs.MappingPath 优先正式路径，ExactValueStorage.MappingVersion 仍为同一1.0.0。

原 PostgreSQL18.6 / Npgsql8.0.8 实测210/210及随后重叠57/57、生命周期1/1继续按准确输入复用；不是267个不同测试。历史运行仍记 PROPOSED，不回写为正式映射运行。本次仅做采用元数据/语义/引用核对，未重启数据库。源绑定、失败记录与 owned fixture 清理证据见[独立验证](coord-adoption/S01-10-original-validation.json)。

13表 Owner 关系、全量对象校验、实际生产写者、租户 schema 选取/权限/池、业务算法与迁移继续各自 NOT_RUN。不得据此把已有 decimal 写者切换上线；参见[Owner采用清单](OWNER-ADOPTION.md)。本文件补充历史 README 的“待协调决定”时点，未改写原候选交付事实；远端main包含正式映射后才在唯一台账记本有界任务DONE。
