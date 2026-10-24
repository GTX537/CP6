# W89 / 七天一期D5：CRM同事务当前身份与会话围栏

COORD本聊天单人，2026-10-24T00:21:10.533343+00:00。Main `e86d5d3f34080c5f3694ed8f74e013cf8258210e`，CRM `1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f`，Handoff `d386156a175ccc0cdb70c5132a365cfc7a9d8343`。W88 Main482/483、Handoff64全部采用，Issue481关闭；40独立本地用例通过，实际26.33min＋前置3min，准确任务分支删除、固定树干净detached、全部席位释放。W37–W88不重复。

## 前置和限定交付

AGENTS/POLICY哈希未变，CRM当前AGENTS已全文核对；官方status/capacity无告警，D盘206.76GB/C盘104.15GB可用，未获allowlist历史目录与根用户文件保留。WP02-09前窗W87持久信任已完成；W51/W54/W55/W56/W57/W58已有会话、共享7键锁、原生身份接收/权威发布及在线身份检查，W87有同事务签名信任。当前InspectCurrentIdentityAsync和CrmCurrentIdentityReaderV1仍为detached检查，不能当业务事务已持有的身份授权。CRM-INQ-01§7要求锁内当前权限，实际Site/Form签名生产者及业务Owner仍需后继接线。

本窗补可独立交付的原生同事务身份检查接缝：保持既有detached行为，新增调用方拥有同连接/Serializable事务的持有式检查与最终重核。复用CRM.Identity.SharedLocks.pg-v1既有7键顺序，核现有注册与会话/发布源；租户fence、Ingress State、当前Publication及实际session行均按已有写入顺序保护。尤其验证等待写者后的旧Serializable快照不能返回旧授权（应失败重读，不自动重试）；不因使用advisory lock就宣称获得新快照。

输入来自可信宿主已完成的在线核验与受保护会话路径，固定组织/主体/issuer/jti/认证epoch/原会话身份，不接受浏览器自报权限。实际session hash/incarnation及合法有效期在锁内核对，logout、会话替换、token撤销、禁用/漂移/发布撤回或过期都不能复用旧结果。输出不含token、cookie、受保护payload和业务授权；证据绑定实际连接/事务/注册/发布/会话，事务结束或换事务/连接即失效。最终核验使用数据库当前时钟，并检查同事务内状态变化。

必要新受限SQL函数只提供持锁/核查，不隐式新建/续期会话、提升权限、恢复身份、commit或重试；固定search_path与schema限定，PUBLIC权限撤销，运行身份只显式SELECT/EXECUTE所需能力，不为SELECT FOR UPDATE/SHARE授予任意表UPDATE。安装显式调用，新namespace发现漂移fail closed；不自动安装既有环境。和W87信任组合时遵守身份围栏→信任head→业务锁，不在持锁区间进行在线网络I/O。

CanAuthorize=false保持。此接缝仍不等于真实crm-site动作/Site/Form/字段范围、Main签名生产者或完整配置保存/审计/Outbox Owner；本窗不自动注册HTTP、修改生产宿主、替代旧业务授权或把新库API当生产采用。后继Owner须使用真实受保护会话证明及自身业务最终门。

## 时间、验证与收尾

预计实际40–60min：当前身份/会话原生持锁20–30，受限角色与当前时钟/绑定10–15，相关PG验证/集中审查/三仓收尾10–15；独立只读前置约4min，W88收尾重叠读取不重复计。登记start `2026-10-24T00:18:59.541973+00:00`，七天目标ACTIVE。

自有PG复用空闲55446：实际会话/身份和撤销、两个方向锁争用、等待后的旧快照40001/重读拒绝、同事务最终变化、过期/取消、跨tenant/subject/连接/事务、未持有锁和受限角色无UPDATE、外层回滚/结束不可复用及安装漂移。相关W58检查、W51会话与W87组合只按真实影响验证，已有未变证据引用；一次完整作者SPEC/QUALITY，无全仓回归或Actions。

DEV-1 Main协调；DEV-2 Handoff START保存/finish deferred后bind CRM，CRM合并/finish后bind回原Handoff任务。正常PR/远端采用/准确任务分支删除/finish释放，固定目录复用，单人无代理。24/375整卡、125目标0完整交付、247运行NOT_RUN保持。W51指定停止data删除曾由自动审批blocked by policy拒绝，保留不重试。

## 工作区准入补记（2026-10-24T00:30:06.851698+00:00）

原组合命令含 Handoff 暂缓收尾、DEV-2 跨仓 bind 和 CRM start，被自动审批在执行前拒绝，唯一返回原因 `blocked by policy`，没有更具体解释。没有重试被拒组合，没有改用原生 Git、其他席位或复制目录绕过。CRM W89 分支/席位尚未创建，产品代码和数据库未改，START 是协调准备，不能视为 CRM 实现已经开始。

经检查官方 manage.py，`finish --outcome deferred` 仅核实已保存来源、保留分支、切到已确认目标并释放占用；其路径不删除分支或目录。缩小为这一保存动作后执行成功：Handoff `codex/w89-handoff` 精确来源 `e389658b10116fc1aab6f34a63b083f7d5dfe90b` 保留在本地/远端，DEV-2 于 `2026-10-24T00:29:05.759962+00:00` 释放，固定目录仍属 Handoff、干净 detached；尚未执行 bind。

已在本聊天请求针对固定目录 `D:/CP6-worktrees/dev-2` 重绑到 CRM 的明确确认，等待答复期间只推进 Main 协调文档与只读设计。许可不能代替工具实际放行；恢复时仍须官方 status/bind/start 核实来源、干净目录、进程、保存证明及 CRM 最新 main。不得操作历史 W51 被拒删除的数据目录。七天目标仍 ACTIVE，本窗口为 AWAITING_WORKSPACE_AUTHORIZATION；全卡/目标/正式验收数不变。等待时间与后续实现时间分别记账。

## 2026-10-24：W89明确授权后恢复

用户已明确回复“授权重绑并继续 W89”。官方DEV-2 Handoff→CRM bind成功，固定路径不变，无新增树；Main原`codex/w89-coord`resume后快进到`b5d339628ba14d4f850004c002ae8ba937185e75`，CRM新`codex/w89-01`从已确认`1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f`于2026-10-24T03:31:51.844982+00:00真实start。原Handoff START与保存分支继续使用，不重复派工/ACK。

W96 Main504/505、Handoff71已全部采用，实际14.82min＋独立前置6min，准确分支删除、三个固定席位释放，29相关本地检查及类型通过。Issue503移动所有者失败和新MOVE提交保持未完成。W89原窗口剩余预计40–60min，暂缓等待区间不计实现时间；继续原同事务身份/会话接缝，CanAuthorize=false，真实Site/Form动作与业务Owner仍待采用。七天ACTIVE。

## 同事务组合诊断后继

W89-01 42 项相关本地检查通过。实际原生组合诊断发现既有 W87 签名回执在 Npgsql 复用事务对象后仍返回 true，已登记 CRM Issue #123；它不授业务权限，但违反同事务证据合同。遵循一任务一分支，先交付 W89-01，再在同一固定 DEV-2 从最新 main official start `BUG-123` / `codex/bug-123`，预计实际额外 5–10min。其共享信任资源由本 COORD 串行领取；修复、原步骤回归、远端采用及 finish 后再统一 W89 Handoff/Main 收尾。无新增席位/代理/Actions，完整业务验收不提升。

## W89实际交付与独立修复

用户已明确授权“授权重绑并继续 W89”，官方 bind 成功，沿用原 START `e389658b10116fc1aab6f34a63b083f7d5dfe90b` 和原协调/Handoff 分支，不重复 W37–W88、W90–W96。

CRM [PR124](https://github.com/GTX537/CP6.CRM/pull/124)：source `41ded0b42bebcd0b4a27b966bfbc6ea294664e1a`→main `5c0cbcd28e90671c87a9eed473611c5910dbf154`，13任务路径 exact blob 核对；随后独立 [PR125](https://github.com/GTX537/CP6.CRM/pull/125)：source `79dcb76bef6c31877976405015d74bafe7a3b35f`→最新main `59845f28539d89582884db442e25e95cd29ce328`，8路径 exact blob 核对。CRM Issue #123 已在原复现回归及远端采用后关闭。[W89单份产品报告](https://github.com/GTX537/CP6.CRM/blob/59845f28539d89582884db442e25e95cd29ce328/docs/development/evidence/W89-01/DELIVERY.md)；[BUG-123单份报告](https://github.com/GTX537/CP6.CRM/blob/59845f28539d89582884db442e25e95cd29ce328/docs/development/evidence/BUG-123/DELIVERY.md)。

当前身份/原会话检查绑定调用方 Serializable 事务，七键→身份fence→Ingress State→当前Publication→实际session行；等待后的旧MVCC快照报40001，不自动重试。输入冻结、session hash/incarnation/revision与token/epoch匹配，当前原始身份与撤销、数据库新时钟、同事务变化、丢锁及结束后复用拒绝。受限函数不授予运行角色任意UPDATE，显式安装/漂移拒绝，无既有库迁移。

实测发现Npgsql会复用事务对象，W89身份回执和独立BUG-123签名回执均已绑定PostgreSQL实际事务ID；重新取得锁也不能复活原事务回执。W87活跃事务验签与W89组合通过。W89 42项相关本地通过；BUG-123 18项相关PG通过，复用W89未变41项后去重59项；两次真实失败均保留，正式业务NOT_RUN，无Actions。API及必要依赖编译通过；各任务一次集中SPEC/QUALITY。两个自有PG55446已smart stop，数据/日志/TRX保留。

CRM两个准确任务分支均已删除，工作区干净detached、官方finish释放后再由官方bind回Handoff；没有新增物理树/代理/聊天。CanAuthorize=false，FIELD_POLICY与BUSINESS门保持关闭，真实crm-site/Site/Form/字段权限生产者、实际业务Owner与宿主接线仍需后继。24/375整卡、125目标0完整、247正式运行NOT_RUN不提升；七天目标ACTIVE。下一独立优先Main库存MOVE所有者属性Issue503，再推进实际Owner采用。没有Actions、生产部署或历史W51被拒数据删除。

Handoff [PR72](https://github.com/GTX537/CP6.AI-Handoff/pull/72)采用 `99389e2adbba3a7cd31d8d0d92405e1711655bdf`，准确分支删除、固定树干净detached、finish释放。Main仍需本次协调PR/finish，不提前声明席位释放。原准备21.33min＋恢复执行至此20.36min；暂缓等待与W90–W96排除，子任务不叠加；原剩余估计40–60min＋BUG-123预计5–10min，最终按finish记录。
