# PUR-RFQ-01 采购询比价与供应价格

## 1. 目的、操作者与已接受业务边界

采购员把明确需求发给合资格供应商，收录原始报价，按可解释的数量／单位／货币／费用比较，经批准和本域应用形成采购选择；也可维护独立供应价格，并向 PO Owner 提交冻结商业依据。采购审核人、价格维护人、结果恢复人、Source/PO/Projection/OA 机器身份各有专用职责，角色人员映射由 IAM 给出。 [原文 L21](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:21>)、[原文 L23](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:23>)、[原文 L98](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:98>)、[原文 L114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:114>)

| 页面组 | 业务工作 | 范围边界 | 出处 |
| --- | --- | --- | --- |
| 询价 | 查询、来源选择、市场询价、草稿编辑、修订、发行、供应商投递跟踪、撤回及终止 | 不扣PR额度，不把邀请登记当供应商已读 | [原文 L36](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:36>) |
| 报价 | 暂存、正式事实收录、缺报／拒绝、后继、撤回、时效、技术差异与原凭证 | 不覆盖历史报价，不由采购批准替代技术 | [原文 L37](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:37>) |
| 比较与选择 | 原值与统一评价、不可比原因、推荐排序、单行单供方选择、合法同供方分份额、审批／应用／撤回 | 不自动选第一；不把MARKET评价量变成下单授权 | [原文 L38](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:38>) |
| 供应价格 | 手工／RFQ来源草稿、发布、后继、更正、停止、唯一或显式选价及解析说明 | 不默认取最新价，不改历史PO金额，不启用自动多维优先序 | [原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:39>) |
| 转PO依据 | 预检、生成、冻结、释放、逐组申请、三类回执、查询／同键续作／关系修复／取消与后继 | RFQ服务不写正式PO/PR账；不假设跨Owner全局事务 | [原文 L40](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:40>) |
| 共同功能 | 操作权限、字段权、历史、附件授权访问、导出和结果恢复 | 不新建身份、文件、工程、Quality或Finance主账 | [原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:41>) |

| 已接受选择 | 确定行为 | 真实启用证据 | 出处 |
| --- | --- | --- | --- |
| D01 单行一家 | 同一来源/RFQ行仅一个当前供应商；不同行可不同供应商。有权且不重叠的同供方60＋40份额允许，不通过复制行/份额绕过限制 | 来源身份、分配授权及父预算的真实裁决 | [原文 L47](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:47>) |
| D02 受控零价 | 明确 `unitPrice="0.00000000"` 可以表示真实零价；正式使用须专门条款、能力、审批或有据豁免。null不是0，目标不得回退价表 | ZeroPricePolicy证据及PO原值承接能力 | [原文 L48](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:48>) |
| D03 来源明示无终期 | `validity.kind=NO_END`须来源凭证和适用政策；缺终期是UNKNOWN。无终期仍会撤回、停用、受当前资格限制 | NoEndPolicy、来源声明、当前活动资格 | [原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:49>) |
| D04 唯一或显式选价 | 解析唯一有效候选，或授权人对准确候选集合保存显式选择。未裁定多候选返回CONFLICT | 实际选价能力、候选内容及必要审批；不启用自动多维优先序 | [原文 L50](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:50>) |
| D05 逐范围替代 | 准确旧新清单、前驱及生效范围；草稿与WHAT_IF不改变旧效力。无关范围继续原有效依据 | Scope映射、当前绑定前驱、正式替代证据 | [原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:51>) |
| D06 审批矩阵机制 | 发行、选择应用、价发布等按矩阵判断REQUIRED或有证NOT_REQUIRED；空审批绝非豁免。OA事实和采购应用分离 | 真实角色、职责分离、金额/风险阈值及版本化政策实例 | [原文 L52](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:52>) |
| D07 独立组 | 本基线只执行有据独立分配/独立组。真实ALL_UNITS、PROGRESSIVE保留 | 独立条款与组内费用归属；合同累计、跨组包量折扣、共享固定费**不启用** | [原文 L53](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:53>) |

D01–D07 已完成产品选择，不重新问用户。一行一家可有同供方不交叠 60＋40 份额；复制 RFQ 行不能规避共同 source lineage。同行多供方、自动多维优先选价、合同累计／跨组包量／共享固定费的新业务入口均不启用，收到模式返回 RFQ_EXTENSION_NOT_ENABLED；历史外部义务保留，不据此清零。 [原文 L55](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:55>)、[原文 L680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:680>)、[原文 L3022](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3022>)

RFQ 拥有发行内容、收到的报价事实、比较、选择应用和商业依据；BP 管正式 Supplier，工程/PLM 管技术用途，Quality 管检验／豁免，Source 管授权/份额/保护/余额，PO 管订单创建及取消，IAM 管当前权限。SOURCE、PO_EXECUTION、RELATION 是三条独立观察流；RFQ 不写正式 PR/PO 主账。 [原文 L59](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:59>)、[原文 L1168](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1168>)、[原文 L1327](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1327>)、[原文 L1328](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1328>)、[原文 L1329](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1329>)

## 2. 准确正文、接受和复审效力

| 原件 | SHA-256 | 用途 | 入口 |
| --- | --- | --- | --- |
| v1.1.1 正文 | 1787bb4e78ef25872d90f5897307f1259208449b9948010e274c45b21cb3a586 | 492101 bytes／3256 行，当前唯一完整规格 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1>) |
| 当前选择 | f604e958f82ffc2e9c4c36258e47756546a7a4a75d9a53a13cccd049b13d5413 | USER_ACCEPTED_DEVELOPER_SPEC，非部署状态 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f6/f604e958f82ffc2e__CURRENT.json:1>) |
| 正式 UA | 393e8376b7489ac534c32d4dd3a0276f94cdb519d2076808bd7e4b15d0c35545 | 用户“认可，请继续推进”的准确接受范围 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:1>) |
| MD02 复审 | 612dedd7fe483cadb6dee6be4cc57f534b2982d9c6d6ba3a00496ea3bbd6293d | F01–F05 与 COR01–04 的直接补正依据 | [原文 L1](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/61/612dedd7fe483cad__SR-20260929-B-RFQ-MD02_复审与总控补正.md:1>) |

接受对象是 COORD-B-RFQ-MD02-PATCH01 v1.1.1，作者 v1.0/v1.1 保留。主文 REVIEW／待接受是不可变编制时状态，后继 UA 明确当前已接受，不要据标题误降级。接受不扩展为编码、数据库、部署或真实 Owner 采用；原五 SPEC、46 OP 与全部旧范围保留。 [原文 L7](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:7>)、[原文 L11](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:11>)、[原文 L29](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:29>)、[原文 L33](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:33>)、[原文 L44](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:44>)、[原文 L50](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:50>)、[原文 L51](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:51>)

COR01 把完整 canonicalRequest 纳入受签 token 并给恢复路径；COR02 准确披露能力；COR03 E25 行 ID、E45 原 execution evidence 都由真实前响应绑定；COR04 同 Basis 的 batchKey 固定。复审记录的 140 JSON／46 步／86 bindings 与少量 hash/例算是原静态检查，不是本轮实际 HTTP 或业务 PASS。远端主文发布在该冻结 CURRENT 中仍 PENDING，不把接受记录提交当正文 payload。 [原文 L35](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:35>)、[原文 L39](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:39>)、[原文 L40](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:40>)、[原文 L41](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:41>)、[原文 L42](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:42>)、[原文 L49](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/39/393e8376b7489ac5__HISTORY__handoffs__PLAN-REUSE-01B__documentation__MD02__acceptances__B-RFQ__UA-20260929-B-RFQ-MD01.md.txt:49>)

## 3. 实体、字段、身份与准确金额

| 类型 | JSON 形式／约束 | 后端／SQL | 出处 |
| --- | --- | --- | --- |
| Id | UUID小写字符串，非空；由服务端生成实体ID，客户端只生成commandId | Guid / uniqueidentifier | [原文 L131](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:131>) |
| Key | NFC/trim字符串，1～128 UTF-16单位、大小写敏感；Owner原身份不去标点或截断 | string / nvarchar(128) BIN2；不同字段下表可更短 | [原文 L132](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:132>) |
| Seq | `"1"`～Int64最大值十进制串，无前导零 | long / bigint；只在声明的stream/epoch内比较；仅明确EMPTY绑定/初始关系版本允许"0" | [原文 L133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:133>) |
| Version | 8字节rowversion的base64，如`AAAAAAAAB9E=`；客户端不加减 | byte[] / rowversion；不是业务版、审批号或时间 | [原文 L134](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:134>) |
| Qty | 十进制字符串，SQL decimal(19,6)范围；通常>0；零仅在明确剩余量/状态字段 | decimal / decimal(19,6) | [原文 L135](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:135>) |
| UnitPrice | decimal(19,8)范围字符串，正式报价>=0；null表示缺值 | decimal / decimal(19,8) | [原文 L136](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:136>) |
| Amount | decimal(19,4)范围字符串；实际货币小数位0～4由金额政策指定 | decimal / decimal(19,4) | [原文 L137](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:137>) |
| Rate | >0，decimal(28,12)范围字符串，有转换方向/政策引用 | decimal / decimal(28,12) | [原文 L138](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:138>) |
| Instant | ISO 8601带偏移输入；保存/返回UTC，毫秒精度，不接受无时区时间 | DateTimeOffset / datetimeoffset(3)，统一+00:00 | [原文 L139](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:139>) |
| Date | `YYYY-MM-DD`，交付要求日不是报价截止时刻 | DateOnly / date | [原文 L140](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:140>) |

前端金额数量使用字符串，不经 JS Number。后端有限十进制转 BigInteger 有理数，中间乘除不隐式降精度；仅按真实政策指定 ROUND 后检查列范围。Instant 最多毫秒、拒无时区；Key NFC/trim 并区分大小写，Owner 原身份不删标点；未知／重复成员、整数枚举、NaN/Infinity 拒绝。PATCH 未出现不变、null 仅清可空、数组整体替换且核稳定子 ID，不能用脱敏 null 清原价。 [原文 L142](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:142>)、[原文 L144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:144>)、[原文 L146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:146>)、[原文 L148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:148>)

rowVersion 是 8-byte base64 技术 CAS；BusinessVersion 是真实持久内容修订，不互转，也不从工作对象伪填 1。RFQ/Price DRAFT 编辑不增该业务序号但改变 SubjectContentHash；Decision 同理，PriceSelection 采用不可变内容版本。ScopeGuard、关系和外部源观察各有自己的版本，不能由外部回执推进无关草稿 token。 [原文 L212](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:212>)、[原文 L216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:216>)、[原文 L218](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:218>)

| RFQ 字段 | 控件／类型 | 草稿与正式动作／null | 持久位置 | 出处 |
| --- | --- | --- | --- | --- |
| `purpose` | 单选 PROCUREMENT/MARKET | 新建必填，无隐含采购授权；首次发行后不能改用途；首发前变更须重验全部源/报价影响 | Rfq.Purpose | [原文 L224](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:224>) |
| `siteKey` | 授权站点选择，64 | 必填，默认仅来自IAM已核主站点；无主站点需选；不可null | Rfq.SiteKey | [原文 L225](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:225>) |
| `buyerKey` | 授权人员选择，64 | 必填，默认为本人且有权时；不是Creator | Rfq.BuyerKey | [原文 L226](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:226>) |
| `title` | 文本，1～200 | 草稿可null，发行必填；无默认值 | RfqRevision.Title | [原文 L227](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:227>) |
| `replyDeadline` | 日期时间 | 草稿可null；发行必须晚于该次发行时刻；后继延长不可改原版 | RfqRevision.ReplyDeadline | [原文 L228](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:228>) |
| `notes` | 纯文本，0～2000 | 缺省/null均null；PATCH可清空；商业敏感 | RfqRevision.Notes | [原文 L229](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:229>) |
| `lines` | 明细，0～200行 | 草稿可空，发行1～200；出现则替换草稿行集合 | RfqLine | [原文 L230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:230>) |
| `lines[].lineId` | 稳定UUID或null | 新行null由后端生成；后继对应旧行保留ID；不能借位置重用他行ID | RfqLine.LineId | [原文 L231](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:231>) |
| `itemKey`、`requestUom` | 主数据选择128、单位代码16 | 草稿可null，发行必填且有效；名称为只读快照 | RfqLine.ItemKey/RequestUom | [原文 L232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:232>) |
| `requestedQty` | Qty控件 | 草稿可null，发行>0；询价量不是消费量 | RfqLine.RequestedQty | [原文 L233](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:233>) |
| `requiredDate` | Date | 可null，原来源有硬交期则不得绕过；不替代LeadDays | RfqLine.RequiredDate | [原文 L234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:234>) |
| `destinationKey` | 授权收货目的地128 | 草稿可null；发行/正式商业依据需准确适用来源 | RfqLine.DestinationKey | [原文 L235](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:235>) |
| `technicalBasis` | TechnicalBasis | UNKNOWN可保留草稿/原事实；相应新使用按活动门验证 | RfqLine.TechnicalJson | [原文 L236](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:236>) |
| `sourceLinks` | SourceLink[]，每行0～50 | MARKET可[]；PROCUREMENT发行时需有效来源；保留每源份额，合量不丢来源 | RfqSourceLink | [原文 L237](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:237>) |
| `suppliers` | `{supplierKey,recipientKey,channel}`[]最多50；Key各128 | 可草稿为空；发行至少1，去重按正式Supplier身份；channel=MANUAL/ADAPTER | Invitation | [原文 L238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:238>) |
| `attachmentRefs` | Ref[]，最多20 | []默认；发行须逐接收方披露资格；不接受任意公网下载URL | RfqRevision.AttachmentsJson | [原文 L239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:239>) |

### 通用对象、来源责任、技术与有效期

[原文 L152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:152>)

```ts
type Id = string;
type Key = string;
type Seq = string;
type Version = string;
type Qty = string;
type UnitPrice = string;
type Amount = string;
type Rate = string;
type Fraction = string; // decimal(28,12)，政策比例>=0；FX仍使用正Rate
type Instant = string;
type DateText = string;
type Availability = 'AVAILABLE' | 'NOT_CONFIGURED' | 'NOT_AUTHORIZED' | 'TEMPORARILY_UNAVAILABLE';
interface Ref { owner: Key; id: Key; version: Key; }
interface FieldError { field: string; code: string; message: string; }
interface Gate { code: string; allowed: boolean; evidenceRefs: Ref[]; missing: string[]; }
interface Actionability { operation: string; allowed: boolean; reasonCodes: string[]; checkedAt: Instant; }
interface PageRequest { page: number; pageSize: 20 | 50 | 100; }
interface PageResult<T> { items: T[]; page: number; pageSize: number; total: number; asOf: Instant; }
interface Mutation { expectedVersion: Version; }
interface ReasonMutation extends Mutation { reason: string; }
interface ResourceRef { kind: string; id: Id; businessVersion: Seq | null; version: Version | null; }
interface CommandResult {
  commandId: Id; outcome: 'SUCCEEDED' | 'ACCEPTED' | 'REJECTED' | 'CANCELLED_NO_EFFECT';
  effect: string; replayed: boolean; changed: boolean;
  resource: ResourceRef | null; currentReadable: boolean; errorCode: string | null;
  recordedAt: Instant; operationUrl: string | null;
}
interface Problem {
  type: string; title: string; status: number; code: string; detail: string;
  traceId: string; commandId: Id | null; errors: FieldError[];
  retry: 'NONE' | 'SAME_KEY' | 'QUERY_ORIGINAL' | 'NEW_INTENT_AFTER_REVIEW';
  currentVersion: Version | null;
}
interface Detail<T> { data: T; version: Version; actions: Actionability[]; redactedFields: string[]; }
interface SourceResponsibility {
  owner: Key; sourceType: 'PR' | 'AUTHORIZED_PURCHASE'; sourceId: Key;
  sourceLineId: Key; authorizationScopeId: Key;
}
interface SourceLink {
  linkId: Id | null; responsibility: SourceResponsibility; sourceBusinessVersion: Key;
  quantity: Qty; uomCode: string; observationRef: Ref;
}
interface TechnicalBasis {
  itemKey: Key; revisionRef: Ref | null; purpose: string; siteKey: Key;
  result: 'APPLICABLE' | 'NOT_APPLICABLE_WITH_BASIS' | 'UNKNOWN' | 'BLOCKED';
  evidenceRef: Ref | null;
}
interface Validity {
  kind: 'END_AT' | 'NO_END' | 'UNKNOWN'; from: Instant | null; to: Instant | null;
  sourceDeclarationRef: Ref | null; applicablePolicyRef: Ref | null;
}
interface BindingPredecessor { scopeAtomKey:Key; epoch:Key; revision:Seq; }
interface Replacement {
  expectedBindings: BindingPredecessor[]; predecessorRef: Ref;
  scopeAtomKeys: Key[]; oldToNew: { oldLineId: Id; newLineId: Id; sourceShareGrantIds: Key[] }[];
  effectiveAt: Instant; policyRef: Ref; reason: string;
}
```

### RFQ 草稿、修订、列表与检索 DTO

[原文 L241](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:241>)

```ts
interface RfqLineInput {
  lineId: Id | null; itemKey: Key | null; requestedQty: Qty | null; requestUom: string | null;
  requiredDate: DateText | null; destinationKey: Key | null;
  technicalBasis: TechnicalBasis | null; sourceLinks: SourceLink[];
}
interface SupplierInvitationInput { supplierKey: Key; recipientKey: Key; channel: 'MANUAL' | 'ADAPTER'; }
interface RfqDraftInput {
  purpose: 'PROCUREMENT' | 'MARKET'; siteKey: Key; buyerKey: Key;
  title: string | null; replyDeadline: Instant | null; notes: string | null;
  lines: RfqLineInput[]; suppliers: SupplierInvitationInput[]; attachmentRefs: Ref[];
}
type RfqDraftPatch = Mutation & { rootExpectedVersion?:Version } & Partial<RfqDraftInput>;
interface RfqRevisionData extends RfqDraftInput {
  id: Id; rfqId: Id; rfqNo: string; businessVersion: Seq;
  status: 'DRAFT' | 'ISSUED' | 'WITHDRAWN' | 'ABANDONED';
  issuedAt: Instant | null; contentHash: string | null; predecessorRevisionId: Id | null;
}
interface RfqListItem {
  id: Id; rfqNo: string; title: string | null; purpose: 'PROCUREMENT' | 'MARKET';
  siteKey: Key; buyerKey: Key; lifecycle: 'OPEN' | 'TERMINATED';
  currentRevisionId: Id | null; quoteCount: number; pendingGroupCount: number; updatedAt: Instant;
}
interface RfqSearch extends PageRequest {
  keyword: string | null; lifecycle: 'OPEN' | 'TERMINATED' | 'ALL';
  siteKey: Key | null; buyerKey: Key | null; supplierKey: Key | null;
  itemKey: Key | null; sourceId: Key | null; deadlineFrom: Instant | null; deadlineTo: Instant | null;
  sort: 'updatedDesc' | 'numberAsc' | 'deadlineAsc';
}
```

### 报价与价格共用商业条款、报价族 DTO

[原文 L276](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:276>)

```ts
interface Tier {
  tierId: Id | null; lower: Qty; upper: Qty | null; unitPrice: UnitPrice | null;
}
interface Charge {
  chargeId: Id | null; economicTermKey: Key; kind: string;
  knownness: 'KNOWN' | 'INCLUDED' | 'NOT_APPLICABLE_WITH_BASIS' | 'UNKNOWN';
  amount: Amount | null; currencyCode: string | null;
  scope: 'LINE' | 'GROUP'; occurrenceKey: Key | null;
  includedInComponentKey: Key | null; sourceRef: Ref | null;
}
interface TaxTerms {
  mode: 'INCLUDED' | 'EXCLUDED' | 'EXEMPT_WITH_BASIS' | 'UNKNOWN';
  taxCode: string | null; policyRef: Ref | null;
}
interface DeliveryTerms {
  leadDays: number | null; leadDayKind: 'WORKING_DAY' | 'CALENDAR_DAY' | 'UNKNOWN' | null;
  promisedDate: DateText | null; jointInterpretation: 'SAME_DATE' | 'UNRESOLVED' | null;
  startBasis: 'PO_CONFIRMED' | 'SPECIFIED_DATE' | 'UNKNOWN';
  specifiedStart: DateText | null; calendarRef: Ref | null;
  freightResponsibility: 'SUPPLIER' | 'BUYER' | 'UNKNOWN';
}
interface CommercialTerms {
  billingMode: 'ALL_UNITS' | 'PROGRESSIVE' | 'UNRESOLVED';
  quantityBasis: 'PER_ALLOCATION' | 'INDEPENDENT_GROUP' | 'CONTRACT_CUMULATIVE' | 'CROSS_GROUP';
  currencyCode: string | null; quoteUom: string | null; thresholdUom: string | null;
  priceBasisQty: Qty | null; tiers: Tier[]; minimumOrderQty: Qty | null; orderMultiple: Qty | null;
  tax: TaxTerms; charges: Charge[]; delivery: DeliveryTerms; validity: Validity;
  zeroPriceEvidenceRef: Ref | null; termsEvidenceRef: Ref | null;
}
interface QuoteLineInput {
  quoteLineId: Id | null; rfqLineId: Id; bid: 'QUOTED' | 'NO_BID' | 'NOT_PROVIDED';
  offeredQty: Qty | null; terms: CommercialTerms | null;
  offeredTechnicalBasis: TechnicalBasis | null; deviationText: string | null;
}
interface QuoteDraftInput {
  rfqRevisionId: Id; supplierKey: Key; supplierQuoteNo: string | null;
  replacementVoucherRef: Ref | null; receivedAt: Instant; channel: 'EMAIL' | 'PAPER' | 'PORTAL' | 'OTHER';
  evidenceRefs: Ref[]; lines: QuoteLineInput[]; notes: string | null;
}
type QuoteDraftPatch = Mutation & Partial<Omit<QuoteDraftInput,'rfqRevisionId'|'supplierKey'>>;
interface QuoteExternalIdentity {
  kind: 'SUPPLIER_QUOTE_NO' | 'REPLACEMENT_VOUCHER';
  quoteNo: string | null; voucherOwner: Key | null; voucherId: Key | null;
}
interface QuoteData extends QuoteDraftInput {
  id: Id; familyId: Id; businessVersion: Seq;
  familyBinding: 'UNBOUND' | 'BOUND'; boundExternalIdentity: QuoteExternalIdentity | null;
  status: 'DRAFT' | 'RECORDED' | 'WITHDRAWN'; predecessorId: Id | null;
  eligibility: { rfqLineId: Id; state: 'ELIGIBLE' | 'INCOMPLETE' | 'EXPIRED' | 'BLOCKED'; reasons: string[] }[];
}
```

Qty decimal(19,6)、UnitPrice(19,8)、Amount(19,4)、Rate(28,12) 分开；明确零价与 null 缺价不同。offeredQty 用 RFQ requestUom，MOQ/倍数用 quoteUom，阶梯阈值用 thresholdUom；offeredQty=null 不是无限，必须有“对请求量有效”条款或准确上限。档位 lower 含、upper 不含；upper=null 只是有据无上限数量，不是无终期。validity=[from,to)，当地含当天截止转换为下一当地日零点，保时区及原日期证据，不能跨夏令时直接加 24 小时。 [原文 L329](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:329>)、[原文 L331](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:331>)、[原文 L550](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:550>)

QuoteFamily 初始独立 UNBOUND，恰一个初始 DRAFT，FamilyId 永不改；编号／替代凭证只是候选，两个空身份草稿不共享族。首次 record 才按 Tenant/RFQ/Supplier/类型化外部身份绑定：有 QuoteNo 用规范原编号；无号有真实凭证则 owner/id，凭证 version 不入族键。BOUND 后不得换身份，另一真实报价建新族；同外部身份并发仅一族成功，失败草稿不搬行、不自动并族。 [原文 L552](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:552>)、[原文 L554](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:554>)、[原文 L556](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:556>)、[原文 L557](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:557>)、[原文 L558](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:558>)、[原文 L560](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:560>)、[原文 L562](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:562>)、[原文 L564](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:564>)

后继仅 BOUND 的已收录／有权撤回版本可建；respondsToRevisionId 必备可空，null 沿原修订，非 null 须显式同 RFQ 已发修订，不自动取最新。旧合法草稿晚收录不回退 CurrentRecordedVersionId，也不等最新价自动采用。真实缺项、过期或停用供应商的有权报价事实可保存，Eligibility 独立阻新用。 [原文 L564](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:564>)、[原文 L566](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:566>)、[原文 L572](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:572>)、[原文 L580](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:580>)、[原文 L582](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:582>)

### 持久设计：实体列与全部业务表

| 共享列组 | 准确列 | 出处 |
| --- | --- | --- |
| `Entity` | TenantId uniqueidentifier NOT NULL；Id uniqueidentifier NOT NULL；CreatedAt datetimeoffset(3) NOT NULL UTC；CreatedBy nvarchar(64) NOT NULL；PK(TenantId,Id) | [原文 L1454](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1454>) |
| `Mutable` | UpdatedAt datetimeoffset(3) NOT NULL；UpdatedBy nvarchar(64) NOT NULL；RowVersion rowversion NOT NULL | [原文 L1455](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1455>) |
| `Versioned` | BusinessVersion bigint NOT NULL CHECK>0；PredecessorId uniqueidentifier NULL；ContentHash binary(32) NULL（冻结后非空）；状态按所属表枚举CHECK | [原文 L1456](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1456>) |
| `Json` | nvarchar(max)；非空列必须ISJSON=1；应用反序列化到本文的精确DTO，拒绝未知成员。实际正文大小受API/集合上限约束，不能无限堆积 | [原文 L1457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1457>) |
| Key/Ref | Key为nvarchar(128) COLLATE Latin1_General_100_BIN2，OwnerKey64、RefVersion64；Ref不是跨库FK，必要时保存三个列或明确RefJson | [原文 L1458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1458>) |
| Decimal/Time | Qty decimal(19,6)，Price decimal(19,8)，Amount decimal(19,4)，FX/策略比例 decimal(28,12)；所有datetimeoffset保存+00:00，纯日为date | [原文 L1459](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1459>) |

#### 询价与报价

| 表 | 字段 | 约束／索引 | 出处 |
| --- | --- | --- | --- |
| Rfq | Entity+Mutable；RfqNo varchar(16)，Purpose varchar(16)，SiteKey64，BuyerKey64，Lifecycle varchar(16)，NextRevision bigint | UQ(TenantId,RfqNo)；INDEX(TenantId,SiteKey,BuyerKey,Lifecycle,UpdatedAt DESC,Id)。Purpose为PROCUREMENT/MARKET | [原文 L1467](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1467>) |
| RfqRevision | Entity+Mutable+Versioned；RfqId UUID，Title nvarchar(200) NULL，ReplyDeadline instant NULL，Notes nvarchar(2000) NULL，Status varchar(16)，IssuedAt instant NULL，AttachmentsJson(Json)；IssueApprovalIntentId UUID NULL | FK Rfq；UQ(TenantId,RfqId,BusinessVersion)；DRAFT/ISSUED/WITHDRAWN/ABANDONED；已发内容不可PATCH | [原文 L1468](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1468>) |
| RfqLine | TenantId,RevisionId,LineId UUID；LineOrdinal int；ItemKey128 NULL，RequestedQty Qty NULL，RequestUom16 NULL，RequiredDate date NULL，DestinationKey128 NULL，TechnicalJson(Json) NULL | PK(TenantId,RevisionId,LineId)，FK Revision；UQ(TenantId,RevisionId,LineOrdinal)。同LineId可出现在不同修订，ordinal不是身份 | [原文 L1469](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1469>) |
| RfqSourceLink | TenantId,RevisionId,LineId,LinkId UUID；OwnerKey64，SourceType32，SourceId128，SourceLineId128，AuthorizationScopeId128，SourceBusinessVersion64，Quantity Qty，UomCode16，ObservationRefJson | PK(TenantId,RevisionId,LineId,LinkId)，FK RfqLine；INDEX(TenantId,OwnerKey,SourceId,SourceLineId)；同责任在该行不得重复分量 | [原文 L1470](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1470>) |
| Invitation | Entity+Mutable；RfqId,RevisionId UUID，SupplierKey128，RecipientKey128，Channel varchar(16)，DeliveryKey UUID，State varchar(24)，ResponseState varchar(16)，DisclosureRefJson NULL，LastDeliveryEvidenceJson NULL | UQ(TenantId,RevisionId,SupplierKey)；UQ(TenantId,DeliveryKey)；FK Revision。State与响应分轴；ChangedChild必须同步版本 | [原文 L1471](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1471>) |
| QuoteFamily | Entity+Mutable；RfqId UUID，SupplierKey128，BindingState varchar(8)，IdentityKind varchar(24) NULL，ExternalQuoteNo nvarchar(80) BIN2 NULL，VoucherOwner64 NULL，VoucherId128 BIN2 NULL，BindingEvidenceRefJson NULL，BoundAt instant NULL，NextVersion bigint，CurrentRecordedVersionId UUID NULL | PK为内部FamilyId。UNBOUND要求全部绑定列NULL；BOUND要求且仅有类型所需成员。编号和凭证分别使用下述过滤唯一索引；不可UPDATE已绑定身份；当前已录版用同Tenant/Family FK校验 | [原文 L1472](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1472>) |
| QuoteVersion | Entity+Mutable+Versioned；FamilyId,RevisionId UUID，SupplierQuoteNo nvarchar(80) NULL，VoucherRefJson NULL，ReceivedAt instant，Channel16，EvidenceJson，Notes nvarchar(2000) NULL，Status16，RecordedAt instant NULL，IdentityCandidateJson NULL | FK Family/Revision；UQ(TenantId,FamilyId,BusinessVersion)。DRAFT身份候选按§5.2校验；RECORDED版本外部身份必须匹配Family绑定，VoucherRef保留证据version；冻结后不可改 | [原文 L1473](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1473>) |
| QuoteLine | TenantId,QuoteVersionId,QuoteLineId,RfqLineId UUID；Bid varchar(16)，OfferedQty Qty NULL，TermSetId UUID NULL，TechnicalJson NULL，DeviationText nvarchar(2000) NULL | PK(TenantId,QuoteVersionId,QuoteLineId)，UQ(TenantId,QuoteVersionId,RfqLineId)；关联Revision+Line由QuoteVersion明确；NO_BID允许Terms空 | [原文 L1474](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1474>) |
| TermSet | Entity；BillingMode24，QuantityBasis24，CurrencyCode8 NULL，QuoteUom16 NULL，ThresholdUom16 NULL，PriceBasisQty Qty NULL，MinimumOrderQty Qty NULL，OrderMultiple Qty NULL；TaxJson，DeliveryJson，ValidityJson，ZeroPriceEvidenceJson NULL，TermsEvidenceJson NULL，Frozen bit | 源Quote/Price持独立TermSet；Frozen后不可改。税/交期/有效期JSON完全按3.4 DTO，不新增隐藏默认 | [原文 L1475](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1475>) |
| TermTier | TenantId,TermSetId,TierId UUID；Ordinal int；LowerQty Qty NOT NULL，UpperQty Qty NULL，UnitPrice Price NULL | PK(TenantId,TermSetId,TierId)；UQ(TenantId,TermSetId,Ordinal)；Lower>=0，Upper为空或>Lower，Price空或>=0；档间不交叠/模式规则由同事务应用检查 | [原文 L1476](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1476>) |
| TermCharge | TenantId,TermSetId,ChargeId UUID；EconomicTermKey128，Kind32，Knownness40，Amount Amount NULL，CurrencyCode8 NULL，Scope16，OccurrenceKey128 NULL，IncludedInComponentKey128 NULL，SourceRefJson NULL | FK TermSet；PK(TenantId,TermSetId,ChargeId)；同TermSet中EconomicTermKey/Occurrence重复需能证明同项包含，否则拒绝；不是Finance费用主账 | [原文 L1477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1477>) |

#### 比较、应用与供应价格

| 表 | 字段 | 约束／索引 | 出处 |
| --- | --- | --- | --- |
| Comparison | Entity；RfqId,RevisionId UUID，Mode16，EvaluationAt instant，ComparisonCurrency8，InputHash binary(32)，InputJson，ResultJson，EvidenceMode8 | 内容不可变；FK Rfq/Revision。ResultJson为完整ComparisonData不含当前Actionability；当前资格每读另算；INDEX(TenantId,RfqId,CreatedAt DESC) | [原文 L1496](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1496>) |
| Decision | Entity+Mutable+Versioned；RfqId,ComparisonId UUID，Purpose32，Status16，Reason nvarchar(1000)，ExpectedBindingsJson(Json)，ReplacementJson NULL，ApplicationId UUID NULL | FK Comparison；草稿可改；提交/应用冻结内容；不是OA审批结果主表 | [原文 L1497](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1497>) |
| DecisionAllocation | TenantId,DecisionId,AllocationId UUID；RfqLineId,QuoteVersionId,QuoteLineId UUID，SupplierKey128，Qty Qty，UomCode16，GrantRefsJson，TermsHash binary(32)，EvaluationJson | PK(TenantId,DecisionId,AllocationId)；FK Decision；合法sameSupplier不交叠分量允许多条，不错误UQ整个来源行；供应商一致性在scope锁下检查 | [原文 L1498](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1498>) |
| ApprovalIntent | Entity+Mutable；SubjectKind32，SubjectId UUID，SubjectBusinessVersion bigint NOT NULL CHECK>0，SubjectRowVersion binary(8) NOT NULL，SubjectContentHash binary(32)，SubjectContentJson，PolicyRefJson，PolicyDecisionJson/Hash binary32，ApplicationPredecessorJson/Hash binary32，IntentIdentityHash binary32，ReplacesIntentId UUID NULL，InstanceKey128 NULL，Decision16，ApplicationState24，RequestedAt instant | UQ(TenantId,IntentIdentityHash)；摘要对应§6.3.1完整元组并存原值作碰撞比较，SubjectRowVersion不参与业务去重。技术token快照禁止转bigint；Intent自己的RowVersion由Mutable独立提供 | [原文 L1499](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1499>) |
| ApprovalFact | Entity；IntentId UUID，InstanceKey128，Producer64，Decision16，DecidedBy64，DecidedAt instant，EvidenceRefJson，SemanticHash binary32 | UQ(TenantId,Producer,InstanceKey,DecisionEvidenceKey)，DecisionEvidenceKey128；同一批准内容不可覆盖，矛盾写InboxConflict | [原文 L1500](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1500>) |
| Application | Entity；IntentId UUID，SubjectId UUID，SubjectBusinessVersion bigint，SubjectContentHash binary32，Outcome24，PreviousBindingJson，ResultBindingJson，ControlRevisionJson，ErrorCode64 NULL，EvidenceJson | UQ(TenantId,IntentId,SubjectContentHash)；新应用比较真实内容身份不比较因SUBMITTED变化的技术token；暂时BLOCKED可由原任务续作，终态只回原结果，尝试历史另审计 | [原文 L1501](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1501>) |
| ScopeGuard | TenantId，ScopeAtomKey128，Epoch64，Revision bigint，CurrentControlHash binary32，RowVersion | PK(TenantId,ScopeAtomKey)，无行用同域锁初始化EMPTY；控制/绑定实际应用共同锁此范围，不比较别的scope版本 | [原文 L1502](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1502>) |
| BindingAtom | TenantId，ScopeAtomKey128，BindingRevision bigint，DecisionId UUID NULL，ApplicationId UUID NULL，SupplierKey128 NULL，State16，SourceLineageRefJson | PK(TenantId,ScopeAtomKey)；Current仅这一行，历史在Application/Audit；实际scope映射由Owner，不信前端换个字符串就不重叠 | [原文 L1503](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1503>) |
| ControlFact | Entity；ControlKind32，Producer64，AuthorityRefJson，ResourceKind32，ResourceId UUID NULL，ScopeAtomsJson，EffectiveAt instant，ReceivedAt instant，SupersedesRefJson NULL，Reason nvarchar(1000)，SemanticHash binary32 | 不可变事实；INDEX(TenantId,ResourceKind,ResourceId,EffectiveAt)；PriceControl只是ControlKind=PRICE_STOP/PRICE_SUPERSEDE的用途，不另建一张同义表 | [原文 L1504](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1504>) |
| PriceFamily | Entity+Mutable；SupplierKey128，ItemKey128，NextVersion bigint | INDEX(TenantId,SupplierKey,ItemKey)；不同真实商业来源可多价族，不错误地只准一价族 | [原文 L1505](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1505>) |
| PriceVersion | Entity+Mutable+Versioned；FamilyId UUID，SupplierKey128，ItemKey128，ItemScopeJson，SiteScopeJson，TermSetId UUID，AmountPolicyRefJson NULL，SourceKind24，SourceEvidenceJson，SourceDecisionId UUID NULL，Reason nvarchar(1000)，Status16 | UQ(TenantId,FamilyId,BusinessVersion)；INDEX(TenantId,SupplierKey,ItemKey,Status)；PUBLISHED/STOPPED内容不可改 | [原文 L1506](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1506>) |
| PriceSelectionFamily | Entity+Mutable；SupplierKey128，ItemKey128，NextVersion bigint | 内部稳定族身份；同族后继在锁内分配版本，初始1、之后递增，不以外部token作FamilyId | [原文 L1507](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1507>) |
| PriceSelection | Entity+Mutable+Versioned；FamilyId UUID，RequestJson，RequestHash binary32，SettlementCurrencyCode varchar(8)，ComparisonCurrency varchar(8)，CandidateHash binary32，CandidateVersionsJson，SelectedPriceVersionId UUID，SelectedContentHash binary32，AuthorityRefJson，ApprovalIntentId UUID NULL，Reason nvarchar(1000)，State16，TokenClaimsHash binary32 | FK Family；UQ(TenantId,FamilyId,BusinessVersion)；PredecessorId指同族前版且同Tenant；创建即内容不可变，无商业PATCH。ContentHash按四Subject映射；两币种列与RequestJson一致CHECK/保存校验；只改状态/token不改业务序号或内容hash | [原文 L1508](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1508>) |

#### 依据、量观察和三流结果

| 表 | 字段 | 约束／索引 | 出处 |
| --- | --- | --- | --- |
| Basis | Entity+Mutable+Versioned；DecisionId UUID，BatchKey UUID，ApplicationId UUID，Status16，AllocationIdsJson，FrozenJson NULL，AdapterContractRefJson NULL，UsableUntil instant NULL，ValidityKind16，Reason nvarchar(1000) | FK Decision；FROZEN以后内容hash不可改；未明期限不能写NO_END | [原文 L1516](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1516>) |
| BasisDeliverySnapshot | TenantId,BasisId,GroupId,ManifestRowId UUID；RequiredDate date NULL，RequirementKind varchar(16)，RequiredDeliveryJson，SupplierDeliveryJson，ResolutionJson，CalendarSnapshotRefJson NULL，DeliverySnapshotHash binary32，SourceTermsRefJson | PK(TenantId,BasisId,GroupId,ManifestRowId)；FK BasisGroup；由冻结同事务从FrozenJson的对应目标行产生，Hash/JSON必须相等；不可变、无单独修改入口；不是PO履约主账 | [原文 L1517](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1517>) |
| BasisGroup | TenantId,BasisId,GroupId UUID | PK(TenantId,BasisId,GroupId)，FK Basis/GroupIntent；同规范组可被多个合法依据引用但不重建 | [原文 L1518](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1518>) |
| GroupIntent | Entity+Mutable；CanonicalGroupKey UUID，BusinessIdentityHash binary32，ManifestHash binary32，ManifestJson，Generation bigint，DecisionApplicationId UUID，Preparation24，Execution24，ExecutionTerminal bit，SourceDisposition24，RelationState24，Cancellation24，DispatchFenceGeneration bigint NULL | UQ(TenantId,CanonicalGroupKey)，UQ(TenantId,BusinessIdentityHash)；BusinessIdentityHash由定义的源身份字段算并保存IdentityJson作碰撞复核；不是随机客户端hash | [原文 L1519](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1519>) |
| GroupShare | TenantId,GroupId UUID，GrantOwner64，GrantId128，GrantVersion64，Generation bigint，ResponsibilityHash binary32，ResponsibilityJson，Qty Qty，Uom16，AllocationId UUID，ManifestRowId UUID | PK(TenantId,GroupId,GrantOwner,GrantId,Generation)；UQ(TenantId,GrantOwner,GrantId,Generation)；只登记本域组关联不表示Claim。跨Owner真正唯一性仍由Source守卫 | [原文 L1520](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1520>) |
| GrantPlanJob | Entity+Mutable；RequestKey UUID，RequestHash binary32，RequestJson，State24，ResultJson NULL，ErrorCode64 NULL | UQ(TenantId,RequestKey)；对应source-grant-plans异步命令，结果保留GrantPlanResult含isReservation=false | [原文 L1521](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1521>) |
| SourceObservation | Entity+Mutable；ResponsibilityHash binary32，ResponsibilityJson，StreamId128，Epoch64，StreamVersion bigint，QuantityBasisRefJson，SourceBusinessVersion64，ItemKey128，TechnicalJson，SiteKey64，Uom16，A/C/P/R/D各Qty NULL，AsOf instant，CoverageJson，State24 | UQ(TenantId,ResponsibilityHash,StreamId,Epoch)；只通过SOURCE处理器更新；源业务改版不生成新授权责任，当前epoch通过StreamCursor明确选定 | [原文 L1522](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1522>) |
| SourceContribution | TenantId,ObservationId UUID，EffectId128，EffectRevision bigint，GrantIdentityHash binary32，GrantRefJson，Qty Qty，Uom16，Bucket32，DispositionRefJson | PK(TenantId,ObservationId,EffectId,GrantIdentityHash,EffectRevision)；完整快照替换该观察当前覆盖成员，历史原证据保存在Inbox；不与PO Effect再相加 | [原文 L1523](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1523>) |
| ClaimObservation | Entity+Mutable；GroupId UUID，ClaimRefJson，GrantOwner64，GrantId128，Generation bigint，Qty Qty，Uom16，State32，OwnerRevision64，EvidenceJson | UQ(TenantId,GrantOwner,GrantId,Generation,GroupId)；值由对应Owner，不能因本地定时器超时改RELEASED | [原文 L1524](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1524>) |
| PoExecutionObservation | Entity+Mutable；GroupId UUID，Producer64，StreamId128，Epoch64，StreamVersion bigint，Outcome24，Terminal bit，CompleteForManifest bit，ReceiptJson，SemanticHash binary32 | UQ(TenantId,GroupId,Producer,StreamId,Epoch)；严格PO类型；更正不得让创建成功倒退为“从未发生”，后续取消独立证据 | [原文 L1525](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1525>) |
| PoEffect | Entity；GroupId UUID，EffectId128，EffectRevision bigint，ManifestRowId UUID，PoId128，PoLineId128，Qty Qty，Uom16，GrantRefsJson，SourceShareQuantitiesJson，CommercialHash binary32，DeliverySnapshotHash binary32，DeliveryEvidenceRefJson，EvidenceRefJson | UQ(TenantId,ProducerOwner,EffectId,EffectRevision)，ProducerOwner64；FK Group；仅接收与冻结交付快照一致的hash/证据，不用数量相等判重复；不写源量/PO交期 | [原文 L1526](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1526>) |
| RelationSet | Entity+Mutable；GroupId UUID，Revision bigint，AuthorityEvidenceRefJson，AuthorityEvidenceHash binary32，State16 | UQ(TenantId,GroupId)；ExpectedRelationRevision与证据一起CAS；读不写 | [原文 L1527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1527>) |
| RelationItem | TenantId,RelationSetId UUID，ManifestRowId UUID，GrantIdentityHash binary32，PoId128，PoLineId128，Qty Qty，Uom16 | PK(TenantId,RelationSetId,ManifestRowId,GrantIdentityHash,PoId,PoLineId)；显式份额分解；不靠物料名join | [原文 L1528](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1528>) |
| CoverageDiscrepancy | Entity+Mutable；ResponsibilityHash binary32，EffectId128，ExpectedCoverageRefJson NULL，State24，ReasonCode64，LastEvidenceRefJson | UQ(TenantId,ResponsibilityHash,EffectId)；COVERED关闭只对此差异，不能关闭所有组 | [原文 L1529](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1529>) |
| ImpactItem | Entity+Mutable；ControlFactId UUID，GroupId UUID NULL，ResourceId UUID，ScopeAtomKey128，State24，DispositionEvidenceJson NULL | UQ(TenantId,ControlFactId,ResourceId,ScopeAtomKey)；迟到追溯控制不抹原事实，等待该Owner处置 | [原文 L1530](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1530>) |
| RevokeRequest | Entity+Mutable；GroupId UUID，RequestKey UUID，ManifestHash binary32，Reason nvarchar(1000)，State24，FenceJson NULL，OwnerResultJson NULL | UQ(TenantId,RequestKey)；同组重复申请回原请求或有权后继，不据本地REQUESTED返量 | [原文 L1531](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1531>) |

#### 命令、审计和可靠工作

| 表 | 字段 | 约束／索引 | 出处 |
| --- | --- | --- | --- |
| Command | TenantId,CommandId UUID，ActorKey64，Method8，CanonicalPath nvarchar(256)，RequestHash binary32，HttpStatus smallint，Outcome32，Effect64，ResourceKind32 NULL，ResourceId UUID NULL，BusinessVersion bigint NULL，ResultVersion binary8 NULL，ErrorCode64 NULL，SafeResultJson，CreatedAt/CompletedAt instant | PK(TenantId,CommandId)。无业务提交前PROCESSING仅在未提交事务内，不能留无法恢复的持久占位。永久保留防重/取消墓碑最小内容 | [原文 L1541](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1541>) |
| RfqAuditClock | TenantId,RfqId UUID，LastSequence bigint NOT NULL CHECK>=0 | PK(TenantId,RfqId)，FK Rfq；在该RFQ事务内以UPDLOCK/HOLDLOCK或同域applock分配时间线号；不是跨Owner事件流 | [原文 L1542](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1542>) |
| Audit | Entity；RfqId UUID NULL，ResourceKind32，ResourceId UUID，AuditSeq bigint，RfqTimelineSeq bigint NULL，BusinessVersion bigint NULL，Operation64，Reason1000 NULL，ChangesJson，CommandId UUID NULL，EvidenceRefsJson | UQ(TenantId,ResourceKind,ResourceId,AuditSeq)；过滤UQ(TenantId,RfqId,RfqTimelineSeq) WHERE RfqId IS NOT NULL；RfqId为空则TimelineSeq为空，非空则Seq>0；INDEX(TenantId,RfqId,RfqTimelineSeq DESC,Id DESC)。无业务修订的工作事件BusinessVersion=NULL；访问时再遮蔽 | [原文 L1543](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1543>) |
| Inbox | TenantId，Producer64，EventId UUID，Type32，StreamDomainHash binary32，StreamId128，Epoch64，StreamVersion bigint，TransportHash binary32，SemanticHash binary32，PayloadJson，Outcome32，ErrorCode64 NULL，ReceivedAt/ProcessedAt instant | PK(TenantId,Producer,EventId)；同事件异内容不能覆盖；未处理前驱留PENDING_PREDECESSOR可查 | [原文 L1544](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1544>) |
| InboxConflict | Entity；Producer64，EventId UUID，OriginalHash/IncomingHash binary32，IncomingPayloadJson，ConflictKind32，ResolutionState24，RelatedInboxEventId UUID NULL | 保存冲突新证据，不插第二个同键Inbox、不覆盖可信原件 | [原文 L1545](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1545>) |
| StreamCursor | TenantId，Type32，DomainHash binary32，Producer64，StreamId128，CurrentEpoch64，AppliedVersion bigint，CheckpointRefJson NULL，RowVersion | PK(TenantId,Type,DomainHash,Producer,StreamId)；epoch变更有checkpoint；源、执行、关系各自分域 | [原文 L1546](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1546>) |
| EvidenceArchive | Entity；Owner64，EvidenceKey128，EvidenceVersion64，Type32，SemanticHash binary32，PayloadJson，EvidenceMode8，VerifiedAt instant | UQ(TenantId,Owner,EvidenceKey,EvidenceVersion,Type)；同证据异内容入Conflict，不静默覆盖；敏感范围独立授权 | [原文 L1547](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1547>) |
| Outbox | Entity+Mutable；WorkType64，AggregateKind32，AggregateId UUID，BusinessKey128，PayloadJson，PayloadHash binary32，Status16，AttemptCount int，NextAttemptAt instant，LeaseId UUID NULL，LeaseUntil instant NULL，EverDispatched bit，CompletedAt instant NULL，LastErrorCode64 NULL | UQ(TenantId,WorkType,BusinessKey)；重投复用同一WorkId/业务键。EverDispatched/租约历史只追加，不因超时清0 | [原文 L1548](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1548>) |
| ExportJob | Entity+Mutable；Kind24，FilterJson，RequestedColumnsJson，AuthorizationRevision128，State16，Rows int NULL，ArtifactKey128 NULL，ArtifactHash binary32 NULL，ExpiresAt instant NULL，ErrorCode64 NULL | Rfq敏感导出重新核权；10,000上限，失败不输出半文件 | [原文 L1549](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1549>) |
| DownloadTicket | Entity；RfqId UUID，EvidenceRefJson，ActorKey64，AuthorizationRevision128，ExpiresAt instant，RevokedAt instant NULL | UQ(TenantId,Id)；GET下载再核现权；对外披露不能用此票据 | [原文 L1550](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1550>) |
| Number | TenantId UUID，Kind16，NextValue bigint，RowVersion | PK(TenantId,Kind)；事务内条件分配，不从MAX推算 | [原文 L1551](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1551>) |

这是 pur_rfq 拟议 SQL Server 映射，不是已执行 DDL。所有域内 FK 含 Tenant，外 Owner 用准确 Key/Ref，不建伪跨库 FK；已冻结版本、原证据与命令墓碑追加保留，草稿子项变化与聚合 RV 同事务。现有 Pur_* 与新协议必须明确单 writer 切换，不能部署第二套采购主账。 [原文 L1448](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1448>)、[原文 L1461](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1461>)、[原文 L1488](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1488>)、[原文 L1490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1490>)

## 4. 从询价到选择、供应价格和 PO 依据

### 4.1 询价发行与投递

创建只保存 Draft、来源引用和 PREPARED 邀请：Number 在锁内分配 RFQ-＋12 位租户流水，不从 MAX 推断；编号、头/修订/行/源、Audit、Command 同 commit。草稿可缺标题／期限／技术，但无权或不存在来源不能以草稿为由放行。无 Claim、无 PO、无发送。新修订只是候选，不使旧选择失效；稳定 lineId 继承，不用最大修订号决定当前效力。 [原文 L457](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:457>)、[原文 L467](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:467>)、[原文 L477](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:477>)、[原文 L1490](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1490>)

发行先核源／正式 Supplier／当前技术活动／每接收方附件披露，再取矩阵 REQUIRED 或有据 NOT_REQUIRED；提交时重核 RV、精确 ExpectedBindings、内容 hash 和当前控制，固定全部发行及每家 deliveryKey/outbox/audit/receipt。任一披露不许可使本次本地整份拒绝。effectiveAt 采用服务器当前点，用户偏差超 60 秒拒绝，本版无未来定时发布；外部追溯控制另保原生效点。 [原文 L489](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:489>)、[原文 L491](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:491>)、[原文 L493](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:493>)

ISSUED 不等每家收到。发送前重核 recipient/disclose；渠道必须可靠原键查询／去重，否则只允许人工凭证模式。可能已发而响应丢失保 UNKNOWN，不盲换 deliveryKey；人工证据须相同 revision/supplier/recipient，MANUAL_EVIDENCED 不等已读。撤回/终止只停准确 scope 新用，保历史与在途，不返源量、不删 PO、不把未回复批量写 DECLINED。 [原文 L497](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:497>)、[原文 L505](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:505>)、[原文 L513](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:513>)、[原文 L738](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:738>)

### 4.2 比较、采购选择与审批应用

比较先核相同标的／技术／用途、当前有效期、供给上限、MOQ/倍数，固定评价量、币种、单位、时点与金额政策。MARKET 无 PR 可比较和保存推荐，评价量不升级采购授权；完整价税费/UOM/FX 不齐时原值和已知商品额保留，完整总额与 rank=null。多供方替代候选不相加成总成本。 [原文 L589](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:589>)、[原文 L678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:678>)、[原文 L680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:680>)、[原文 L694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:694>)

| 步骤 | 准确算法与禁止推断 |
| --- | --- |
| 1 | 有版本 UOM 分别将 evaluationQty 转 qQ（计费）与 qT（阈值）；不得靠舍入凑 MOQ。 |
| 2 ALL_UNITS | qT 命中唯一 [lower,upper)，全量 qQ/priceBasisQty×unitPrice。 |
| 3 PROGRESSIVE | 每档转换到报价单位，segment=max(0,min(qQ,upper)-lower)，逐段精确金额求和，完整覆盖实际量。 |
| 4 原币政策 | 按显式金额步骤计算折扣／税／费用，缺步骤 BLOCKED；只允许 MUL_RATE/ADD_COMPONENT/INCLUDED_TAX_TO_NET/NET_TO_GROSS/PERCENT_DISCOUNT/ROUND。 |
| 5 每组件 FX | 每项原币按真实舍入阶段处理，再用自身币种→comparisonCurrency 的方向转换；同币恒等 fxRef=null。 |
| 6 固定费 | 同 occurrence 只一 CANONICAL 表示，HEADER 与 LINE 分解不双计；无分摊政策仅显示组额。 |
| 7 排序与重算 | 仅完整同口径可比金额排序，相同金额可并列；SupplierKey 仅稳定显示，不自动选第一；分配改量必须重算档/MOQ/费用。 |

例算仅设计夹具：100EA÷10EA/BOX×12CUA=120CUA，FX2 为240CUB，再加30CUB运费=270CUB。120EA 整量100档10得1200，前100×12＋20×10累进得1400；不得选便宜算法或用平均单价伪等价。金额表达式禁止任意脚本、循环与重复输出，最终 Amount 必须显式 ROUND，比例零与 null 不混，FX 必须正。 [原文 L591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:591>)、[原文 L593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:593>)、[原文 L594](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:594>)、[原文 L595](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:595>)、[原文 L596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:596>)、[原文 L597](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:597>)、[原文 L598](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:598>)、[原文 L599](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:599>)、[原文 L600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:600>)、[原文 L604](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:604>)、[原文 L1216](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1216>)、[原文 L1218](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1218>)

保存 Decision 草稿就生成稳定 SavedAllocation IDs；同 Supplier 的不交叠份额可多条。读取准确 comparison/引用版本后按实际分配量重算并列差异；不占源量、不停旧 binding、不发 OA。GrantPlan 由 Source 发行，isReservation=false，换 requestKey 不得重铸重叠份额；正式 Claim 再争父预算。 [原文 L680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:680>)、[原文 L702](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:702>)、[原文 L1144](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1144>)、[原文 L1146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1146>)

| 审批 subject | 提交 token 来源 | 批准内容／业务版 | 状态与应用 | 出处 |
| --- | --- | --- | --- | --- |
| RFQ_ISSUE | RfqRevision.RowVersion→SubjectRowVersion | RfqRevision.BusinessVersion；批准当前修订的头行/来源映射/接收名单/待披露附件及发行所需政策内容，排除投递观察/查询时点 | 草稿备注/商业字段属于内容时改hash，旧证明不能发新版；OA实例回填或状态token改变不直接使同内容失效。发行需独立命令 | [原文 L725](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:725>) |
| DECISION | Decision.RowVersion | Decision.BusinessVersion；comparisonId及固定输入hash、SavedAllocation全部字段、reason、ExpectedBindings、Replacement、用途 | SUBMITTED/APPLIED、ApplicationId、当前Actionability不入内容hash；比较/分配/前驱变更必须新内容或后继；新应用仍校验scope | [原文 L726](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:726>) |
| PRICE_PUBLISH | PriceVersion.RowVersion | PriceVersion.BusinessVersion；supplier/item/site、TermSet含交付条款、金额政策、来源和发布相关商业条件 | 状态、UpdatedAt、审批实例回填不入hash；改terms/范围不能沿旧批准，发布按钮独立执行 | [原文 L727](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:727>) |
| EXPLICIT_PRICE_SELECTION | PriceSelection.RowVersion | PriceSelection.BusinessVersion；familyId/该版本id、解析RequestHash、两币种、候选集合hash、所选价格/TermsHash、authorityEvidenceRef、reason、predecessorSelectionId | 新建时就固定商业内容，无商业PATCH；DRAFT→SUBMITTED→APPLIED仅技术token变化。内容变化通过同族后继的新Id/BusinessVersion，再审 | [原文 L728](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:728>) |

ApprovalIntent 固定 SubjectBusinessVersion、提交前 SubjectRowVersion、SubjectContentJson/hash、PolicyDecision、ExpectedBindings。IntentIdentityHash=H(kind,id,businessVersion,contentHash,PolicyDecisionHash,ApplicationPredecessorHash)，不含 rowVersion；新调用生成的 proofRef/time/trace 也不进入稳定 PolicyDecisionHash。相同内容换 token/command 仍同 Intent，原拒绝或冲突不复活。 [原文 L710](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:710>)、[原文 L711](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:711>)、[原文 L712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:712>)、[原文 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:713>)、[原文 L714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:714>)、[原文 L732](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:732>)、[原文 L734](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:734>)

OA 回调比 Intent 保存快照，不拿 SUBMITTED 后的新 token 判旧。应用再核真实业务版与内容 hash；仅生命周期 token 变而内容同不 stale。DECISION 的全部 scope 前驱/CAS 一次成立才应用，任一冲突整次 CONFLICT，OA 历史仍 APPROVED。RFQ_ISSUE/PRICE_PUBLISH 回调只准备证明，独立发行/发布命令消费；不能自动外发或发布。 [原文 L713](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:713>)、[原文 L714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:714>)、[原文 L715](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:715>)、[原文 L716](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:716>)、[原文 L717](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:717>)、[原文 L736](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:736>)、[原文 L740](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:740>)

### 4.3 供应价格与受签显式选价

MANUAL 价不伪造 RFQ 来源；从已应用 PROCUREMENT selection 复制只生成新 Draft，保准确条款和金额政策，不自动发布、不把 MOQ 置0、不把生效日改今天、不扩大技术/Site。EXACT 一站、SET 1..50、ALL_WITH_POLICY 必须真实 policy，空列表不等全租户；技术版空须工程不适用证据。发布锁 Supplier+Item 全有关价域，重叠无准确 supersede/scope manifest 拒绝；停止只改指定范围，历史条款/PO 不变。 [原文 L805](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:805>)、[原文 L807](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:807>)、[原文 L811](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:811>)、[原文 L813](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:813>)、[原文 L821](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:821>)、[原文 L823](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:823>)

ResolvePrice 同时必填 settlementCurrencyCode（货款原币过滤）与 comparisonCurrency（每项输出评价币）；改任一必须清 token 并重新解析。按 Supplier/Item/原币/Site/技术/时段/量档及完整费用判定：无候选 MISSING；未知会影响唯一性 BLOCKED；恰一合法且无未判定竞争者 MATCHED；多合法 CONFLICT。无足够授权判全候选为 BLOCKED_AUTHORIZATION，不把可见唯一当全域唯一，不以最新价／最大 MOQ 选赢者。 [原文 L749](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:749>)、[原文 L835](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:835>)、[原文 L837](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:837>)、[原文 L839](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:839>)、[原文 L841](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:841>)

RequestHash 仅排除指回证明的 explicitSelectionId，保两币种、量、UOM、asOf、技术与金额 Ref。RFQ-RESOLVE-2 token 携完整 canonicalRequest（selectionId=null），并以 RFQ-PRICE-RESOLUTION 域保护全部 claims；含 Tenant/Actor/auth revision/requestHash/candidateSetHash/issued/expires/keyId，10 分钟只限制首次创建。接收端验算法/密钥/用途/主体/期满，独立复算 requestHash，再用恢复的准确请求重查候选与 TermsHash；不能从 hash 反推或信只可解码载荷。 [原文 L841](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:841>)、[原文 L843](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:843>)、[原文 L848](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:848>)、[原文 L851](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:851>)、[原文 L860](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:860>)、[原文 L862](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:862>)

存 RequestJson 与两币种同源，保存 TokenClaimsHash 而非可复用签名 token。长审批使用已保存内容和当前资格，不重签改变意图。PriceSelection 首次新 Family/业务版1，没有商业 PATCH；改币种/量/理由/候选须新解析 token＋明确 predecessor，在同族锁下新版本。首次 approvalIntentId=null，需审批先存 Draft202再用真实 ID 送审，有据豁免同事务 APPLIED。 [原文 L864](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:864>)、[原文 L868](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:868>)、[原文 L870](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:870>)、[原文 L878](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:878>)、[原文 L880](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:880>)

### 4.4 冻结商业依据与原组转换

Basis 草稿固定 batchKey；manifest 从已批准 SavedAllocation/SourceGrant 服务端生成。冻结先重核应用/当前 binding/控制、各源准确责任、实际分量价档/MOQ/费用、交期及 PO 承接能力，再固定完整 GroupManifest/商业 payload/hash/mapping/DeliverySnapshot。分组键为 Supplier+Site+原结算币+Destination+PaymentTermsRef+PricingScopeRef+AdapterContractVersion；跨组价格依赖直接拒绝。付款政策不得默认现金/月结。 [原文 L946](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:946>)、[原文 L948](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:948>)、[原文 L982](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:982>)、[原文 L984](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:984>)

CanonicalGroup 业务唯一元组=Tenant/DecisionApplicationId/AllocationPartitionGrantSetHash/GroupCompatibilityHash/Generation；另一 Basis 命中同一组不能再下单。FROZEN→RELEASED 只记录资格，没有 Claim/PO；submit 只持久组意图/alias/outbox，202 不表示 PO 成功。全来源保护后才整组原子创建；准备部分成功不是部分采购，先 fence 全组再逐源清理。 [原文 L984](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:984>)、[原文 L986](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:986>)、[原文 L994](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:994>)、[原文 L1000](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1000>)

来源量观察按责任分别显示 A/C/P/R/D 与单位，完整一致时 A+D=C+P+R，不把不同 Item/量纲相加。PO 原回执须每行 EffectId、PO/line、各 Grant 的准确数量、commercialPayloadHash、deliverySnapshotHash/evidence；sources=[] 表尚无可信观察，不是余额0。执行证据和 Source 已覆盖贡献不再相加：E1=30/E2=20 被 C50 覆盖仍 C50。 [原文 L887](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:887>)、[原文 L1010](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1010>)、[原文 L1012](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1012>)、[原文 L1397](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1397>)、[原文 L1409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1409>)

## 5. 完整接口、DTO 与交付／证据接缝

公开根 /api/pur/rfq/v1，内部根 /internal/pur/rfq/v1；v1 是协议版本，不恢复项目 V2。普通 JSON 2MiB、内部证据8MiB；已声明 READ POST 无写头/Command。人写用 Idempotency-Key＋对象 expectedVersion，机器 event/stream 用原去重，不伪造 rowVersion。列表 page1..1000，size20/50/100、稳定排序末项 Id，商业读 no-store。 [原文 L352](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:352>)、[原文 L354](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:354>)、[原文 L366](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:366>)

### 询价与投递

| 路由 | 能力／性质 | 输入输出／效果 | 错误／页面 | 出处 |
| --- | --- | --- | --- | --- |
| GET `/options` | read或create；READ | 无body → Options；200 | 字典失败只禁依赖控件；IAM失败拒整请求，不伪造可操作能力 | [原文 L375](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:375>) |
| POST `/lookups/suppliers/search` | read；READ | LookupSearch → PageResult<SupplierOption>；200 | 未接入503；无权不返回隐藏供应商数量 | [原文 L376](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:376>) |
| POST `/lookups/source-lines/search` | create；READ | LookupSearch → PageResult<SourceOption>；200 | 源Owner不可读显示来源不可用，MARKET草稿仍能建立 | [原文 L377](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:377>) |
| POST `/lookups/items/search` | read；READ | LookupSearch → PageResult<ItemOption>；200 | 失效物料可历史读，不能当新候选 | [原文 L378](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:378>) |
| POST `/rfqs/search` | read；READ | RfqSearch → PageResult<RfqListItem>；200 | 非法排序400；故障非空表 | [原文 L379](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:379>) |
| GET `/rfqs/{rfqId}` | read；READ | UUID路径 → RfqDetail；200 | 不存在/越权404并清该详情 | [原文 L380](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:380>) |
| GET `/rfq-revisions/{revisionId}` | read；READ | UUID路径 → Detail<RfqRevisionData>；200 | 旧版可读，动作资格按当前时点返回 | [原文 L381](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:381>) |
| POST `/rfqs` | create；WRITE | RfqDraftInput → CommandResult；201，RFQ_DRAFT_CREATED | 来源越界404、字段400；不分配PR或建PO | [原文 L382](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:382>) |
| PATCH `/rfq-revisions/{revisionId}` | draft.edit；WRITE | RfqDraftPatch → CommandResult；200，RFQ_DRAFT_SAVED | 旧版409；已发行409 RFQ_STATE_CONFLICT | [原文 L383](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:383>) |
| POST `/rfqs/{rfqId}/revisions` | revision.create；WRITE | SuccessorRevision → CommandResult；201，RFQ_REVISION_CREATED | 非本RFQ前驱409；未发内容可弃置，不自动替代 | [原文 L384](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:384>) |
| POST `/rfq-revisions/{revisionId}/abandon` | revision.abandon；WRITE | ReasonMutation → CommandResult；200，RFQ_DRAFT_ABANDONED | 只有DRAFT；已发行用撤回而非弃稿 | [原文 L385](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:385>) |
| POST `/rfq-revisions/{revisionId}/preflight` | issue；READ | IssuePreview → PreflightResult；200 | allowed=false+逐门原因；不生成发行或投递 | [原文 L386](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:386>) |
| POST `/rfq-revisions/{revisionId}/issue` | issue＋附件disclose；WRITE | IssueRevision → CommandResult；200，RFQ_REVISION_ISSUED | 政策/来源/披露/期限缺证422；相同scope前驱冲突409 | [原文 L387](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:387>) |
| POST `/rfq-revisions/{revisionId}/withdraw` | withdraw；WRITE | ScopedControl → CommandResult；200，RFQ_REVISION_WITHDRAWN | 影响scope不属于版本409；未决执行单独显示 | [原文 L388](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:388>) |
| POST `/rfqs/{rfqId}/terminate` | terminate；WRITE | ScopedControl → CommandResult；200，RFQ_TERMINATED | 不删除历史/PO/Claim；在途未清不阻止本域停新用 | [原文 L389](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:389>) |
| GET `/rfq-revisions/{revisionId}/invitations` | read；READ | page/pageSize → PageResult<InvitationData>；200 | 逐家投递与响应独立，无读权不泄露收件人 | [原文 L390](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:390>) |
| POST `/invitations/{invitationId}/manual-evidence` | delivery.record＋disclose；WRITE | DeliveryProof → CommandResult；200，DELIVERY_EVIDENCE_RECORDED | 错修订/供方409 RFQ_EVIDENCE_CONFLICT，不记SENT | [原文 L391](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:391>) |
| POST `/invitations/{invitationId}/retry` | delivery.request；WRITE | ReasonMutation → CommandResult；202，DELIVERY_RETRY_REQUESTED | 未知且渠道不能按原键去重/回查409 RFQ_DELIVERY_UNRESOLVED，禁盲发 | [原文 L392](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:392>) |

### 报价

| 路由 | 能力／类型 | 效果／错误 | 出处 |
| --- | --- | --- | --- |
| GET `/rfqs/{rfqId}/quotes` | read＋按字段commercial.read；page/pageSize/supplierKey? → PageResult<QuoteSummary> | 200；历史当前资格另轴，不改过去报价 | [原文 L524](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:524>) |
| GET `/quote-versions/{quoteVersionId}` | read → Detail<QuoteData> | 200；越界404 | [原文 L525](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:525>) |
| POST `/rfqs/{rfqId}/quotes` | quote.edit → QuoteDraftInput → CommandResult | 201 QUOTE_DRAFT_CREATED；未邀/错修订422 | [原文 L526](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:526>) |
| PATCH `/quote-versions/{quoteVersionId}` | quote.edit → QuoteDraftPatch → CommandResult | 200 QUOTE_DRAFT_SAVED；已收录/旧version409 | [原文 L527](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:527>) |
| POST `/quote-versions/{quoteVersionId}/record` | quote.record → Mutation → CommandResult | 200 QUOTE_RECORDED；引用错/非法数值422，商业未知记录为INCOMPLETE | [原文 L528](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:528>) |
| POST `/quote-versions/{quoteVersionId}/successors` | quote.edit → QuoteSuccessor → CommandResult | 201 QUOTE_SUCCESSOR_CREATED；前驱保留，不自动撤回 | [原文 L529](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:529>) |
| POST `/quote-versions/{quoteVersionId}/withdraw` | quote.withdraw → QuoteWithdrawal → CommandResult | 200 QUOTE_WITHDRAWAL_RECORDED；无可信供应商证据422 | [原文 L530](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:530>) |
| POST `/invitations/{invitationId}/decline` | response.decline → DeclineInput → CommandResult | 200 SUPPLIER_DECLINE_RECORDED；未响应不能被无证批量拒绝 | [原文 L531](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:531>) |

### 比较与审批

| 路由 | 能力／性质 | 输入输出 | 出处 |
| --- | --- | --- | --- |
| POST `/comparisons/preview` | compare+commercial.read；READ | ComparisonRequest → ComparisonData；200，即使不可比也返回明确reasons；无状态副作用 | [原文 L610](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:610>) |
| POST `/comparisons` | compare；WRITE | ComparisonRequest → CommandResult；201 COMPARISON_SAVED；冻结输入/输出，仍非当前选择 | [原文 L611](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:611>) |
| GET `/comparisons/{comparisonId}` | read+commercial.read；READ | 无body→ComparisonData；200；原值与当前适用性分栏 | [原文 L612](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:612>) |
| POST `/decisions` | decision.edit；WRITE | DecisionInput → CommandResult；201 DECISION_DRAFT_CREATED | [原文 L613](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:613>) |
| GET `/decisions/{decisionId}` | read；READ | 无body→Detail<DecisionData>；200 | [原文 L614](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:614>) |
| PATCH `/decisions/{decisionId}` | decision.edit；WRITE | DecisionPatch → CommandResult；200 DECISION_DRAFT_SAVED；非DRAFT/旧Version409 | [原文 L615](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:615>) |
| POST `/approval-intents` | approval.submit；WRITE | ApprovalSubmit → CommandResult；202 APPROVAL_REQUESTED 或200 APPROVAL_NOT_REQUIRED_RECORDED | [原文 L616](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:616>) |
| GET `/approval-intents/{intentId}` | read；READ | 无body→ApprovalIntentData；200；真实实例尚未分配时null不是批准 | [原文 L617](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:617>) |
| POST `/decisions/{decisionId}/withdraw` | decision.withdraw；WRITE | ScopedControl → CommandResult；200 DECISION_FUTURE_USE_STOPPED | [原文 L618](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:618>) |
| POST `/internal/pur/rfq/v1/approval-results` | 服务ingest.approval | ApprovalResultEnvelope → EvidenceAck；200/202；只接可信OA，详第10节 | [原文 L619](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:619>) |

### 供应价格

| 路由 | 能力／性质 | 输入输出 | 出处 |
| --- | --- | --- | --- |
| POST `/prices/search` | read+商业字段权；READ | PriceSearch → PageResult<PriceSummary>；200；无权候选不泄露 | [原文 L753](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:753>) |
| GET `/prices/{priceVersionId}` | read；READ | 无body→Detail<PriceData>；200 | [原文 L754](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:754>) |
| POST `/prices` | price.edit；WRITE | PriceDraftInput → CommandResult；201 PRICE_DRAFT_CREATED | [原文 L755](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:755>) |
| POST `/prices/from-decisions/{decisionId}` | price.edit＋原选择read；WRITE | PriceFromDecision → CommandResult；201 PRICE_DRAFT_CREATED；市场推荐不是正式RFQ采购价来源 | [原文 L756](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:756>) |
| PATCH `/prices/{priceVersionId}` | price.edit；WRITE | PricePatch → CommandResult；200 PRICE_DRAFT_SAVED；发布版409 | [原文 L757](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:757>) |
| POST `/prices/{priceVersionId}/successors` | price.edit；WRITE | ReasonMutation → CommandResult；201 PRICE_SUCCESSOR_CREATED；复制新TermSet，不覆盖旧版 | [原文 L758](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:758>) |
| POST `/prices/{priceVersionId}/publish` | price.publish；WRITE | PublishPrice → CommandResult；200 PRICE_PUBLISHED；重叠未处理409 RFQ_PRICE_OVERLAP | [原文 L759](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:759>) |
| POST `/prices/{priceVersionId}/stop` | price.stop；WRITE | StopPrice → CommandResult；200 PRICE_FUTURE_USE_STOPPED | [原文 L760](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:760>) |
| POST `/prices/resolve` | price.resolve；READ | ResolvePrice → ResolutionData；200；MISSING/CONFLICT/BLOCKED是业务解析结果，不是服务器异常 | [原文 L761](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:761>) |
| POST `/price-selections` | price.select；WRITE | ExplicitPriceSelection → CommandResult；201 PRICE_SELECTION_RECORDED（有据不需审），或202 PRICE_SELECTION_APPROVAL_REQUIRED（已存待审草稿）；候选集变化409 | [原文 L762](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:762>) |
| GET `/price-selections/{selectionId}` | read＋commercial.read；READ | 无body→PriceSelectionData；200，附当前适用性 | [原文 L763](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:763>) |

### 依据与组恢复

| 路由 | 能力／性质 | 输入输出 | 出处 |
| --- | --- | --- | --- |
| POST `/bases/preview` | basis.prepare；READ | BasisPreview→PreflightResult；200；没有来源占用或冻结 | [原文 L954](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:954>) |
| POST `/bases` | basis.prepare；WRITE | BasisDraftInput→CommandResult；201 BASIS_DRAFT_CREATED | [原文 L955](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:955>) |
| GET `/bases/{basisId}` | read；READ | →Detail<BasisData>；200；当前资格与历史内容分离 | [原文 L956](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:956>) |
| POST `/bases/{basisId}/freeze` | basis.freeze；WRITE | FreezeBasis→CommandResult；200 BASIS_CONTENT_FROZEN | [原文 L957](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:957>) |
| POST `/bases/{basisId}/release` | basis.release；WRITE | ReleaseBasis→CommandResult；200 BASIS_RELEASED；仍NOT_SUBMITTED，无Claim | [原文 L958](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:958>) |
| POST `/groups/{groupId}/submit` | conversion.submit；WRITE | SubmitGroup→CommandResult；202 GROUP_INTENT_RECORDED | [原文 L959](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:959>) |
| GET `/groups/{groupId}` | conversion.read；READ | →GroupResult；200；原键查询副作用0，不在GET补关系 | [原文 L960](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:960>) |
| POST `/groups/{groupId}/replay` | conversion.replay；WRITE | GroupRetry→CommandResult；202 GROUP_RECOVERY_REQUESTED，终态200 ORIGINAL_RESULT_RETURNED | [原文 L961](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:961>) |
| POST `/groups/{groupId}/relations/repair` | relation.repair＋结果读；WRITE | RepairRelations→CommandResult；200 RELATION_REPAIRED；旧关系版本409 | [原文 L962](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:962>) |
| POST `/groups/{groupId}/revoke` | conversion.revoke；WRITE | RevokeGroup→CommandResult；202 GROUP_REVOCATION_REQUESTED；不表示返额 | [原文 L963](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:963>) |
| POST `/groups/{groupId}/successors` | conversion.successor；WRITE | GroupSuccessor→CommandResult；201 GROUP_SUCCESSOR_REGISTERED；旧UNKNOWN或缺fence/再授权422 | [原文 L964](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:964>) |

### 历史、附件、导出和命令

| 路由 | 能力／类型 | 边界 | 出处 |
| --- | --- | --- | --- |
| GET `/rfqs/{rfqId}/history?page=1&pageSize=20` | history.read；无body → PageResult<AuditEntry> | 按本RFQ的timelineSequence倒序；同事务count/page并逐项授权；资源局部sequence不用于跨对象排序 | [原文 L1041](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1041>) |
| GET `/rfqs/{rfqId}/sources?page=1&pageSize=20` | read；无body → PageResult<SourceView> | 返回有权源引用及其观察/可用性；不是实时额度承诺 | [原文 L1042](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1042>) |
| GET `/rfqs/{rfqId}/attachments` | attachment.read；无body → AttachmentView[] | 仅已关联且当前有权附件；不可见附件不返回名称、数量或存在性提示 | [原文 L1043](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1043>) |
| POST `/attachments/{attachmentKey}/download-ticket` | attachment.download；WRITE → CommandResult | DownloadTicketRequest；201 ATTACHMENT_DOWNLOAD_AUTHORIZED，resource.kind=DOWNLOAD_TICKET；不是公开URL | [原文 L1044](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1044>) |
| GET `/download-tickets/{ticketId}/content` | attachment.download；无body → 文件流 | 再核对象/文件当前授权，成功200；过期410；不因先前出票有权就放行 | [原文 L1045](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1045>) |
| POST `/exports` | export；WRITE → CommandResult | ExportRequest；202 EXPORT_REQUESTED；只持久请求/列/筛选和生成任务 | [原文 L1046](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1046>) |
| GET `/exports/{exportId}` | export＋对象范围；READ → ExportData | 查询作业，不触发生成；失败不是空文件 | [原文 L1047](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1047>) |
| GET `/exports/{exportId}/content` | export.download；READ → CSV文件流 | 下载前再次核行/列与授权版，200；失权403；未就绪409 | [原文 L1048](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1048>) |
| GET `/commands/{commandId}` | command.read；READ → CommandObservation | 本人命令或IAM明确允许的恢复人员；返回安全历史结果/未观察，不泄漏无权对象 | [原文 L1049](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1049>) |
| POST `/commands/{commandId}/fence-unobserved` | command.fence；WRITE → CommandObservation | LocalFenceRequest；200或409；只封本模块尚未生效命令，详第12节，不取消远端PO | [原文 L1050](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1050>) |

### Owner 适配契约：地址需真实采用

| Producer 路由 | 类型 | Consumer／失败边界 | 出处 |
| --- | --- | --- | --- |
| IAM `POST /rfq-access/resolve` | AdapterContext的可信请求身份 → AccessData | 所有读写/下载，无法核现权时拒绝敏感披露/新使用 | [原文 L1150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1150>) |
| BP/工程联合适配 `POST /purchase-eligibility/check` | EligibilityRequest → AdapterResult<EligibilityResult> | 当前活动准入；报价事实收录不要求未来收货QC，缺当前替代证据只阻相应候选 | [原文 L1151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1151>) |
| 来源 `POST /purchase-share-plans` | GrantPlanRequest → AdapterResult<GrantPlanResult> | 正式选择分量身份；不Claim，不承诺数量已锁 | [原文 L1152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1152>) |
| 来源 `POST /purchase-claims/prepare` | ClaimPrepareRequest → AdapterResult<ClaimBundle> | 执行前保护，必须按CanonicalGroup/Grant防重并核父预算；部分保护时不调用PO创建 | [原文 L1153](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1153>) |
| 来源 `POST /purchase-claims/release` | ClaimReleaseRequest → AdapterResult<ClaimReleaseResult> | 无效果/fence后逐份额处置；PO拒绝消息本身不直接返额 | [原文 L1154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1154>) |
| 来源 `POST /purchase-quantities/query` | `{responsibility:SourceResponsibility,requiredEffectIds:Key[]}` → AdapterResult<SourceEvidenceBody> | 真实余额和覆盖证明；必须返回第10.4节完整类型，不以PO贡献自己算总余额 | [原文 L1155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1155>) |
| 金额/UOM `POST /rfq-calculation-contexts/resolve` | CalculationContextRequest → AdapterResult<CalculationContext> | 比较/冻结的单位/FX/税费/舍入依据；未配则原值可读，相关确定金额BLOCKED | [原文 L1156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1156>) |
| 审批 `POST /purchase-approval-policies/evaluate` | ApprovalEvaluationRequest → AdapterResult<ApprovalEvaluation> | 发行/选择/价发布/显式选价门；空矩阵/多个冲突规则不是NOT_REQUIRED | [原文 L1157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1157>) |
| OA `POST /purchase-approval-instances` | ApprovalInstanceRequest → AdapterResult<ApprovalInstanceResult> | 按intentKey幂等，失败保留申请待接入；不能伪造批准或重新发不同实例绕过 | [原文 L1158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1158>) |
| 来源条件 `POST /purchase-delivery-requirements/resolve` | DeliveryRequirementRequest → AdapterResult<RequiredDelivery> | 固定RFQ修订/源条件逐项证明NONE/REQUESTED/HARD；不能把页面日期或缺值当硬门/豁免；归属仍来源Owner | [原文 L1159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1159>) |
| 日历 `POST /delivery-calendars/resolve` | DeliveryCalendarRequest → AdapterResult<DeliveryCalendarSnapshot> | 精确源版本、时区和计日规则；失败原报价可留存，依赖该日历的交期承接BLOCKED | [原文 L1160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1160>) |
| PO `POST /rfq-commercial-capabilities/check` | CommercialCheckRequest → AdapterResult<CommercialCheckResult> | 原币/模式/费用及RequiredDelivery/SupplierDelivery逐项映射；不兼容阻断新效果，不试写订单探测 | [原文 L1161](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1161>) |
| PO `POST /rfq-groups/execute` | PoExecuteRequest → AdapterResult<ExecutionEvidenceBody> | 全manifest一次创建；UNKNOWN同CanonicalGroup回查，绝不换键补建 | [原文 L1162](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1162>) |
| PO `POST /rfq-groups/query` | `{canonicalGroupKey:Id,manifestHash:string}` → AdapterResult<ExecutionEvidenceBody> | 可能有查询水位滞后；未见不等NO_EFFECT | [原文 L1163](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1163>) |
| PO `POST /rfq-groups/fence` | `{canonicalGroupKey:Id,manifestHash:string,generation:Seq,reason:string}` → AdapterResult<RevokeResultBody> | 与创建串行；创建先则TOO_LATE，取消先则持久拒绝旧代次；不能删除历史PO | [原文 L1164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1164>) |
| 文件 `POST /evidence-access/check` | `{refs:Ref[],rfqId:Id,recipientKey:Key／null,purpose:'READ'／'DOWNLOAD'／'DISCLOSE'}` → AdapterResult<FileAccessResult> | 各动作独立；未配置/失权/不可读分开，不回裸地址 | [原文 L1165](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1165>) |
| 投递 `POST /rfq-invitations/deliver` | DeliveryRequest → AdapterResult<DeliveryResultBody> | 原deliveryKey幂等或明确不支持；不支持结果查询的未知投递禁止盲发 | [原文 L1166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1166>) |

### 内部证据：唯一写集合

| 完整路由 | 能力／类型 | 准许写集 | 出处 |
| --- | --- | --- | --- |
| `/internal/pur/rfq/v1/evidence/source` | ingest.source；SourceEvidenceEnvelope → EvidenceAck | Inbox/Conflict、SourceObservation、SourceContribution、StreamCursor、CoverageDiscrepancy；不写PR账、PO结果 | [原文 L1327](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1327>) |
| `/internal/pur/rfq/v1/evidence/execution` | ingest.execution；ExecutionEvidenceEnvelope → EvidenceAck | Inbox、PoExecutionObservation、PoEffect、Group结果投影、待关系任务；不填整条源余额 | [原文 L1328](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1328>) |
| `/internal/pur/rfq/v1/evidence/relation` | ingest.relation；RelationEvidenceEnvelope → EvidenceAck | Inbox、RelationSet/Item的受信本域结果、修复问题；不改PO/源账 | [原文 L1329](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1329>) |
| `/internal/pur/rfq/v1/approval-results` | ingest.approval；ApprovalResultEnvelope → EvidenceAck | OA事实、Inbox、Application任务/结果；实际应用仍核subject/scope前驱 | [原文 L1330](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1330>) |
| `/internal/pur/rfq/v1/control-results` | ingest.control；ControlEnvelope → EvidenceAck | ControlFact、ImpactItem、相关Gate/停止派发；不抹历史执行 | [原文 L1331](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1331>) |
| `/internal/pur/rfq/v1/delivery-results` | ingest.delivery；DeliveryEnvelope → EvidenceAck | Invitation投递观察及Inbox，不生成QUOTE或PO | [原文 L1332](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1332>) |
| `/internal/pur/rfq/v1/revocation-results` | ingest.revocation；RevokeResultEnvelope → EvidenceAck | Inbox、RevokeRequest/组取消结果；禁止直接回补Source量或抹掉PO成功 | [原文 L1333](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1333>) |

Source Grant 代理补充 POST /source-grant-plans（decision.edit、写 key、GrantPlanRequest）返回202，GET /source-grant-plans/{requestKey} 查 AdapterResult<GrantPlanResult>；不存在接口时仍可 MARKET/缺项草稿，不能正式应用采购选择绕门。生产各 Owner 地址从部署配置，预算读2秒／执行与查5秒，超时 UNKNOWN，不直接重复创建。 [原文 L1118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1118>)、[原文 L1146](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1146>)

### 选项、前驱与邀请

[原文 L394](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:394>)

```ts
interface LookupSearch extends PageRequest { keyword: string | null; siteKey: Key; }
interface SupplierOption { supplierKey: Key; name: string; recipientKeys: Key[]; eligible: boolean; observationRef: Ref; }
interface ItemOption { itemKey: Key; name: string; uomCodes: string[]; active: boolean; }
interface SourceOption {
  responsibility: SourceResponsibility; sourceBusinessVersion: Key; itemKey: Key; technicalBasis: TechnicalBasis;
  authorizedQty: Qty | null; consumedQty: Qty | null; potentialQty: Qty | null; actionableQty: Qty | null;
  uomCode: string; requiredDate: DateText | null; observationRef: Ref; coverage: 'COMPLETE' | 'INCOMPLETE' | 'UNKNOWN';
}
interface Options {
  schemaVersion: 'RFQ-SPEC-1'; actorKey: Key; siteKeys: Key[]; defaultSiteKey: Key | null;
  siteTimeZones:{siteKey:Key;timeZoneId:string;policyRef:Ref}[];
  capabilities: string[]; currencies: {code: string; minorUnits: number}[];
  units: {code: string; label: string}[]; dictionaryAvailability: Availability;
  policyMechanisms: {singleSupplierPerLine: true; controlledZeroPrice: true; explicitNoEnd: true; autoPricePriority: false; scopedReplacement: true; crossGroupPricing: false};
  limits: {rfqLines: 200; suppliers: 50; tiersPerLine: 20; chargesPerLine: 20; attachments: 20};
  evidenceMode: 'REAL' | 'DEMO';
}
interface RfqDetail {
  rfq: RfqListItem; version: Version; revisions: {id: Id; businessVersion: Seq; status: string}[];
  actionability: Actionability[]; integrations: {owner: Key; availability: Availability}[];
  redactedFields: string[];
}
interface SuccessorRevision { predecessorRevisionId: Id; expectedRfqVersion: Version; reason: string; }
interface IssuePreview { proposedEffectiveAt: Instant; replacement: Replacement | null; }
interface PreflightResult { allowed: boolean; gates: Gate[]; checkedAt: Instant; contentHash: string; }
interface IssueRevision extends Mutation { effectiveAt: Instant; replacement: Replacement | null; approvalIntentId: Id | null; }
interface ScopedControl extends ReasonMutation { scopeLineIds: Id[]; effectiveAt: Instant; evidenceRef: Ref | null; }
interface DeliveryProof extends Mutation { sentAt: Instant; channel: 'EMAIL' | 'PAPER' | 'OTHER'; evidenceRef: Ref; reason: string; }
interface InvitationData {
  id: Id; revisionId: Id; supplierKey: Key; recipientKey: Key | null; version: Version;
  delivery: 'PREPARED' | 'SENT' | 'FAILED' | 'UNKNOWN' | 'MANUAL_EVIDENCED';
  response: 'UNANSWERED' | 'QUOTED' | 'DECLINED'; deliveryKey: Id;
  attemptedAt: Instant | null; evidenceRefs: Ref[];
}
```

### 报价后继／撤回

[原文 L533](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:533>)

```ts
interface QuoteSummary { id: Id; familyId: Id; familyBinding: 'UNBOUND'|'BOUND'; boundExternalIdentity:QuoteExternalIdentity|null; supplierKey: Key; supplierQuoteNo: string | null; businessVersion: Seq; status: string; receivedAt: Instant; eligibleLineCount: number | null; }
interface QuoteSuccessor { expectedVersion: Version; reason: string; newEvidenceRefs: Ref[]; respondsToRevisionId: Id | null; }
interface QuoteWithdrawal extends ReasonMutation { effectiveAt: Instant; quoteLineIds: Id[]; supplierEvidenceRef: Ref; }
interface DeclineInput extends ReasonMutation { occurredAt: Instant; evidenceRef: Ref; }
```

### 比较、分配和审批

[原文 L623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:623>)

```ts
interface ComparisonRequest {
  rfqRevisionId: Id; mode: 'MARKET' | 'PROCUREMENT'; evaluationAt: Instant;
  comparisonCurrency: string; amountPolicyRef: Ref;
  lines: {rfqLineId: Id; evaluationQty: Qty; evaluationUom: string; quoteVersionIds: Id[]}[];
}
interface MoneyComponent {
  componentKey: Key; amount: Amount | null; currencyCode: string;
  comparisonAmount: Amount | null; comparisonCurrency: string;
  inclusion: 'CANONICAL' | 'DERIVED' | 'UNKNOWN';
  fxRef: Ref | null; evidenceRefs: Ref[];
}
interface ComparisonCandidate {
  rfqLineId: Id; quoteVersionId: Id; quoteLineId: Id; supplierKey: Key;
  eligible: boolean; comparable: boolean; reasons: string[];
  components: MoneyComponent[]; goodsAmount: Amount | null;
  comparableAmount: Amount | null; rank: number | null; quoteUomQty: Qty | null;
}
interface ComparisonData {
  id: Id | null; input: ComparisonRequest; inputHash: string;
  candidates: ComparisonCandidate[]; completeTotal: Amount | null;
  currentApplicable: boolean; gates: Gate[]; evidenceMode: 'REAL' | 'DEMO';
}
interface AllocationInput {
  allocationId: Id | null; rfqLineId: Id; quoteVersionId: Id; quoteLineId: Id;
  supplierKey: Key; qty: Qty; uomCode: string; sourceShareGrantRefs: Ref[];
}
interface DecisionInput {
  predecessorDecisionId: Id | null; comparisonId: Id; purpose: 'MARKET_RECOMMENDATION' | 'PROCUREMENT_SELECTION';
  allocations: AllocationInput[]; reason: string; expectedBindings: BindingPredecessor[];
  replacement: Replacement | null;
}
type DecisionPatch = Mutation & Partial<Pick<DecisionInput,'allocations'|'reason'|'comparisonId'|'replacement'|'expectedBindings'>>;
interface SavedAllocation extends Omit<AllocationInput,'allocationId'> { allocationId: Id; }
interface DecisionData extends Omit<DecisionInput,'allocations'> {
  allocations: SavedAllocation[];
  id: Id; businessVersion: Seq; status: 'DRAFT' | 'SUBMITTED' | 'APPLIED' | 'WITHDRAWN';
  approval: ApprovalIntentData | null;
  application: {state: 'NOT_APPLIED' | 'APPLIED' | 'CONFLICT' | 'BLOCKED' | 'REJECTED'; bindings: BindingPredecessor[] | null; errorCode: string | null; applicationRef: Ref | null};
}
interface ApprovalSubmit {
  subject: {kind: 'RFQ_ISSUE' | 'DECISION' | 'PRICE_PUBLISH' | 'EXPLICIT_PRICE_SELECTION'; id: Id; expectedVersion: Version};
  policyRef: Ref; replacementIntentId: Id | null; reason: string;
}
interface ApprovalIntentData {
  id: Id; subjectKind: ApprovalSubmit['subject']['kind']; subjectId: Id;
  subjectBusinessVersion: Seq; subjectRowVersion: Version; subjectContentHash: string;
  version: Version; intentIdentityHash: string;
  policyRef: Ref; requirement: 'REQUIRED' | 'NOT_REQUIRED_WITH_POLICY';
  approvalInstanceKey: Key | null; decision: 'PENDING' | 'APPROVED' | 'REJECTED' | 'NOT_REQUIRED';
  applicationState: 'NOT_APPLIED' | 'APPLIED' | 'CONFLICT' | 'BLOCKED' | 'REJECTED';
  resultRef: Ref | null; requestedAt: Instant;
}
```

### 供应价及显式选择

[原文 L765](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:765>)

```ts
interface SiteScope { mode: 'EXACT' | 'SET' | 'ALL_WITH_POLICY'; siteKeys: Key[]; policyRef: Ref | null; }
interface ItemScope { itemKey: Key; technicalRevisionRefs: Ref[]; notApplicableEvidenceRef: Ref | null; }
interface PriceDraftInput {
  supplierKey: Key; itemScope: ItemScope; siteScope: SiteScope;
  terms: CommercialTerms; amountPolicyRef: Ref|null; sourceKind: 'MANUAL'; sourceEvidenceRefs: Ref[]; reason: string;
}
interface PriceFromDecision { expectedDecisionVersion: Version; allocationId: Id; reason: string; }
type PricePatch = Mutation & Partial<Pick<PriceDraftInput,'itemScope'|'siteScope'|'terms'|'amountPolicyRef'|'sourceEvidenceRefs'|'reason'>>;
interface PriceData extends Omit<PriceDraftInput,'sourceKind'> {
  id: Id; familyId: Id; businessVersion: Seq; status: 'DRAFT' | 'PUBLISHED' | 'STOPPED';
  sourceKind: 'MANUAL' | 'RFQ_DECISION'; sourceDecisionId: Id | null; predecessorId: Id | null;
}
interface PriceSearch extends PageRequest { supplierKey: Key | null; itemKey: Key | null; siteKey: Key | null; status: 'DRAFT'|'PUBLISHED'|'STOPPED'|'ALL'; asOf: Instant | null; sort:'updatedDesc'|'supplierAsc'; }
interface PriceSummary { id: Id; familyId: Id; businessVersion: Seq; supplierKey: Key; itemKey: Key; status: string; currencyCode: string | null; sourceKind: string; updatedAt: Instant; }
interface PublishPrice extends Mutation { approvalIntentId: Id | null; supersedePriceVersionIds: Id[]; scopeManifestRef: Ref | null; }
interface StopPrice extends ReasonMutation { effectiveAt: Instant; siteKeys: Key[]; }
interface ResolvePrice {
  supplierKey: Key; itemKey: Key; technicalBasis: TechnicalBasis; siteKey: Key;
  quantity: Qty; uomCode: string; settlementCurrencyCode: string; comparisonCurrency: string; asOf: Instant;
  amountPolicyRef: Ref; explicitSelectionId: Id | null;
}
interface ResolutionCandidate { priceVersionId: Id; businessVersion: Seq; eligible: boolean; reasons: string[]; terms: CommercialTerms | null; }
interface ResolutionData {
  outcome:'MATCHED'|'MISSING'|'CONFLICT'|'BLOCKED'; request: ResolvePrice;
  selectedPriceVersionId: Id | null; requestHash: string; candidateSetHash: string; candidates: ResolutionCandidate[];
  resolutionToken: string; expiresAt: Instant; components: MoneyComponent[]; gates: Gate[];
}
interface ExplicitPriceSelection {
  resolutionToken: string; selectedPriceVersionId: Id; reason: string;
  authorityEvidenceRef: Ref; approvalIntentId: Id | null; predecessorSelectionId: Id | null;
}
interface PriceSelectionData extends Omit<ExplicitPriceSelection,'resolutionToken'> {
  id: Id; familyId: Id; businessVersion: Seq;
  resolvedRequest: ResolvePrice; requestHash: string; candidateSetHash: string; selectedContentHash: string;
  recordedAt: Instant; currentApplicable: boolean; version: Version;
  state: 'DRAFT'|'SUBMITTED'|'APPLIED'|'BLOCKED';
}
```

### 服务器内部受签 claims

[原文 L850](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:850>)

```ts
interface ResolutionTokenClaims {
  schemaVersion: 'RFQ-RESOLVE-2';
  tenantId: Id; actorKey: Key; authorizationRevision: Key;
  canonicalRequest: ResolvePrice; // 完整成员；explicitSelectionId必须为null
  requestHash: string; candidateSetHash: string;
  issuedAt: Instant; expiresAt: Instant; signingKeyId: Key;
}
```

### 准确商业依据、组清单及六轴结果

[原文 L893](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:893>)

```ts
interface GrantShare {
  grantRef: Ref; responsibility: SourceResponsibility; sourceBusinessVersion: Key;
  quantity: Qty; sourceUom: string; generation: Seq;
}
interface ManifestRow {
  rowId: Id; allocationId: Id; rfqLineId: Id; supplierKey: Key; itemKey: Key;
  technicalBasis: TechnicalBasis; siteKey: Key; quoteVersionId: Id; quoteLineId: Id;
  sourceShares: GrantShare[]; quantity: Qty; uomCode: string;
  commercialSourceHash: string; targetPayloadHash: string; mappingRef: Ref;
}
interface GroupManifest {
  groupId: Id; canonicalGroupKey: Id; businessIdentityHash: string; manifestHash: string;
  executionMode: 'ATOMIC_MANIFEST'; supplierKey: Key; scopeAtomKeys: Key[];
  rows: ManifestRow[]; commercialPayload: TargetCommercialPayload;
}
interface BasisDraftInput { decisionId: Id; expectedDecisionVersion: Version; allocationIds: Id[]; reason: string; }
interface BasisPreview { decisionId: Id; allocationIds: Id[]; evaluationAt: Instant; }
interface FreezeBasis extends Mutation { expectedDecisionApplicationRef: Ref; adapterContractRef: Ref; }
interface ReleaseBasis extends Mutation { expectedContentHash: string; }
interface SubmitGroup extends Mutation { basisId: Id; expectedBasisVersion: Version; expectedManifestHash: string; }
interface GroupRetry extends Mutation { expectedManifestHash: string; reason: string; }
interface GroupSuccessor {
  predecessorGroupId: Id; terminalReceiptRef: Ref; fenceRef: Ref; reauthorizationRefs: Ref[];
  replacementBasisId: Id; reason: string;
}
interface RepairRelations extends Mutation { executionReceiptRef: Ref; expectedRelationRevision: Seq; reason: string; }
interface RevokeGroup extends Mutation { reason: string; expectedManifestHash: string; }
interface BasisData {
  id: Id; batchKey: Id; decisionId: Id; status:'DRAFT'|'FROZEN'|'RELEASED'|'STOPPED'; businessVersion: Seq;
  contentHash: string | null; groups: GroupManifest[]; gates: Gate[];
  currentApplicable: boolean; usableUntil: Instant | null; validityKind:'END_AT'|'NO_END'|'UNKNOWN';
}
interface SourceQuantity {
  responsibility: SourceResponsibility; sourceBusinessVersion: Key; itemKey: Key; technicalBasis: TechnicalBasis; siteKey: Key;
  uomCode: string; authorizedQty: Qty | null; consumedQty: Qty | null; potentialQty: Qty | null;
  actionableQty: Qty | null; overcommittedQty: Qty | null;
  streamId: Key; epoch: Key; version: Seq; asOf: Instant; coverage:'COMPLETE'|'INCOMPLETE'|'UNKNOWN';
}
interface PoLineResult { manifestRowId: Id; effectId: Key; poId: Key; poNo: Key; poLineId: Key; quantity: Qty; uomCode: string; sourceShareGrantRefs: Ref[]; sourceShareQuantities:{grantRef:Ref;quantity:Qty;uomCode:string}[]; commercialPayloadHash: string; deliverySnapshotHash:string; deliveryEvidenceRef:Ref; }
interface GroupResult {
  groupId: Id; canonicalGroupKey: Id; manifestHash: string; version: Version;
  preparation:'NOT_STARTED'|'PARTIAL'|'PROTECTED'|'UNKNOWN';
  execution:'NOT_STARTED'|'PENDING'|'SUCCEEDED'|'REJECTED'|'CANCELLED_NO_EFFECT'|'UNKNOWN';
  executionTerminal:boolean; poLines:PoLineResult[];
  sourceDisposition:'NOT_STARTED'|'PENDING'|'CONFIRMED'|'CONFLICT';
  relationState:'PENDING'|'SYNCED'|'FAILED'|'CONFLICT'; relationRevision:Seq;
  cancellation:'NONE'|'REQUESTED'|'NO_EFFECT'|'TOO_LATE_EXECUTED'|'UNKNOWN';
  sources:SourceQuantity[]; unreconciledEffectIds:Key[]; currentRestrictions:string[];
  evidenceRefs:Ref[]; evidenceMode:'REAL'|'DEMO';
}
```

### 审计、导出和有限本地 fence

[原文 L1054](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1054>)

```ts
interface AuditEntry {
  id:Id; sequence:Seq; timelineSequence:Seq; resourceKind:string; resourceId:Id; businessVersion:Seq|null;
  operation:string; actorKey:Key; occurredAt:Instant; reason:string|null;
  changes:{field:string; before:string|null; after:string|null; redacted:boolean}[];
  commandId:Id|null; evidenceRefs:Ref[];
}
interface SourceView { link:SourceLink; availability:Availability; observation:SourceQuantity|null; reasonCode:string|null; }
interface AttachmentView { ref:Ref; fileName:string; mediaType:string; bytes:Seq; versionHash:string; canDownload:boolean; canDisclose:boolean; }
interface DownloadTicketRequest { rfqId:Id; evidenceRef:Ref; purpose:'REVIEW'; }
interface ExportRequest {
  kind:'RFQ_LIST'|'COMPARISON'|'SUPPLIER_PRICE'|'GROUP_RESULTS';
  rfqFilter:RfqSearch|null; priceFilter:PriceSearch|null;
  comparisonId:Id|null; groupIds:Id[]; includeCommercial:boolean; reason:string;
}
interface ExportData {
  id:Id; state:'QUEUED'|'RUNNING'|'READY'|'FAILED'|'REVOKED'|'EXPIRED';
  fileName:string|null; rowCount:number|null; sha256:string|null;
  expiresAt:Instant|null; errorCode:string|null; downloadAllowed:boolean; observedAt:Instant;
}
interface CommandObservation {
  commandId:Id; state:'NOT_OBSERVED'|'SUCCEEDED'|'ACCEPTED'|'REJECTED'|'CANCELLED_NO_EFFECT';
  result:CommandResult|null; canReplay:boolean; canFenceLocal:boolean; checkedAt:Instant;
}
interface LocalFenceRequest {
  originalMethod:'POST'|'PATCH'; originalPath:string; originalRequestHash:string;
}
```

### 适配 envelope、Grant、Claim 与 fence

[原文 L1122](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1122>)

```ts
interface AdapterContext { tenantId:Id; actorKey:Key; siteKey:Key; authorizationRevision:Key; correlationId:string; }
interface EvidenceMeta { contractRef:Ref; evidenceRef:Ref; evidenceMode:'REAL'|'DEMO'; asOf:Instant; }
interface AdapterResult<T> { availability:Availability; data:T|null; errorCode:string|null; meta:EvidenceMeta|null; }
interface EligibilityRequest { supplierKey:Key; itemKey:Key|null; technicalBasis:TechnicalBasis|null; activity:'RFQ_ISSUE'|'COMPARE'|'DECISION_APPLY'|'PRICE_PUBLISH'|'PO_CREATE'; at:Instant; }
interface EligibilityResult {
  supplierAllowed:boolean; technicalResult:'APPLICABLE'|'NOT_APPLICABLE_WITH_BASIS'|'BLOCKED'|'UNKNOWN';
  requiredQualityEvidenceRefs:Ref[]; missing:string[]; controlRevision:Key; gateEvidenceRefs:Ref[];
}
interface PoQueryRequest { canonicalGroupKey:Id; manifestHash:string; }
interface SourceQueryRequest { responsibility:SourceResponsibility; requiredEffectIds:Key[]; }

interface GrantPlanRequest { requestKey:Id; responsibility:SourceResponsibility; sourceBusinessVersion:Key; supplierKey:Key; qty:Qty; uomCode:string; allocationLineageKey:Key; }
interface GrantPlanResult { share:GrantShare; allocationLineageKey:Key; scopeAtomKeys:Key[]; remainingAuthorizedObservation:SourceQuantity; isReservation:false; }
interface ClaimPrepareRequest { canonicalGroupKey:Id; manifestHash:string; executionGeneration:Seq; grants:GrantShare[]; controlUseRef:Ref; }
interface ClaimRecord { claimRef:Ref; grantRef:Ref; canonicalGroupKey:Id; qty:Qty; uomCode:string; state:'PROTECTED'|'CONSUMED'|'RELEASED'|'UNKNOWN'; generation:Seq; }
interface ClaimBundle { bundleRef:Ref; canonicalGroupKey:Id; manifestHash:string; state:'ALL_PROTECTED'|'PARTIAL'|'REJECTED'|'UNKNOWN'; claims:ClaimRecord[]; errors:FieldError[]; }
interface FenceProof { ref:Ref; canonicalGroupKey:Id; manifestHash:string; generation:Seq; terminal:'REJECTED'|'CANCELLED_NO_EFFECT'; shareGrantRefs:Ref[]; coveredPaths:Key[]; closedThrough:Key; }
interface ClaimReleaseRequest { bundleRef:Ref; fence:FenceProof; reason:string; }
interface ClaimReleaseResult { claims:ClaimRecord[]; sourceEvidenceRefs:Ref[]; complete:boolean; }
```

### IAM、审批政策与声明式金额步骤

[原文 L1172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1172>)

```ts
interface AccessData { tenantId:Id; actorKey:Key; capabilitySet:string[]; siteKeys:Key[]; readScope:{kind:'OWNER'|'TEAM'|'ALL_AUTHORIZED';buyerKeys:Key[]}; commercialFieldSet:string[]; authorizationRevision:Key; checkedAt:Instant; expiresAt:Instant; }
interface ApprovalEvaluationRequest {
  subjectKind:'RFQ_ISSUE'|'DECISION'|'PRICE_PUBLISH'|'EXPLICIT_PRICE_SELECTION'; subjectRef:Ref;
  contentHash:string; scopeAtomKeys:Key[]; actorKey:Key; siteKey:Key;
  amount:{value:Amount;currencyCode:string;policyRef:Ref}|null;
  flags:{zeroPrice:boolean;noEndDate:boolean;nonLowestChoice:boolean;scopeReplacement:boolean};
  changedCommercialFields:string[]; requestedPolicyRef:Ref;
}
interface ApprovalRule {
  ruleId:Key; subjectKinds:ApprovalEvaluationRequest['subjectKind'][]; siteKeys:Key[];
  requiredFlags:{name:'zeroPrice'|'noEndDate'|'nonLowestChoice'|'scopeReplacement';value:boolean}[];
  amountInterval:{minInclusive:Amount|null;maxExclusive:Amount|null;currencyCode:string}|null;
  result:'REQUIRED'|'NOT_REQUIRED_WITH_POLICY'; approverRoleKeys:Key[];
  separatedFromSubmitter:boolean; reapprovalFields:string[]; exemptionEvidenceRef:Ref|null;
}
interface ApprovalPolicyDocument { ref:Ref; effectiveFrom:Instant; effectiveTo:Instant|null; rules:ApprovalRule[]; sourceSignatureRef:Ref; }
interface ApprovalEvaluation {
  requirement:'REQUIRED'|'NOT_REQUIRED_WITH_POLICY'|'BLOCKED'; matchedRuleId:Key|null;
  policyRef:Ref; subjectHash:string; approverRoleKeys:Key[]; separatedFromSubmitter:boolean;
  proofRef:Ref|null; missing:string[];
}
interface ApprovalInstanceRequest { intentKey:Id; subjectKind:ApprovalSubmit['subject']['kind']; subjectRef:Ref; subjectBusinessVersion:Seq; subjectRowVersion:Version; subjectHash:string; evaluation:ApprovalEvaluation; submitterKey:Key; }
interface ApprovalInstanceResult { intentKey:Id; instanceKey:Key; state:'PENDING'|'APPROVED'|'REJECTED'; resultRef:Ref|null; }
interface CalculationContextRequest { supplierKey:Key; siteKey:Key; itemKey:Key; quantity:Qty; requestUom:string; terms:CommercialTerms; comparisonCurrency:string; evaluationAt:Instant; amountPolicyRef:Ref; }
interface UnitConversion { fromUom:string; toUom:string; numerator:Seq; denominator:Seq; ref:Ref; }
interface FxConversion { fromCurrency:string; toCurrency:string; rate:Rate; ref:Ref; at:Instant; }
interface AmountStep {
  id:Key; operation:'MUL_RATE'|'ADD_COMPONENT'|'INCLUDED_TAX_TO_NET'|'NET_TO_GROSS'|'PERCENT_DISCOUNT'|'ROUND';
  inputs:Key[]; output:Key; rate:Fraction|null; scale:number|null;
  rounding:'HALF_AWAY_FROM_ZERO'|'HALF_EVEN'|'TOWARD_ZERO'|null; policyRef:Ref;
}
interface CalculationContext {
  unitConversions:UnitConversion[]; fxConversions:FxConversion[];
  steps:AmountStep[]; outputComponentKeys:Key[];
  currencyScales:{currencyCode:string;scale:number}[];
  noMoqEvidence:Ref|null; noMultipleEvidence:Ref|null;
  declaredSupplyCoverage:Ref|null; paymentTermsRef:Ref|null;
  independentPricingScopeRef:Ref; validThrough:Instant; missing:string[];
}
```

### 目标商业／三段交付／日历与投递

[原文 L1230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1230>)

```ts
interface RequiredDelivery {
  requiredDate:DateText|null; constraint:'NONE'|'REQUESTED'|'HARD'|'UNKNOWN';
  destinationKey:Key; requirementEvidenceRefs:Ref[];
}
interface DeliveryResolution {
  mode:'FIXED_DATE'|'RELATIVE_LEAD_DAYS'|'COMBINED'|'UNKNOWN';
  anchorStatus:'KNOWN'|'PENDING_PO_CONFIRMED'|'NOT_APPLICABLE'|'UNKNOWN';
  anchorDate:DateText|null; anchorEvidenceRef:Ref|null; resolvedDate:DateText|null;
  calendarSnapshotRef:Ref|null; calendarRuleHash:string|null; calculationEvidenceRef:Ref|null;
  requiredDateCheck:'NOT_REQUIRED'|'MEETS'|'LATE'|'PENDING_ANCHOR'|'UNKNOWN';
  combinedCheck:'NOT_APPLICABLE'|'AGREES'|'PENDING_ANCHOR'|'CONFLICT'|'UNRESOLVED';
  admission:'ALLOWED'|'BLOCKED'; reasonCodes:string[];
}
interface TargetLine {
  manifestRowId:Id; itemKey:Key; quantity:Qty; uomCode:string; settlementCurrency:string;
  billingMode:'ALL_UNITS'|'PROGRESSIVE'; quoteUom:string; priceBasisQty:Qty;
  appliedTiers:{lower:Qty;upper:Qty|null;unitPrice:UnitPrice;appliedQty:Qty}[];
  goodsAmount:Amount; tax:TaxTerms; roundingEvidenceRefs:Ref[]; sourceTermsRef:Ref;
  sourceShareRefs:Ref[]; requiredDelivery:RequiredDelivery;
  supplierDelivery:DeliveryTerms; deliveryResolution:DeliveryResolution;
}
interface TargetCharge {
  occurrenceKey:Key; economicTermKey:Key; currencyCode:string; amount:Amount;
  representation:'HEADER_CANONICAL'|'LINE_ALLOCATIONS_CANONICAL';
  allocations:{manifestRowId:Id;amount:Amount}[]; includedInComponentKey:Key|null; sourceRef:Ref;
}
interface TargetCommercialPayload {
  supplierKey:Key; siteKey:Key; destinationKey:Key; settlementCurrency:string; paymentTermsRef:Ref;
  pricingScopeRef:Ref; lines:TargetLine[]; charges:TargetCharge[];
  sourceHash:string; payloadHash:string; adapterContractRef:Ref;
}
interface MappingItem { sourcePath:string; targetPath:string; disposition:'PRESERVED'|'EXPLICIT_TRANSFORMATION'|'BLOCKED'; sourceRef:Ref; transformationPolicyRef:Ref|null; reasonCode:string|null; }
interface CommercialCheckRequest {
  basisCandidateKey:Key; selectionApplicationRef:Ref;
  rows:{rfqRevisionId:Id;allocation:SavedAllocation;terms:CommercialTerms;
    requiredDelivery:RequiredDelivery;sourceShares:GrantShare[]}[];
  adapterContractRef:Ref;
}
interface CommercialCheckResult { supported:boolean; mappingRef:Ref; mapping:MappingItem[]; targetPayload:TargetCommercialPayload|null; unsupportedFields:string[]; }
interface PoExecuteRequest { canonicalGroupKey:Id; manifestHash:string; generation:Seq; manifest:GroupManifest; claims:ClaimBundle; controlUseRef:Ref; }
interface RevokeResultBody { canonicalGroupKey:Id; manifestHash:string; generation:Seq; outcome:'NO_EFFECT'|'TOO_LATE_EXECUTED'|'PENDING'|'UNKNOWN'; fence:FenceProof|null; executionEvidenceRef:Ref|null; }
interface DeliveryRequirementRequest {
  rfqRevisionId:Id; rfqLineId:Id; requiredDate:DateText|null; destinationKey:Key;
  sourceLinks:SourceLink[];
}
interface DeliveryCalendarRequest { calendarRef:Ref; leadDayKind:'WORKING_DAY'|'CALENDAR_DAY'; }
interface DeliveryCalendarSnapshot {
  sourceRef:Ref; snapshotRef:Ref; ruleHash:string; timeZoneId:Key;
  countRule:'EXCLUDE_ANCHOR_COUNT_VALID_DAYS'; zeroDaysRule:'ANCHOR_DATE';
  coveredFrom:DateText; coveredTo:DateText;
  workingWeekdays:number[]; closedDates:DateText[]; extraWorkingDates:DateText[];
}
interface FileAccessResult { permitted:boolean; objectRefs:Ref[]; recipientKey:Key|null; accessProofRef:Ref|null; expiresAt:Instant|null; missing:string[]; }
interface DeliveryRequest { deliveryKey:Id; rfqRevisionRef:Ref; invitationId:Id; supplierKey:Key; recipientKey:Key; attachmentRefs:Ref[]; disclosureProofRef:Ref; contentHash:string; }
interface DeliveryResultBody { deliveryKey:Id; invitationId:Id; state:'SENT'|'REJECTED'|'UNKNOWN'; channelEvidenceRef:Ref|null; occurredAt:Instant; reasonCode:string|null; }
```

### 三流及控制／审批／投递／撤销证据

[原文 L1335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1335>)

```ts
interface EvidenceHeader {
  eventId:Id; producerOwner:Key; contractRef:Ref; streamId:Key; epoch:Key; version:Seq;
  occurredAt:Instant; evidenceMode:'REAL'|'DEMO';
}
interface CoveredContribution {
  effectId:Key; effectRevision:Seq; grantRef:Ref; quantity:Qty; uomCode:string;
  bucket:'CONSUMED'|'POTENTIAL'|'RELEASED_WITH_PROOF'; dispositionRef:Ref;
}
interface CoverageDeclaration {
  completeForResponsibility:boolean; manifestRef:Ref; contributions:CoveredContribution[];
  producerWatermarks:{producerOwner:Key;streamId:Key;epoch:Key;throughVersion:Seq}[];
}
interface SourceDelta {
  baseVersion:Seq; newVersion:Seq; quantityBasisRef:Ref;
  changes:{effectId:Key;grantRef:Ref;fromBucket:'NONE'|'POTENTIAL'|'CONSUMED';toBucket:'POTENTIAL'|'CONSUMED'|'RELEASED_WITH_PROOF';quantity:Qty;uomCode:string;proofRef:Ref}[];
}
interface SourceCorrection {
  correctionId:Key; replacesEvidenceRef:Ref; baseVersion:Seq;
  correctedObservation:SourceQuantity; correctedCoverage:CoverageDeclaration; reason:string; authorityRef:Ref;
}
interface CheckpointTransfer {
  oldStreamId:Key; oldEpoch:Key; throughVersion:Seq; newStreamId:Key; newEpoch:Key;
  openingVersion:Seq; coveredEvidenceRefs:Ref[]; coverage:CoverageDeclaration; authorityRef:Ref;
}
interface SourceEvidenceBody {
  kind:'SNAPSHOT'|'DELTA'|'CORRECTION'|'CHECKPOINT'; responsibility:SourceResponsibility;
  quantityBasisRef:Ref; observation:SourceQuantity|null; coverage:CoverageDeclaration|null;
  delta:SourceDelta|null; correction:SourceCorrection|null; checkpoint:CheckpointTransfer|null;
}
interface SourceEvidenceEnvelope extends EvidenceHeader { type:'SOURCE_QUANTITY'; body:SourceEvidenceBody; }
interface ExecutionEvidenceBody {
  groupId:Id; canonicalGroupKey:Id; manifestHash:string; generation:Seq;
  outcome:'ACCEPTED_PENDING'|'SUCCEEDED'|'REJECTED'|'CANCELLED_NO_EFFECT'|'UNKNOWN';
  terminal:boolean; completeForManifest:boolean; poLines:PoLineResult[];
  possibleEffects:{manifestRowId:Id;grantRefs:Ref[];quantity:Qty;uomCode:string}[];
  fence:FenceProof|null; commercialPayloadHash:string; authorityRef:Ref;
}
interface ExecutionEvidenceEnvelope extends EvidenceHeader { type:'PO_EXECUTION'; body:ExecutionEvidenceBody; }
interface RelationEvidenceEnvelope extends EvidenceHeader {
  type:'RELATION'; body:{groupId:Id;executionEvidenceRef:Ref;expectedRelationRevision:Seq;mapping:PoLineResult[];repairKey:Id;result:'SYNCED'|'FAILED'|'CONFLICT';reasonCode:string|null};
}
interface ApprovalResultEnvelope extends EvidenceHeader {
  type:'APPROVAL'; body:{intentId:Id;instanceKey:Key;subjectBusinessVersion:Seq;subjectRowVersion:Version;subjectHash:string;policyRef:Ref;decision:'APPROVED'|'REJECTED';decidedBy:Key;decisionAt:Instant;decisionEvidenceRef:Ref};
}
interface ControlEnvelope extends EvidenceHeader {
  type:'CONTROL'; body:{controlId:Key;scopeAtomKeys:Key[];kind:'SUPPLIER_STOP'|'TECHNICAL_STOP'|'SOURCE_REDUCTION'|'SCOPED_REPLACEMENT';effectiveAt:Instant;authorityRef:Ref;supersedesControlRef:Ref|null;reason:string};
}
interface DeliveryEnvelope extends EvidenceHeader { type:'DELIVERY'; body:DeliveryResultBody; }
interface RevokeResultEnvelope extends EvidenceHeader { type:'REVOCATION'; body:RevokeResultBody; }
interface EvidenceAck {
  eventId:Id; outcome:'APPLIED'|'DUPLICATE'|'HISTORICAL'|'PENDING_PREDECESSOR'|'QUARANTINED';
  observedVersion:Seq|null; errorCode:string|null; recordedAt:Instant;
}
```

TargetLine 必须显式保 requiredDelivery、supplierDelivery、deliveryResolution 三结构，sourceTermsRef 不代正文。Required date 的 NONE/REQUESTED/HARD/UNKNOWN 由真实源证明；固定承诺日不由需求日或今天补。相对 PO_CONFIRMED 未发生就 PENDING_PO_CONFIRMED、anchor/resolved=null；不能把创建日当确认。HARD 已知 LATE 或无法证明满足均阻新用，REQUESTED 的 LATE 仅明确警示并走原审批机制。 [原文 L335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:335>)、[原文 L339](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:339>)、[原文 L340](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:340>)、[原文 L341](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:341>)、[原文 L342](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:342>)、[原文 L343](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:343>)、[原文 L344](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:344>)、[原文 L346](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:346>)、[原文 L348](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:348>)、[原文 L1304](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1304>)、[原文 L1306](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1306>)、[原文 L1309](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1309>)、[原文 L1311](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1311>)

日历固定准确版本、时区、有效范围、weekday/closed/extra；支持 EXCLUDE_ANCHOR_COUNT_VALID_DAYS，0日=anchor，大于0从次日起按原规则计。closed 与 extra 同日冲突，范围不足阻断；COMBINED＋SAME_DATE 锚点已知必须两承诺同日，未知保两约束，不擅取早/晚。PO 创建同事务保存不可变 DeliverySnapshotJson/hash，回同 hash/evidence；真正确认后仅新增 DeliveryAssessment 引原快照和真实 Confirmation，不改原 payload 或改用最新日历。 [原文 L1308](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1308>)、[原文 L1310](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1310>)、[原文 L1312](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1312>)、[原文 L1313](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1313>)、[原文 L1315](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1315>)、[原文 L1317](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1317>)

不支持 PROGRESSIVE 的 decimal-only 目标必须 supported=false，不算平均单价；不能保独立费用币种则 BLOCKED，不把270评价CUB写成270结算CUA。MappingItem 全字段 PRESERVED 必须相等，转换须明确 policy；任一必需 BLOCKED 无 targetPayload，不能先建 PO 再补。每 occurrence 仅一 canonical 费用表示，货款结算币与费用原币独立。 [原文 L1288](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1288>)、[原文 L1290](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1290>)、[原文 L1312](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1312>)

## 6. 事务、停止竞争、幂等与恢复

本地 Command 指纹是 UPPER(method)+换行+canonicalPath+换行＋原始 UTF-8 JSON bytes 的 SHA256；重排属性/空白也算异文。业务摘要另用 RFQ-CANON-1：Ordinal键序、集合按准确身份排序、有序 tier/step 保序、各scale十进制串、完整原商业语义；不同域不得通用化。hash相同正文不同仍冲突。 [原文 L1292](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1292>)、[原文 L1623](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1623>)、[原文 L1625](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1625>)

唯一命令 Scope 为 Tenant+CommandId 并固定 Actor；另一 Actor 同 ID 404。认证／语法／权限先核，再命令锁读原终态，不拿今日报价过期改旧成功；新业务拒绝只持久 Command.REJECTED，业务变更 rollback/savepoint；成功业务/Audit/Outbox/Command 同 commit。PROCESSING 只未提交事务占位，已提交工作用 GroupIntent/GrantPlanJob/ExportJob，ACCEPTED 命令不因工作后来失败改成未登记。 [原文 L1553](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1553>)、[原文 L1629](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1629>)、[原文 L1630](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1630>)、[原文 L1631](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1631>)、[原文 L1632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1632>)、[原文 L1633](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1633>)、[原文 L1634](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1634>)、[原文 L1636](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1636>)

拟议 MAIN_LOCAL_ATOMIC_OWNER_ADAPTER：PO Owner 开真实 Main 同库短事务，Source消费守卫＋Decision当前效果守卫共同核组/Grant代次/保护/控制/binding/商业与交付 hash。Source 在此标 EXECUTED_PENDING_LEDGER 并绑定 Effect 防止 TTL 再用；其后源汇总 P→C 与通知可异步，因此 PO 成功与 Source pending 可共存。停止与创建争相同 ScopeGuard；停止先拒新效果，创建先保原事实。跨库／远端默认禁用，除非有正式等价串行化与全路径 fence 采用证据。 [原文 L1413](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1413>)、[原文 L1415](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1415>)、[原文 L1417](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1417>)、[原文 L1419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1419>)

本域锁顺序 Command→已知父聚合→排序 Scope→CanonicalGroup→排序 Source/Grant→Relation；证据 Inbox→Scope→Group→Stream/Source→Relation，无法顺序处理先持久 Inbox 后任务。报价在父 RFQ 后 external identity→Family→QuoteVersion；PriceSelection 价域→Family；所有 RFQ 时间线最后取 RfqAuditClock，不回锁业务资源。applock 仅同 DB/principal 的 Transaction/Exclusive，3秒超时负值必须回滚。真正跨 PR/PO 的锁域需联合采用，不能只因三者同库便推无死锁。 [原文 L1557](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1557>)、[原文 L1559](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1559>)、[原文 L1561](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1561>)、[原文 L1583](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1583>)、[原文 L1600](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1600>)

| 用例 | 同事务写集 | 事务外／禁止效果 | 出处 |
| --- | --- | --- | --- |
| RFQ保存 | Number(仅创建)、Rfq/Revision/Lines/SourceLinks/Invitation(PREPARED)、Audit、Command | 不发邀请、不Claim、不建PO；任一子项失败整批rollback | [原文 L1587](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1587>) |
| RFQ正式发行 | Revision冻结/内容hash、审批证据引用、ControlFact(显式替代时)、Invitation待投递、Outbox、Audit、Command | Supplier发送在提交后；发行事务失败不得留下SENT；无权附件先阻断 | [原文 L1588](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1588>) |
| 报价正式收录 | Quote/TermSet冻结、可信证据引用、Audit、Command | 不改旧报价，不自动选中，不自动价表回写 | [原文 L1589](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1589>) |
| 保存比较 | Comparison不可变JSON/hash、Audit、Command | preview没有这次写；不修改Binding、价格或源观察 | [原文 L1590](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1590>) |
| 选择实际应用 | Application终态、BindingAtom、ScopeGuard revision、Audit、内部工作结果 | 前驱冲突仅记录应用冲突及历史批准，不部分替代scope；不建PO | [原文 L1591](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1591>) |
| 发布价格 | PriceVersion状态、ControlFact(精确旧范围)、Audit、Command | 网络审批在之前以证据准备；不在持锁中等OA，历史Terms不改 | [原文 L1592](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1592>) |
| 冻结/释放依据 | Basis、GroupIntent/GroupShare/BasisGroup(仅冻结)、Audit、Command | 不先Claim；localgroup防重不代Source预算权威 | [原文 L1593](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1593>) |
| 组请求/撤销 | GroupIntent控制/Outbox/RevokeRequest、Audit、Command | PO仍未知；所有网络在提交后；发送超时不返量 | [原文 L1594](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1594>) |
| 收证据 | Inbox/Conflict及该类型允许的投影/游标/对应任务 | 非法类型不写另一Owner投影；源与PO不能加两次C | [原文 L1595](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1595>) |
| 修复关系 | RelationSet/Item、该异常处理结果、Audit、Command | 不创建PO/修改Source余额；新证据先赢时旧repair409 | [原文 L1596](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1596>) |

原组查询只读；SUCCEEDED/REJECTED/CANCELLED_NO_EFFECT replay 都回原终态，未决才原键原 manifest 续作，不能拒绝重试至成功。Relation repair 只在当前 evidence＋relation revision CAS 写映射，不建 PO/改源量。Owner revoke 与 creation 串行，NO_EFFECT+完整 fence 才可源处置；TOO_LATE 保成功；UNKNOWN 保 P。未派发本地证明要求全部路径未领取/无在途并原子装 dispatch tombstone，删 outbox 或 lease 超时都不够。 [原文 L1014](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1014>)、[原文 L1016](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1016>)、[原文 L1020](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1020>)、[原文 L1022](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1022>)、[原文 L1030](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1030>)

fence-unobserved 只封本模块尚未提交的命令，两 CommandId 排序共锁，白名单 method/path＋原 Actor/hash；不存在且全部效果必经同命令事务才永久 CANCELLED_NO_EFFECT，迟到原请求410。已有 ACCEPTED 外部工作不能用它抹组，原组取消另走 Owner。Command/CanonicalGroup/Grant 墓碑最小身份不 TTL 删除。 [原文 L1648](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1648>)、[原文 L1650](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1650>)、[原文 L1652](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1652>)

SOURCE SNAPSHOT 替换完整观察；DELTA 要连续 baseVersion，只同量基 P→C/有证释放，不夹改 A；CORRECTION 完整更正链；CHECKPOINT 提供旧高水位＋新起点，不按 epoch字串或99/1跨域比较。消息同 event 原字节幂等、同 scope/version 同 body semantic重复，异文隔离；SOURCE 之外消息不能写全源余额。缺 coverage/前驱继续未知阻对应责任新用。 [原文 L1393](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1393>)、[原文 L1395](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1395>)、[原文 L1397](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1397>)、[原文 L1409](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1409>)

| 故障窗 | 持久事实 | 唯一恢复 | 出处 |
| --- | --- | --- | --- |
| 本地事务未提交 | 无已提交业务/Outbox，可能Command仍锁中 | 原命令查询/重放或有限本地fence争同锁，不新编号 | [原文 L1722](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1722>) |
| 发出已提交但人工投递未证 | ISSUED、Invitation PREPARED/UNKNOWN | 查原投递/记录真实凭证，不自动标SENT/QUOTED | [原文 L1723](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1723>) |
| 一Owner Claim成功、另一Owner拒绝 | PREPARATION_PARTIAL，未建PO | fence组所有执行路径后逐份额释放，等待来源真回执 | [原文 L1724](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1724>) |
| PO已创建但网络丢回执 | 原组Owner有真实PO，本线UNKNOWN/P保护 | 原组query，补执行证据，再源P→C，再修关系，不回滚已建PO | [原文 L1725](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1725>) |
| 来源快照先到，PO消息晚到 | Source C已覆盖Effect清单 | 晚到只补PO引用，不能把covered消费再加C | [原文 L1726](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1726>) |
| 源授权降量，后来PO成功到 | 两事实均存在，观察可能不完整 | 请求一致新source；A40/C50得到D10/R0，不能删PO配平 | [原文 L1727](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1727>) |
| 两同scope审批同时批准 | 两OA事实、一个当前Application | 同前驱CAS只一份应用，另一冲突；不改OA拒绝 | [原文 L1728](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1728>) |
| 停止与实际PO效果竞争 | 由同库Owner最终效果域裁定先后 | 停止先拒新创建；创建先保历史并影响待处置；未知不返量 | [原文 L1729](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1729>) |
| 修复r7与机器更正r8竞争 | 可信新AuthorityEvidence与关系版 | 旧repair失败；新更正可开后继问题，旧Closed不屏蔽新异常 | [原文 L1730](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1730>) |
| 用户撤权但Machine回执到 | Machine有合法producer权限 | 保存真实事实；原用户查/下载仍拒绝，机器权不转授 | [原文 L1731](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1731>) |

Worker 30秒 LeaseId；派发前重核 Scope/Group 并置 EverDispatched，回写须仍持该 lease；过期只能同键接管，不能清曾派发。1/5/30/120/600秒重试，20次转 MANUAL_RECONCILIATION 只是停止自动尝试，业务仍 UNKNOWN/受保护。前端15秒后按1/2/5秒查原 Command，再人工查询，丢 body 不重新填近似请求。 [原文 L1640](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1640>)、[原文 L1718](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1718>)

## 7. 权限、附件、导出与审计

| 能力后缀（pur.rfq.） | 作用 | 出处 |
| --- | --- | --- |
| `read`, `history.read`, `commercial.read`, `commercial.write` | 业务对象、历史、原价/税费/选择理由等商业敏感内容。无商业读时字段值null且列`redactedFields`，不泄漏总额/名次 | [原文 L102](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:102>) |
| `create`, `draft.edit`, `revision.create`, `revision.abandon`, `issue`, `withdraw`, `terminate` | RFQ草稿/发行/控制；edit不能替代issue | [原文 L103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:103>) |
| `delivery.record`, `delivery.request`, `attachment.read`, `attachment.download`, `attachment.disclose` | 人工投递凭证、重投请求和附件三种独立权限；可下载不代表可外发 | [原文 L104](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:104>) |
| `quote.edit`, `quote.record`, `quote.withdraw`, `response.decline` | 报价草稿、真实收录、撤回/拒绝凭证，不授技术批准权 | [原文 L105](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:105>) |
| `compare`, `decision.edit`, `approval.submit`, `decision.withdraw` | 比较和选择；实际应用由受信系统消费者执行 | [原文 L106](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:106>) |
| `price.edit`, `price.publish`, `price.stop`, `price.resolve`, `price.select` | 手工/来源价格、发布、停止、解析与显式选价分别检查 | [原文 L107](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:107>) |
| `basis.prepare`, `basis.freeze`, `basis.release`, `conversion.submit`, `conversion.read`, `conversion.replay`, `conversion.successor`, `conversion.revoke`, `relation.repair` | 独立按钮，不用一个“恢复”覆盖全部操作 | [原文 L108](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:108>) |
| `export`, `export.download`, `command.read`, `command.fence` | 导出申请/下载、原命令安全结果及有限本地未执行封闭 | [原文 L109](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:109>) |
| 服务能力 | `pur.rfq.ingest.source`, `.execution`, `.relation`, `.approval`, `.control`, `.delivery`, `.revocation`；分别绑定Producer和Tenant/Site/scope；人类Bearer不可调用 | [原文 L110](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:110>) |

disclose 准确名 pur.rfq.attachment.disclose，下载不授外发。IAM OWNER 按 Buyer，TEAM 为明确 Buyer 集合，不推组织树；Tenant/Site/对象/源行/文件逐层交集，404隐藏存在，403仅对象可见动作拒绝。商业无权字段 null＋redactedFields，不暴露总额/rank/隐藏候选数量；失权清商业草稿，不用 null PATCH。机器合法迟到事实继续保全，不转授原用户读取权。 [原文 L112](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:112>)、[原文 L114](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:114>)、[原文 L568](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:568>)、[原文 L1111](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1111>)、[原文 L1731](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1731>)

AuditSeq 是资源局部号；RFQ综合时间线另用 Tenant+RfqId 的 RfqAuditClock，在业务事务最后取锁递增，回滚连时钟回滚。迟到事件按本地 timelineSequence 入新位置，occurredAt 保原源点；count/page 同短 SNAPSHOT 授权谓词，跨分页不承诺固定快照。旧已有审计无 clock 要先有证对账水位，不直接从0。 [原文 L1083](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1083>)、[原文 L1085](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1085>)、[原文 L1087](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1087>)、[原文 L1561](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1561>)

附件 ticket 5分钟且当前对象/文件权下载再验，流式受控内容不跳未鉴权裸 URL；过期原命令只回旧 ticket，新出票需新有权命令。CSV 查看导出异步最多10000行、不按当前page截断；生成与下载均当前逐行列授权，未知写 UNKNOWN、掩码 REDACTED。UTF8 BOM/CRLF/引号转义，外部首字符公式风险文本加 TEXT:；不改源数据。3次生成尝试/120秒/READY24小时只是技术默认，非法法规保留值。 [原文 L1089](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1089>)、[原文 L1093](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1093>)、[原文 L1095](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1095>)、[原文 L1097](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1097>)、[原文 L1099](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1099>)、[原文 L1101](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1101>)

## 8. 页面、错误与用户可观察结果

| 页面 | 路由 | 布局／返回 | 出处 |
| --- | --- | --- | --- |
| RFQ列表 | `/pur/rfq` | 顶部筛选＋业务列表；无装饰大屏；返回保留同会话已应用筛选和页码 | [原文 L70](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:70>) |
| 新增 | `/pur/rfq/new` | 独立表单：用途/站点/采购员、需求行、供应商、技术附件 | [原文 L71](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:71>) |
| 详情 | `/pur/rfq/:rfqId?tab=overview` | 概览、修订/邀请、报价、比较/选择、依据/执行、历史六页签，关联页懒加载 | [原文 L72](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:72>) |
| 修订编辑 | `/pur/rfq/:rfqId/revisions/:revisionId/edit` | 原版本只读对照；仅DRAFT可改；已发版使用新修订入口 | [原文 L73](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:73>) |
| 报价编辑 | `/pur/rfq/:rfqId/quotes/:quoteVersionId/edit` | 供应商原文/版本在左，逐行商业条件在右；新建用详情“录入报价” | [原文 L74](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:74>) |
| 比较 | `/pur/rfq/:rfqId/compare` | 固定评价量和时点，矩阵横向滚动；不可比候选独立区域 | [原文 L75](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:75>) |
| 选择 | `/pur/rfq/:rfqId/decisions/:decisionId` | 分配、审批事实、实际应用、当前绑定四区，不合成单一“已批准”灯 | [原文 L76](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:76>) |
| 价表 | `/pur/supplier-price`、`/pur/supplier-price/:priceVersionId` | 价族/版本列表、草稿编辑、解析说明/显式选择；不展示未授权价 | [原文 L77](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:77>) |
| 依据/恢复 | `/pur/rfq/:rfqId/bases/:basisId` | 冻结条件、组清单、来源保护、PO执行、源处置、关系六区 | [原文 L78](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:78>) |
| 原结果 | `/pur/rfq/commands/:commandId` | 本人/获授权命令结果；恢复入口不包含报价敏感正文 | [原文 L79](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:79>) |

查询默认 OPEN/page1/size20/updatedDesc，所有授权站点，不自动本人；无主站点必须明确选。LOADING→EDITING→SUBMITTING→SUCCESS；确定拒绝回编辑、冲突 CONFLICT、提交不明 RESULT_UNKNOWN、失权 ACCESS_LOST。未知冻结原请求，Abort 只取消浏览器读，不是业务撤销。sessionStorage 只放安全 command/path/hash/time/获权 ID，不存商业正文。 [原文 L81](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:81>)、[原文 L83](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:83>)、[原文 L118](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:118>)、[原文 L120](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:120>)、[原文 L1761](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1761>)

依据页同时显示内容、请求登记、来源保护、PO执行、来源处置、关系六轴。预检/冻结/释放/提交/查原/同键续作/关系修复/撤销/后继各专用按钮，不一个“恢复”任意改价或重建。需求日期、固定承诺、相对条款与起算点、日历、运费责任并列；取价双币种必填，不把 MISSING/CONFLICT/BLOCKED 显示0。 [原文 L335](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:335>)、[原文 L749](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:749>)、[原文 L887](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:887>)、[原文 L889](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:889>)

| HTTP／code | 条件／数据库事实 | UI／retry | 出处 |
| --- | --- | --- | --- |
| 400 RFQ_VALIDATION | 类型/未知成员/重复属性/长度/必填/金额格式错误，无业务写 | 定位errors.field，不自动截断；NONE | [原文 L1660](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1660>) |
| 401 RFQ_UNAUTHENTICATED | 身份失效，不返回敏感旧结果 | ACCESS_LOST清缓存，重新登录后同键查原结果 | [原文 L1661](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1661>) |
| 403 RFQ_ACTION_FORBIDDEN | 对象可见但动作能力缺失 | 刷新能力，不降成通用edit；NONE | [原文 L1662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1662>) |
| 403 RFQ_FIELD_FORBIDDEN | 写入/导出受限字段，整个命令拒绝 | 清受限草稿，不先保存其他字段；NONE | [原文 L1663](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1663>) |
| 404 RFQ_NOT_FOUND | 不存在或租户/Site/对象范围无权，不能披露区别 | 返回列表/安全回执，不泄漏ID/名称 | [原文 L1664](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1664>) |
| 409 RFQ_VERSION_CONFLICT | 合法expectedVersion与实际rowversion不符 | CONFLICT；加载最新并明确新意图，不能自动覆盖 | [原文 L1665](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1665>) |
| 409 RFQ_STATE_CONFLICT | 目标已发/已收录/已发布/已冻结，不能走草稿写 | 显示当前状态，走有权后继入口 | [原文 L1666](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1666>) |
| 409 RFQ_COMMAND_CONTENT_CONFLICT | 同Command不同method/path/body | 禁止自动续作；原请求只查询；NONE | [原文 L1667](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1667>) |
| 409 RFQ_COMMAND_BUSY | 原命令/业务锁仍被持有 | Retry-After:3，原键查询；SAME_KEY/QUERY_ORIGINAL | [原文 L1668](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1668>) |
| 410 RFQ_COMMAND_FENCED | 有CANCELLED_NO_EFFECT墓碑 | 原命令不执行；有权重新填写新意图 | [原文 L1669](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1669>) |
| 409 RFQ_BINDING_CONFLICT | 应用前驱/scope已改变，OA批准仍真实 | 并列OA Approved/ApplicationConflict；新前驱重新评估 | [原文 L1670](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1670>) |
| 409 RFQ_SCOPE_CONFLICT | ScopeAtom映射交叠不明或不合法替代范围 | 逐scope解释，不拆请求绕过；NEW_INTENT_AFTER_REVIEW | [原文 L1671](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1671>) |
| 409 RFQ_DELIVERY_UNRESOLVED | 渠道存在未知外发且没有安全原键查询/幂等能力 | 停盲发，查原投递凭证/人工核实，不能换deliveryKey | [原文 L1672](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1672>) |
| 422 RFQ_SOURCE_INVALID | 来源版/责任/单位/用途不合法 | 刷新来源与证据，只阻有关源行 | [原文 L1673](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1673>) |
| 422 RFQ_SHARE_CONFLICT | 相同Grant已在另一组/父预算不足 | 原组/来源Owner核查，不换ShareId或复制行 | [原文 L1674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1674>) |
| 422 RFQ_SUPPLIER_INELIGIBLE | 当前新效果Supplier资格停止 | 停新使用，历史和事实登记仍按读权保留 | [原文 L1675](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1675>) |
| 422 RFQ_TECHNICAL_BLOCKED | 当前用途/替代/质量活动所需证据缺失 | 只阻相应候选，显示需哪项凭证；不补未来QC | [原文 L1676](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1676>) |
| 422 RFQ_EXTENSION_NOT_ENABLED | D01多供方、D04自动优先、D07跨组累计/包量/共享扩展请求 | 明确本基线不启用，不重新询问已选政策 | [原文 L1677](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1677>) |
| 422 RFQ_TERMS_INCOMPLETE | mode/基数/币种/期限/费用/原凭证无法解释 | 原值/报价事实可保存，正式新使用不能放行 | [原文 L1678](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1678>) |
| 422 RFQ_QUOTE_IDENTITY_REQUIRED | UNBOUND草稿正式收录时既无编号也无真实替代凭证；不绑定族 | 定位编号/凭证，保留草稿；补内容后新意图 | [原文 L1679](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1679>) |
| 409 RFQ_QUOTE_IDENTITY_CONFLICT | 类型化外部身份已绑定另一Family；不自动并族 | 有权列表查规范族，明确successors；无权不泄露另一族 | [原文 L1680](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1680>) |
| 409 RFQ_QUOTE_IDENTITY_IMMUTABLE | 已绑定族试图改编号/凭证Owner或ID；无写入 | 保留历史；另一真实报价新建UNBOUND草稿 | [原文 L1681](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1681>) |
| 409 RFQ_QUOTE_FAMILY_UNBOUND | 尚未正式绑定的草稿请求successors | 先完成原草稿或另建独立草稿，不续未识别族 | [原文 L1682](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1682>) |
| 409 RFQ_APPROVAL_SUBJECT_MISMATCH | 回调业务修订/提交token/hash或实例不匹配Intent快照 | 隔离来件，保留真实线索；不发起新应用 | [原文 L1683](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1683>) |
| 409 RFQ_APPROVAL_CONTENT_CHANGED | 当前批准对象业务修订或内容已变；旧OA事实保留 | 新内容/后继重新评估，不用rowversion变化伪推STALE | [原文 L1684](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1684>) |
| 409 RFQ_PRICE_SELECTION_SCOPE_MISMATCH | 引用选择的两币种或其他请求维度与保存RequestHash不符 | 重新resolve并显式选择；不复用旧token/选择权限 | [原文 L1685](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1685>) |
| 422 RFQ_DELIVERY_TERMS_UNRESOLVED | 起算/天数类型/组合含义/需求约束或日历不可确定 | 可保留原事实；补准确来源，不填目标默认值 | [原文 L1686](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1686>) |
| 422 RFQ_DELIVERY_DATE_CONFLICT | 已知锚点下SAME_DATE两承诺算得日期不一致 | 同时显示原固定/计算日期，需来源修订，不暗选其一 | [原文 L1687](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1687>) |
| 422 RFQ_REQUIRED_DATE_UNSATISFIED | 有据HARD需求日已迟或尚无法证明满足 | 阻相应新效果；保留需求与承诺，不自造豁免 | [原文 L1688](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1688>) |

| HTTP／code | 条件／数据库事实 | UI／retry | 出处 |
| --- | --- | --- | --- |
| 422 RFQ_QUOTE_EXPIRED | 当前正式选择/新使用的报价已过期或撤回 | 获取新报价/有权替代；旧执行结果仍可查 | [原文 L1690](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1690>) |
| 422 RFQ_ZERO_PRICE_UNAUTHORIZED | 明确零但缺专用条款/能力/审批或目标适配 | 不当null、不自动取另一个价 | [原文 L1691](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1691>) |
| 422 RFQ_NO_END_UNPROVEN | NO_END缺来源明示/适用政策 | 保留UNKNOWN事实，不默认无限 | [原文 L1692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1692>) |
| 422 RFQ_MOQ_OR_MULTIPLE | 实际分量不满足MOQ/包装倍数/上限 | 重新合法分配/询价，不自动多采购 | [原文 L1693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1693>) |
| 422 RFQ_CALCULATION_RANGE | 中间/最终范围或精度不可承接 | 指明量纲/输出字段，不降精度蒙混 | [原文 L1694](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1694>) |
| 422 RFQ_CALCULATION_BASIS_MISSING | 单位/FX/税费/舍入/费用覆盖未齐 | 保留已知分项，不出完整总额/排名 | [原文 L1695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1695>) |
| 409 RFQ_PRICE_OVERLAP | 发布价的作用域/时间/量档交叠无明确supersede | 旧价继续原资格，新版不发布 | [原文 L1696](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1696>) |
| 409 RFQ_PRICE_CANDIDATES_CHANGED | 显式选择token/候选集合过时 | 重新resolve并显示差异，不能暗换候选 | [原文 L1697](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1697>) |
| 422 RFQ_APPROVAL_EVIDENCE_REQUIRED | 缺精确subject/Policy/实例/有据豁免 | 保留草稿/申请；空值不代表批准 | [原文 L1698](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1698>) |
| 422 RFQ_TARGET_UNSUPPORTED | PO不能保留原币/模式/基数/费用/金额或固定/相对交付语义 | 冻结/释放/实际创建前硬阻断，不试写PO | [原文 L1699](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1699>) |
| 409 RFQ_GROUP_CONTENT_CONFLICT | 同CanonicalGroup换币种/成员/商业hash | 停止重放，合法后继需旧终态与再授权 | [原文 L1700](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1700>) |
| 422 RFQ_SUCCESSOR_NOT_FENCED | 旧UNKNOWN或无全路径fence/准确再授权 | 保留潜在量，不能新建替代原组 | [原文 L1701](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1701>) |
| 409 RFQ_EVIDENCE_CONFLICT | 同事件/流版本异内容、manifest缺行等 | 隔离可信线索和异常，不覆盖旧投影，不重建PO | [原文 L1702](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1702>) |
| 409 RFQ_RELATION_CONFLICT | repair旧版本/异PO映射/不匹配权威证据 | 查当前回执和关系，拒旧修复，不改PR/PO | [原文 L1703](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1703>) |
| 409 RFQ_EXPORT_NOT_READY | 文件尚排队/失败未生成 | 查看作业错误，不下载空文件 | [原文 L1704](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1704>) |
| 422 RFQ_EXPORT_LIMIT | 授权结果超过10,000行 | 缩小条件，不接收截断“成功” | [原文 L1705](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1705>) |
| 403 RFQ_DOWNLOAD_REVOKED | 当前对象/字段/文件权已撤销 | 作废下载资格并清缓存，不恢复其他权限 | [原文 L1706](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1706>) |
| 410 RFQ_ARTIFACT_EXPIRED | 票据/导出对象过期 | 当前有权时新出票/导出，历史作业不变 | [原文 L1707](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1707>) |
| 428 RFQ_VERSION_REQUIRED | 修改缺expectedVersion | 客户端契约错误，重新读取，不造版本 | [原文 L1708](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1708>) |
| 413 RFQ_BODY_TOO_LARGE | 超请求字节/集合上限 | 拒绝整个请求，未知Group不得客户端临时拆分 | [原文 L1709](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1709>) |
| 429 RFQ_RATE_LIMITED | 技术限流 | 遵Retry-After，仍用原Command/业务键 | [原文 L1710](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1710>) |
| 503 RFQ_DEPENDENCY_UNAVAILABLE | 必要Owner/IAM/DB读取不可用 | 显示未接入/暂不可读区别；不假空数据 | [原文 L1711](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1711>) |
| 503 RFQ_RESULT_UNKNOWN | 无法确认本地commit或外部真实效果 | RESULT_UNKNOWN，QUERY_ORIGINAL，不换键重建 | [原文 L1712](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1712>) |

200 解析诊断的 MISSING/CONFLICT/BLOCKED 不等写入成功；原 REJECTED 保原 HTTP/code，X-Command-Replayed:true，不偷偷扩 Problem。结果读权不足不返回敏感旧字段错误。 [原文 L1714](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1714>)

## 9. 旧代码差距、实施队列与兼容

| 事项 | 固定来源事实 | 当前设计／未实施 | 出处 |
| --- | --- | --- | --- |
| 模块入口 | 旧Main已有RfqController、SupplierPriceService及Vue询价入口 | 保留用户业务入口概念，新增`/api/pur/rfq/v1`协议与应用服务；v1是协议版，不恢复项目V2 | [原文 L2259](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2259>) |
| 报价/价表 | 旧固定源码的报价/价格保存存在原地赋值路径，解析只返回decimal? | 独立不可变商业版本、逐项Terms、来源证据和可解释解析；不能称代码已经改好 | [原文 L2260](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2260>) |
| 数值 | 旧Rank方法按原价、LeadDays/Supplier排序；不构成统一币种/税费比较证明 | 规范decimal字符串＋精确有理数中间结果＋明确政策舍入；独立真实mode不改算法 | [原文 L2261](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2261>) |
| PO承接 | 已核旧RFQ转换DTO未传报价Currency，PO创建取Supplier.Currency；非正价会走取价分支 | 原币/单位/基数/费用TargetPayload及显式Mapping；不兼容阻断，不静默回退 | [原文 L2262](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2262>) |
| 当前作用域 | 已接受R02/R03要求真实替代、OA事实/应用分开 | `expectedBindings[]`逐scope/epoch/revision；应用CAS，body version不用伪强ETag | [原文 L2263](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2263>) |
| 事务 | 本轮未读取或运行当前数据库/Provider | SQLServer本域短事务＋applock＋唯一约束＋rowversion；Main共库Owner最终效果参与式事务为拟议默认；远端未证路径禁用 | [原文 L2264](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2264>) |
| 故障恢复 | 已接受原键、整组、源保护/执行/投影分账 | 字节级Command防重、业务CanonicalGroup、Owner Grant三层唯一性；有限本地fence不代外部取消 | [原文 L2265](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2265>) |
| 载体 | 旧R04曾生成实际工作簿，原字节发布未完 | MD02用户已替代Excel要求；本稿不补传/修打印/转表格，也不把旧载体状态改成已通过 | [原文 L2266](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2266>) |

固定旧 CP6 157630594e3371fe181955d2f6227ff3b6962c84 的已读方法：RfqService/SupplierPriceService/PurchaseOrderService、RfqController，以及 RfqView.vue 前240行。归档指出原地赋值、decimal?选价、原价直接排序、转换未传Currency而PO取Supplier.Currency、非正价回退价表；不能据此冒称最新运行态或全消费者源码已审。 [原文 L2268](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2268>)

| 任务 | 后端 | 前端 | 未来验收条件 | 出处 |
| --- | --- | --- | --- | --- |
| DEV01 契约与公共请求 | 本文全部DTO、Problem、未知成员/decimal/string校验、身份/证据模式适配、端点OpenAPI | types、金额文本控件、字段错误映射、统一Query/Command composable | 每个本文JSON可反序列化；未知tenantId/状态成员拒绝；无权字段不回传 | [原文 L1742](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1742>) |
| DEV02 本地事务和命令基座 | Command、Number、Audit、Inbox/Outbox、规范hash、逻辑锁与rowversion条件写、有限fence | RESULT_UNKNOWN/CONFLICT/ACCESS_LOST页面、仅安全恢复metadata | 两实例同Command只产生一次本地事实；fence先赢时迟到创建不能执行 | [原文 L1743](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1743>) |
| DEV03 RFQ列表/草稿/发行 | 查询、Rfq/Revision/Lines/SourceLinks/Invitation、发行与披露门 | 五区创建表、来源选择、预检、修订/投递面 | 建草稿可查询；发行前无发送任务；无披露权时整发行拒绝；草稿不误停旧版 | [原文 L1744](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1744>) |
| DEV04 报价与准确金额 | Quote/Terms版本、事实收录、金额有理数/策略执行、knownness、整量/累进、期限 | 原凭证对照、报价编辑、单位/原币、未知/0区别 | 两报价同量可解释；1200与1400模式例算正确；费用缺值不出总排名 | [原文 L1745](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1745>) |
| DEV05 比较/选择/审批 | 比较快照、合法份额规划、D01检查、审批实例和当前Binding应用CAS | 候选矩阵、选择理由、OA事实/应用分栏、scope冲突 | 两批准同前驱最多一个应用；MARKET可推荐但不能形成采购授权 | [原文 L1746](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1746>) |
| DEV06 价表与显式选择 | 草稿/来源复制/发布/停止、冲突解析、PriceSelection待审/应用 | 价族版本列表、解析原因、显式候选复核、非自动优先 | 唯一匹配或明确选择；多候选无授权不选第一；修改新价不改旧PO | [原文 L1747](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1747>) |
| DEV07 依据与本地恢复 | Basis冻结/释放、组注册、Grant组唯一关联、原键replay/revoke/successor | 六轴依据页、每份源单位、原结果/修复/撤销独立按钮 | 冻结/释放无Claim；组登记202不显示PO成功；同份额换组不双用 | [原文 L1748](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1748>) |
| DEV08 Owner/三流适配 | Source/PO/Projection类型路由、coverage/checkpoint、更正、Claim准备和最终效果守卫 | 源观察/未对账/保护/执行/关系分别展示 | 30＋20被C50覆盖不变100；PO成功源滞后不重建；用户撤权Machine仍保真实事实 | [原文 L1749](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1749>) |
| DEV09 历史/附件/导出 | 私有票据、异步CSV、下载再鉴权、审计过滤、失效清理 | 历史抽屉、受控下载、导出进度/失败/过期 | 不从历史/总额泄漏价；下载前撤权拒绝；不是静态CSV占位 | [原文 L1750](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1750>) |
| DEV10 兼容与端到端 | 旧接口/表适配清单、真实SQLServer并发/唯一性/事务测试、生产适配准入报告 | 实际API联调、键盘/响应式/异常体验 | 下面E2E与边界场景有真实运行证据；未接入Owner不得写完整生产验收通过 | [原文 L1751](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1751>) |

REV1 将报价族绑定／两唯一索引、审批三种版本与内容hash、交付三结构、解析双币种、真实返回绑定和时间线并入上述原任务。实际代码目录可适配现有 Main，职责保持；这份整理没有启动 DEV、迁移、编译或演示宿主。 [原文 L1753](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1753>)、[原文 L1757](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1757>)、[原文 L1602](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1602>)、[原文 L1616](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1616>)

## 10. 原验收与当前有效解释

**所有业务验收 NOT_RUN。** 原162 TC、60 MD-AC、26 REV1-AC 有重叠，不合计成248次执行。以下直接列可据以实现的 HTTP/页面/持久断言；E01–E46 的过程表已读，完整46步JSON、86条bindings尚在准确附件闭合队列，不能冒已运行。

| MD-AC | 前置／动作 | HTTP／页面 | 数据库／集成 | 出处 |
| --- | --- | --- | --- | --- |
| MD-AC01 | U-A在S-A查询，手传T-B或无权S-B对象ID | 404，不显示名称/数量/币种 | 无业务变化，无跨Tenant从表访问成功 | [原文 L2148](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2148>) |
| MD-AC02 | 新增JSON夹带tenantId/status，或同名属性两次 | 400 RFQ_VALIDATION且字段定位 | 无Number/Rfq/Command业务成功记录 | [原文 L2149](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2149>) |
| MD-AC03 | requestedQty用数值100或科学计数文本1e2，超scale6 | 400，不截断、不由JS浮点纠正 | 无有效Qty入库 | [原文 L2150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2150>) |
| MD-AC04 | Rfq草稿仅标题/空行，随后发行 | 草稿允许；发行422明确缺行/截止/供应商 | 草稿保留；没有ISSUED或投递任务 | [原文 L2151](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2151>) |
| MD-AC05 | 两人同version修改不同草稿字段 | 一次成功，另一409 CONFLICT | 一次版本变更，不自动合并覆盖他人 | [原文 L2152](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2152>) |
| MD-AC06 | 草稿改变Site/Buyer但没rootExpectedVersion | 428；有版本也须新Site/Buyer权限 | Rfq根和Revision要么同提交、要么均不变 | [原文 L2153](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2153>) |
| MD-AC07 | 同Command同body两个服务实例创建RFQ | 同结果或BUSY后同结果 | 一Rfq、一编号、一套行和初始审计 | [原文 L2154](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2154>) |
| MD-AC08 | 同Command换body顺序/内容重放 | 409 CONTENT_CONFLICT | 原资源不变，不分配第二编号 | [原文 L2155](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2155>) |
| MD-AC09 | 创建事务写SourceLink失败 | 无成功响应/明确事务失败或UNKNOWN | Rfq/Revision/行/审计/Outbox全部不留半成品 | [原文 L2156](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2156>) |
| MD-AC10 | commit成功但响应丢失 | 查询原Command可回成功 | 重放仍一Rfq，不能重建 | [原文 L2157](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2157>) |
| MD-AC11 | 刷新丢body，原命令尚持锁，申请fence | BUSY而非已取消 | 不插与执行并存可重用墓碑，不允许新意图逃避 | [原文 L2158](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2158>) |
| MD-AC12 | 原命令尚无记录，fence先取得同锁 | CANCELLED_NO_EFFECT；迟到原请求410 | 无Rfq，永久墓碑；已ACCEPTED远端组则不能以此取消 | [原文 L2159](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2159>) |
| MD-AC13 | 有edit/issue但无某附件向V-A披露权 | preflight给缺口，issue拒绝 | 不发该附件，不部分发行成假成功 | [原文 L2160](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2160>) |
| MD-AC14 | v1有效，创建/弃置v2草稿 | v1仍当前可用，v2 ABANDONED | 无Binding/Control变更；保留TC104解释 | [原文 L2161](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2161>) |
| MD-AC15 | 只替代X scope的正式v2，Y不相关 | X按新依据，Y保持旧依据 | 原历史不改，禁止ScopeId换名绕过重叠 | [原文 L2162](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2162>) |
| MD-AC16 | 投递Channel无可靠去重，已可能发送但响应丢失 | UNKNOWN，不能按钮盲重发 | 不生成新的deliveryKey伪消除未知 | [原文 L2163](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2163>) |
| MD-AC17 | 未邀供方或错误Revision/Line报价 | 422/400具体引用错误 | 不正式收录为可选报价，不按名称相近映射 | [原文 L2164](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2164>) |
| MD-AC18 | 被邀方真实报价有NULL价/UNKNOWN费用；与明确0价对照 | 可收录事实，缺项/0分别显示 | NULL不写0；无零价证据不得正式使用0 | [原文 L2165](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2165>) |
| MD-AC19 | 真实报价v2收录，v1明确未撤回 | 提示新版但原选择仍引用v1 | 不覆盖Quote1、Comparison1/原价 | [原文 L2166](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2166>) |
| MD-AC20 | NO_END有明示/政策与缺日期两组 | 前者按门可用，后者UNKNOWN阻正式使用 | 不把ValidTo=NULL一律解释为无限 | [原文 L2167](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2167>) |
| MD-AC21 | 120EA ALL_UNITS达到100档10CU/EA | 商品1200.0000 | 算法mode/阈值/基数可追，非累进1400 | [原文 L2168](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2168>) |
| MD-AC22 | 120EA PROGRESSIVE前100×12后20×10 | 商品1400.0000 | 逐段量和120，未使用平均单价绕mode | [原文 L2169](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2169>) |
| MD-AC23 | 货款120CUA×2＋30CUB费用 | 完整比较270CUB，不是300 | target保持原币货款/独立费用，不将270写CUA | [原文 L2170](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2170>) |
| MD-AC24 | 费用currency CUC但缺CUC→CUB；或含税口径未定 | 已知商品可见，完整金额/rank null | 不把未知税费/FX填0/1 | [原文 L2171](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2171>) |
| MD-AC25 | Group fee30出现在两行摘要，存在18/12分解 | 总费30不是60 | 仅一个canonical occurrence，派生行不再计费 | [原文 L2172](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2172>) |
| MD-AC26 | MARKET无PR但评价量120，随后试release | preview有效、推荐可存；release422 | 无SourceGrant/Claim/PO授权被制造 | [原文 L2173](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2173>) |
| MD-AC27 | 一source行分两Supplier，或复制RFQ行分开选 | 422 EXTENSION_NOT_ENABLED | 无双Supplier有效Binding；已有同Supplier非交叠60/40不误拒 | [原文 L2174](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2174>) |
| MD-AC28 | 实际分配B40低于其MOQ50 | 422 MOQ_OR_MULTIPLE | 不静默加购10，不按询价总100享档位 | [原文 L2175](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2175>) |
| MD-AC29 | v1审批待应用，v2只有DRAFT未替代 | v1合法批准可应用 | 无因为最新草稿version较大而STALE，保留TC306后继解释 | [原文 L2176](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2176>) |
| MD-AC30 | 两份OA Approved都ExpectedBinding=r7且范围交叠 | 一份APPLIED r8，另一ApplicationCONFLICT | 两OA事实保留，仅一个有效Binding；失败方不改前驱自动再用 | [原文 L2177](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2177>) |
| MD-AC31 | 缺审批规则或匹配两个冲突规则 | BLOCKED，不是NOT_REQUIRED | 无伪造OA实例/豁免证明 | [原文 L2178](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2178>) |
| MD-AC32 | RFQ_ISSUE或PRICE_PUBLISH批准回调到 | 显示可用于准入证明，不自动发出/发布 | 无意外Invitation发送/Price状态变更 | [原文 L2179](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2179>) |
| MD-AC33 | 手工Price草稿无RFQ来源，PATCH旧published版 | 手工草稿合法；旧版409 | 草稿未参与resolve，已发布条款不被覆盖 | [原文 L2180](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2180>) |
| MD-AC34 | 同Supplier/Item两个同资格价，无自动优先政策 | resolve CONFLICT | 不按MinQty最大/最新日期取赢家 | [原文 L2181](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2181>) |
| MD-AC35 | 显式选价后候选变更或所选已撤回 | 409候选变化或currentApplicable=false | 无新的MATCHED假授权；历史选择不删 | [原文 L2182](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2182>) |
| MD-AC36 | 显式选价矩阵REQUIRED且首次无intent | 202已存待审草稿，可用返回ID送审 | 未先应用；审批通过且候选未变才APPLIED | [原文 L2183](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2183>) |
| MD-AC37 | 价发布重叠而未给supersede清单 | 409 PRICE_OVERLAP | 旧发布版和历史PO不变；新版本仍草稿 | [原文 L2184](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2184>) |
| MD-AC38 | 部分Site停止价，其他Site仍适用 | 相关Site排除，其他继续 | ControlFact精确scope，不能把整个价族停掉 | [原文 L2185](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2185>) |
| MD-AC39 | 依据freeze/release成功尚未submit | 六轴明确未占用/未创建 | Source Claim0、PO0；本地组身份可存在 | [原文 L2186](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2186>) |
| MD-AC40 | 同CanonicalGroup换Command、同Manifest再次提交 | 返回原组/202原恢复，不新建 | 一CanonicalGroup/一次SourceShare关联，不多PO | [原文 L2187](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2187>) |
| MD-AC41 | 同Grant放入不同组；两个各80争100 | 一方冲突/拒绝或未决 | 有效Claim不能160；本地GroupShare和Source父预算均核 | [原文 L2188](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2188>) |
| MD-AC42 | 两来源准备第一成功第二拒绝 | PREPARATION_PARTIAL，没有部分PO | 不进PO创建；fence整组后逐源清理，未知保护保留 | [原文 L2189](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2189>) |
| MD-AC43 | 目标不支持零价/累进/费用币种但其他字段合法 | TARGET_UNSUPPORTED | 无正式PO副作用，原0/null/模式不自动替换 | [原文 L2190](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2190>) |
| MD-AC44 | 目标PO实际成功，本线源处置/关系失败 | PO成功、SourcePending、RelationFailed并列 | 不重建PO、不把P超时释放、关系只修原PO | [原文 L2191](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2191>) |
| MD-AC45 | E1=30/E2=20先到，再完整SourceC50覆盖 | C50/R50，不是C100 | PO Effect留证，Source贡献仅源流更新 | [原文 L2192](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2192>) |
| MD-AC46 | SourceC50先到，再E2/E1与重复到 | C仍50 | 不因消息顺序重复消费/回退 | [原文 L2193](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2193>) |
| MD-AC47 | 覆盖未齐、缺前驱、同版本异内容、epoch无交接 | 202待前驱或409隔离，新使用BLOCKED | 不计算推测可用额，不最后到达覆盖 | [原文 L2194](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2194>) |
| MD-AC48 | Source授权降40，晚到真实消费50 | 赤字D10/R0或待Owner一致快照 | 原PO不删，不改失败以配平 | [原文 L2195](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2195>) |
| MD-AC49 | PO Producer冒发SOURCE余额或跨Tenant消息 | 403/404，类型身份拒绝 | 源观察无非法写入，不因字段相似放权 | [原文 L2196](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2196>) |
| MD-AC50 | 原组终态拒绝后同键重放，再完整fence+新授权后继 | 旧键仍拒绝，新代次有前驱 | 不复活旧组；原UNKNOWN缺fence不能创建后继 | [原文 L2197](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2197>) |
| MD-AC51 | 删除本地Outbox但曾有在途租约，或Claim TTL到期 | UNKNOWN/P保留 | 不据此返额/新建；需所有路径终态证明 | [原文 L2198](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2198>) |
| MD-AC52 | 停止先赢Owner效果锁 vs 创建先赢 | 分别拒新建 vs 保留真实PO/TOO_LATE | 冻结预检不绕当前门；不声称跨库未证顺序 | [原文 L2199](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2199>) |
| MD-AC53 | 追溯Stop晚到且只含X | X影响待处置，Y不误停 | 不抹旧PO、不取消未涉及scope | [原文 L2200](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2200>) |
| MD-AC54 | Machine关系r8先到，人工repair持r7 | 409 RELATION_CONFLICT | r8不被旧修复覆盖；新异常不被旧Closed屏蔽 | [原文 L2201](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2201>) |
| MD-AC55 | 用户撤权后Machine真实PO回执到且用户下载 | Machine接收，用户读/下载拒绝 | 真实事实保全，机器权不转给用户 | [原文 L2202](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2202>) |
| MD-AC56 | 导出筛选100条、当前页20；另测超10,000 | 首次100条，超限422 | 不只导出当前页、不截断成功 | [原文 L2203](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2203>) |
| MD-AC57 | 导出文本以=或制表起始，生成期间撤权 | 查看版转义或因撤权不提供下载 | 原数据不改、无敏感列旁路、没有半文件成功 | [原文 L2204](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2204>) |
| MD-AC58 | 关系issue Closed，源/取消/费用仍未知 | 各状态独立显示 | 不级联CL01～CL06，不把未知历史义务清零 | [原文 L2205](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2205>) |
| MD-AC59 | 生产注册DEMO写适配，或DEMO回执写REAL组 | NOT_READY/拒接 | 不生成被误标真实的PO/证据 | [原文 L2206](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2206>) |
| MD-AC60 | 源输入decimal最大边界、1/3单位比例需舍入 | 超范围/无余数政策明确拒绝 | 不增加可买数量；实际Amount按明确策略round后存 | [原文 L2207](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2207>) |

| REV1-AC | 评审项 | Given／When | Then | 持久事实／禁止效果 | 状态 | 出处 |
| --- | --- | --- | --- | --- | --- | --- |
| REV1-AC01 | B-MD-F01 | 创建两份supplierQuoteNo=null、replacementVoucherRef=null草稿，Idempotency-Key不同 | 两次201；两FamilyId不同且UNBOUND；record各422 RFQ_QUOTE_IDENTITY_REQUIRED | 两内部族无外部唯一值；没有空身份公共族或Record成功；仍可编辑。 | NOT_RUN | [原文 L2225](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2225>) |
| REV1-AC02 | B-MD-F01 | UNBOUND草稿先填QA-001，再PATCH QA-002并正式record | PATCH/record合法后绑定QA-002；详情boundExternalIdentity与SupplierQuoteNo一致 | FamilyId不变；QA-001无绑定；record将族绑定、版本冻结、审计与回执同提交。 | NOT_RUN | [原文 L2226](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2226>) |
| REV1-AC03 | B-MD-F01 | 两个Family草稿用相同Tenant/Rfq/Supplier/编号并发record | 一份200；另一409 RFQ_QUOTE_IDENTITY_CONFLICT，不自动挂到胜者族 | 仅一个外部身份索引命中；失败Family仍UNBOUND，行/Terms未搬移，不泄露无权规范族。 | NOT_RUN | [原文 L2227](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2227>) |
| REV1-AC04 | B-MD-F01 | 同voucherId分别来自FILES-A和FILES-B；另测FILES-A同id的新version | 不同Owner可有不同族；同Owner/id新version不能创建第二规范族，应走明确后继 | Voucher.version留在QuoteVersion证据，不进入族唯一键；无静默合并。 | NOT_RUN | [原文 L2228](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2228>) |
| REV1-AC05 | B-MD-F01 | 已绑定QA-001族的后继DRAFT改成QA-002；原RECORDED也尝试PATCH | 后继409 IDENTITY_IMMUTABLE，原版409 STATE_CONFLICT | Family外部身份及历史报价不变；另一真实报价须新建UNBOUND，不恢复已用编号。 | NOT_RUN | [原文 L2229](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2229>) |
| REV1-AC06 | B-MD-F01 | QuoteSuccessor缺respondsToRevisionId，再传null，再传另一RFQ修订 | 缺成员400；null沿原修订合法；另一RFQ422/409引用拒绝 | 只有合法请求分配同族新业务修订，前版及其映射不变。 | NOT_RUN | [原文 L2230](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2230>) |
| REV1-AC07 | B-MD-F02 | DECISION送审保存businessVersion=3和rowversion A；状态变SUBMITTED后token B；OA回调仍携A/3/原hash | 同内容可APPLIED；不能把A base64转bigint或因为当前B判过时 | ApprovalIntent三字段分别保存；Application对应business3；Binding只有一次变化。 | NOT_RUN | [原文 L2231](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2231>) |
| REV1-AC08 | B-MD-F02 | RFQ_ISSUE/PRICE_PUBLISH审批后仅回填实例或状态token变化，再用当前token点击发行/发布 | 同内容证明可用；审批回调本身不发行/发布 | 无回调触发的投递/发布副作用；点击独立事务核contenthash、当前门后成立。 | NOT_RUN | [原文 L2232](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2232>) |
| REV1-AC09 | B-MD-F02 | RFQ草稿已获批准后实质改quantity/requiredDate，businessVersion标识仍同修订，contentHash改变 | 旧证明409 RFQ_APPROVAL_CONTENT_CHANGED或明确准入拒绝；新内容须新Intent | 旧OA事实及Intent保留，发行不发生；hash不包含生命周期但必须包含商业字段。 | NOT_RUN | [原文 L2233](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2233>) |
| REV1-AC10 | B-MD-F02 | 同Subject内容/政策语义/前驱，用当前不同token和新Command再次申请；新评估仅proofRef变了 | 回同一规范Intent，无第二OA实例；旧终态拒绝仍原终态 | UQ IntentIdentityHash一次；PolicyDecisionHash排除调用级凭证ID，不排实际规则或豁免语义。 | NOT_RUN | [原文 L2234](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2234>) |
| REV1-AC11 | B-MD-F02 | 显式选价首次创建、送审、应用，再改评价币经新token+predecessorSelectionId建立后继 | 同内容状态流businessVersion不变；后继真实+1、新id/Intent；无商业PATCH | PriceSelectionFamily分配版本，旧选择/审批不覆盖；ResourceRef不对工作对象伪填businessVersion=1。 | NOT_RUN | [原文 L2235](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2235>) |
| REV1-AC12 | B-MD-F02 | OA回调intentId正确但subjectBusinessVersion、subjectRowVersion或subjectHash任一不匹配保存快照 | 409 RFQ_APPROVAL_SUBJECT_MISMATCH并隔离，不误按当前token匹配 | 不产生新Application/Binding；拒绝证据可查，真实历史不伪改。 | NOT_RUN | [原文 L2236](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2236>) |
| REV1-AC13 | B-MD-F03 | 只有供应商固定promisedDate=2026-10-08、leadDays=null；需求日2026-10-15为REQUESTED | TargetLine保存FIXED_DATE、resolvedDate10-08、leadDayKind=null、anchor NOT_APPLICABLE | BasisDeliverySnapshot与Target行相同；没有用今天或PO确认点生成新的固定日期。 | NOT_RUN | [原文 L2237](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2237>) |
| REV1-AC14 | B-MD-F03 | 来源“PO确认后5个工作日”，尚无PO确认；目标支持相对未定锚点 | 可携PENDING_PO_CONFIRMED，anchorDate/resolvedDate为null，日历版本/原leadDays保留 | PO创建持久原交付快照且回deliverySnapshotHash；创建不伪造确认或明日承诺。 | NOT_RUN | [原文 L2238](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2238>) |
| REV1-AC15 | B-MD-F03 | 同上但目标只支持必填固定日期或丢失freightResponsibility/calendarRef | supported=false，冻结/新效果前RFQ_TARGET_UNSUPPORTED | 无新PO/新Claim效果；不写“当前时间+5”、Supplier默认交期或把缺失数据忽略。 | NOT_RUN | [原文 L2239](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2239>) |
| REV1-AC16 | B-MD-F03 | 日历夹具v1和指定锚点09-29，5工作日；随后系统有另一“当前v2”日历 | 按v1算10-08，引用v1快照/规则hash；历史不重算 | Source/Target hash含准确日历与原条款；用v2替换旧快照导致不同hash并被拒。 | NOT_RUN | [原文 L2240](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2240>) |
| REV1-AC17 | B-MD-F03 | 固定日10-09且相对条款按已知锚点算10-08，source jointInterpretation=SAME_DATE | 422 RFQ_DELIVERY_DATE_CONFLICT；两原值并列 | 不选早者/晚者覆盖；已有历史不变，必须由来源修订解决。 | NOT_RUN | [原文 L2241](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2241>) |
| REV1-AC18 | B-MD-F03 | 需求HARD10-07，承诺10-08；另测HARD但未来PO锚点未定；再测REQUESTED10-07 | 两HARD分支阻断；REQUESTED显示LATE警示并沿已接受选择/审批机制，不暗改需求日 | 无新增延期豁免；source约束必须有Evidence，日期本身不推HARD/NONE。 | NOT_RUN | [原文 L2242](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2242>) |
| REV1-AC19 | B-MD-F03 | 目标PO真实已建，执行回执缺deliverySnapshotHash或提供不同hash | 409 RFQ_EVIDENCE_CONFLICT，保留真实线索/UNKNOWN待对账 | 不确认错误完整承接、不重建PO；改当前默认配置也不能重解释已成PO。 | NOT_RUN | [原文 L2243](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2243>) |
| REV1-AC20 | B-MD-F04 | 货款120CUA+30CUB；settlementCurrencyCode=CUA，comparisonCurrency=CUA；明确FX CUB→CUA=0.5 | 命中原价CUA；原商品无FX，费用15CUA，完整比较135CUA | request回显两币种；所有comparisonCurrency=CUA；原terms/目标结算币仍CUA。 | NOT_RUN | [原文 L2244](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2244>) |
| REV1-AC21 | B-MD-F04 | 同原价，comparisonCurrency=CUB，FX CUA→CUB=2 | 240+30=270CUB；同币CUB费用fxRef=null | 不是270CUA；RequestHash与上一评价币不同，不能共用解析token或已应用选择。 | NOT_RUN | [原文 L2245](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2245>) |
| REV1-AC22 | B-MD-F04 | 已保存选择请求CUA/CUB；再次resolve仅改comparisonCurrency或settlementCurrencyCode仍带旧selectionId | 409 RFQ_PRICE_SELECTION_SCOPE_MISMATCH，要求新解析/显式后继 | 旧PriceSelection RequestJson/hash及两币种不变，不重新询问D04政策。 | NOT_RUN | [原文 L2246](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2246>) |
| REV1-AC23 | B-MD-F04 | 仅有CUC费用但缺CUC→comparisonCurrency证据；或省comparisonCurrency/使用旧currencyCode成员 | 费用FX缺失返回诊断BLOCKED；必填缺失/未知成员400 | 完整总额为空而不是缺费当0；不从Supplier/策略猜评价币。 | NOT_RUN | [原文 L2247](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2247>) |
| REV1-AC24 | B-MD-F05 | 按§14.2执行E01～E46，所有bindings取实际前响应并做DTO校验 | 无需测试作者补路由/字段/ID/token；最终六轴及原商业/交付条件一致 | RFQ1、规范报价族2、比较1、Binding1、原PO效果1；C100非C200；全部身份从保存结果取得。 | NOT_RUN | [原文 L2248](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2248>) |
| REV1-AC25 | B-MD-F05 | 同RFQ两个不同资源各局部AuditSeq=1，并发写审计后查history；再加迟到源事件 | timelineSequence唯一、按本RFQ提交序列降序；sequence各保留1，迟到项按登记顺序 | RfqAuditClock和Audit同事务；回滚不留成功项；total/page相同授权快照、不跨资源序号比较。 | NOT_RUN | [原文 L2249](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2249>) |
| REV1-AC26 | B-MD-F05 | J37来源asOf14:00、外层响应查询asOf；J42规划时尚未消费，授权量100 | 外层不早于已包含观察；J42 C0/P0/R100并标isReservation=false | 不混用另一C50已消费夹具；无物理数据写入的文档校验不标业务PASS。 | NOT_RUN | [原文 L2250](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2250>) |

原162有效解释完整入口 L3039：TC104 新草稿不使旧选择失效；TC306 要真实 ReplacementIntent 才使旧待应用目标 stale，仅更大 Draft 时原 v1 可合法应用。条件保留明确不启用新扩展，而非删除历史义务。原流乱序、覆盖与18 SQ、12 SG、6 CL 均维持；关系 Closed 不能级联源/PO/费用结清。 [原文 L3041](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3041>)、[原文 L3043](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3043>)、[原文 L3050](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3050>)、[原文 L3068](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3068>)、[原文 L3150](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3150>)、[原文 L2982](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2982>)、[原文 L2995](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2995>)、[原文 L3012](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3012>)、[原文 L3016](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3016>)、[原文 L3027](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3027>)、[原文 L3031](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3031>)、[原文 L3036](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:3036>)

连续配方的保存／再读取边界必须直接落实到客户端与Adapter；下列E/J标识保留源文入口，不新增接口。

| 步骤／wire入口 | 必须保存和验证的值 | 结果不能扩大为 |
|---|---|---|
| E03/E13/E26 | 新lineId、quoteLineId、SavedAllocation.allocationId均从创建后读取；E25实际RFQ行来自E03，E31采用E30的allocationId和新Decision token。 | 不用输入null、预设随机ID或旧token替代实际返回。[原文 L1875](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1875>)、[原文 L2013](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2013>)、[原文 L2043](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2043>) |
| E20–22 | A的原币商品120 CUA→比较240 CUB，加运费30 CUB=270；B为280。两者互为候选，completeTotal=null；独立J24单候选示例才是270。 | 不能把候选加总成550，也不能把比较币总额写成PO原价。[原文 L1977](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1977>)、[原文 L2458](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2458>) |
| E27–30 | ApprovalIntent保存提交前SubjectBusinessVersion、SubjectRowVersion、SubjectContentHash；OA回调用这份提交快照。SUBMITTED/APPLIED造成的技术token变化不单独使同内容审批失效。 | APPROVED事实不能越过scope前驱与实际Application。[原文 L2019](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2019>)、[原文 L2031](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2031>)、[原文 L2037](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2037>) |
| E33–39 | 冻结保存原商业／需求REQUESTED及PO_CONFIRMED相对条款；Basis释放仍无Claim。submit同时带group token和basis token，之后完整ClaimBundle ALL_PROTECTED才准执行。 | 202 GROUP_INTENT_RECORDED不等PO；basis token不能替换group token。[原文 L2055](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2055>)、[原文 L2079](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2079>)、[原文 L2085](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2085>)、[原文 L2091](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2091>) |
| E40–46 | PO提交后丢回包，原canonicalGroupKey+manifestHash查询；执行凭证完整带sourceShareQuantities、commercialPayloadHash、deliverySnapshotHash。Source覆盖PO-E1后C100，关系Projection单独引用原执行回执。 | 不再建第二PO、不把PO100+Source100算C200；PO创建后交期仍PENDING_PO_CONFIRMED。[原文 L2097](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2097>)、[原文 L2103](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2103>)、[原文 L2115](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2115>)、[原文 L2127](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2127>)、[原文 L2133](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2133>) |
| J51–54 | DELTA按baseVersion3→4转P10到C；独立CORRECTION把E2有效20改10使C40/R60；CHECKPOINT明确旧epoch覆盖转新epoch。NO_EFFECT回执含全路径fence，Source返额仍独立。 | 三个独立夹具不能串成一个流；新的epoch数字不自动授权换代；PO无效果不自动返源量。[原文 L2620](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2620>)、[原文 L2626](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2626>)、[原文 L2632](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2632>)、[原文 L2638](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2638>) |
| J55–60 | 日历快照保存时区、排除起算日计数、零日规则及覆盖期；固定日期分支resolvedDate由原承诺给定。CommercialCheck带SavedAllocation和原terms；OA实例请求带policy evaluation及submitter separation。 | 夹具节假日不能作真实日历；PriceSelection SUBMITTED仍currentApplicable=false，businessVersion1不随技术token推进。[原文 L2644](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2644>)、[原文 L2656](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2656>)、[原文 L2662](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2662>)、[原文 L2668](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2668>)、[原文 L2674](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2674>) |

## 11. 真实采用与整合退出条件

| 待证项 | 缺证时准确行为 | 仍可开发范围 | 出处 |
| --- | --- | --- | --- |
| IAM及Producer真实能力 | 无法确认权限时不披露敏感值/不产生新效果；Machine类型拒未知来源 | DTO、DEMO有状态替身、页面、真实身份接入后的本地事务 | [原文 L2276](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2276>) |
| Source分量/预算/Grant/Claim/fence | 不正式选择应用或提交真实PO；保留原观察/未决 | RFQ/报价事实、MARKET比较、采购选择和依据缺项草稿 | [原文 L2277](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2277>) |
| PO全组原子及原商业承接 | TARGET_UNSUPPORTED/未接入Gate；不试写、不回退价表 | 完整Adapter合同、DEMO整组与未知恢复、真实采用前合同测试 | [原文 L2278](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2278>) |
| 审批/零价/无终期实值 | 不将null视为豁免，所涉正式动作待证 | 专门机制、规则解析、草稿/历史与具名夹具 | [原文 L2279](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2279>) |
| 工程/质量当前活动实例 | 对应替代/新使用阻断，不要求未来QC先发生 | 有据不适用及普通事实登记、已发生历史读取 | [原文 L2280](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2280>) |
| 实物部署/旧表切换/性能安全 | 无Implementation Ready或上线率；历史不迁移造事实 | 本Spec及后续经授权的真实实现任务；当前不运行业务 | [原文 L2281](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2281>) |

真实金额/审批/FX/日历/零价/无终期政策实例、IAM人员职责、Source Grant/Claim/fence、PO全组原子与交付承接、Projection 采用、旧 writer 收敛和数据库并发仍待证；8 ISS/4 RFC 不批量关闭。D01–D07 产品选择已定，真实输入未齐不能倒推全部方案待定。 [原文 L2272](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2272>)、[原文 L2283](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:2283>)

与 [PUR-PR](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/PUR-PR-01.md>)、[PUR-PO](<D:/CP6/docs/CP6_开发设计文档_20261010/modules/PUR-PO-01.md>) 的状态层差异已对照当前PO原案：RFQ Source观察A/C/P/R/D与PR内部A=F+P+E+U+R+B不是同一枚举账；PO明确EXECUTED_PENDING_LEDGER是Source内部保护状态，原ClaimRecord wire保持CONSUMED等原枚举，最终Effect与PO共提交，随后Source观察可异步。设计层解释已明确，实际adapter／共享锁全writer采用仍未证。 [原文 L692](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:692>)、[原文 L693](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:693>)、[原文 L695](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/15/15edbc1dfe25be7f__PUR-PO-01_采购订单与履约_前后端开发Spec_v1.1_DRAFT.md:695>)

按部署实际选择共同事务参与者、统一所有 writer ResourcePlan/锁序和准确原 wire；任何跨库等价未证则关对应新效果，保历史读、事实收录和已发生结果。本文不自定生产审批阈值、供应商默认币种、迟交豁免或未来确认日。 [原文 L1413](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1413>)、[原文 L1417](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1417>)、[原文 L1419](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1419>)、[原文 L1559](<D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/17/1787bb4e78ef2587__PUR-RFQ-01_采购询比价与供应价格_前后端开发Spec_v1.1.1_REVIEW.md:1559>)

## 12. 本轮阅读覆盖与未读队列

主文3256行的必要语义现已全部归并：L1–1859、L2135–2316、L2677–3256逐段全文读取；E01–E46及J01–J60全部106 JSON按原行／JSON pointer结构实读。相同子对象仅在精确内容匹配时复用前一已读值；8组去重表达全部读完，106对象还原后字段顺序／数组顺序／值类型及序列化内容0差异。它是实际结构语义补读，不冒全部重复排版逐字符重读，更不表示业务运行。

UA89行、CURRENT52行、MD02独审119行全文。原46OP／29ACT／10PROC／20REQ／90顶层逻辑字段与规则／错误映射现已补读；90字段不是新增90物理列。原162项、60MD-AC、26REV1-AC有重叠且全NOT_RUN。UA L53／69明确旧Excel打印、旧C04及作者旧版不作当前归档前置；历史源代码与旧图形仍未全审。状态 `required_materials_consolidated` 指当前必要设计材料语义归并，真实Owner／全writer／数据库／运行保持§11的未证边界。详见[商业阅读台账](<D:/CP6/docs/CP6_开发设计文档_20261010/evidence/commercial-reading.json>)。
