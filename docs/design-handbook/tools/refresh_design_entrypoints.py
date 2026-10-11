"""Publish the explicit revision overlay without rewriting archived reading records."""
import argparse
import json
import re
from pathlib import Path

START='<!-- CP6-DESIGN-REVISION-ENTRY:START -->'
END='<!-- CP6-DESIGN-REVISION-ENTRY:END -->'


def overlay(path,body):
    text=path.read_text(encoding='utf-8')
    text=re.sub(re.escape(START)+r'.*?'+re.escape(END)+r'\n*','',text,flags=re.S)
    head,sep,tail=text.partition('\n')
    path.write_text(head+sep+'\n'+START+'\n'+body+'\n'+END+'\n\n'+tail.lstrip('\n'),encoding='utf-8')


def main(book):
    file=book/'remediation/00-disposition-register.json'
    if not file.exists():return
    register=json.loads(file.read_text(encoding='utf-8'))
    if register['status']!='DESIGN_REVISION_COMPLETE_RUNTIME_NOT_RUN':return
    coverage=json.loads((book/'remediation/10-target-coverage.json').read_text(encoding='utf-8'))
    byid={i['issue_id']:i for i in register['issues']}
    for target in coverage['targets']:
        issues=[byid[k] for k in target['issue_ids']]
        body='## 当前设计修订入口（2026-10-11 R1）\n\n'
        if issues:
            body+='本模块有以下明确处置。编码时把本补充与准确原合同共同固定；下方归档整理中的旧待核描述保留来源背景，当前范围以本表决定为准。\n\n| 原登记编号 | 分类与当前决定 | 设计入口 |\n|---|---|---|\n'
            for i in issues:
                docs=[o for o in i['outputs'] if o.endswith('.md')]
                links=' / '.join('[设计](../'+o+')' for o in docs)
                body+='| '+', '.join(i['source_finding_ids'])+' | '+i['classification']+'：'+i['decision'].replace('|','\\|').replace('\n',' ')+' | '+links+' |\n'
        else:
            body+='本轮115项登记问题未要求修改本模块合同，复用原必要材料阅读与范围记录；不宣称重新全文阅读所有原件或运行已通过。\n'
        body+='\n[统一处置表](../remediation/00-disposition-register.json) / [验收目录](../remediation/08-acceptance-catalog.json) / [编码交接](../remediation/09-implementation-handoff.md)。实际采用仍 UNPROVEN、业务验收 NOT_RUN；原历史接受和缺件边界不变。'
        overlay(book/target['source_module'],body)
    intro='**当前版本：设计修订R1。** 115个原编号已逐项处置，去重后114个问题族，125个目标均有覆盖记录。新增设计与修订资产从[设计修订入口](remediation/README.md)阅读，按[编码交接](remediation/09-implementation-handoff.md)实施。实际采用 UNPROVEN、业务运行验收 NOT_RUN；缺失历史原件保持缺失。下方保留原归档整理的范围说明。'
    overlay(book/'README.md',intro)
    for name in ['开发者与AI阅读指南.md','模块目录.md']:
        overlay(book/name,intro)
    architecture={
      '01-系统边界与开发地图.md':('02-commercial-physical-contracts.md','ACC/ORD/DEM物理入口与Owner接缝已补；原业务权威边界不变。'),
      '02-事务回执与恢复.md':('03-transactions-and-recovery.md','共同ResourcePlan、所有最终写者、未知结果和取消close按R1准确合同采用。'),
      '03-资料版本与接受边界.md':('01-version-and-authority.md','R1为本次新设计；原历史接受及六台账字节不变，当前处置不可倒写成历史PASS。'),
      '04-关键身份与状态词典.md':('06-wire-types-and-fixtures.md','金额/数量/单位/null与native版本保无损；DEMO身份和真实Owner原件明确分开。'),
      '05-编码顺序与适配门.md':('09-implementation-handoff.md','原必要材料整理已完成；此次115项设计处置已形成，编码按新的依赖与实际采用门推进。')}
    for name,(doc,explanation) in architecture.items():
        path=book/'architecture'/name
        overlay(path,'**2026-10-11当前修订：** '+explanation+' [R1具体决定](../remediation/'+doc+')；实际运行仍NOT_RUN。')
    p=book/'architecture/05-编码顺序与适配门.md'
    text=p.read_text(encoding='utf-8').replace('125模块仍逐批补必要附件；','125模块现有必要材料已整理；指定历史独审缺件保留。')
    p.write_text(text,encoding='utf-8')
    navpath=book/'indexes/module-navigation.json'
    nav=json.loads(navpath.read_text(encoding='utf-8'))
    target_map={t['target_id']:t for t in coverage['targets']}
    rows=nav.get('modules',nav.get('targets',[]))
    for r in rows:
        if r.get('target_id') not in target_map:continue
        t=target_map[r['target_id']]
        r.setdefault('pre_revision_next_action',r.get('next_action'))
        r['design_revision']={'version':register['revision'],'issue_ids':t['issue_ids'],'coverage_status':t['coverage_status'],'runtime':'NOT_RUN'}
        r['next_action']='按remediation/09-implementation-handoff.md进入实际采用与编码；原件缺口和未读范围沿原记录保留。'
    nav['summary']['design_revision']={'version':register['revision'],'source_findings':115,'issue_families':114,'target_count':125,'runtime':'NOT_RUN'}
    navpath.write_text(json.dumps(nav,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'module_entries':125,'architecture_entries':len(architecture),'navigation_rows':len(rows)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--handbook',type=Path,required=True);main(p.parse_args().handbook)
