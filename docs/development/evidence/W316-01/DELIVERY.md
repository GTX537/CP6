# W316 — 旧个人表单入口受控适配

原 WP12-03/04 的旧草稿与提交 HTTP 路由已改用同一 PersonalFormOwnerV2。创建、保存、重基、提交、墓碑删除共用现有事务、实际人权限、字段策略、当前版本、提交原子性和原命令恢复；不再由 Main 宿主注册旧 DraftService / FormSubmissionService 写入口。旧类保留历史来源，不宣称所有库消费者退役。原卡继续局部完成。

## 旧接口与准确前驱

请求仍使用旧 DTO 字段，响应仍为 `{code:0,message:"OK",data:...}`；提交保留 SubmitFormResult 六字段，删除成功 data 为 true。旧大小写不敏感字段规则及可选 null/省略等价保留，大小写重复字段拒绝。Data 中的字段名、数字词法与内容不做 DTO 字段归一化。

| 操作 | 路由 | 旧请求体 | 必需调用上下文 |
|---|---|---|---|
| 创建 | POST /api/oa/forms/{formKey}/drafts | Data、Title? | X-Form-Version-Id |
| 保存 | PUT /api/oa/drafts/{id} | Data、Title?、RowVersion | 原读取返回的 RowVersion |
| 直接提交 | POST /api/oa/forms/{formKey}/submissions | Data、DraftId? | 五上下文；非空 DraftId 明确要求使用草稿提交路由 |
| 草稿提交 | POST /api/oa/drafts/{id}/submit | RowVersion | 五上下文，不接受 Data/formKey/额外 token |
| 重基 | POST /api/oa/drafts/{id}/rebase | TargetVersion、ConfirmRemovedValues、RowVersion | X-Form-Version-Id、X-Form-Rebase-Preview、X-Form-Rebase-Digest；必要时准确处置引用 |
| 删除 | DELETE /api/oa/drafts/{id} | 无或空对象 | If-Match 为原 RowVersion，可带双引号 |

所有写入携带 Idempotency-Key。五上下文为 X-Form-Version-Id、X-Form-Activation-Revision、X-Form-Binding-Revision、X-Flow-Version-Id、X-Flow-Activation-Revision；可空值显式传 `null`，省略不等于已确认无绑定。GET `/api/oa/forms/{formKey}/initiation-context` 返回真实当前上下文。旧调用方没有完整前驱时必须重新读取并明确形成新意图，服务端不选当前版本填充旧期望。

RowVersion 是原生 GUID 字节的规范 Base64，无损双向转换；旧库 8 字节 token 不能据此猜成原生 token。重基仍先调用原生 `/api/oa/v2/drafts/{id}/rebase-preview`，使用准确源 RowVersion GUID、目标发布 GUID 和预览结果。旧整数 TargetVersion 在最终事务内对比实际发布版本。隐藏字段损失沿用 W302/W303 真实处置，X-Form-Disposition-Request/Receipt 仅传准确请求与事实引用，不授予权限。旧自由文本诊断不支持字段披露合同，返回经过当前掩码过滤的移除路径；原验证失败保留明确 FORM_VALIDATION_FAILED，不能变成空错误成功。

错误映射保留 E-WF-039/040/041/044/047/048/043；未知提交/存储错误返回 READ_ORIGINAL，不重试造新意图。成功响应 Location 和 GET `/api/oa/legacy-form-commands/{operation}/{resource}/{key}` 指向当前实际人的原命令查询。

## 固定映射与数据库边界

新增 PostgreSQL 前向迁移创建 legacy_commands。旧键固定映射为 `legacy1.` + SHA256(UTF8 key)，槽仍完整绑定租户、环境、实际人、操作、资源。保存原旧请求字节、版本 FORM-LEGACY-ADAPTER/1 和规范化旧 DTO 摘要；原生命令另保存准确转换后信封与原生 WFC 摘要，不能用旧摘要替代 Owner 摘要。别名大小写和省略可选 null 归一到相同意图；内容、前驱或目标改变仍冲突。

映射与原生效果、提交槽、草稿版本、日志同事务。数据库插入守卫要求相同当前事务、实际人/租户/环境/操作/资源与未完成原命令；映射不可改写或删除。实际守卫不可用时写入快速失败并回滚，不保留部分原命令或业务效果。已存在旧 Wf_FormData.SubmissionKey 却无固定映射时返回 LEGACY_SUBMISSION_MAPPING_REQUIRED，不把历史提交解释为 NOT_OBSERVED 或重新执行。

合法 data 仍为 1 MiB，旧外层 DTO 另计有界开销，内部值不因 Unicode 转义扩张或数字精度变化被拒绝。没有新增 SQL Server、双库要求或既有环境迁移。

## 历史记录与页面

GET `/api/oa/legacy-drafts` 只读取当前实际人、当前租户的旧草稿目录，记录准确旧 head/version 配对、原时间与来源。历史补录不冒充可信发布；缺版本快照明确标记。标题使用当前字段权限，旧 DataJson 不在尚无准确发布/字段披露映射时返回。原始内容保留，目录没有编辑、提交、删除入口。

“我的草稿”主入口采用已有当前／历史草稿切换组件，身份变化重新挂载，历史异步结果在卸载/身份切换后丢弃。读取失败清除旧列表并显示明确失败。真实 Chrome 经登录后的页面验证了目录可达、无旧写按钮、删除取消不发请求、旧 HTTP 保存使当前页面删除 CAS 失败且保行、成功删除后的刷新不复活。内部 Owner/API 不模拟，业务输入为明确构造数据。

## 原卡仍未完成的范围

- W317：准确重基前快照恢复为新个人草稿，保留原历史和源版本，不能把旧草稿改回旧 schema 冒充恢复。
- 历史旧行的内容披露与显式采用需准确发布、字段政策和身份来源映射；本窗只有受控目录，不宣称历史内容完整恢复。后续与 W305 实际历史来源链核对。
- 原生草稿删除已提交但响应丢失、随后整页刷新的页面恢复入口有待独立复现核对：活动列表可能不再含该行，挂载编辑器条件可能遮蔽原命令查询。此次 Chrome 覆盖明确成功/明确拒绝，不覆盖此未知结果场景。
- 原 375 维持 31 DONE / 2 IN_PROGRESS / 342 PLANNED，原 59 局部卡剩 52；125 目标、247 正式验收状态不因本窗改变。业务验证与部署分别记录，无生产部署。


## 验证、审查与生命周期

9项独立实际PostgreSQL18场景通过：6项旧接口/映射/数据边界场景、1项实际登录Chrome全链路、2项受影响原生提交原子性/并发回归。历史目录3项Vue行为通过（按各场景最近有效结果复用），主入口更新后的vue-tsc通过。模拟业务输入、实际内部Owner/HTTP/PG/Chrome，无内部API替身。

实际来源 `D:/CP6-local-test/wave316/foundation/final-validation.json`，包含每个场景准确执行来源。最初旧接口缺前驱未按新合同拒绝的红测保留；旧DTO大小写/可选null归一后提交场景重跑通过。历史Vue首次一个失败来自测试标题与静态说明同词，修正fixture标题后只重跑相关两项，第三项采用此前成功来源。Chrome首轮发现主“我的草稿”仍直接挂载当前组件，修正为已有切换组件后真实页面通过。上述失败记录均保留，不删失败、不重复制造证据。此次最终只针对新增上限与共用原生事务路径再跑3项，全部通过。浏览器截图已查看，历史目录截图处于loading淡出动画；行为断言与实际响应为通过依据。

COORD一次集中SPEC/QUALITY审查完整分支diff：核旧DTO兼容、准确五上下文/版本、不可变原命令映射、实际人及租户隔离、原生事务与字段披露、原历史保留和页面恢复边界。新迁移只在隔离PG执行；共享解析默认合同未变。无本窗未处置合并阻断项；既有未知结果刷新入口缺口已独立登记#759，下一分支真实复现修复，不以本窗成功响应测试冒称覆盖。

当前有效22841秒，W316开工20123秒，最终耗时以官方finish后采样为准。流程检查确认本任务PG/API/Chrome/Node进程已退出；没有停止无关进程。未运行全仓回归/SQL Server/正式发布门禁，未触发或取消Actions，无工作流或依赖变更、既有数据库迁移、生产部署。

分支准确删除、固定工作区干净、两席位官方finish释放以及两仓远端采用以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave316-final-verification.json` 的最终PASS为准，证据生成前不宣称已释放。原37531DONE/2IN_PROGRESS/342PLANNED，原59余52；125/247状态不改。下一独立BUG759待复现/修复，再W317准确前快照新草稿恢复及W305后续；24h目标持续ACTIVE。
