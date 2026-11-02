# 租户创建 V1

POST `/api/platform/tenant/creation-v1/commands`接受OperationRef、ExpectedTemplateVersion、Code、Name、ExpireDate、Remark、AdminUserName；GET `/template`读取当前模板，POST `/commands/read`按OperationRef查询。所有调用使用当前完整平台管理员会话、独立scope和同一IAM完成门；严格JSON、CSRF和NoStore生效，不接受自报actor/租户。

模板是原默认租户RoleId=1的菜单集合，角色必须启用，菜单必须非空、存在且启用。Version组合现有role:1和permission-catalog修订，不额外生成模板资格或审批Owner。原生菜单/角色映射写入、删除重建、ABA由原IAM修订推进，目录其他变更也可使版本过期；创建在同一个IAM门内读当前版本和复制菜单，变化412。缺默认角色、菜单或目录修订明确TENANT_TEMPLATE_NOT_READY，不创建无权限可用租户。初始化仍由既有Owner负责。

首次创建同一PG事务保存Tenant、普通首管理员（RoleId=1/IsPlatformAdmin=false/MustChangePassword=true）、角色、菜单、原管理强审计和无密码原结果。创建核心接受已核对菜单，不再独立读取一份模板。沿现有PG大小写不敏感TenantCode唯一索引；Code≤50、Name≤200、AdminUserName≤100、Remark≤500，必填非空白，文本必须合法Unicode且无NUL；保留原字符和尾空格。ExpireDate沿原timestamp without time zone，接受无偏移且最多微秒精度，不静默截断。

原键为非空有效Unicode，bytea保存原键/请求/结果，SHA256只作索引并比原字节。同一原actor/actorTenant及同义请求恢复原结果；不同输入409、他人403。恢复优先于当前模板/租户存在性，所以模板变化或原租户删除后仍返回历史，绝不重建。新请求才核模板和编码。提交前失败全部回滚，开始提交后的不确定错误503 TENANT_OUTCOME_UNKNOWN；用原键查询或原样重试，不能换键猜测成功。

Result=Receipt+TempPassword+NeedsCredentialReset。仅首次新提交成功返回TempPassword；commands表只存Receipt，从未保存明文或可恢复密码。每次重试/查询返回null密码和NeedsCredentialReset=true，表示原一次性密码不能再取，不是断言账号当前必须重置。若原密码已经安全保管无需重复重置；若丢失，沿IAM受控凭据重置设置新凭据，本期未新增跨租户重置入口。Receipt是历史创建证据，不代表当前租户/凭据状态。

新schema cp6_tenant_creation_v1仅新增命令表，禁止历史UPDATE/DELETE/TRUNCATE，不修改原租户数据。历史保留期限/擦除需治理任务。当前授权在门内决定，最后复查门外时钟/注销和原授权租户期限；复用W264事务与会话适配，旧管理行为不变。

页面新建入口先读模板，412保留草稿并要求重读；发送前在当前身份隔离sessionStorage保存准确请求，未知/刷新可查询或原样重试。后续查询404或权限拒绝不清除未确认请求。原Owner明确模板/编码拒绝发生在权威原命令读取之后，可解除待确认并保留草稿。临时密码仅存在当前内存，关闭/离页/身份切换即清除，禁止进URL/storage/通知日志。旧身份晚到响应忽略，存储失败不发送。

旧CreateAsync/API兼容路径仍无原命令恢复，不得对其未知响应盲目重放。所有写者共同fence、下游传播及整卡范围见[交付](evidence/W265-01/DELIVERY.md)，不因本期变为已完成。实际验收为本地PG18/Main/Chrome和明确模拟数据，无既有环境迁移或生产部署。
