# W61 / 七天一期 D6：原配置草稿冻结与实际PG日历绑定

COORD本聊天单人，2026-10-23T09:47:10.348218+00:00。Main `3e9fcede0380952d844822af4681ea8b35ebf673`，CRM `8be1a856b50aaa68a733b8ca47df1ecd25fbc8a9`，Handoff `d1419def25aad4939e1418956462729364163ff8`。W60 CRM105/Handoff36/Main429全采用，92项相关本地用例通过，实际登记36.31min，准确任务分支删除、固定树干净detached、全部席位释放；Actions均0。W37–W60不重复，持续目标ACTIVE。

## 原合同与准入范围

最新AGENTS/POLICY内容哈希未变；已核三仓最新目标、实时status/capacity和W61重复PR，席位可复用。WP03-01完整前置WP02-09仍只有局部基础，不能启动真实Owner写入/公开表单。因此继续独立配置准备组件：按已采用原ConfigurationDraft严格schema冻结完整原始JSON与精确wire值，再在调用方PG连接/事务内读取W60不可变日历并核绑定。

本窗产物是可交给后继Owner的不可变配置值与日历匹配证据；不写FormBundle表、不分配版本/命令槽、不发preview/attempt，也不宣称发布审批。原JSON保留完整policy、UTC日期/任意合法小数精度、字符串及数组，不用CLR有界时间/数字改写合法输入。输入摘要明确是原字节SHA256，不冒充原合同canonical contentHash/renderedHash、签名或法定政策。

严格核已接受的隐私告知SHA256语义、原schema/重复未知字段；租户由可信宿主传入，不以浏览器tenant自证。实际calendar通过W60同连接/事务Read取得，不接收请求自造verified日历；核原SourceVersionAlias、IANA时区、星期0=Sunday映射、完整工作时段/节假日集合，并分别保留两个生效时刻。原日历codec为ISO1–7存储，比较从其真实Domain解码，不把星期数照抄。不同顺序但相同集合可匹配，不截断子分钟日历去冒充分钟级配置。配置生效区间与日历版本生效时间是独立轴，原合同没有恒等条件；分别保留原ExactJsonTimestamp及实际日历UTC ticks，不强制相等、不舍入额外小数位，当前区间选择与真实host能力由后继Owner核实。

配置完整字段、原生成器/冻结输入与现有业务路径不改。已匹配结果防调用方修改影响内部事实，不能作为权限/发布票据；真实Owner仍须当前权限、字段Owner映射、法定政策、命令槽、audit/outbox及最终提交门。新业务计算仍使用原时区规则一致性检查；冻结绑定不替换实际host/tzdb验收。

## 实际执行估计与验证

预计35–55min：15–20min实现原输入冻结及PG日历绑定；10–15min实际PG正反例和相关合同/日历回归；10–20min一次作者SPEC/QUALITY及三仓收尾。Main登记start至finish，不重复累计子任务；七天目标继续。

验证未知/重复字段、告知摘要篡改、非法Unicode、原合法长数字/时间精度保留、集合复制、跨租户/错ID、时区/来源版本/时段/假日/生效错绑、Sunday映射及旧快照事实。复用W60未变数据库写入/守卫证据，只为新增实际读取绑定启动独有隔离PG。无全仓重复测试、Actions、生产信任配置、既有库迁移或部署。

DEV-1登记/四状态；DEV-2先Handoff START保存并finish deferred，再bind CRM实施/合并/分支清理/finish，返回Handoff交付后Main收尾。固定2+1席位复用，本聊天单人。整卡24/375、125目标0完整交付、247 NOT_RUN保持。W51准确data删除的自动审批拒绝（blocked by policy）继续停止保留，不重试/绕过。

## W61实际交付

CRM [PR106](https://github.com/GTX537/CP6.CRM/pull/106)已采用，source `7f7b168864b6b07bb5181e0a0987f39cebb7053a`，远端main `42a40ed606c5f457b89fed5feec01274b9799158`，8任务路径exact blob核对通过。[单份产品报告](https://github.com/GTX537/CP6.CRM/blob/42a40ed606c5f457b89fed5feec01274b9799158/docs/development/evidence/W61-01/DELIVERY.md)。19项去重相关行为通过（7项局部最终、12项未受影响的本轮成功）；真实Owner/公开表单验收NOT_RUN，无Actions。

新增原ConfigurationDraft不可变原字节冻结与告知摘要核查，原严格schema/decoder、完整policy、合法精确时间/数字保留。SourceSha256只是原字节摘要，不冒充canonical contentHash/renderedHash、签名或审批。每次Read返回独立值，结果CanAuthorize/CanActivate均false。

实际PG同连接/事务读取W60日历，核tenant/ID、原来源版本、时区、完整时段/节假日；正确处理Sunday与ISO日历映射，不截断子分钟。配置和日历生效时间是独立轴，分别保留，不强制恒等、不执行当前区间选择。原START对此的初稿已按原合同纠正。

没有新增表/修改SQL、写Bundle、分配版本/命令槽、发preview/attempt或接Program。一次作者完整SPEC/QUALITY通过；本轮19项去重行为有效，初始桩红灯和异常类型预期失败保留，修正后局部7项通过，其余12项复用原成功。W60/W59未变原存储/日历/合同证据仅引用。独有PG正常停止，data/log保留。CRM准确本地/远端分支删除、固定树干净detached、finish释放。

七天目标ACTIVE，继续真实Owner必要前置和D7集成差距核对。完整24/375整卡、125目标0完整交付、247 NOT_RUN不提升。无Actions、既有库迁移或部署。W51准确data删除此前被自动审批拒绝，原因仅“blocked by policy”；停止保留，无重试绕过。

Handoff [PR37](https://github.com/GTX537/CP6.AI-Handoff/pull/37)已采用 `b83442c4c421211c246266a4805db839d0448865`，准确本地/远端分支删除、固定树干净detached、finish释放。Main仅剩协调PR/finish，不提前声明释放。登记开工至此约23.24min，原预算35–55min，最终实际以finish为准，不重复累计子任务；此前只读前置约6min。
