# 已知义务的语义复核及不能覆盖的边界

本记录为本轮来源与责任复核，不是用户签署或业务PASS。逐ID全文见 obligation-matrix.json；每条保留原来源和原断言。观察维度以原断言为裁判，生成的“对象/操作”仅帮助审阅。没有以295标题数或43映射数证明所有未知原文的语义完备。

## 正文与现存场景

ITER REQ001–030 的责任链仍为项目/首目标修订原子建档、准确需求输入、新品/改型分开、候选分支、技术引用、任务交付、评审、评价、结果型关闭和恢复。REQ001 允许尚未解析产品的草稿，但首修订与项目同事务，不能生成产品/WO/库存；REQ002 更高需求修订只产生待复核，不改在审输入；REQ006 技术引用保持Object/Revision/Occurrence与Owner，不接受latest。矩阵按每条的Actor、动作段和结果段拆开，相关TC只作样例，不能替正文其他条件。

BR001–040 的规则按原款全文逐项观察。BR001 的六种身份及数据库并发令牌各自独立；BR002 的输入版本更新不能静默替换原包；BR028 当前对象授权与BR029同键重放、BR030技术并发一起适用，页面隐藏按钮不代替服务端拒绝。BR/REQ/PROC中涉及权重分母、Blocked、Unknown、任务100%与业务FAIL、外部安全终结的条件分别留证，不压成单一“已完成”。

FLD001–078 每行固定页面、精确绑定、必填/默认、控制与来源。比如 Search.q 可选/空，Search.ownerId 可选/我且无权限不列候选；原建议文本长度不自动变成新存储上限。PROC001–020 分别固定授权/前置、输入、步骤、事务、响应/错误；PROC001须先可信Tenant/Site与对象授权，再稳定分页，Unknown不报假零；PROC002保存Project+PlanRevision+Audit+Receipt并可用原RequestKey查回。候选端点不是已存在服务的证据。

DATA001–093 保留逻辑字段全文、可空/关联/唯一性/租户/CAS要求。每条写入、回读与历史读须对已批准PostgreSQL映射验证合法域无损；这里不选择UUID、numeric精度或长度，也不授权删除TenantId。类型建议、物理映射和租户schema仍需独立决定；新开发只要求PostgreSQL，旧正文SQLServer字样仅保原历史语义。

REL 18个具名PG01动作和8个字段目录保留全部输入/动作前条件/逐步处理/副作用/恢复/Owner。字段目录在同一矩阵行内逐字段列观察，不把8个页面当8个单字段。四Purpose没有强弱继承，许可/预约/DEV Claim/MES真实建立独立；Q=C+R+P及超额X不混算，累计2→4不算6，根6/子2+4不重复消费。根Cancelled、NOT_FOUND、超时都不足以证明可安全释放；C4→3未封闭转P、终态更正须后继。逐Target的ACK/接受/就绪/真实采用/相符性/后果分轴，A成功不能覆盖B未知，删除B不能消除历史义务。

221个恢复场景逐条核原完整断言、目录目标、源ID/别名、Owner分配及准确原件行；不是重编号旧30/48。PG05-CLAR-001保留技术缺证与现场缺件差异，CLAR-002保留份额结清与业务记录关闭由各Owner独立裁决。完整解释补遗作为全局输入保留，受影响ID关系未删。

## 工作簿18项补充逐项复核

每条下列ID的Sheet/单元格和原值由XLSX XML直接比对，不以手册转述替原值。未运行Excel、重算公式或做Office视觉验收。

| 补充ID | 原对象/触发 | 观察及责任边界 |
|---|---|---|
| WB-01 | ProjectId/IterationId路由、跨Tenant | 稳定ID及当前逐对象授权，统一404掩码；名称不作身份 |
| WB-02 | mode改编辑/直调动作 | mode只管展示；ITER后端阶段和权限仍判拒绝 |
| WB-03 | 写命令无If-Match、过期、业务Revision替代 | 缺前置428、冲突412；技术令牌与业务修订分离 |
| WB-04 | RequestKey超时重试/异体/token | 同键同体原结果、异体409；认证秘密不能持久作业务键 |
| WB-05 | SourceRevision=latest或Owner不可达 | 拒latest；Unknown阻依赖动作，不能假称无漂移 |
| WB-06 | ReturnTo/源Owner深链/撤权 | 受控内部路由、保筛选、重查真实权限状态，不接受外部任意URL |
| WB-07 | 歧义日期/FactTime注入 | 计划日期无时区、事实UTC；拒歧义与客户端覆写审计时间 |
| WB-08 | 同updatedAt多ID分页/limit | 先授权再updatedAt DESC,id DESC；50/200待DP决定 |
| WB-09 | 页面ACT01–20进入PROC | 按工作簿真实页面—ACT—PROC关系核，不能仅从字段推断 |
| WB-10 | 修改确认/重复点击/412/409/503 | 摘要版本原因可见、输入保留、原回执可追；按钮禁用不代幂等 |
| WB-11 | 在审/终态/HELD绕UI | 冻结不可PATCH、终态不覆盖删除、HELD禁新增提交评价冻结 |
| WB-12 | 旧轮成员/下载/历史/源水位 | 历史只读及权限遮蔽；Unknown/asOf和UTC+ID排序保留 |
| WB-13 | 导出公式样文本/下载撤权/截图 | 不执行公式、下载重验权；EvidenceUnavailable显式；截图不是VER PASS |
| WB-14 | 旧生产资料批量导入“合格迭代” | 未批准导入；先映射和缺证，不能造可信历史 |
| WB-15 | 只读/审计/服务issuer-tenant-action | 读不附送写/恢复/下载权；服务不代用户决定；失败日志不泄漏 |
| WB-16 | 错误分类/已独立发生外部事实 | 原403/404、409/412/422语义；拒绝无新增业务，外部事实保留查原键 |
| WB-17 | 通用Update.Status/双ActiveSelection/问题转移 | 专用动作、原枚举、至多一当前选择；转移需受理方/期限，HTTP200非业务接受 |
| WB-18 | 改型缺ECO援引追跡表AL149 N/A | 保留冲突；按REQ023及処理詳細!N85核ECR/ECO，不凭N/A删义务 |

完整ID均为 `WP05-01-REVIEW/<ID>`。复用项保 prior_basis_ids，新增显式观察只补具体触发，不新增旧AC，也不授权更改Owner协议。

## CP22有界专业采用

CP22-01–04：SourceDescriptor/registry cut、真实Source search→Preparation→EngineeringCheck→Snapshot→CREATE链不允许造前驱。PREPARE恰四capture且preparationRef=null，后续各恰十一并绑定真实preparationId/revision；每个issuer/documentRef.owner须原Owner，MES只运输。SDK tuple/providerContractId/version/digest、AdoptedByOwner/有效期/撤销及完整canonical正文独立验证；旧capture不可回退为current head，缺源/不明适用性进入RequiredFenced并零后继native派发。

CP22-05–10：PurposePolicy需准确purposePolicyRef、PRODUCTION/NEW_MANUFACTURE、APPLICABLE、Site/窗口和technical basis，与REL同cut。CFG必须RESOLVED/UNIQUE/count1；ENG原RELEASED基线、完整variables/operations/materials/outputs、无环DAG及消费点关系不能裁短。单位映射需有版本、正rational、EXACT整数与loss守恒。五member manifest须逐项对独立capture正文，不以complete=true/hash取代来源。native check七CREATE_WO gate和Snapshot链逐值匹配真实前驱。

CP22-11–13：本candidate DEV只接受独立NOT_APPLICABLE_PROVEN/空refs且与REL/ENG身份同一，不扩展DEV适用Claim流程。REL只在原全body的PRODUCTION GRANTED/current/有效窗及准确scope/version/ranges/quantity相等时有效。REL_RESERVATION是**真root之后**的独立Owner事实，不插入前置十一项、不把Preparation当预约，MES/PLAN-PO不能自造R/P、预约/释放或root refs。

CP22-14–15：Owner receipt仍嵌于role15，canIngest与canReadOwnEvents分离，sequence gap持久WAITING_PREDECESSOR，同event异bytes冲突；原body/ordinal/status/键/digest保真，APPLIED只观测，observedAt不可改成Now。历史链不要求伪造今日来源，负MES结果不是永久noeffect。总角色17，actualAdopted=0、providerGrant=0、37AC NOT_RUN的既有边界继续保留；设计签署、旧RC1和资产静态PASS都不能代实际provider采用或后继根接受。

## R1有界叠加

R1-01–03：OA受众来自真实注册；完整候选保原ID/version/ref/digest/排序，不截前1000、不静默分包。canonical拒重复JSON键/孤立surrogate，不偷做Unicode normalize或Number精度转换；Scope/Round/aggregate/Package双向集合相等，原业务摘要与snapshot摘要分开。外域原Round/Ref超OA域须真实持久不可变别名且完整双向解析，不丢原字节；256KiB snapshot与1MiB body无截断。

R1-04–06：OA封闭字段/大小写/null不变，不新增RETURN、不向Engineering塞rejectDisposition；该策略只本地冻结。Inbox接收与业务应用独立事务，同决定异运输只应用一次，GAP不跳过，应用时当前绑定/SOD/全部Blocking再核；Receipt/Outbox/Audit与应用同commit。回执发送失败用相同不可变回执重投，只有许可的BLOCKED追加revision/previousReceiptId；旧同步callback与新worker不得形成双应用入口。

R1-07–10：多输出Occurrence、input/operation集合与配置保全；无输入须准确原工程依据。原单PRODUCT封闭wire不改，Manifest与RelationSet/Binding一起核对且原PRODUCT至少匹配一个真实输出。新schema/domain canonical与原Manifest分开；**重复输入引用必须拒绝，不能先去重后接受**。ENG只在完整本地写集同事务中做后继冻结；原key恢复，错误422 REQUIRED_OUTPUT_MAPPING/412配置漂移/409异体/503 Owner不可得各保语义，失败无新Baseline。REL真实消费采用需另证。

## 永久历史缺口与八项未决决定

两历史GAP完整保留在decision模板和gate contract。现存37Sheet/5343非空单元格工作簿有准确hash，不因此恢复缺失旧30项；REL旧48、旧页/FIELD/ACT/完整状态和ReleaseAssessment/ReleaseDecision/AdmissionCheck完整字段仍不明，不用PG01猜补。

| DP | 仍需的后继决定 | 已知有界变化，不能当全部解决 |
|---|---|---|
| DP-ITER-01 | 共同摘要/修订序列化及准确外域合法域 | R1给出聚合canonical及别名边界；不替原工程摘要造算法 |
| DP-ITER-02 | 项目成员、评审角色、代理、自批例外、真实OA策略 | 无策略仍拒绝；R1绑定模型不批准实际人员角色 |
| DP-ITER-03 | 实际PostgreSQL身份/字段/CAS映射 | PostgreSQL-only取消新增双库要求；不批准UUID缩域或Tenant删除 |
| DP-ITER-04 | 保留期、脱敏、合法清除政策 | 关闭/否决及原证据不任意删除；不默认30天 |
| DP-ITER-05 | 页长、引用、评审人、附件上限和容量专项 | 50/200/500/50/50仍建议；不能靠fixture值批准SLO |
| DP-ITER-06 | VER/MES真实安全终结能力及联调 | 超时不能推断外部已停止，缺证阻取消/结案 |
| DP-ITER-07 | 日/韩等正式翻译批准流程 | 当前中文审阅不批准机器译文发布 |
| DP-ITER-08 | 已批准实施包与当时实际路径/服务采用 | 模板和设计不是当前服务存在证据；本次不进行产品代码验收 |

所有DP仍标UNRESOLVED；这里保留来源中已知有界修订，不填造“已解决”。后续如有新的准确决定，应逐项绑定后继版本、权限和无损依据。未覆盖的Office表现、真实角色策略、provider、外部安全封闭、真实业务/并发/数据库/跨仓采用分别留空，不能用静态清单PASS关闭。

本次核对能证明来源身份、引用定位、原断言承接和可审查的观察责任。它不能证明遗失原件逐项等价、真实产品已实现、所有外部原文未知要求已穷尽或签署者具有批准权；实际采用决定必须明确接受这些边界。
