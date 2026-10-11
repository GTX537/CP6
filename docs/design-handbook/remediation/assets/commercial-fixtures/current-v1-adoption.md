# W5 商业夹具 current-v1 设计修订

版本：CP6-DESIGN-REVISION-20261011/R1；profile：`erp-credit/current-v1-design-20261011-r1`。
这是本次任务新增设计补充及有界派生配方。历史接受、原归档字节和原执行资格不变；actual adoption = UNPROVEN，implementation = NOT_IMPLEMENTED，runtime = NOT_RUN。

## 采用与版本边界

消费者必须同时固定原文件 SHA、有界 patch 文件 SHA、派生文件 SHA/bytes、当前 TYPE SHA 与 profile。生成器只在内存派生完整文件，公开仓库仅保存有界补丁。原件 hash 或 expected_count/JSON preimage 不同立即停止，不能模糊匹配或跳过操作。禁止用派生夹具覆盖归档、历史 EVIDENCE、FINAL_LOCAL_RESTORE_INDEX 或已签发事实。

采用方显式选择 frozen-R8 或 current-v1 独立包/namespace。不得按payload外观猜版本，也不得自动降级到另一版本。current-v1 的 TYPE 精确字节 SHA 为 `564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda`；具体根core/fixtures依赖及其SHA见 derivation-manifest.json。任何依赖变化须重算、重新审查本profile，旧pin不能沿用。TYPE文件名在隔离包内保留，以解析原相对引用；C# namespace为 `Cp6.ErpCredit.CurrentV1.Generated`，TS为 `ErpCreditCurrentV1`。准确WMS-RMA/FIN-AR原schema仍锁原SHA。现有method/path/header/权限字段保持原契约，不新增版本HTTP路由。

旧保存响应按原profile和原字节回放。迁移适配器只能在首次新命令/新查询的生产边界生成current-v1；不得改写旧command记录、Owner原始raw、receipt、digest或过去的接受结论。Client先部署识别profile及当前Problem三元组，Server再按显式部署配置启用新producer；未具备某Owner完整qualification的分支继续原fence，不能由文档fixture代替。发布、DB、真实Owner及Store采用属于后续独立实施/验证。

## RETURN：四个叶节点及完整依赖闭包

`12.G6R07#/quantity` 10→6，`12.L2R07#/quantity` 10→2，`12.EP#/11/payload/quantity` 10→4，`12.EP#/25/payload/quantity` 10→6；数值均保留8位小数字符串。父Part、原range、Manifest input/baseQuantity不改。

读取12.E的189条及12.EP的100条，精确解析fullBodyLocation JSON pointer，只移除显式minus字段。再加入D04/D06/G6R09/L2R09的lineBusinessDigest、permitBusinessDigest和D14/D16/G6R12/L2R12的stockPlan摘要。按digest去重后298节点。每个原像必须先与原摘要一致；若重复digest对应不同body或有依赖环立即拒绝。H为sorted-key compact UTF8 JSON的SHA256，无尾LF。

每条完整string值若等于已注册digest即构成依赖边。按子节点优先重算：四叶quantity→Allowance→Manifest/line→authorization/permit/stockPlan→Owner原像及使用者引用。明确业务hash原像见生成器return_nodes；line只有空claimSpends，本次不推导未出现的claim格式。127个摘要变化，171个不变；每次全串替换都带原出现次数，且历史附录没有受影响摘要。manifest保留298节点的旧/新摘要、来源位置和全部边。不得只改四个量而遗留父摘要。

## EST：当前政策与600秒准备窗口

R1Q10.quoteReadiness.policyRevision改为DEMO-QTN-POL-2；同proof/head的当前撤销观察由R1Q09承接，lastProven仍是DEMO-QTN-POL-1。8个Lookup的本版本DEMO发出时刻选各LookupResult.asOf；2个Seal缺原发出时刻，本次显式制定20:00:30.500Z和20:00:56.500Z，落于已列Run创建和Version创建之间。这10项都是修订夹具时钟见证，不能宣称原Owner实际发出证据。逐项contextId/record pointer/issuedAt/expiresAt见manifest。

expiresAt = issuedAt + 600秒，严格now < expiresAt才能首次Apply。Server实际实施须保存可信context创建时刻及全scope/actor/draft绑定；不在原闭合wire增加issuedAt。E71的payloadJson仅改ExpiresAt，以SHA256(domain + LF + sorted compact payloadJson)重算，再同步Candidates/Bind或SealPreview/SealApply/reference。Reference与Legacy上下文保持不变。过期首次写为原过期错误和零业务写；已知同command终态恢复原结果而不续期。时钟见证不是生产验收。

## CREDIT：Money正文与Store依赖

只修改四个公开正文中18个schema明确引用WMS Money的位置；native Finance、COGS、Inventory数值域不做泛化amount替换。Money必须精确{value,currency}，保原decimal字符串和货币，net+tax=gross。四正文sorted compact UTF8/no-LF，接着更新MULTI lookup-binding正文自身hash；五个正文的准确旧/新bytes/hash见manifest。

依赖顺序为public body→lookup-binding body→CURRENT02 body/rawRef8和Store7→CURRENT04/05→CURRENT12/13。rawRef8及Store7的digest/byteLength/handle一起更新；Store7七字段canonical序列化后再算receiptCanonicalUtf8/Bytes/Sha256。CURRENT12独立existing-target body的324处typed source同时修订。修改只落在列明bodyKey和JSON pointer，历史索引/证据均不动。

这些receiptId只是当前DEMO配方中的符号，必须与本版本命名空间一起解释；不得用新hash冒充旧Owner签发receipt或更新不可变Store。actualStoreReceipt/actualRuntimeStoreReceipt保持null，actualNewMetadataWrites保持0。实际采用时以真实Store返回七字段与原scope生成身份、验完整raw、SHA、长度和同scope handle；未获得准确issuer/permission/codec/source/Store证明仍REQUIRED_FENCED。禁止把fixture canonical验算升级为实际Store成功。

## CREDIT：OpenAPI、回放与codec

原closed JSON根后10个YAML扩展块有界并入同一根；第二个x-stage93按其stage94 currentBindingKey命名x-stage94，所有值不改。结果可用JSON parser直接作为OpenAPI生成输入；29条method/path和192个本地/TYPE引用均解析。C01/C02首次HTTP201保存COMMITTED完整body；同command相同请求HTTP200逐字节返回该body，绝不换成COMMITTED_REPLAY，亦不重新渲染/脱敏。不同请求碰同key仍遵从原幂等冲突。单一输入只消除载体歧义，不创建任何Cash或Finance新写路由。

Current-v1 native COMPLETED wire判定：APPLIED必须非空resultRef；REJECTED必须null；NO_CHANGE允许null或准确Ref。FOUND接受APPLIED或有Ref的NO_CHANGE，ownerResultRef须结构相等，并且producer另行证明原native BusinessResult及完整current AR/GL/member/finite来源。codec parse成功只证明wire，不授予FOUND。来源未全，原COMPLETED保持UNKNOWN，保原raw及command/query身份、已有PREPARED/POST_READY和所有占用。

FAILED_NO_EFFECT只允许REJECTED/NO_CHANGE且null Ref，并验证exact base64解码、无重复JSON键、SHA及结构等价；此外producer仍须完整可证明的无效果/无未来效果来源。HTTP成功、超时、NOT_OBSERVED或缺源不构成此证明。UNKNOWN恢复只查询同原command/intent，不能release、重复dispatch或替换经济分支。FROZEN-R8旧pin与判定保留原包，current-v1不覆盖它。

## 根补丁复用与11项验收

PR两项、九Problem别名、NEW_READY/旧父键、WN11 projection和MULTI before/after由`../fixtures/bounded-patches.json`提供本次同版本补丁。本目录workpart直接引用它们，并在构建时验证原SHA和可应用preimage；不复制第二份补丁。新的9个Problem三元组顶层code=issues[].code，type为小写短横code URN，原HTTP/status/fence及FE恢复语义不变。冻结旧body继续原样回放。

NEW_READY三CAS绑定同阶段完整保护集；旧78/79关闭只引用OLD_END旧plan，不使用后续43 OUTPUT。WN11-04操作31生产partition_projection并先于23提供其实际receipt，28仍生产C06响应。MULTI-02两个ledger的业务revision/head_version/projection_revision取保护值+1，更新时刻取同Uow事件；opaque row_version仅取provider OUTPUT，四CAS原子提交，未知结果查询原intent。

acceptance.json逐项列正向与负向场景。validation.json只报告本次本地数据派生、摘要及源/结构检查；C#/TS独立编译证据另见codec-compile-validation.json，仅在最终派生SHA相同时复用，首轮失败保留。HTTP/业务/DB/Owner集成均NOT_RUN。设计决定完整，不意味着历史已接受或实现获准。
