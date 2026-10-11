"""Record explicit root-authored decisions and required acceptance; no status inference."""
import argparse
import json
from pathlib import Path


def build(book):
    register=json.loads((book/'remediation/00-disposition-register.json').read_text(encoding='utf-8'))
    by_id={s:i for i in register['issues'] for s in i['source_finding_ids']}
    rows=[]
    def add(id,classification,decision,positive,negative,outputs,targets=None):
        old=by_id[id]
        row={k:old[k] for k in ['issue_id','source_finding_ids','batch','evidence']}
        row.update(classification=classification,targets=targets or old['targets'],decision=decision,source_version='Accurate pinned original combination + CP6-DESIGN-REVISION-20261011/R1 bounded successor',supersedes_scope='Only the named discrepancy/consumer boundary; exact original bytes and historical acceptance unchanged',preserved_constraints=['No broader authority or narrower native input domain','Original business facts/receipts and raw bytes retained','Actual provider/all-writer adoption must be proven separately'],outputs=['remediation/'+o for o in outputs],design_status='DESIGN_RESOLVED' if classification in ('DESIGN_GAP','ARTIFACT_CONFLICT','DOCUMENT_STALE') else 'SUPERSESSION_RECORDED' if classification=='SUPERSEDED' else 'BOUNDARY_RECORDED',actual_adoption_status='UNPROVEN',implementation_status='NOT_IMPLEMENTED_BY_THIS_DESIGN_TASK',verification_status='STATIC_DESIGN_CHECKS_ONLY_RUNTIME_NOT_RUN',dependencies=['DR-VERSION-AUTHORITY'],consumer_adoption={'required':'Provider/consumer pin, exact identity/domain mapping and current final gate must be registered before new effects; preserved historical reads use current disclosure permission','runtime_status':'UNPROVEN'},acceptance=[{'id':id+'-POS','scenario':positive,'expected':positive,'expected_writes':'Only original contracted business write set after exact current qualification; read-only adapters produce capture/receipt only','verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'},{'id':id+'-NEG','scenario':negative,'expected':negative,'expected_writes':'No new unauthorized or duplicate business effect; retain original facts and append only permitted audit/blocked receipt','verification_level':'REQUIRED_RUNTIME','result':'NOT_RUN'}])
        rows.append(row)
    W4=['05-planning-material-mapping.md']
    existing=[
      ('COM-EST-PRICE-COVERAGE-V2','ADOPTION_PENDING','EST R2 v2采用整张coverage+准确coveragePolicy+最终保护，旧PRICE列不能代替；保留原估算/报价用途。','完整coverage/policy/Scope/保护向量才允许当前v2消费','旧字段或缺政策/保护保持对应路径FENCED'),
      ('COM-FSC-FACT-GUARD-EXTENSION','ADOPTION_PENDING','采用FSC R2 EST事实读与QTN关系双守卫；EST I03原Usage不扩域。','完整事实及关系两个当前守卫共同成立','只持一个守卫或拿旧I03替新用途拒绝新效果'),
      ('COM-RFQ-PR-SOURCE-STATE-ADAPTER','INTENTIONAL_DIFFERENCE','内部EXECUTED_PENDING_LEDGER与ClaimRecord wire CONSUMED属于不同层；PR P→E和RFQ异步P→C保各自receipt推进。','各层只消费准确原receipt/manifest后推进本层','运输接收不得伪装另一Owner业务已入账'),
      ('COM-GR-ORIGINAL-SCOPE-AND-COMPOSITE-ADOPTION','ADOPTION_PENDING','采用GR逐叶原Domain/有效RangeSet/当前ScopeSet和Quality，Stock/PO专业后继完整共同ResourcePlan；非前缀VOID保持当前已修设计。','准确叶范围且全参与者同事务门通过','无专业映射/漏叶/旧前缀简化算法不得商业验收或MATCH'),
      ('OPS-IF-013','INTENTIONAL_DIFFERENCE','QA-POL采购按准确source/part叶总体；制造按原完整总体，不能母单100覆盖采购40/60。','采购40/60分别按叶选政策，制造沿原总体','跨域统一数量口径或用母域政策批准叶拒绝'),
      ('OPS-IF-014','SUPERSEDED','IQC R5已对QAP EffectLocator准确追加ORDINARY_STOCK_USE；镜像通用说明不能覆盖此有界后继。','准确后继namespace/用途/locator满足原消费','仅见同名枚举而无版本化后继映射拒绝'),
      ('OPS-IF-016','SUPERSEDED','NCR准确后继已无损采用IPQC旧SourceEnvelope、standalone和Resolution27；保原字段/摘要/Scope。','完整原包与准确映射可入本轮消费者','删字段或将不同来源同名Ref强转拒绝'),
      ('OPS-IF-018','ADOPTION_PENDING','SLOT/REP NEW路线与Transfer相容，但SourceDemand Owner及全current门必须真实采用。','完整SourceDemand+current资格形成一次Transfer','相同NEW枚举或预检票据不作真实提供者许可'),
      ('OPS-IF-035','ADOPTION_PENDING','SCHED完整Root组、换机六阶段及参与者已设计；WO改期原白名单不扩大。','全Root/六阶段当前资源资格后统一应用','只改单条WO或向原白名单加入未授权字段拒绝'),
      ('OPS-IF-036','ADOPTION_PENDING','PERF必须来自原Owner槽、局部Pause和真实分窗量，不能从聚合猜分母。','当前原Owner完整槽/分窗证据可发布指标','缺分窗/缺真实Owner则不发布假CLOSED指标'),
      ('OPS-IF-037','SUPERSEDED','采用PERF R2.1取消WHOLE_SHIFT必暂定；CLOSED两分母在同一发布门齐证。','WHOLE_SHIFT有完整同门分母可合法CLOSED','任一分母缺失仍阻正式CLOSED，不能单独补0'),
      ('PLAN-IF-002','ADOPTION_PENDING','POL/SUP功能接受不替ATP R11准确端口；schema pin/Scope/完整来源/全部writer最终门分别登记。','准确端口实例及完整连续供给与政策身份共同满足','缺Required条件保持原FENCED'),
    ]
    for row in existing:add(*row,W4)
    add('PLAN-IF-001','DESIGN_GAP','新增MAT-MRP-RESIDUAL/1由MAT Owner准确生产NET/GROSS两种表达；保完整MaterialBinding、原责任代表、抵扣覆盖并集、已抵及排除集合、仍占保护与当前cut，原RemainingBasis不改名。','80转50+30仍80；NET40+新180−自由50=170，受限后仅Owner后继50可得180','旧80+新50=130、重复扣已抵20、UNKNOWN补0、模式/单位/份额相交未证一律阻正式Seal',['05-planning-material-mapping.md','assets/material-handoff/wire-1.0.0.schema.json','assets/material-handoff/ports-1.0.0.json','assets/material-handoff/examples-1.0.0.json','assets/material-handoff/validation.json'])
    for row in [
      ('PLAN-IF-006','原StoredLegacyAnchorRecord两Reported字段使用原SignedDecimal；新责任非负约束不变。','历史负数原值可往返','不得abs/clamp/补0'),
      ('PLAN-IF-009','逐portion branches不以三种枚举限制三条，删除maxItems3；新contractVersion和完整contractBody SHA明确发布，closedSchemaSet原摘要不变。','四个不同portion精确匹配完整allocation集合','缺/重/多tuple或旧provider pin不得默许新版本'),
      ('PLAN-IF-012','PO15 OpenAPI503同步当前API登记的三个真实关闭错误，底层恢复语义不改。','三个准确错误在新OpenAPI可解析','客户端不能把缺失闭包当成功或任意重试新意图'),
      ('PLAN-IF-013','18处PlanningFactHead包装ResourceKey采用原实际Row完整主键；实际数据行保持。','包装与实际主键完全一致','同hash但不同原ResourceKey不得混入完整行证明'),
    ]:add(row[0],'ARTIFACT_CONFLICT',*row[1:],W4+['assets/fixtures/bounded-patches.json']+(['assets/fixtures/owner-profile-successor-pin.json'] if row[0]=='PLAN-IF-009' else []))
    W5=['06-wire-types-and-fixtures.md','assets/core-types/bounded-patches.json']
    add('OPS-IF-002','ARTIFACT_CONFLICT','保CREDIT原38总位/8小数合法wire；decimal(38,0)整数系数+Scale0..8无损存储，任意精度中间运算后核原输出域。','38个9及30整数+8小数精确往返','39位输出/指数/负零按原Schema拒绝；禁止先走decimal(38,8)',['06-wire-types-and-fixtures.md','assets/core-types/numeric-storage-1.0.0.json','assets/core-types/numeric-validation.json'])
    add('OPS-IF-030','ARTIFACT_CONFLICT','INV开放整数位采用大整数文本+scale；66个有限decimal列给出准确替代，MAVG分子分母不使用decimal(76,0)，原wire和摘要字节独立保存。','90整数位+18/12小数及原合法-0.000保持精确值和原文','不得有限CAST/SUM、分母0、超小数位或静默舍入',['06-wire-types-and-fixtures.md','assets/core-types/numeric-storage-1.0.0.json','assets/core-types/numeric-validation.json'])
    for row in [
      ('PLAN-IF-004','ATP单位保持完整ASCII StableId1..128；R1显式修Schema/TS/.NET末尾LF锚点歧义，五DDL列128/check；独立编译同时修准确字节Parse重载。','17/128合法单位不截断，三语言完整末尾规则一致','空/空白/中文/129字符及短或128字符后附LF/CRLF拒绝，原历史profile不重写'),
      ('NCR-TYPE-01','原5处null-only及编译发现2处ObservationInput const-null统一required NullOnly+严格converter；不以默认0代null。','必需成员显式null反序列化并原样输出','0/数字/对象/字符串null/缺字段均拒绝'),
      ('NCR-FIELD-01','按准确shape修5条nullable元数据；CLOSE_MAIN.priorHeadRef不可空，其余4条查询字段允许null。','准确可空值/必需非空按shape表现','字段元数据不授予写权限或改变Source范围'),
      ('COM-LEAD-GENERATED-CS-NULLABLE','OwnerId只改单层string?，保原UUID/unassigned/null Schema；独立编译通过。','三种原允许形式不丢','任意非法string仍由严格Schema拒绝'),
      ('OPS-IF-015','IPQC未绑定Evidence/Uom按准确QapEvidence/QapUom成员定义修，独立TS严格编译通过。','准确原证据/单位成员可编译并过原Schema','不把任意同名Ref强转为原QAP Evidence'),
    ]:add(row[0],'ARTIFACT_CONFLICT',*row[1:],W5+['assets/core-types/attachment-compile-validation.json'])
    add('COM-INQ-GENERATED-CONTRACT-ADOPTION','ARTIFACT_CONFLICT','按六组子问题修OpenAPI引用、TS事件与decoder声明、C#结构相等及空数组、隐私终态expiresAt、下载proof/ETag及长度负例前置；完整派生C#/TS独立编译，严格wire未放宽。','55引用可解析；不同对象键序算同值；终态公开字段null；合法幂等键下201/4001字符长度负例422','boolean扩展字段仍由decoder检查，重复JSON键拒绝；旧过期proof或错误ETag不能下载/写入',W5+['assets/fixtures/bounded-patches.json','assets/core-types/DesignRevisionTypes.cs','assets/core-types/attachment-compile-validation.json'])
    add('COM-LEAD-AC-ATOMIC-SUPERSESSION','SUPERSEDED','CP22保R3原60AC身份，AC042/048–052改同事务H02+Manifest；AC035 hiddenCount不是当前closed字段，旧Saga/202/部分成功预期不再作当前验收。','完整同Uow H02/Manifest写集原子成功及原结果重放','任一步失败整体不生新业务效果；不得返回旧202或部分Map成功',['06-wire-types-and-fixtures.md','assets/core-types/lead-acceptance-supersession.json'])
    add('PLAN-IF-003','DOCUMENT_STALE','AC087步骤陈旧readcut22改为当前精确input.fixedReadCut113，保原case身份及所有权限/页约束。','完整指定cut113与主文/packet一致','不得仅换说明却混用22的实际集合',['06-wire-types-and-fixtures.md','assets/fixtures/editorial-patches.json'])
    W6=['07-shared-consumer-mappings.md','assets/shared-consumers/sidecar-1.0.0.schema.json','assets/shared-consumers/acceptance.json','assets/shared-consumers/validation.json']
    shared=[
      ('PE-ACT-FILE-COMMIT-MODE','INTENTIONAL_DIFFERENCE','ACT仅LOCAL_AUTHORITATIVE_GUARD；通用远端租约不是已采用模式。','准确本地最终门及同事务附件提交','远端reserve-confirm不经ACT采用不得提交',['CRM-ACT-01','SYS-FILE-01']),
      ('PE-HANDOFF-NAMESPACE','DOCUMENT_STALE','H编号必须用来源+合同名+版本解释；采购H31与工程H41独立。','准确来源解析到相应规范合同','孤立H31/H41不得跨文档拼接',['PUR-RETURN-01','ENG-REV-01','PLM-REL-01']),
      ('PE-ENG-MULTI-OUTPUT-MANIFEST','DESIGN_GAP','新增独立输出Occurrence RelationSet及BaselineOutputBinding；原单PRODUCT技术Manifest及摘要不变，同Freeze事务生成完整新绑定，旧Baseline不能事后补造。','相同产品两个生成工序保两Occurrence，输入/操作/配置及双digest完整','缺输入/操作/配置、旧消费者只读PRODUCT或修改历史Baseline拒绝',['ENG-PROD-01','ENG-REV-01','ENG-BOM-01','ENG-ROU-01','PLM-REL-01']),
      ('PE-ENG-TOOL-OWNER-PARTICIPANT','ADOPTION_PENDING','X1-R2每个真实Owner native schema及HoldExact共同提交保护必须实际采用；设计正面结论不是实例。','全Owner当前同一实际最终点','缺Owner/native schema/HoldExact保持FENCED',['ENG-TOOL-01','ENG-REV-01']),
      ('PE-ITER-OA-PERSISTENT-APPLICATION-MAPPING','DESIGN_GAP','用不可变候选全集聚合及RoundBinding映射OA2，保Subject/Package/Policy/Technical四身份与rejectDisposition；OwnerInbox/应用回执/Outbox及撤回竞争完整持久化。','同决定多运输一次应用；完整集合匹配且Blocking关闭才能APPROVED','缺reject映射、候选漂移、旧轮次迟到只拒绝/STALE，不改变新轮',['PLM-ITER-01','OA-APP-01','PLM-VER-01']),
      ('PE-CP22-EXACT-PROFESSIONAL-ADOPTION-NOT-RUNTIME','SUPERSEDED','CP22 role15内专业设计已有准确ADOPT；总17角色、实际provider/adoption0保持，不扩旧RC1批准。','精确role15子合同范围可进入后续编码清单','不得宣称18角色或运行已采用',['PLAN-EXC-01','PLM-REL-01','PLM-DEV-01','ENG-REV-01']),
      ('PE-SPACE-R2-S5-EXACT-CONSUMER-MAPPING','DESIGN_GAP','新增SPACE/S5身份权限组合、61字段78DTO路径及完整grant-clause交集；四原Space版本与S5向量独立；每页缓存导出下载当前撤权门。','先S5真实行MIN再默认，精确全clause+行+字段+用途共同放行','外部write永远false；撤权、缺registry、拼grant、旧cursor都不放行',['SPACE-EXT-01','SPACE-PUB-01','SPACE-OPS-01','SYS-IAM-01']),
      ('COM-EST-QTN-V2-DECODER','ADOPTION_PENDING','保原EstimateBasis并注册完整v2 ModelIdentity/FormulaDigest/Disclosure解码与I03/I05用途。','完整v2由准确decoder消费','旧decoder/缺披露/误用途保持拒绝',None),
      ('COM-IC-ORIGINAL-TRANSPORT','INTENTIONAL_DIFFERENCE','新HO/OPP运输原封闭bytes和opaque版本；旧C03删敏感字段/int版本不可无损替代。','按准确新路由运输原包并当前授权重放','删字段/改版/Platformcheckpoint替原Ownercut拒绝',None),
      ('COM-PR-CP22-NATIVE-OWNER-ADOPTION','SUPERSEDED','CP22 port14已有PR R3原生intake专业ADOPT；保原Digest结构、版本与namespace，实际实例另证。','完整原生包和完整namespace pin一致','仅同名字段不能开放真实Owner入口',None),
      ('COM-PRICE-EST-UNIT-SCALE','INTENTIONAL_DIFFERENCE','保PRICE0..18与EST0..12能力差异；EST不支持的精度显式拒绝或原Owner准确版本化转换。','scale12原值消费','scale13不得静默截到12',None),
      ('ROOT-X3-X4-ABC-01','INTENTIONAL_DIFFERENCE','SLOT前缀阈值和ANALYTICS末项累计阈值保独立排序/分组/算法版本。','单产品100%按SLOT A、ANALYTICS C分别正确','同一个ABC函数跨域复用应被合同测试拒绝',None),
    ]
    for id,cl,decision,pos,neg,targets in shared:add(id,cl,decision,pos,neg,W6,targets)
    for row in rows:
        if 'PLAN-IF-004' in row['source_finding_ids']:
            row['outputs']+=['remediation/assets/core-types/atp-stableid-vectors.json','remediation/assets/core-types/atp-stableid-validation.json']
        for output in row['outputs']: assert (book/output).is_file(),output
    (book/'remediation/workparts/root.json').write_text(json.dumps({'findings':rows},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'root_issue_families':len(rows),'source_findings':sum(len(r['source_finding_ids']) for r in rows)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);build(p.parse_args().handbook)
