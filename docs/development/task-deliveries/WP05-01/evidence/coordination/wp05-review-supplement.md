# WP05-01 独立审查补充建议

状态：REVIEW_SUGGESTION_ONLY；业务运行全部 NOT_RUN。原221/295及proposed manifest均保留，不修改旧制品，不生成旧30/48编号。

共43项审查建议，其中工作簿18项、CP22 15项、R1精确条件10项；属于来源映射/观察设计，不是43个新增历史AC。

详细原文、hash、单元格/行号和原矩阵复用ID见同目录 wp05-review-supplement.json。

## WP05-01-REVIEW/WB-01 · 稳定路由ID及对象授权

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B10,P10,AP10；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：语义已在旧295中，补准确工作簿来源映射，不重复计原AC。

场景：按名称伪造路由或访问另一Tenant的Project/Iteration。

观察：仅稳定ID定位；当前逐对象授权；统一掩码不泄漏对象存在。

## WP05-01-REVIEW/WB-02 · mode不是动作权限

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B14,P14,AP14；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：已有通用权限检查未定位mode这个客户端输入反例。

场景：更改mode为编辑并直接调用受禁动作。

观察：只改变展示；服务端仍按当前阶段和权限拒绝且无新业务写入。

## WP05-01-REVIEW/WB-03 · 缺并发前置428与技术/业务版本分离

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B18,P18,AP18；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：旧295覆盖不匹配412，但未包含缺失428；不能仅测试过期令牌。

场景：写命令分别省略ExpectedVersion/If-Match、给过期ETag、以业务Revision替代并发令牌。

观察：按适用命令返回缺失428/不匹配412；原业务状态/回执写集不产生未经授权变化；合法当前ETag才继续。

## WP05-01-REVIEW/WB-04 · RequestKey生命周期及秘密排除

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B22,P22,AP22；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：重放语义已覆盖；秘密不得作为RequestKey这一精确约束需补观察。

场景：同一逻辑动作超时重试、同键异载荷及使用密钥/token充当请求键。

观察：重试保原键，同体回原结果、异体409；不把认证秘密作为业务键或持久回执索引。

## WP05-01-REVIEW/WB-05 · SourceRevision与Unknown

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B26,P26,AP26；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：已有明确义务，补来源链接即可。

场景：传latest或断开源Owner后请求依赖动作。

观察：拒绝latest；不可得显示Unknown并阻所依赖动作，不默认为无漂移。

## WP05-01-REVIEW/WB-06 · ReturnTo及源Owner深链

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，引数説明!B30,P30,AP30；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，画面遷移!AT48,AT80,AT124；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：候选239/295均无ReturnTo、内部枚举或外部URL拒绝定位。

场景：ReturnTo或源Owner深链给外部URL/未知路由；合法返回前发生撤权或状态变更。

观察：仅受控内部路由枚举；保原筛选游标；深链按当前授权解析，返回重查权限和真实状态；拒绝任意外部跳转。

## WP05-01-REVIEW/WB-07 · 日期时间与审计事实不可由客户端覆写

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，項目説明 (入力値)!B50,P50,AM50；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：旧字段只读/UTC并未把输入歧义与FactTime注入列成精确观察。

场景：提交有歧义的日期时间或注入FactTime/审计发生时点。

观察：计划日期不带时区；事实UTC并本地时区展示；歧义拒绝；客户端不能覆盖服务器审计时间。

## WP05-01-REVIEW/WB-08 · 授权前置稳定分页及未定容量

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，項目説明 (入力値)!B46,P46,AM46；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，検索入出力仕様!O10,AF10,BA10；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，根拠・未決事項!P76,AU76；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：排序已明确覆盖；补工作簿关联并保留容量未定，勿制造重复缺陷。

场景：同updatedAt多ID跨页、无权项目混入、limit边界。

观察：先权限过滤再按updatedAt DESC,id DESC稳定排序；未知非0；50/200等须DP后继决定，不自动当已批SLO。

## WP05-01-REVIEW/WB-09 · 20动作的真实页面归属

动作：ADD_EXACT_SOURCE_MAPPING。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，画面項目制御!S56,S71,S86,S101,S116,S131,S146,S161,S176,S191,S206,S221,S236,S251,S266,S281,S296,S311,S326,S341；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，画面遷移!B48,B52,B56,B60,B64,B68,B72,B76,B80,B84,B88,B92,B96,B100,B104,B108,B112,B116,B120,B124；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：旧295只有PROC正文，未有工作簿直接声明的页面/动作映射。

场景：逐ACT01..20由准确页面和角色进入对应PROC。

观察：按原工作簿已明确页面—ACT—PROC关系核对，拒绝只靠字段归属反推；这是已存工作簿定位，绝非旧30AC恢复。

## WP05-01-REVIEW/WB-10 · 修改动作确认摘要与输入保留

动作：ADD_EXPLICIT_OBSERVATION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，画面項目制御!S89,S95,S179,S185,S299,S305,S344,S350；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：旧矩阵泛称页面反馈，未保留这些明确页面异常恢复义务。

场景：修改/不可逆动作确认及412/409/503失败、重复点击。

观察：确认显示摘要/版本/原因输入；重复按钮禁用不代服务端幂等；失败保留输入和真实原回执链接，不假报外部完成。

## WP05-01-REVIEW/WB-11 · 状态与HELD界面控制仍有服务端门

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，画面項目制御!L20,AU20,L35,AU35,L40,AD40,AU40,L45,AD45,AU45；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：核心语义已有；将控件位置绑定原义务。

场景：在审、终态、HELD和跨租户页面绕UI直调命令。

观察：冻结内容不可PATCH；终态不可覆盖/删除；HELD禁止新增提交/评价/冻结；所有拒绝由后端执行，隐藏存在性。

## WP05-01-REVIEW/WB-12 · 查询遮蔽、源水位与历史排序

动作：ADD_EXACT_SOURCE_MAPPING。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，検索入出力仕様!AF20,BA20,AF25,BA25,AF30,BA30,AF35,BA35；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：大部分读授权已有；精确查询输出与历史稳定顺序需连到工作簿，避免通用查询观察遗漏。

场景：无权读旧评审成员/源文件、源不可达、同UTC事件历史查询。

观察：旧轮只读且不列无权成员；源Unknown/asOf保留，不泄漏裸下载地址；历史UTC+ID稳定顺序。

## WP05-01-REVIEW/WB-13 · 文件导出、冻结引用、报告真相

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，ファイル入出力仕様!AI10,AI16,AI22；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：原295已覆盖，不把它误列为新义务；补直接来源即可。

场景：公式样式文本导出、下载前撤权、缺技术文件或上传截图冒充VER结果。

观察：文本不执行公式；下载重验权；EvidenceUnavailable显式；PLM-DOC文件版本不替换，ITER不直接上传成PASS。

## WP05-01-REVIEW/WB-14 · 旧记录导入不自动生成合格历史

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，ファイル入出力仕様!P28,AI28；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：历史缺口已保留；附旧导入禁止条款而不新增导入能力。

场景：只有旧产品/生产记录而无可信迭代证据，尝试批量导入为通过。

观察：本册不批准批量导入；先来源/状态映射和缺证列表；不得伪造迭代或通过。

## WP05-01-REVIEW/WB-15 · 角色最小权限与服务受众

动作：ADD_EXACT_SOURCE_MAPPING。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，権限・監査!N10,AO10,N15,AO15,N20,AO20,N25,AO25,N30,AO30,N35,AO35,B42；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：已有一般授权与自批门；独立角色/服务输入反例需显式绑定。

场景：只读/审计角色尝试恢复或下载原文件；服务回执错issuer/tenant/action；失败请求带受限信息。

观察：角色职责分离；读取不附送编辑/恢复/原文件权限；服务身份核issuer/tenant/action且不能伪造用户决定；审计及失败日志不漏敏感对象。

## WP05-01-REVIEW/WB-16 · 错误矩阵和拒绝写集

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，処理詳細(チェック仕様)!N310,N313,N329,N332,N335,N365,N368；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，メッセージ一覧!Y118,Y126,Y138；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：对应BR/PROC已保留；无需新增40个重复AC，仅明确同源CHK/MSG链接。

场景：权限、状态、并发、字段错误及已发生外部事实后本地拒绝。

观察：按原403/404掩码、409/412/422分类；无新增业务写入；已独立发生外部事实保留，查原请求；不换键逃避。

## WP05-01-REVIEW/WB-17 · 状态/选择/问题转移与默认值

动作：REUSE_WITH_EXACT_WORKBOOK_LINK。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，項目説明 (入力値)!P10,AM10,P14,AM14,P18,AM18,P22,AM22,P26,AM26,P30,AM30,P34,AM34,P38,AM38,P42,AM42；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：主要语义在原矩阵；建议补workbook到既有条目映射并在实现决策处收口。

场景：通用Update.Status、两个ActiveSelection、无责任Blocking转移、HTTP200代业务接受、将建议长度当获批上限。

观察：按专用动作和原完整枚举；至多一当前选定；转移需受理责任期限；技术尝试独立；未知/建议参数仍留边界。

## WP05-01-REVIEW/WB-18 · ECO冲突不放宽义务

动作：REUSE_CONFLICT_DISPOSITION。适用：ITER UI/API/worker; references and external facts remain with their original Owner。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，処理詳細(項目・任務)!N85；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/26/262f327165510b91__PLM-ITER-01_工程项目迭代与方案评审_詳細設計書_v0.1_REVIEW.xlsx`，追跡マトリクス!AL149；SHA-256 `262f327165510b91753a50598d6e5b5b86972a47bf31aecfcc76fbc0bcc30b38`。

原矩阵：README30已准确保留；补精确双单元格及既有义务即可。

场景：改型迭代缺ECO并援引追踪表N/A。

观察：按正文REQ023和处理细则核ECR/ECO；保留原AL149冲突，不能凭N/A删核验或批准导入。

## WP05-01-REVIEW/CP22-01 · CP22 registry与真实前驱链

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L113–114；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：错registry cut、用未来伪造preparation/check/root ID推进后继。

观察：固定完整原Owner SourceDescriptor/digest/current body与namespace；仅Source search→真实Preparation→EngineeringCheck→Snapshot→CREATE原五步；不得编造前驱结果。

## WP05-01-REVIEW/CP22-02 · 前置capture集合及preparation绑定

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L115–116；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：PREPARE混入第五项；CHECK缺一/多一/混用旧preparation。

观察：PREPARE恰四且preparationRef=null；CHECK/SNAPSHOT/CREATE各恰十一，同真实preparationId/revision；缺项多项或跨cut不得后继派发。

## WP05-01-REVIEW/CP22-03 · 原issuer与authenticated SDK tuple

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L117–118；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：MES替签REL truth、错E/T或executionKey、provider pin缺失/撤销、只给digest无正文。

观察：MES仅运输；issuer/documentRef.owner为原Owner；完整tuple及真实providerContractId/version/digest、AdoptedByOwner/有效期/未撤销验证，canonical全文digest独立核；不造未接受HTTP入口。

## WP05-01-REVIEW/CP22-04 · current capture head及RequiredFenced

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L119–120；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：旧capture回退、跨cut拼接、缺issuer/provider/body或适用性未知。

观察：current head准确绑定documentKind/executionKey/preparationRef/sourceDescriptorDigest；保不可变旧capture但不可回退；WO_SUPPORTING_OWNER_REQUIRED_FENCED，零后继native派发；PLAN-PO不得签上游truth。

## WP05-01-REVIEW/CP22-05 · PLM PurposePolicy依赖精确匹配

动作：ADD_EXPLICIT_OBSERVATION。适用：REL只验证其授权引用的PLM依赖；PLM产生原truth，不在REL复制PLM主账。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L127–133；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：普通policyRef冒充purposePolicyRef、NOT_APPLICABLE/UNKNOWN或与REL不同cut。

观察：只接受原PLM全body、PRODUCTION/NEW_MANUFACTURE、当前APPLICABLE、准确Site/窗口和technical basis；与REL同cut；不可用MES布尔绕过。

## WP05-01-REVIEW/CP22-06 · CFG完整解析依赖

动作：ADD_EXPLICIT_OBSERVATION。适用：CP22跨Owner依赖观察；CFG负责解析，REL不取得CFG决策权限。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L134–140；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：CFG非唯一、baseline/request/source refs不匹配，删除问题行凑resolutionCount=1。

观察：request RESOLVED、resolved UNIQUE且count1；完整variables/operations/materials/outputs与ENG canonical相等；其他状态fence。

## WP05-01-REVIEW/CP22-07 · ENG released内容与DAG依赖

动作：ADD_EXPLICIT_OBSERVATION。适用：仅CP22当前采用的普通路径；不能据此缩小其他已接受工程合同。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L141–145；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：普通路径PRIMARY数非1、DAG循环、材料/操作/输出与CFG不一致或过期baseline。

观察：固定原RELEASED baseline/item/site/purpose/窗口和全technicalContent；操作身份、依赖、consumptionPoint和材料关系无损；完整三数组相等。

## WP05-01-REVIEW/CP22-08 · 单位和量义映射依赖

动作：ADD_EXPLICIT_OBSERVATION。适用：ENG提供原单位映射；REL授权quantity/ranges消费同一量义。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L146–146；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：相同数值不同量义、无版本单位、非正比例、EXACT非整数或loss不守恒。

观察：保完整target/material/output usages和versioned units、正rational、EXACT整数比例及loss逐项守恒；数值相同不代量义证明。

## WP05-01-REVIEW/CP22-09 · ENG前置与五member manifest

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L147–148；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：complete=true但缺独立正文/auth；五member缺项、ref/kind/bodyDigest错配。

观察：missingInstructions/missingQualityRequirements为空仍须独立可信body；完整五member逐项对独立capture，coverage COMPLETE/missingRefs空；技术hash不代正文。

## WP05-01-REVIEW/CP22-10 · native EngineeringCheck和Snapshot链

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L149–150；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：七CREATE_WO gate或source version/baseline/resolved refs/digest/validUntil任一错，Snapshot替换candidate/preparation/check。

观察：全部准确匹配原capture和真实前驱；保accepted native wire，错配不后继派发；未证provider仍fence。

## WP05-01-REVIEW/CP22-11 · DEV仅当前有界正常路径

动作：ADD_EXPLICIT_OBSERVATION。适用：限定CP22已接受candidate；REL其他合法偏差流程不因此取消。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L151–157；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：DEV APPLICABLE/UNKNOWN或本地complete=true伪造不适用。

观察：本CP22 candidate只采用独立DEV NOT_APPLICABLE_PROVEN且空refs，与REL scope/execution和ENG digest相同；其他情况RequiredFenced；不批准替代Claim/余额流程。

## WP05-01-REVIEW/CP22-12 · REL用途授权全body及当前效力

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L158–162；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：REVOKED/UNKNOWN、窗口外、错PurposePolicy/Site/execution或擅扩scope/version/ranges/quantity。

观察：原REL全body无损；仅PRODUCTION GRANTED/current有效窗，candidate executionScope与准确授权scope/version/ranges/quantity相等；任何错配拒绝本CP22路径。

## WP05-01-REVIEW/CP22-13 · post-root预约独立事实

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L163–165；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：把Root preparation当reserve、把REL_RESERVATION塞进前置十一项、MES/PLAN-PO自产R/P或伪造root refs。

观察：只接受真root后原Owner post-root全schema；authorization/execution/scope/ranges/quantity/state/WO/reservationVersion齐全；保真root.relReservationRefs，零自产预约/释放。

## WP05-01-REVIEW/CP22-14 · Owner receipt流与历史真实链

动作：ADD_EXPLICIT_OBSERVATION。适用：有REL关联原回执的CP22消费链；不能把PLAN/MES实现算作REL已采用。。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L121–126；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：错authenticated Producer/E/T/admission，sequence gap，同event异bytes，intermediate APPLIED当后继授权，改observedAt，负MES结果当永久noeffect。

观察：nested仍role15；完整原native body/ordinal/status及所有键/digest保真；canIngest与canReadOwnEvents分离；gap持久WAITING_PREDECESSOR；重放原bytes，异文冲突；APPLIED仅观测，Now与原observedAt分离；noncommanded已注册read/eval才null command key；历史链不要求重造今日支持源。

## WP05-01-REVIEW/CP22-15 · 历史批准与实际采用分轴

动作：ADD_EXPLICIT_OBSERVATION。适用：REL CP22 role15 bounded producer/consumer responsibility; other nested Owners remain independent。

来源：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/cb/cbd38c11127b763e__OWNER_TECH_CP22_PROFESSIONAL_DELTA_ADOPTION_20261004.md`，L167–176；SHA-256 `cbd38c11127b763e0fff1d982a34f64103179a15a5b136cc393797d029eeea34`。

原矩阵：Existing CP22 pair verifies role15 design-scope/status statements; it does not enumerate these exact source fields, native sequence, authenticated evidence or fail-closed observations. README post-root note alone does not supply executable per-source coverage.

场景：仅设计recordId/签署或静态检查通过即要求放开Required路径。

观察：保准确accepted pins/历史批准范围；actual provider未证、17 actualAdopted=0、providerGrant=0、37AC NOT_RUN；完整后继独审/根接受不可由本意见替代。

## WP05-01-REVIEW/R1-01 · OA注册受众及完整集合/容量

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L7–11；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：浏览器换Owner/Tenant/callback；双候选漏一项/身份重复；集合超上限截前1000。

观察：注册受众来自真实可信注册；完整成员保原ID/version/ref/digest及排序；不截断；超原路由能力阻提交，分包另有协议。

## WP05-01-REVIEW/R1-02 · 聚合canonical与双向集合相等

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L13–13；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：重复JSON键、孤立surrogate、Unicode静默normalize、数字改Number、同ID异版/多缺成员。

观察：严格domain+canonical完整字节摘要；Scope/Round/aggregate/Package双向相等；原业务摘要不覆写。

## WP05-01-REVIEW/R1-03 · OA round与外域Ref别名无损

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L15–17；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：原Round/Ref超OA域、别名冲突/未注册、非sha256算法塞sha256、256KiB snapshot或1MiB body越界。

观察：合法原键保原值，否则真实持久不可变别名+完整原字节/schema双向解析；拒错误算法；Snapshot独立摘要；大小限制无截断。

## WP05-01-REVIEW/R1-04 · OA封闭字段和reject策略

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L19–19；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：将RETURN新增到OA enum或rejectDisposition塞入Engineering。

观察：字段/大小写/null逐字遵OA R2；rejectDisposition仅本地冻结；绑定精确匹配，拒非法wire。

## WP05-01-REVIEW/R1-05 · OA接收与应用不同事务/去重身份

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L25–29；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：同Event异体、同决定异运输、sequence gap、只有HTTP接收/trace、应用前权限或Blocking变化。

观察：按原Tx及独立唯一性持久Inbox/原包；GAP不跳过；当前绑定/SOD/全部Blocking核验，应用与Receipt/Outbox/Audit同commit；OA投影不再次驱动Owner。

## WP05-01-REVIEW/R1-06 · OA回执恢复和旧路由禁止重复应用

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L31–35；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：应用回执发送失败重试、BLOCKED恢复、换action名重复应用、旧同步callback与新worker并存。

观察：一个固定应用单元；相同不可变回执重投；仅许可BLOCKED追加revision/previousReceiptId，终态不覆写；锁外投递；不新增旁路HTTP，未注册/不完整/不无损阻新提交。

## WP05-01-REVIEW/R1-07 · 多输出输入/配置与合法无输入依据

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L67–69；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：同产品两工序去重、缺生成操作/输入、配置不一致、无输入直接默认合法。

观察：保独立输出occurrence及完整注册input/operation集合和配置；合法无输入有明确原工程依据；原single-PRODUCT wire不修改。

## WP05-01-REVIEW/R1-08 · RelationSet/Binding/原PRODUCT交叉身份

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L71–73；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：Binding错Scope/version/bodyDigest、原兼容PRODUCT不是任一真实输出、只读PRODUCT就声称多输出支持。

观察：同时验证原Manifest和新RelationSet/Binding，准确跨引用/配置/主PRODUCT至少匹配一输出；缺任一不给完整输出。

## WP05-01-REVIEW/R1-09 · 多输出摘要canonical及原字节

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L75–75；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：乱序/重复输入引用、混用新domain到原Manifest、仅hash代完整Ref身份。

观察：新schema域分离canonical；数组按occurrence排序并去重复输入；保原字节/长度，Scope+完整Ref/version精确比较。

## WP05-01-REVIEW/R1-10 · 冻结写集/错误/未知及消费者采用

动作：ADD_EXACT_OBSERVATION_OR_EXPLICIT_REUSE_MAPPING。适用：Only target responsibility; original OA/ENG providers supply independently verified source facts。

来源：`docs/design-handbook/remediation/07-shared-consumer-mappings.md`，L77–79；SHA-256 `7defc5f9b65df9269124f433abf58f3331b69dc9fde36f1422ffd0de32d864aa`。

原矩阵：Existing R1 summary/detail cases and generic overlays cover the principal mechanism, but the pinned bounded revision has more exact field, capacity, canonicalization and recovery conditions; link each condition to an executable observation instead of treating vector count as full coverage.

场景：原Baseline事后补关系、Freeze提交后响应丢失、旧consumer缺输出映射、Owner不可得。

观察：ENG本地完整同事务后继冻结；原key查回；缺映射422 REQUIRED_OUTPUT_MAPPING、源配置漂移412、异体409、Owner503且无新Baseline；旧封闭wire只经版本化错误适配；REL实际读用证据另列。
