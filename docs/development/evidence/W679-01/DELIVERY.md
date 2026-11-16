# W679-01 · 个人表单/附件权限核销与旧扫描修复

2026-11-16 UTC，COORD；基线 Main `3f856aadd2799b997d8808fc62fd0a5e9c947415`，固定 DEV-1，分支 `codex/w679-01`。官方 START `2026-11-16T08:45:34.683351+00:00`。本窗口领取个人表单、附件实际字段消费核销，以及 [P2 #1202](https://github.com/GTX537/CP6/issues/1202) 所报 OA/WF、Plan/Pub 旧属性扫描；不扩展到全部 Operations 消费者。

本窗口完成限定核销与测试修复，没有修改产品权限、数据库、API 或路由。个人表单已采用自己的当前权限和动态字段策略，不能以未直接调用通用字段决定类判定“未接入”。本轮扫描中的缺属性入口均追踪到实际 Owner、原位权限门或已关闭入口；未发现该批报告中的产品绕过。WP02-03 保持 IN_PROGRESS。

## 个人表单与附件消费边界

`PersonalFormOwnerV2.Actor/Mask/Final` 从当前原身份读取 PostgreSQL 权限；`PostgreSqlPermissionReader` 对真实角色字段记录先 MIN，再由消费端处理缺省值。封存域必须准确为 Title/DataJson，未知字段或非法访问值立即报错。动态字段来自准确发布版本及当前使用策略，其策略与 DataJson 整体权限相交；Title 独立控制。没有将动态表单字段混入全局受控字段注册。

目录、草稿、提交、退回、重基/快照恢复及原结果沿各自 Owner。草稿只允许本人；局部保存保留隐藏/只读原值，不能借重读原命令泄露现已隐藏的数据。收件箱及表单查询先形成当前授权成员集，再派生搜索、标题、总数和明细；代理读取还与委托人权限相交。授权根门、WF_AUTH 当前快照和最终核对覆盖政策/权限变化，不能仅因最终代码比较 actor 就忽略同事务持有的授权门。

附件使用 `Main.File.FormContent.v2`，不是通用 FileContent v1 的表单别名。`PersonalFormOwnerV2.Files` 按 FORM/DRAFT/RETURNED/TASK/HISTORY 核准确对象、版本、谱系及 upload 字段；`NativeInboxOwnerV2.Files` 再以当前可见实例/查询结果约束绑定下载。隐藏字段拒绝，写入要求 edit；HOLD/RELEASE 另需 file-content 的当前 edit、范围和字段权。字节必须处于允许状态并通过原摘要检查，最终权限通过后才输出。未知提交通过原操作恢复，不以重传或默认空内容冒充成功。

已核对 Controller 没有个人表单导出入口；附件下载属于原 v2 当前上下文读取合同，并没有声称独立 Download 用途已采用。文件/受控文档 v1 的 query/add/edit 语义仍按原合同保存。当前清单更新为 personal-form 组；目录、路由及其他资源规模保持 81/114/2103，这些数值不是验收目标。

## #1202 原入口核对与扫描修正

下表覆盖失败清单的所有控制器组。方法级准确登记保存在两份扫描测试；没有按整个控制器放行未来方法。

| 实际入口 | 已追踪边界 |
| --- | --- |
| FlowAuthoring / FormAuthoring 及各 AuthoringPolicy、PublicationPolicy | 各自 v2 Owner 的当前身份、读取/写入/管理动作、对象可见性与字段决定；命令新写和原结果读取分别判断。 |
| ApprovalBinding | `ApprovalBindingOwnerV2` 当前动作、Configuration 字段决定与 WF_AUTH 门。 |
| Draft / FormSubmission / PersonalForm | `LegacyPersonalFormHttpV1` 适配当前 `PersonalFormOwnerV2`；原生个人命令也直接进入该 Owner，APP 决策分支进入对应审批 Owner。 |
| FormFieldDisposition | 当前请求人安全读取或合格处置人动作/字段授权、准确处置范围与最终门；复用 [W303](../W303-01/DELIVERY.md) 原子消费证据。 |
| FormFiles / NativeInbox / NativeFormQuery | 个人表单与 NativeInbox 当前上下文、字段遮罩、成员可见集及最终门；POST 读取/下载不是未经授权的业务写入。 |
| NativeTaskOperations / NativeTaskBatches | APP/SFS 各自任务 Owner 与当前授权证据；批量入口要求 oa-inbox query、batch-transfer，每项调用自己的 Owner。 |
| TaskDecision / TaskDelegation | 原生 APP/SFS 使用当前任务 Owner；legacy 决策保留 oa-inbox approve，并在 `TaskDecisionService` 的共同 IAM 门内核当前身份/动作及实际任务归属。委派 Owner 核本人、当前管理动作、准确 grant 和最终门。 |
| FlowDraft.Create | 已退役 v1 Writer：新命令返回 410；已提交原命令核原 actor、准确输入/定义及当前 authoring 读取/字段权，不再次创建草稿。不能当作无条件关闭入口。 |
| Definition.Retired / Designer.Retired / FlowAdmin.Retired / Flow.DefinitionRetired / Form.DefinitionRetired | `LegacyDefinitionResult.Gone()`，明确关闭；与仍提供受控读取的 FlowDraft 分开登记。 |
| CodeGenMetadata.Save/Original/Adopt | `CodeGenMetadataOwnerV1` 当前 view/save、原操作本人及共同 IAM 门，提交前重核当前动作。 |
| NumberRule.Version/Change/Original | `NumberAllocationOwnerV1` 由可信租户/规则注册绑定；版本及原结果是当前 query，新变更是 edit，最终同一动作/身份门。 |
| FileContent.Upload/Command/Close | `FileContentOwnerV1` 与 Operations 当前权限、父对象、状态及原操作边界；保留已采用文件 v1 合同。 |

旧 OA/WF 只读 POST 和 Attachment 组件条目的范围保持，未因本次扫描修正宣称全部历史入口完成新用途采用。后者仍由宿主业务检查和当前附件服务负责，个人表单文件不使用它代替 v2 Owner。

修正固定控制器/贴点数量、未纳入实际 Owner、禁止 GET 权限和“端点与权限键必须一对一”的过时断言。CodeGen 的 GET Preview 与多个 POST 预览/比较可合法共同使用 `pub-codegen:view`。独立资源键集合仍双向核对；GET 的准确映射另外核对。新增未登记 POST/PUT/PATCH/DELETE 仍失败，登记项必须实际存在、要求登录、不能 AllowAnonymous，也不能与方法属性/旧豁免冲突。扫描现在包含继承的 HTTP action，不将普通基类辅助方法当作端点。

## 实际验证与来源

私有工件根 `D:/CP6-test-artifacts/W679-01`。业务源为本窗口基线，未改产品；测试和索引源码保存为 `ddda3e8b01fd78f136215dd9b714da2854b7e937`；后续提交仅同步报告与状态。复用 W678 的已构建产品依赖及缓存目录 `D:/CP6-test-artifacts/W678-01/build`，仅重编 CP6.Tests，不将缓存位置误记成旧测试源码。`current-build.log` 为最终构建：135 项既有 warning、0 error。

| 实际运行 | 结果和范围 |
| --- | --- |
| `baseline/baseline.trx` | 本轮先复用 W678 原测试程序集，限定 OA/WF、Plan/Pub 两类：6 失败 / 5 通过 / 0 跳过。原失败保留。 |
| `verification/verification.trx` | 最终 21 通过 / 0 失败 / 0 跳过。两组扫描 11 项，原生 PostgreSQL 行为 10 项。 |
| 新 DataJson 回归 | 整体 readonly 拒绝覆盖；改为 hidden 后草稿和原结果均不披露字段；空局部保存保留原值。按解析后的业务值检查，避免 JSON Unicode 转义造成错误结论。 |
| 既有行为定向回归 | 动态 hidden/readonly 保留与原结果过滤、最终政策变化全回滚、收件箱授权成员集和隐藏标题搜索、附件当前撤权/未知提交恢复、同事务绑定、hold/完整性/删除草稿、隐藏引用重基及退回替换后当前下载。 |
| 元数据与索引 | 产品/路由/封存目录未变化，复用 W678 `final-metadata`；实际运行 `index_coverage.py` 更新分类及当前源码导航。routes.jsonl 内容保持。 |

实际命令：`dotnet build CP6.Tests/CP6.Tests.csproj --artifacts-path <W678缓存>/build --no-restore -p:BuildProjectReferences=false -p:Cp6PostgreSqlDevelopment=true -p:UseSharedCompilation=false --disable-build-servers -m:1`，随后本轮 `validate.ps1` 使用 `dotnet vstest` 的明确类名/方法名筛选。环境为 Windows、.NET 8.0.424、隔离 PostgreSQL 18.6，私有端口 57366。用户、表单、业务政策、文件字节、内容存储及扫描器为明确模拟输入；实际产品 Owner、迁移、权限源、事务和数据库不模拟。

本轮没有重新运行 HTTP、浏览器或 Defender。[W285](../W285-01/DELIVERY.md) 原有真实 Main/Chrome/Defender 文件边界结果和 [W317](../W317-01/DELIVERY.md) 快照来源恢复按其原范围保留，不改称本轮实跑；当前定向 PG 场景覆盖本轮核销的字段及文件授权。没有重跑全仓、跨仓 CRM 或全部 PGT。

## 集中审查、剩余范围与生命周期

一次作者 SPEC/QUALITY 审查完整任务差异：方法登记与真实调用边界、旧读取与新写分流、匿名/未来入口检查、共享权限键语义、字段隐藏与原值保留、测试的实际环境和证据范围。未改变产品权限、合同版本、租户/Owner、数量精度、迁移或部署。相关验证全部通过；台账、两个生成页、协作入口及四份项目状态文件同任务同步，文档只做差异/引用/生成一致性检查。

WP02-02 保持 DONE；WP02-03 保持 IN_PROGRESS。后续继续 Operations 有受控字段的实际派生 DTO、写回读、独立用途，以及文件 v1/表单附件 v2 与独立用途模型的合同核销。订单产品查入、其他追踪/未出货入口和 WP13-04 专用组合仍按各原范围推进；没有用本窗口关闭整张字段权限卡。375 卡保持 39 DONE / 1 IN_PROGRESS / 335 PLANNED，125 完整目标 0 DONE，PGT 27/27、询盘 60/60 保持。

本轮未触发或取消 GitHub Actions，没有触发配置或分支保护变更，没有生产部署。push/PR 前已核对远端 main 仍为开工基线、工作流及依赖未变，复用 W678 已分析结论：普通验证仅手动、R2 只匹配版本 Tag，本任务分支/PR 不触发。正常受保护 PR、远端采用核对及官方 finish 后才声明分支清理、工作区干净和席位释放；最终回执保留在私有工件根。
