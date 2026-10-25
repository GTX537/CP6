# W126-01 人工撤回同意的当前权限来源交付

旧Main human context只支持intake四用途，不能支撑冻结privacy:consent-withdraw。本轮新增独立MainInquiryConsentFieldsV1，将实际crm-privacy.consent映射为consent，只有Access1可写；沿用完整当前角色stored-MIN后注册默认，没有插入每角色默认覆盖隐藏/只读。字段注册与数据范围注册仅提供管理目录，不自动授予动作或扩大范围。实际环境须由受保护版本化管理入口配置菜单动作、角色和范围。

权限需要crm-lead:query+crm-privacy:consent-withdraw；主管沿用实际crm-lead:assign与完整健康部门树，并取lead和privacy部门范围交集。缺少范围按本人1处理而不能提供主管部门，超出256线格式限额拒绝，不能截断。无PII查看或放行/拒绝权限借用。当前认证根锁、最终Bearer重查、五分钟有效期和用户/所有角色来源签名保持；抽取现有健康部门计算供两个来源使用，原intake语义不变。

CRM配套新增明确PrepareConsentWithdrawalIdentityAsync和确切用途验证，保留原保护会话、CSRF、具体在线OIDC、nonce/epoch/精确站点表单、全部角色来源及原PG事务当前证明。来源证明CanAuthorize=false，业务资源和最终提交授权仍由真实Owner承担。

48项唯一相关本地检查通过：Main17项PostgreSQL/真实登录OIDC/HTTP，CRM29项具体保护会话/HTTP/当前来源，跨仓实际签名/原始快照到CRM PostgreSQL2项；必需0跳过。

成功来源：main-fixed-20261025T025333Z（12）、crm-fixed-20261025T025334Z（14）、main-cross-20261025T025720Z（1，覆盖同名HTTP最终版本）、crm-cross-20261025T025755Z（2）。另复用main-scope-20261025T025034Z中5项未受影响的intake部门范围和crm-context-20261025T025120Z中15项旧human来源/传输成功。唯一测试名去重，汇总D:/CP6-local-test/wave126/foundation/final-validation.json保留每项来源。最初两个运行各11/14个新用例因重复全局菜单键和基线边界标记夹具失败，未触及产品授权结论；修正夹具后定向通过，日志/TRX保留。此前6个Main成功中重复consent用例按最终版本替换。

跨仓使用实际Main登录OIDC/HTTP签名和原始持久来源payload，未重签/改时钟，核对CRM当前持久信任及全角色发布匹配。该互操作检查的inspection actor为明确本地夹具，并非实时Main保护会话到CRM业务事务；具体保护会话另由本地TLS/HTTP/PG检查证明。未把两种证据合称生产或撤回业务端到端验收。没有Web/浏览器检查，因为本轮未改页面；没有既有数据库迁移/实际授权配置或生产部署。

集中SPEC/QUALITY审查通过，冻结合同、依赖、模型/迁移和工作流未变。相关权限/租户/来源风险已定向验证，未扩散全仓检查。工作流及相关输入与既有审查基线相同，普通验证仍手动，本轮Actions零；无绕过保护。

CRM [PR151](https://github.com/GTX537/CP6.CRM/pull/151)已完整采用source27ed7ec592e0e49fd7d5f15c6b89c6e12dc61277到main9181897d533c3cc6309c264d84aaf1aaf59babfb，Handoff [PR102](https://github.com/GTX537/CP6.AI-Handoff/pull/102)采用source60b57d55ec7ef60cf73941de9ffb1c7061011811到bf651822fe09c246d826d973512d43ba776edf53，两任务官方finish确认准确本地/远端分支删除和席位释放。固定DEV-2回到Handoff留作复用。自有PG55452已身份核对后停止，数据/失败日志/缓存保留。Main正常合并及finish仍待本轮完成，最终以wave126-final-verification.json核销，下次任务准入统一更新本快照。

原估55–90分钟，截至交付准备26.25分钟，最终按Main START至三仓最后finish计一次，不叠加跨仓时间。原生撤回Owner、精确Submission/Consent资源、ETag/幂等槽、原事务即时事实和目标本地版本事件、页面入口紧随后继；撤回不改已有保留deadline。隐私导出/FILE当前下载/嵌套分类与原生Outbox发布仍另行采用。

正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY保持。D6 PARTIALLY_ADOPTED，七天ACTIVE，本窗口不关闭完整WP03-04。
