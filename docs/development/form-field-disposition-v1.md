# FORM 字段受控处置 v1（W302 / W303）

状态：W302本地实现与验收完成，远端采用/finish见交付证明。W302 提供真实源、当前资格、审批、留存事实及原结果；W303 已实现并验证准确处置引用到实际重基；远端采用/finish见W303交付证明。原始依据为 OA-FORM-01 R1 第6节和 SYS-PRIV-01；沿用当前 PostgreSQL、模拟业务验收和精简验证口径。

## 所有权与作用

数据仍由 `PersonalFormOwnerV2` 拥有。新增 `FormFieldDispositionOwnerV1` 只处理当前租户、当前部署环境内的准确 FORM Active 草稿及最新已发布重基目标，作用为 `FORM_REBASE_RETAIN`。它提供有权处理人的真实处置事实，不执行通用主体擦除、租户删除或跨仓隐私操作。

准确原件来自 `cp6_personal_form_v2.history` 中与当前草稿 token 对应的不可变 after_json。计划绑定 historyId、draftId、ownerId、源/目标发布版本与 schemaDigest、当前 FORM 政策版本及损失范围；沿用现有重基映射规则，保持金额/数量精度和 unset/null/empty。计划和安全回执引用原历史，不将隐藏原文另复制到隐私数据库或原命令日志。

申请人就是准确草稿拥有者，使用现有 FORM edit 权申请。申请不允许选择实际处理人、虚构 sourceRef 或自行声明已具备数据读取资格。申请人读取自身请求时只见引用、准确草稿/目标身份和状态；不可见字段的 path/value 仍不返回。

## 当前资格与独立操作

处置工作台采用独立 `oa-form-dispositions` 菜单及 query / approve / retain / revoke / hold 动作；启动种子只登记动作，不默认授给角色。读取具体损失还必须满足原 FORM 当前政策、当前 DataJson 字段权限及每个受影响字段的可读资格。系统管理员标记不代替动作、字段或业务检查。

批准者必须具备当前 approve 和对应数据读取资格，且与申请人不同；批准固定准确计划。留存执行者需当前 retain 及对应数据读取资格，可以是批准者，真实身份取认证上下文。请求人、批准者、执行者分别记录，客户端不能代填。

保留依据采用实际 FORM 已配置政策的准确版本、RetentionPolicyRef 和 RetentionDays。缺失或不匹配立即拒绝。hold 是本地真实的草稿处置控制，独立 hold 动作设置/解除并保留原因、原命令和版本；存在 hold 时不批准、不新执行、不消费旧事实。新机制不修改已有保留期限，也不替真实法律主体决定例外。

## 持久状态及原命令

请求及每次状态变化均不可变，按准确前驱版本串接。当前投影取最高连续版本：REQUESTED → APPROVED → RETAINED；REQUESTED 可 REJECTED，尚未消费的请求/事实可 REVOKED。W303 的实际消费才记录 CONSUMED，不能提前把批准或留存写为草稿已重基。

RETAINED 表示实际 Owner 已按准确政策登记原历史的留存责任，输出有真实记录支持的处置引用、scope、历史引用、批准/执行身份及 retainUntil。它不表示隐藏值已物理销毁。原内容仍受原 Owner 当前读取权限约束；旧历史不因重基删除。

请求、批准、执行、撤销、hold 均使用原 commandId / expectedVersion。输入在边界冻结，结果及审计与业务事实共用当前 Main PostgreSQL 事务和原操作日志。相同命令异体拒绝，未知提交先查询原操作，明确 close 仅关闭未提交原键；原结果查询采用当前读取权，不要求历史写权继续有效。安全原结果不包含隐藏值。

所有可变决定在共同 authentication/WF_AUTH fence 内核对；原草稿、FORM 政策和定义写者已使用该门。源 token、目标最新版本、政策、hold、当前身份/字段资格或处置状态变化均使旧计划不能产生新效果。授权检查位于权威入口，内部不可变 DTO 和事实直接采用，不叠加证明包装或重复哈希校验。

## API 与页面边界

根路由为 `/api/oa/form-field-dispositions/v1`。命令均拒绝额外字段，operationId 为非空 UUID，reason 为必需且最多500个 Unicode 标量；版本为准确 UUID，缺版本与版本冲突分别明确拒绝。

| 路由 | 输入 / 作用 |
| --- | --- |
| POST `/requests` | `{operationId,draftId,expectedDraftVersion,targetVersionId,reason}`；原 Owner 读取并冻结真实源，不接受客户端字段清单/原值/actor |
| GET `/requests/{id}` | 当前安全状态；符合独立 query 与实际损失字段读取资格者另得到准确损失视图，普通申请人不获得隐藏内容 |
| POST `/requests/{id}/approve`、`/reject` | `{operationId,expectedVersion,reason}`；独立合格审批者固定请求范围 |
| POST `/requests/{id}/retain` | 同一命令形状；记录实际原历史留存责任和合格处理人，返回 `{requestId,receiptVersion}` 处置引用 |
| POST `/requests/{id}/revoke` | 同一命令形状；独立 revoke 权取消尚未消费的资格，保全部旧事实 |
| POST `/requests/{id}/hold` | `{operationId,expectedVersion,active,basis,reason}`；expectedVersion 对当前草稿 hold，首次明确 null；请求提供可信草稿身份，basis 必需且有界 |
| GET `/operations/{operationId}`、POST `/operations/{operationId}/close` | 当前真实实际人的原结果 / 明确关闭未提交原键，不重新执行业务 |

任何客户端可见处置引用均由真实持久 RETAINED 事实生成；请求引用只表示请求存在，不能冒充处置完成。actor、源 historyId、scopeDigest、保留期限、政策版本和处理结果不得由客户端代填。

工作台面向当前合格处理人，按明确请求引用加载准确内容；普通请求人看到安全状态。读取、计数和原结果不得绕当前范围；GET 不创建历史或补齐丢失记录。申请/批准/留存与原草稿及后续重基状态分别展示。

W303 重基消费需要同一 CP6Context/DbTransaction 的准确处置引用。它必须复核当前持有者资格、原版本/目标、政策、hold、有效状态，并将消费与准确重基历史/原命令共同提交。普通申请人的 confirmRemovedValues 仅确认其可见损失，不能代替隐藏损失处理；隐藏处置责任始终引用原有权处理人的真实事实。不开放客户端任意指定 actor 或以布尔参数跳过隐藏损失检查。

## W303 实际消费合同

原 `POST /api/oa/v2/drafts/{id}/rebase-preview` 仍要求本人 Active 草稿、准确 token、当前 edit 及最新发布目标。遇到尚未获处置的隐藏有值损失时，在同一授权门内创建真实 REQUESTED 记录并提交，然后返回 403 `REBASE_HIDDEN_LOSS_BLOCKED` 和 `dispositionRequestId`；不返回隐藏 path/value。相同源、目标和政策的有效未消费请求被复用，因此预览响应丢失后再次预览不会重复申请。预览不会批准或执行留存，也不会升级草稿。

处置人批准准确损失并登记原件留存，表示允许该范围用于原草稿到准确目标的重基。本人随后读取请求的安全状态，并在预览和确认体中携带 `{dispositionRef:{requestId,receiptVersion}}`。服务端核对该引用的实际 requester、draft token、目标、政策、留存期限、hold、撤销及消费状态；重新读取真实留存执行人的当前启用/锁定状态、retain 动作和受影响字段读取权。客户端不能选择这个人、修改范围或以盲确认替代处置。

授权仅作用于准确损失的内部映射，不改变本人读取掩码、Owner 身份或原写者规则。预览只列本人可见变化，`requiresConfirmation` 只对应可见有值损失；混合损失仍须明确确认可见部分。精度、未设值/null/empty 及所有原明细行沿用原映射规则；缩小目标行数限制不自动截断原行。

消费使用原 `FORM_DRAFT_REBASE` commandId / expectedVersion，不另造一次可独立提交的业务写入。`cp6_form_disposition_v1.consumptions` 与原命令、原件 before 快照、升级后的草稿/行版本在同一 PostgreSQL 事务提交；约束核对 native command writer_xid、真实本人、留存执行人、精确来源历史及源/目标版本。每份请求只消费一次，消费后当前状态为 CONSUMED，安全投影显示原命令及两种实际责任身份。撤销不能倒退已提交的重基，原命令恢复不要求历史 retain 权继续有效，也不会重复消费。

所有资格与可变业务事实位于原 authentication/WF_AUTH 串行门内。先到的 hold/撤权使确认失败；确认先完成则记录真实消费，不伪造撤销已回滚。失败保持原草稿及未消费事实，未知提交沿用本人原命令查询/明确 close；真实已提交结果只返回原结果。原历史继续留存且受当前字段读取权限约束。

个人草稿页面展示真实请求引用、读取当前处置结果、重新预览及采用后的本人原命令恢复。合格处置工作台显示准确原文并明确授权含义，消费后显示实际责任，删除不适用的撤销入口。模拟验收和本地环境证据见 W303 交付报告；本实现不等于整张个人表单/隐私卡、通用擦除或生产部署完成。

## 验证与交付

先以实际 HTTP 缺能力场景形成红灯，再实现当前 Owner。定向 PG18/真实认证 HTTP 覆盖准确源/范围、申请人不泄露隐藏值、缺读取或处置动作拒绝、当前撤权/政策/hold/版本变化、独立批准、真实留存引用、原键恢复、双连接及同事务失败回滚。受影响页面验证当前身份和明确原结果恢复。

一份 W302 交付报告记录实际运行、复用证据及真实余项；W303 消费未运行时保持未完成。无 Actions、既有数据库迁移或生产部署。正式状态以代码、实际验证及两仓远端采用/finish 为准。
