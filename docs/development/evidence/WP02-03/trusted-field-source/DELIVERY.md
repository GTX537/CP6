# W31-01 字段权限可信 PostgreSQL 来源：单份交付

2026-10-20，COORD当前聊天独立，codex/w31-01；基线439c164084f8aa911645fbe56b46810f745d64dc。[计划](../../../dispatch/20261020-wave31/README.md)。限定范围本地可合并：Yes。没有新增HTTP/wire或包合同、数据库迁移、前端页面；完整IAM和业务采用不提升。

## 实现和消费

CurrentPermissionContext新增内部GetFieldSourceAsync入口，与原GetAsync共享同一主体绑定检查：只认一个已认证identity，拒绝待完成token_use、他身份拼接、缺失/冲突subject或tenant。旧GetAsync保持原异步失败和聚合流程；旧FieldPermService无配置no-op、掩码及原值恢复语义未改变。

CurrentFieldPermissionSourceReaderV1只使用PostgreSQL，从当前可信主体读取。全部值参数化、表限定于EF模型所属schema，内部版本表固定限定cp6_authentication_v1。同一条SQL/statement snapshot读取tenant/user启用、锁定/到期、主/附有效角色、user/role持久revision、租户认证政策revision和本资源全部实际字段行。主/附角色去重且保留完整Int32，停用/不存在/他租户角色不参与；未注册字段及非法Access不在SQL中被过滤成“无存行默认1”。

目录必须来自已封存完整快照，资源须准确存在。原行ID、角色revision、资源/字段原文和显式1均保留；来源先经原MainStoredFieldMinThenMaterializeV1结构校验，失败抛CurrentPermissionUnavailableException，绝不返回空字典补默认。集合复制并只读，领域字段定义和来源版本分别计算。SourcePermissionVersion包含现有ordinary SQL writer维护的持久revision及实际来源，字段/角色/用户隶属ABA会变版，不只依赖值哈希，也不使用xmin冒充持久授权epoch。

FieldPermissionSourceV1.Evaluate把可信来源绑定原计算器；消费者提供其预期policy/registry/mapping/source版本、已审批Owner映射和各用途限制。版本不匹配返回无效决定；缺映射/独立限制不能允许。reader不制造OwnerApproved，测试中的批准与action/object允许均为明确夹具；不得将测试映射发布到真实业务。IsAvailable仅为来源结构可用，实际仍查逐字段/用途Allowed。

读取参加原调用事务，不改变隔离、自动重试或提交。RepeatableRead按原事务看旧快照，测试明确证明；该来源既不是在线凭据，也不是业务效果提交前的当前授权门。Owner须在真正消费/提交时用其当前政策/锁和已验收映射；不能将本对象发浏览器再接回作权威。内部入口与计算器已接通，具体业务Controller/Owner尚未接线，完整字段映射签认、导出/下载最终门、CRM数据库持久采用仍未完成。

## 实际验证与复用

证据根D:/CP6-local-test/wave31/foundation。**50项不同相关.NET/原生PostgreSQL测试通过，最终0失败/0跳过**；重叠执行不重复计数。

|执行|结果|
|---|---|
|source-red-20261020T094328Z|实现桩始终拒绝时5项中4失败/1通过：3个来源正向行为失败，另1个测试夹具HttpContextAccessor在异步Seed返回后丢失HttpContext导致NullReference。保留原记录；修正夹具持有DefaultHttpContext并在调用处设Accessor。这个夹具错误不算产品缺陷复现。|
|source-native-20261020T094519Z|47通过：5个新原生PG场景、原字段算法和当前主体/tenant绑定相关测试。|
|source-final-20261020T094730Z|8通过：最终7个原生PG来源场景+非PG/旧聚合禁止降级边界。仅追加边界测试，产品输入未再改变。|

PG夹具实际建本轮专用数据库并运行现有正式迁移；使用真实writer触发器和持久revision。覆盖：主/附0和int.MinValue角色、他租户同号角色、停用/缺失角色、原始3和显式1后MIN、完整域缺项默认；字段和隶属ABA后旧expected版本拒绝；未封存/未知域/非法Access/未知字段/缺role revision拒绝；tenant/subject伪绑定、pending、多identity、锁定/停用/过期；只一条数据语句、读取中真实另连接撤权后仍是完整旧快照，下次读取见撤权；RepeatableRead不擅自提交；读故障及显式/RequestAborted取消不回退旧allow；来源不可变、无映射不能读写。

原W30目录封存/宿主接线、W29 writer门/迁移、W27认证链和前端恢复未改，按原报告复用，不称重新跑浏览器/HTTP或真实DEV。只编译相关项目依赖，不全仓、SQLServer、远程测试。无新编译警告；既有无关警告保留。没有删除/跳过测试或伪造远程检查。

## 集中作者SPEC/QUALITY与资源

已一次检查完整产品/测试差异和接受合同：可信绑定共用且旧行为保持、参数化/schema、同语句源/版本、有效角色/完整Int32、实际行先MIN、封存域/版本拒绝、ABA与不可变、事务所有权及映射/fence边界、失败/取消和准确验证范围。未发现本窗阻断，这是作者审查，不冒称独立审查。未新增业务Issue：本窗补既有接受合同的缺项，原实现桩不属于线上缺陷。

自有PG55435随夹具停止；未启动Vite或业务API。三处本轮可再生PG data均核ownership、停止证据、进程、绝对路径和reparse后删除；原日志/TRX/失败/ownership保留。逻辑回收341042361字节（0.318GiB）；回收时段D盘空闲实际增加342904832字节（0.319GiB）。不处理历史目录、既有数据库、Docker或用户资料，不搬目录伪称节省。

原DEV-1/DEV-2复用，无新工作树/代理/clone/整仓副本；候选时仍占任务席位，正常PR并核对远端后才manage finish定向删除任务分支、固定目录detached释放。最终结果随后记入同份报告。无Actions/触发配置变更、既有环境迁移、真实tenant审批或生产；125目标/247真实运行维持原状态。
