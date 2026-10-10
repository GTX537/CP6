# SYS-LANG-01 · 字典与多语言

整理状态：`core_semantics_consolidated`。本册为已接受静态设计的开发展开，候选接口/字段明确标识；不等于现有代码已采用。

## 1. 业务目的、操作者与边界

字典管理员维护稳定TypeCode/Value与标签，翻译编辑者改draft，lang:review审校人和lang:publish发布人独立执行各自动作，普通客户端消费公开global包或经过认证的本tenant覆盖。机器上传/批量维护按同一注册actor政策，不能绕审校。显示文本、业务enum/API错误码、原Ref分开，语言切换不改业务身份。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:9) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:11)

保留zh-CN、zh-TW、en、ja、ko五语言；新主链中文稿不删旧语言。未知lang在GetByLang回ZhCN仅为该legacy路径行为，不推断其他路径fallback同样实现。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:5) [原文B:11](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:11)

## 2. 选定组合与接受范围

正文v0.1，SHA256 `4638ed3c19d5f24a2935edf39e9c7c518725e92b1c96259d282ebf14e6c94580`，83行，5原SPEC/20AC/15任务。它是S5 v0.2混合十稿中的原字节保留成员；八未改册继承原完整独审，IAM/CLIENT双根定点复审完成50SPEC结论，不是十册都重审。[原文A:154](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e9cccdb17e70bdd8__UA-20261008-S5-SYS-LANG-01-STATIC-MD02.json:154) [原文R:14](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:14) [原文B:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:83)

delegate接受/恢复确认覆盖当前静态详细设计，非用户亲签；02:18:29Z原逐字决定未重建，04:09:04Z恢复确认另记。候选正文STOPPED是历史标签；实施false、AC NOT_RUN、Owner采用UNPROVEN不变。[原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e9cccdb17e70bdd8__UA-20261008-S5-SYS-LANG-01-STATIC-MD02.json:8) [原文A:307](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e9cccdb17e70bdd8__UA-20261008-S5-SYS-LANG-01-STATIC-MD02.json:307)

## 3. 字段、标识与版本

|对象|精确字段/身份|规则|
|---|---|---|
|DictType|Id/TypeCode/TypeName/Enable/OrderNo|已被引用TypeCode是稳定码；变更需影响/兼容映射。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:15)|
|DictData|Id/TypeCode/Value/Label/OrderNo/Enable|现编辑不改TypeCode；Label可翻译，Value不随语言变。[原文B:15](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:15)|
|DictChangeCommand候选|expectedVersion/operationId/reason/referenceImpactRef +原白名单|OptionSetView为dictionaryVersion/scope/items/unavailableReason。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:19)|
|LangEntry|LangKey/TenantId/ZhCN/ZhTW/En/Ja/Ko/Status/UpdatedBy/UpdatedAt|唯一域(TenantId,LangKey)，global TenantId=null；edit不改TenantId且回draft。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31)|
|EntryCommand候选|scope/key/各语种值/expectedEntryVersion/placeholderSignature/operationId|Scope由当前可管理域导出，不信body自选全局/他租；渲染占位符/复数/HTML遵实际库。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31) [原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:33)|
|ReleaseManifest候选|version/createdAtUtc/createdBy/sourceEntryRevision/languages/namespaceSet/各包contentHash+byteLength/reviewPolicyVersion/minCompatibleClient/previousVersion|原LangManifest完整schema未读，不声称这些已存在。[原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:45)|
|review command候选|entryId+expectedEntryVersion+reviewer+reason+reviewSetDigest|显式准确集合；不能批准尚未见的新修订。[原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:57)|
|ClientLanguageState候选|desiredLanguage/effectiveLanguage/bundleVersion/loadedNamespaces/fallbackReason/compatibilityStatus|固定完整bundle版本，缺包不混新旧namespace。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:71)|

namespace以首段切分，wms/sales/erp/mes懒加载，_core含其他前缀和无点旧日文key。不能为了新前缀规则重命名旧key；迁移留old→new兼容期/调用者清单，删除先查引用。[原文B:29](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:29) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31)

## 4. 修改、审校、发布和回退写集

字典：原dict:add/edit/delete→类型/值/引用范围校验→expectedVersion CAS→同事务存变更→按typeCode+scope失效→回新版本。现类型删除级联数据；候选在业务引用/保留未解时拒，优先停用，历史已停值可解释且不可无条件新选。业务提交仍由Owner验证Value，不信下拉选项。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:17) [原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:19)

词条：编辑已reviewed条目回draft；review必须对所见准确entry版本。**旧 POST /review 的ids空/缺省审全部非reviewed条目，不能记录成no-op**；候选版本拒空集合，兼容旧管理工具显式呈“全量审校”及固定数量/集合。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:57)

发布：选已审集合和五语言政策→验key/placeholder/缺译/fallback/global覆盖→冻结输入→生成全部包并算hash/bytes→确认整集合可读后CAS切manifest→写发布审计。生成任意失败不改旧指针；发布不改变字典业务码/业务授权。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:47)

回退：lang:publish选择已存在目标包→校完整性/可读/客户端兼容→expectedManifestVersion CAS切指针→审计。不修改目标version字节，不删当前draft，不回滚审校历史或业务数据。[原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:59) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:61)

## 5. API、消费合同与Owner依赖

|现路由|实际/候选边界|
|---|---|
|GET/POST/PUT/DELETE /api/Dict/types 与 /api/Dict/data|原类型/数据管理；候选补稳定码影响和CAS。[原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:19)|
|GET /api/Dict/options/{typeCode}|原DictOptionDto(Value,Label)，Enable数据缓存30分钟；所有消费者是否读Type.Enable尚未证。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:17) [原文B:19](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:19)|
|GET /api/Lang/{langCode}、/{lang}/ns/{ns}|匿名只global，不含tenant覆盖；旧namespace空/错语义需适配。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:33)|
|管理GET/POST/PUT/DELETE /api/Lang|lang:update/delete；候选准确scope/entryVersion/placeholder。[原文B:33](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:33)|
|GET /api/Lang/manifest|no-cache；未发布Version为空。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:43)|
|GET /api/Lang/published/{version}/{lang}|存在包immutable一年缓存；缺包404。同version不可改bytes。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:43)|
|POST /api/Lang/publish；POST /api/Lang/review；POST /api/Lang/publish/rollback|分别lang:publish、lang:review、lang:publish；review空集合旧语义与候选显式集合严格区分。[原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:43) [原文B:57](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:57) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:59)|

字典消费Owner负责“历史可保/当前可用”规则；语言发布Owner补实际服务原子/版本证据；Web/native消费者登记namespace/缓存失效与兼容版本；CLIENT另管强制升级，LANG不自行升级设备。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:25) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:53) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:75) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:79)

## 6. 幂等、CAS、缓存与失败恢复

同operation发布返回原结果；manifest提交unknown先查原operation/当前manifest，不能重用同version覆写包。并发publish/rollback仅一个CAS成功，另一方412重读；无完整新集合保持旧指针，旧immutable包清理须满足兼容/历史引用/保留条件。[原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:47) [原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:49) [原文B:59](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:59)

字典保存unknown查原operation，并发冲突不另建同码替代；缓存失效失败不可“等30分钟即正确”，新业务提交按权威版本再核。语言feed/ns原缓存1小时，编辑失效五语言及_core/wms/sales/erp/mes；新增namespace必须进消费者/失效登记，不能默认已全传播。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:21) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:69)

客户端按version/lang/namespace缓存；manifest不长缓存，公开包可长期缓存。首次未发布走明确legacy feed，不猜latest；缺包/404/网络故障保上一个完整可用bundle，验证全部所需依赖才切换，禁止混版本namespace。tenant切换清私有覆盖重新认证，不改公开包、不清业务草稿。[原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:71) [原文B:75](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:75)

## 7. 权限、租户与输出安全

edit≠review≠publish；body TenantId不可信；global管理需明确全局角色。公开feed/包只含global授权内容，tenant专用名称不能复制成公开词条。公开预览和认证tenant覆盖预览分开，搜索/分页只查当前scope。[原文B:9](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:9) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:35) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:47)

占位符签名不一致阻review/publish，富文本按实际渲染库不能执行任意HTML。错误保stable code+params，翻译失败退安全原码/基准文本，不泄堆栈/机密。离线语言缓存不延续撤销的数据访问权限。[原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:37) [原文B:71](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:71) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:73)

## 8. 页面与服务器校验

字典稳定码/标签分列，已引用码只读；表单已停值标历史不可新选，不能换成第一项。翻译编辑显示scope、namespace、五语对照和placeholder差异；已reviewed一经改动即draft。审全部先展示精确数量与集合。[原文B:21](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:21) [原文B:35](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:35) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:61)

发布预览列key变更、缺译/fallback、五语言、消费者、原新version；过程分生成/待切换/已发布/未知，不把HTTP超时变成再次全发布。回退展示目标版/影响/原因，成功只说指针回退。语言切换不改数值、币种、UTC日期或Ref；晚响应按language/version/tenant丢弃。[原文B:49](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:49) [原文B:61](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:61) [原文B:73](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:73)

## 9. 开发顺序与固定实现差距

固定Main90c871...读过DictController/DictService/LangController；完整LangPublishService未在当时本地包，因此整包原子、manifest实际schema和语言客户端运行均未证明。[原文B:5](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:5) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:43) [原文B:45](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:45) [原文B:83](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:83)

15任务按依赖：①稳定码/引用/CAS与业务消费者矩阵；②namespace旧key兼容/Scope/placeholder；③发布服务真实原子与完整包回执；④版本化review集合和rollback门；⑤客户端固定bundle/失效恢复及错误码对照。当前均PLANNED。[原文B:25](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:25) [原文B:39](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:39) [原文B:53](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:53) [原文B:65](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:65) [原文B:79](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:79)

## 10. 验收场景与证据状态

20AC均NOT_RUN：LG01标签不改Value、引用项删受控、缓存/提交版本一致、同码CAS无部分写；LG02全球/tenant域隔离、旧key兼容、placeholder阻发布、修改回draft；LG03缺包/hash错不切manifest、版本bytes不可变、原operation不重复发布、五语言不缩水且公开包无tenant内容；LG04旧空ids全审与新集合拒空边界、review新修订拒、rollback只指针、publish/rollback并发；LG05缓存完整bundle、缺包保旧、晚响应和tenant覆盖不泄、翻译不改业务身份。[原文B:23](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:23) [原文B:37](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:37) [原文B:51](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:51) [原文B:63](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:63) [原文B:77](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:77)

## 11. 待对接与未证明事项

需要实际发布服务/客户端完整实现证据、Type.Enable消费矩阵、placeholder/HTML实际渲染规则、新namespace失效清单及global管理政策。已接受规则选定了完整包后切manifest算法，不能因为运行证据缺失把规则写成“任意选实现”；也不能声称现controller已提供该保证。[原文B:17](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:17) [原文B:31](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:31) [原文B:43](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:43) [原文B:47](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:47) [原文B:69](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:69)

### 历史独审保留的实现对照线索

原R0独审保留两条非阻塞开发对照线索：legacy live feed有`I18n:ServeReviewedOnly`（原默认false），不得把published集合的review保证外推所有旧feed；字典GetOptions的CacheService与GetItems的IMemoryCache/InvalidateType为两条路径，接入失效清单应分别核验。这里保留的是历史审阅指出的固定源码线索，不声称本轮再次审计源码或S5已修改其四份非阻塞正文。[原R0:160](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fd/fd4afbe1e84e9931__S5_十模块50SPEC_独立完整静态审阅报告_20261008.txt:160) [原R0:164](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/fd/fd4afbe1e84e9931__S5_十模块50SPEC_独立完整静态审阅报告_20261008.txt:164)

## 12. 原件与阅读覆盖

本次正文全文1–83；全部5SPEC/20AC/15任务均实读。S5复审全文1–128；组合按全部模块语义字段结构读；接受件按决定/正文/组合/ReviewQualifications/OwnerAndExecutionBoundary读，精确范围见reading JSON。源码结论沿用规范固定文本范围，不称本次完整源码审计。无发布包、代码或测试执行。[原文B:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/46/4638ed3c19d5f24a__S5_SYS-LANG-01_完整设计候选_v0.1.md:1) [原文R:1](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e3/e3522cf5a9c30a6a__S5_v0.2_认证双根独立复审与最小问题报告_20261008.txt:1) [原文A:8](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/e9/e9cccdb17e70bdd8__UA-20261008-S5-SYS-LANG-01-STATIC-MD02.json:8)

机器合同见 [platform-engineering-contracts.json](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json)，精确阅读记录见 [platform-engineering-reading.json](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)。

**当前必要材料归并状态。** 本册为 `required_materials_consolidated`：选定正文、规范附件及准确接受/复核范围已按直接实读、结构化语义或精确复用归并。身份清单核对不等底层源码全文阅读；历史草稿、候选实现和Office/HTML视觉未核范围仍单列。可复查[逐件阅读与处置](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/platform-engineering-reading.json)及[本组闭包台账](D:/CP6/docs/CP6_开发设计文档_20261010/worklogs/platform-engineering-progress.json)。当前必需规范未读清单为空；具体Owner采用、参数/物理接口、真实运行和本节已有来源缺口不因整理闭包而改变。
