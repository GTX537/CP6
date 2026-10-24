# W102 PostgreSQL询盘配置实际Owner与宿主采用

COORD本聊天单人；七天持续目标ACTIVE。Main `0f6622aaf2ac8aac94d147d400f34ddc0aef0c45`、CRM `dc09f558d6070a807c59316adce50b35641d8ba6`、Handoff `b997999715477656dfa94fb36e4a7b5e744f8a02`；AGENTS/POLICY复核，官方status/capacity通过。W101 Main513/CRM128/Handoff77已完整合并finish，准确分支清理、三个席位释放；152项相关验证通过，实际24.18min加前置约5min。本轮登记最终证据，不重复W101或此前轮次。

## 直接前置与实施范围

采用W99受保护原会话、在线身份核对和PG持有式身份fence，W100精确角色Site/Form来源，W101真实Main权限签发，W87持久信任及已完成配置原结果、日历/内容/Bundle、审计和outbox组件。权限证据本身CanAuthorize=false；实际Owner须按冻结矩阵核动作、当前精确pair、资源身份和时效，在同一事务最后再核。签名TTL不等于跨仓瞬时撤权；未消费C2的传播窗口仍需明确。

本轮实现真实配置读/保存Owner与可显式启用的PostgreSQL宿主路径：受保护身份、严格冻结输入、精确资源选择、当前授权、版本/ETag、稳定原请求槽、实际配置内容和不可变Bundle、审计、正式outbox一起提交；外层提交前不披露成功。重试按原键处理，失败或未知提交不伪造成功。先注册可信Site/Form与宿主政策，不能从浏览器自报tenant/actor/资源ID、测试自签密钥或隐含保留期获得授权。默认关闭；安装与来源/信任登记显式执行，不能自动迁移既有环境。

前置检查已发现实际API宿主仍是SQL Server注册，且旧Domain日历DateOnly/DateTimeOffset不能无损容纳冻结配置的全部合法日期/精度。本轮必须在采用处解决或明确保存缺口；不得直接转CLR类型缩窄合同，也不能仅添加注册包装并宣称业务完成。沿用既有可信OIDC/DP会话、BFF传输及CSRF边界；公开API安全约定与实际Cookie/Bearer适配逐项核实。预览、激活、公开Attempt/提交、隐私/线索PII和生产验收仍按后继直接范围处理，不因Draft保存提前声明通过。

## 任务与实际时间估计

- W102-01 Main，DEV-1 codex/w102-01：本窗口台账、总进度与四状态统一同步，前项最终收尾证据。
- W102-02 CRM，DEV-2官方bind后codex/w102-02：实际Owner/必要无损存储/宿主采用、相关本地PG和HTTP验证与一份交付报告。
- W102-HANDOFF，原分支一次START和最终交付；临时保存释放后复用DEV-2，最终正常合并清理。

预计实际120–180min：Owner/必要采用70–110、定向验证与修正30–45、集中审查和三仓收尾20–25。Main START `2026-10-24T06:09:02.126964+00:00`，按最终finish记录实际墙钟，CRM/Handoff不重复相加。持续目标沿用CP6-7D-20261022。

重点验证真实HTTP→PG成功/重放、不同payload同键冲突、租户/主体/purpose/动作/精确pair拒绝、锁等待后撤权或到期、CAS冲突、失败全回滚、未知提交原结果恢复、签名信任撤销、并发唯一版本及合法日期/精度不丢失。对已有成功证据按输入适用性复用，只做受影响验证和一次集中SPEC/QUALITY。

正常PR/远端采用/准确分支处置/manage finish；固定席位保留复用，PostgreSQL唯一目标，不启动Actions、不修改保护、不执行既有数据库迁移或生产部署。完整任务状态24/375、目标0/125和247正式NOT_RUN保持真实，不以本地夹具升级正式验收。

## W102实际交付与收尾

CRM [PR129](https://github.com/GTX537/CP6.CRM/pull/129) 已采用 `d3615db81675ca6cdad2d9c777505d451a552931`，来源 `dff7318f0ec76662c5566e4e013b2ce14a24531f`；[单份交付](https://github.com/GTX537/CP6.CRM/blob/d3615db81675ca6cdad2d9c777505d451a552931/docs/development/evidence/W102-02/DELIVERY.md)。配置GET/保存真实Owner和显式原生PG宿主已实现：从原受保护会话与实际在线身份取得主体，核对当前精确Site/Form、动作、用途和持久签名信任；原结果、日历、无损内容、Bundle、审计、正式outbox同事务。最终重核和deferred完整性约束阻止过期或部分提交，HTTP成功只在commit后返回，原会话不续期。完整Gregorian日期、Unicode字符数、高精度效期及大版本不丢失，源PublicForm版本不因Draft保存递增。

显式 `crm_inquiry_configuration_v2` 解决旧候选FormRevision与冻结Draft语义不兼容，旧资产不迁移；W68原结果复用实际七锁身份事务，未放宽锁合同。BFF inquiry-v2绑定Bearer原会话与权限投影，默认关闭；可信源/主要资源/HMAC/保留期均须明确登记，没有从浏览器输入获得权威。outbox仅原子入箱，Blocked路由尚无dispatcher。

347项唯一相关本地检查通过：30项配置Owner/真实HTTP与PG，166项既有PG身份/原结果/日历/绑定，151项相关单元；0跳过，失败日志保留。 日志 `D:/CP6-local-test/wave102/foundation/`。包括真正并发/回滚/commit约束故障、提交后HTTP回执丢失重放、锁等待后过期、撤权/撤销后拒绝原结果、冻结负例及合法边界。旧schema独立检查改用新自有空库，53项完整通过；未放宽FK/不可变保护。W100原始事件输入重用；W101实际Main签发互通证据独立引用，本轮HTTP用明确本地TLS/OIDC/签名夹具，不能称完整Main→CRM业务E2E。数据库网络COMMIT回执丢失未新注入；异常不返回成功，重试仍用原键。

三个自有PG实例均核对身份smart stop，HTTP进程随测试退出，数据/日志保留；一次COORD集中审查，无Actions/保护变更/既有环境迁移/生产部署。Web BFF/UI采用、预览/激活、公开Attempt/提交、常驻C2消费、投递和跨仓即时撤权继续按直接前置推进；签名TTL和本地发布不冒充Main即时撤权。正式24/375、0/125、247NOT_RUN不提升；七天ACTIVE。
Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/78) 已采用 `087ab22e279520076f78eb474480bd0a82556bab`，准确分支删除、固定席位干净detached并finish释放。Main本次正常PR/finish继续；当前实际63.70min，原估120–180min；最终按Main START到finish计，不叠加CRM/Handoff。
