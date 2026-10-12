# CP6 开发总台账

本台账按用户确认的顺序管理开发：全量评估、拆分大任务、安排批次、临近实施再细拆。范围对应已有125个设计目标；这是开发规划和证据登记，不改变业务合同或历史接受记录。

日常先看[开发进度](03-progress.md)，再看[实现评估结论](05-implementation-assessment.md)、[首批实施切片](06-first-slices.md)、[14个大任务](01-work-packages.md)、[125目标归属](02-target-coverage.md)和[依赖与开发批次](04-batches.md)。

## 唯一登记入口与更新方式

[roadmap.json](roadmap.json)是本轮开发范围、状态和验收映射的唯一登记入口。三个编号视图由它生成，禁止分别维护相互矛盾的进度数字。项目状态页只记录阶段结论和入口；活动BUG仍以[GitHub Issues规则](../quality/BUG-MANAGEMENT.md)为准，这里仅链接Issue，不复制BUG状态队列。

“持续追踪”指在实际任务开工、阻塞变化、验收和交付时同步记录；没有启动后台定时扫描、自动更改验收状态或远程Actions。每次汇报读取最新台账，注明记录日期和证据适用的源码。

一次状态更新包括：

1. 从当时已确认的主线建立对应独立任务分支，定位目标及其所属工作包；不要把整个大任务放进一个长期不交付的分支。
2. 修改JSON中的相应目标、工作包和场景记录，并更新`as_of`、`change_log`。保留历史失败和更正原因；既有日志/报告不覆盖。
3. 执行以下本地命令生成视图并检查。普通状态/文档更新不编译业务系统；检查器变化才运行其定向回归。
4. 按仓库要求在一次任务级审查、相关验证和正常PR交付后，登记实际远端包含性证据。确认交付之前使用`VERIFYING`，后续交付回执可以在专门的文档更新中登记。

从仓库根目录运行（`python`指本机已有Python 3.10或更新版本）：

```powershell
python docs/development/tools/manage_roadmap.py --write
python docs/development/tools/manage_roadmap.py
python -m unittest discover -s docs/development/tools -p "test_*.py"
```

前两个命令只核对登记和生成视图；不会执行验收目录里的业务场景。不得把工具输出的`PASS`填入业务场景。

## 如何理解进度

分开观察四类事实：

- **计划覆盖**：14个大任务与125目标都有归属，不代表实现完成。
- **实现评估**：目标有固定代码依据、复用/修改/新增结论及所需决策；未知部分继续保留`PENDING`。
- **开发交付**：完成一个批准切片可以登记证据，但只有一个目标的全部范围达到完成条件才将该目标记为`DONE`。
- **业务验收**：修订场景按唯一ID计数，原模块完整验收清单另行登记。交付通过与环境/生产准入分别管理。

0项已确认交付表示本轮尚未登记达到新设计的完整交付，不表示原系统没有代码。目标大小不同，当前不将完成目标数、文档数量或相对规模L/XL折算为全项目工时百分比；实际排定切片并形成可比估算后才能增加加权工作量指标，必须保留分母变化记录。

## 状态与证据

目标的`assessment`使用`PENDING`、`REUSE_CONFIRMED`、`CHANGE_REQUIRED`、`NEW_REQUIRED`、`DECISION_REQUIRED`。后四项需固定源码和判断证据；存在待决定事项时不进入实施。

`delivery_state`使用`PLANNED → READY → IN_PROGRESS → VERIFYING → DONE`；`BLOCKED`必须填写具体原因、影响范围和解除条件。尚未排期或正在评估不自动等于受阻。大任务还可使用`ASSESSING`，并在同一次更新中与目标进展保持一致。

每条实现判断的`assessment_evidence`至少记录`path`（仓库内可定位报告）、`source_sha`（40位完整SHA）、`scope`。实际运行证据额外记录`command`、`environment`、`observed_at`和`result`；只有明确为`PASS`的实际结果才计入通过。证据应描述真实Provider、版本、用例及范围，不能放Secret或生产数据。

目标`DONE`必须同时满足：

- 所有原模块验收已盘点，`original_acceptance_inventory.state=COMPLETE`，其中每条有唯一ID、真实结果和证据；存在未执行、失败、受阻项时不能关闭。
- 所有关联的修订运行场景通过。场景为0的目标仍须通过原始验收，不能凭空关闭。
- `implementation_evidence`记载实际代码和相关验证；`delivery_evidence`包含`path/source_sha/scope/remote_main_sha/pr_url`，报告能证明目标仓库远端主线包含交付，并记录必要合并后检查或准确复用范围。
- 人工任务级审查已确认范围完整、证据有效。工具只检查结构、来源和引用，不替代审查、不查询外部运行结果，也不自动认证远端包含性。

WP01完成要求全部125目标的实现评估和待决策事项关闭，原验收清单全部为`COMPLETE`且非空、无重复。此处完成的是盘点，清单里的业务场景仍可为`NOT_RUN`；业务目标交付时才要求实际通过。

## 验收目录边界

[本轮修订验收目录](../design-handbook/remediation/08-acceptance-catalog.json)共288条：247条运行/采用场景、41条已执行的历史静态检查。台账保留全部映射，后41条为`REFERENCE_ONLY`，永不计入新运行通过数。

247条是设计修订涉及的场景，并非CP6全部业务验收。WP01要逐模块读取现行设计中的原验收清单，包括询盘、线索等模块已有的成套AC，建立完整清单后再确定切片完成条件。历史缺件保持准确身份与影响范围；用户已确认无法找回的原件不重新要求补找。

跨工作包的场景由WP14协调，各`consumer_targets`的业务Owner共同参与；单包场景由该包协调。协调人不取得其他Owner的数据写权。同一场景的多消费者引用只统计一次运行结果。

## 台账建立时的初评快照（后续结论见下节）

- [125目标当前覆盖](../design-handbook/remediation/10-target-coverage.json)、[115登记项/114问题族处置](../design-handbook/remediation/00-disposition-register.json)及验收目录按Git blob固定，检查器拒绝静默漂移。
- Main源码固定`90ea752e9029d171e6a0307fcda06c66c3e04a69`；CRM为`c778a3052a4416b82facb07b1244fc98fb6ad8a1`；Platform为`30bd23af6808d217a23878bd9437513043c52834`。本轮只读核对三仓远端main和源码入口，详细计数见JSON的`code_entry_inventory`。
- 入口存在性不证明符合新合同。现有双库、消息事务、CRM页面、WMS/MES/FIN服务和Space分层作为复用候选；PLM WP0按[现有实现起点](../design-handbook/architecture/01-系统边界与开发地图.md)所列候选边界处理。
- 没有业务编码、编译、数据库迁移、环境切换或部署。当前每个目标的逐项实现符合性为`PENDING`，247条运行场景保持`NOT_RUN`。初评可用于安排评估顺序，不能据此承诺精确工期。

本台账建立过程及本地验证范围见[规划任务记录](../superpowers/plans/2026-10-11-development-roadmap.md)。

## WP01当前进展

125/125目标已有固定源码差异判断：105项需改造、20项需新增核心用例。原验收清单完整123/125；PLM-ITER与PLM-REL保留不可恢复的历史原表缺口，WP01整体仍为ASSESSING。

登记13,404条目标—来源验收关联，其中原始AC/TC 8,456条、必需补充校验/连续场景 4,948条。共享条目挂到每个负责目标，不能把这个合计当成全域唯一业务测试数。全部NOT_RUN；247条修订运行场景也保持NOT_RUN，41条历史静态检查仍仅REFERENCE_ONLY。

详细方法、历史缺口及固定源码见[本轮评估](05-implementation-assessment.md)。原清单中的ORIGINAL_AC和REQUIRED_SUPPLEMENTAL_SCENARIO均属于目标完成门；纯设计依赖单独保存。definition_ref指向完整来源定义，禁止只验证标题而遗漏附加refinements、前提和连续步骤。当前已确认业务交付仍为0/125。
