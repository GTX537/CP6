# WP01实现评估与开发安排

125/125目标已有固定源码差异判断：105项需改造、20项需新增核心用例。原验收清单完整123/125；PLM-ITER与PLM-REL保留不可恢复的历史原表缺口，WP01整体仍为ASSESSING。

登记13,404条目标—来源验收关联，其中原始AC/TC 8,456条、必需补充校验/连续场景 4,948条。共享条目挂到每个负责目标，不能把这个合计当成全域唯一业务测试数。全部NOT_RUN；247条修订运行场景也保持NOT_RUN，41条历史静态检查仍仅REFERENCE_ONLY。

这些数字表示已登记范围和证据，不表示软件开发完成。需要新增核心用例的20项仍可能复用旧数据、通用页面或技术能力；需改造的105项也不代表其余逻辑全部符合新设计。原清单COMPLETE只代表来源与条目盘点完成。

## 开发人员从哪里开始

1. 看[125目标差异](assessments/20261011/target-assessments.md)，了解每个模块已有行为、必须改变的语义及实际源码入口。
2. 看[首批实施切片](06-first-slices.md)，先展开S01精确值与身份，随后实施事务/授权门和CRM入站；工程、库存、财务及共享消费者按需求提前配套。
3. 按[原验收目录](assessments/20261011/original-acceptance-catalog.json)和[修订运行目录](../design-handbook/remediation/08-acceptance-catalog.json)选择该切片必须验证的原案、补充场景及正反例。
4. 实际开工、受阻、验收和交付时同步[总台账](roadmap.json)与[进度页](03-progress.md)，填写具体证据，而不是手填总体百分比。

## 大任务的已知实现差异

| 工作包 | 目标数 | 需改造 | 需新增核心用例 | 原清单完整 |
|---|---:|---:|---:|---:|
| WP02 公共平台与系统基础 | 20 | 20 | 0 | 20/20 |
| WP03 CRM客户经营 | 7 | 2 | 5 | 7/7 |
| WP04 商业交易与订单 | 8 | 8 | 0 | 8/8 |
| WP05 工程与产品生命周期 | 14 | 4 | 10 | 12/14 |
| WP06 计划计算与决策 | 7 | 4 | 3 | 7/7 |
| WP07 采购执行 | 7 | 6 | 1 | 7/7 |
| WP08 制造执行 | 7 | 7 | 0 | 7/7 |
| WP09 质量管理 | 4 | 3 | 1 | 4/4 |
| WP10 仓储与物流 | 25 | 25 | 0 | 25/25 |
| WP11 财务核算与结算 | 13 | 13 | 0 | 13/13 |
| WP12 流程与审批 | 5 | 5 | 0 | 5/5 |
| WP13 空间设计与运营 | 8 | 8 | 0 | 8/8 |

代码基础决定改造范围：

- CRM已有实际公开入站事务、线索图、审计/Outbox及回执页面；V15发放尝试、回执/同意事实和H02转化需按新合同接入。ACC/CON/OPP等旧表不等于已有新Owner业务用例。
- Main已有SQL Server/PostgreSQL配置、共享连接入口以及精确decimal存储基础；需要按Owner分别补齐精度、原生身份、共同事务和全部写者门，不能粗暴统一scale。
- 计划转PR/WO仍注册Stub；MES现追加采购按PlanQty−ActualQty及物料去重，现回冲读取当前BOM。这些路径必须接责任链、准确技术快照和原操作结果。
- 采购收货、质量判定、WMS库存生效与计划供给要分清。现Stock/LPN/移动事务基础可复用，新锁图、来源份额和重复执行控制仍须采用。
- FIN已存在期间检查、凭证和业务服务；自动GL与手工路径的预算门、AR/INV成本权威、MATCH/AP纠正取消需按合同收束。发现新合同差异不自动认定当前活动BUG。
- ENG已有ERP版模服务、列表/编辑页与WMS实物服务，归改造；PLM九个核心Owner与ENG-REV新能力仍需新增。未合并WP0候选只允许受控提取适用纯逻辑和测试向量。
- OA与SPACE已有基础能力；审批结论不能替业务应用回执，缓存/grant/旧worker不能替当前最终授权。跨域采用必须由真实Owner证明。

## 历史缺口与实施边界

ITER现有72个TC与其原30AC/Excel声明未形成可证明的一一对应；REL声明的历史48AC原表没有恢复。现行后继、共享场景和可恢复原文已登记，但不能用它们冒充缺失旧原件。用户已确认找不回，不再安排补找。具体缺口以目录中的remaining_scope和准确来源为准。

S00负责按现行已接受规则补订有版本的替代验收设计，明确旧缺口、覆盖和未覆盖范围，经任务级审查后再调整完成条件。本轮不改掉原清单完成门，不将这两个目标标COMPLETE或DONE；来源完整的公共基础/CRM准备可以继续。WMS-DEL历史独审R01071缺失继续保留在原缺件账，验收用例可盘点并不意味着该独审已恢复。

真实Provider、原生schema/Owner登记、权限政策、完整写者、共同事务和部署身份是实施/运行准入。它们没有因为方案已决定或静态检查通过而获证。主线原始档案、已接受设计、修订资产和历史接受记录未修改。

## 固定源码与证据限制

| 来源 | 固定SHA | 适用范围 |
|---|---|---|
| MAIN | `5ca90701bfe1e886ea9e179f1e8e5f413bb44f43` | CONFIRMED_REMOTE_MAIN_BASELINE |
| CRM | `c778a3052a4416b82facb07b1244fc98fb6ad8a1` | CONFIRMED_REMOTE_MAIN_BASELINE |
| PLATFORM | `30bd23af6808d217a23878bd9437513043c52834` | CONFIRMED_REMOTE_MAIN_BASELINE |
| PLM_CANDIDATE | `b7ec269bfc6170f098fa2850361ef26f11a2391e` | UNMERGED_CANDIDATE_NOT_MAIN |

Platform本地HEAD与实际远端main不同，本报告读取固定远端Git blob；候选PLM分支不算Main。现有测试只登记源入口，未执行就保留NOT_RUN。一个可定位的实际差异足以决定改造/新增，不代表本轮穷尽审计每个方法、每条旁路或全部历史包。

源码定位初检保留21处Web目录大小写错误；按Git真实路径改为cp6.web后1118处引用通过，ENG-TOOL补评估另核9处。原失败、修正和复用范围见[初检](assessments/20261011/evidence/code-source-validation.initial.json)、[修正结果](assessments/20261011/evidence/code-source-validation.json)、[审查定向补验](assessments/20261011/evidence/code-source-validation.review-fix.json)。这些是来源/定位检查，不是业务通过。

集中审查同时纠正FIN-INV中92条带Expected/ExpectedCode的规范校验被误列为纯元数据的问题：已纳入必需门，仍NOT_RUN。纯schema、映射和样例上下文保留为设计依赖；它们不能代替实际用例，但也不会被包装成独立业务AC。

完整断言分存四个目录载体，roadmap只保存唯一登记ID、状态、证据槽和definition_ref。原始ID、文件SHA、行/JSON Pointer、ZIP成员及来源组合均保留；无原生ID的规范补充使用内部定位ID，不伪造原AC。嵌入原文里的历史PASS只是引用，台账状态仍NOT_RUN。

本地原件定位以仓库docs归档路径为逻辑地址；未将21.5GB原归档纳入Git，其他机器需要同SHA的本地原件才能复核原文。JSON中的完整断言、来源哈希和定位可供开发阅读；$exact_source_value等大对象引用必须回到准确原件解析，不能忽略其断言。

检查入口：[本轮结构与引用验证](assessments/20261011/evidence/inventory-validation.json)、[台账检查](assessments/20261011/evidence/ledger-validation.json)、[任务级审查](assessments/20261011/evidence/task-review.json)。本轮没有业务编码、业务编译/测试、数据库迁移、现有环境变更、GitHub Actions或部署。
