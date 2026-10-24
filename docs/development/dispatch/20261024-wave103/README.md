# W103 受保护会话、真实权限签发与Web BFF配置链路

COORD本聊天单人；七天目标ACTIVE。Main `7c00bb5b50304ce9d7c3ebf49d555131cfeffb54`、CRM `d3615db81675ca6cdad2d9c777505d451a552931`、Handoff `087ab22e279520076f78eb474480bd0a82556bab` 经远端核对。官方status/capacity和W102最终proof通过；W102 Main514/CRM129/Handoff78已完整finish，三准确分支删除、席位释放，实际68.89min（原估120–180），347项相关本地检查通过，不重复。

## 前置与限定交付

W102真实PG配置Owner接受冻结http-bearer原CRM会话与签名权限投影，但现有Web BFF只签v1、丢弃浏览器Bearer/projection，旧编辑页使用不同合同。直接透传浏览器身份或假造权限不可采用。W101 Main实际签发入口要求原CP6.Web grant/at+jwt；现有CRM OIDC确实发行该audience，受保护会话在.NET服务端持有原accessToken。由可信API读取原保护会话、实际Site/Form绑定并向原受信Issuer请求权限签名，然后调用W102 Owner最终授权/共同事务；令牌及权限证据不泄漏给浏览器或由浏览器自报。

本轮实现该服务器链路与显式启用的Web BFF适配，保留原cookie/CSRF/同源/HMAC边界、原始字节和幂等键。配置Owner仍核对当前主体、精确pair、用途、签名信任和效期，所有网络调用在业务持锁事务之外，未成功提交不返回成功。Web端不修改任意browserBearer/projection为可信头，权限材料只由真实Main签发。对过期、拒绝、上游故障、重放和窜改定向验证。需要的跨仓互通夹具可在同一Main窗口实现，但必须如实区分实际生产者互通、本地TLS夹具与完整业务E2E，不能把历史结果重命名为新执行。

旧UI既缺冻结policy/notice/formKey等数据，也依赖旧workspace/options；本轮不以传输改动冒充完整页面采用。预览/激活/公开Attempt/提交、正式投递、常驻C2及跨仓即时撤权留后继。冻结合同与合法精度不缩窄，不推断企业默认政策/保留期，不从一次签名TTL宣称瞬时跨仓撤权。

## 任务、目录和时间

- W103-01 Main，DEV-1 `codex/w103-01`：台账/四状态/前项最终证据，必要实际签发互通验证支撑。
- W103-02 CRM，DEV-2官方bind后 `codex/w103-02`：原保护会话到真实签发的服务端适配、Web BFF安全转发、相关本地PG/HTTP/Node检查和单份交付报告。
- W103-HANDOFF：原任务分支一次START，推送保存/临时finish后复用DEV-2给CRM，再恢复同一分支记录交付。

预计实际90–150min：服务端签发与Owner链路40–65、BFF及必要验证30–55、集中审查及三仓收尾20–30。Main START `2026-10-24T07:23:09.807582+00:00`，最终按Main finish计，不重复累加CRM/Handoff。沿用现有七天持续目标 CP6-7D-20261022。

本机Node默认20不符本仓>=22.13<23，使用已安装v22.22.0逐进程调用，不切全局。Web AGENTS要求先读实际安装Next16.3.3的本地指南；依赖优先复用，必要时只安装当前workspace，固定目录保留有价值缓存。新SQL只PostgreSQL；相关局部验证+一次集中SPEC/QUALITY。正常PR/远端采用/准确分支删除/官方finish；无Actions、保护变更、既有环境迁移或生产部署。24/375、0/125、247正式NOT_RUN保持真实。

## W103实际交付与生命周期

CRM [PR130](https://github.com/GTX537/CP6.CRM/pull/130) 已采用 `0045bff214909b5282c975b583f79e8247beee2d`，来源 `2b9fb302f8619c24a441e87670973908df10951e`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/0045bff214909b5282c975b583f79e8247beee2d/docs/development/evidence/W103-02/DELIVERY.md)。

原受保护会话中的accessToken只留服务端；API用实际Site/Form绑定向原可信Main签发入口获取新权限证明，再进入现有Owner最终授权和共同事务。网络调用不占用业务持锁事务，原会话不续期，重放仍新取权限并重核当前授权。新内部配置适配入口仅接受原Cookie和inquiry-v2签名，拒绝客户端Bearer/权限投影；大小写及尾斜线变体同样受入口门禁约束。

Web BFF由服务端 `CRM_INQUIRY_NATIVE_ENABLED=true` 显式启用，默认旧行为不变。公开配置GET/POST映射到内部适配入口，保留原始body/幂等键，去除浏览器自报身份与证明。响应保持冻结成功状态、Problem、no-store/no-referrer边界；上游拒绝、重定向、异常及协议不符均受检查。

181项最终适用的唯一相关本地检查通过：27项新增BFF/原生PG HTTP、51项既有Owner/会话、86项OIDC/传输、15项Node BFF、2项实际Main与CRM互通；0跳过。 证据 `D:/CP6-local-test/wave103/foundation/final-validation.json`；Main实际登录、PKCE/OIDC、JWKS、在线身份、签发、Node BFF、CRM PG Owner的两进程链路在 `D:/CP6-local-test/wave103/interop-20261024T075814Z/result.json` 通过；保存/原键重放/读取后，Main撤销configure动作，再试被拒绝且不新增业务记录。Main测试支持仅增加可选隔离目录桥和测试宿主Origin，不改变生产代码。真实权限撤销结果已验证，但Main可信TLS终止由固定loopback测试传输模拟，CRM当前发布/账户为隔离夹具，不能据此声称完整页面、生产TLS或跨仓即时撤权验收。

保留最初夹具编译、数组断言、隔离目录前置以及尾斜线v1门禁失败记录；修复后仅复查受影响范围。相同输入的成功结果明确复用，提交/SHA变化不重复运行。自有Main PG55435、CRM PG55446及测试HTTP/Node已停止，证据和依赖缓存保留；桥接临时session文件已删除。Main55435在互通阶段按现有空闲端口/新目录守卫采用，本次补记实际占用，不冒称START时已预登记。

旧UI尚未采用冻结policy/notice/formKey合同；预览/激活、公开Attempt/提交、正式outbox投递、常驻C2及跨仓即时撤权仍为后继。outbox仍Blocked，无生产迁移或部署。正式24/375、0/125、247NOT_RUN不提升；七天ACTIVE。一次COORD集中审查，无Actions、保护或工作流触发变更。
Handoff [PR79](https://github.com/GTX537/CP6.AI-Handoff/pull/79) 已采用 `c1978b0a92fd81164751c6c5791e20a4c946ca06`，准确分支删除、固定席位finish释放。目录连接清理曾被自动审批拒绝；用户本机仅移除连接后，已核对目标缓存完整，官方bind/resume成功。 Main本次正常PR/finish继续；当前Main START后墙钟62.05min，原估90–150min；包含审批等待，最终单列，不叠加CRM/Handoff。
