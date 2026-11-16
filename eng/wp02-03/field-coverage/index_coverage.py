"""索引封存目录和 MVC 描述；字面引用仅用于导航，不证明行为覆盖。"""
import argparse
import json
import re
import subprocess
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--metadata', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    catalog = json.loads((args.metadata / 'catalog.json').read_text(encoding='utf-8'))
    routes = json.loads((args.metadata / 'routes.json').read_text(encoding='utf-8'))
    source = root / 'CP6.Core/Services/Integration/OperationsOwnerV1.cs'
    constants = dict(re.findall(r'\b(\w+)\s*=\s*"([a-z][a-z0-9-]+)"', source.read_text(encoding='utf-8')))
    symbols = {}
    for name, resource in constants.items():
        symbols.setdefault(resource, []).append('OperationsOwnerV1.' + name)
    paths = subprocess.check_output(['git', '-c', 'core.longpaths=true', 'ls-files', '--cached', '--others', '--exclude-standard', '--',
                                    'CP6.Core/Services', 'CP6.WebApi/Controllers', 'CP6.WebApi/Services'],
                                   cwd=root, text=True, encoding='utf-8').splitlines()
    texts = {path: (root / path).read_text(encoding='utf-8-sig').splitlines()
             for path in paths if path.endswith('.cs')}
    aliases = {'order': ['erp-order', 'order-list', 'erp-order-price-correction'],
               'product': ['erp-product'], 'uom-profile': ['erp-uom-profile'],
               'currency-policy': ['erp-currency-policy'], 'approval-binding': ['oa-flow-admin']}
    records = []
    for entry in catalog['resources']:
        resource, fields = entry['resource'], entry['fields']
        needles = ['"' + name + '"' for name in [resource] + aliases.get(resource, [])] + symbols.get(resource, [])
        refs = []
        for path, lines in texts.items():
            for line, text in enumerate(lines, 1):
                if any(needle in text for needle in needles):
                    refs.append({'path': path, 'line': line})
                    break
        menus = [resource] + aliases.get(resource, [])
        direct = [i for i, route in enumerate(routes, 1) if any(
            a['type'].endswith('.RequirePermissionAttribute') and a['arguments'][0] in menus
            for a in route['attributes'])]
        group = ('scope-only' if fields is None else 'empty-field-domain' if not fields else
                 'order' if resource == 'order' else 'inquiry' if resource.startswith('crm-') else
                 'personal-form' if resource == 'workflow-personal-form' else
                 'inventory-observation' if resource in ('wms-dashboard', 'wms-expiry', 'wms-stock-dwell', 'wms-vmi') else
                 'authoring' if resource in ('approval-binding', 'workflow-definition', 'workflow-form-definition') else
                 'file-download' if resource in ('file-content', 'plm-controlled-document') else
                 'report-center' if resource == 'wms-report-center' else
                 'operations' if resource in symbols else 'consumer-review')
        uses = dict.fromkeys(('query', 'write', 'export', 'download'), 'PENDING_CONSUMER_REVIEW')
        if group == 'empty-field-domain':
            uses = dict.fromkeys(uses, 'NO_REGISTERED_CONTROLLED_FIELDS')
        elif group == 'scope-only':
            uses = dict.fromkeys(uses, 'SCOPE_ONLY_REGISTRATION')
        elif group == 'order':
            uses = dict.fromkeys(uses, 'EXPLICIT_OWNER_MAPPING_WITH_BOUNDED_EVIDENCE')
        elif group == 'personal-form':
            uses.update(query='CURRENT_OWNER_MASK_AND_FINAL_GATE',
                        write='CURRENT_OWNER_MASK_AND_FINAL_GATE',
                        export='NO_EXPORT_ENTRY_IN_REVIEWED_CONTROLLERS',
                        download='ADOPTED_V2_CONTEXTUAL_FILE_READ_NOT_INDEPENDENT_USE')
        elif group == 'inventory-observation':
            uses.update(query='REGISTERED_FIELDS_AND_SOURCE_GATES_REVIEWED',
                        write='LEGACY_MUTATION_EXPLICITLY_CLOSED_ON_POSTGRESQL'
                        if resource in ('wms-expiry', 'wms-vmi') else 'NO_WRITE_ENTRY_IN_REVIEWED_CONTROLLERS',
                        export='NO_EXPORT_ENTRY_IN_REVIEWED_CONTROLLERS',
                        download='NO_DOWNLOAD_ENTRY_IN_REVIEWED_CONTROLLERS')
        elif group in ('operations', 'authoring', 'file-download', 'report-center'):
            uses.update(query='READ_MAPPING_PRESENT', write='WRITE_MAPPING_PRESENT')
            if group == 'report-center':
                uses['export'] = 'LEGACY_CSV_EXPLICITLY_CLOSED_ON_POSTGRESQL'
            if group == 'file-download':
                uses['download'] = 'ADOPTED_V1_CURRENT_READ_GATE_REQUIRES_USE_RECONCILIATION'
        elif group == 'inquiry':
            uses['write'] = 'EXPLICIT_SOURCE_MAPPING_WITH_BOUNDED_EVIDENCE'
            if resource == 'crm-privacy':
                uses.update(export='EXPLICIT_SOURCE_MAPPING_WITH_BOUNDED_EVIDENCE',
                            download='EXPLICIT_SOURCE_MAPPING_WITH_BOUNDED_EVIDENCE')
        if resource in ('wms-warehouse', 'wms-location'):
            uses.update(query='CURRENT_MASTER_OWNER_REVIEWED_OTHER_CONSUMERS_PENDING',
                        write='NAME_AND_DRAFT_FIELD_GATES_REVIEWED_OTHER_CONSUMERS_PENDING')
        elif resource == 'erp-business-partner':
            uses.update(query='CURRENT_IDENTITY_AND_PROFILE_QUERY_SCOPED',
                        write='CURRENT_ACTION_SCOPE_FIELDS_AND_PROFILE_READBACK',
                        export='INDEPENDENT_EXPORT_ACTION_AND_FIELD_USE',
                        download='NO_DOWNLOAD_ENTRY_IN_REVIEWED_CONTROLLERS')
        elif resource in ('wms-stock', 'wms-inbound-order', 'wms-inbound-receipt', 'wms-outbound-order'):
            uses.update(query='CURRENT_OWNER_UNIT_PRICE_SCOPED',
                        write=('CURRENT_TARGET_AND_ALLOCATION_SOURCE_PRICE_GATES'
                               if resource == 'wms-outbound-order' else 'CURRENT_OWNER_UNIT_PRICE_WRITE'),
                        export='NO_EXPORT_ENTRY_IN_REVIEWED_CONTROLLERS',
                        download='NO_DOWNLOAD_ENTRY_IN_REVIEWED_CONTROLLERS')
        elif resource == 'mes-process-cost-rate':
            uses.update(query='CURRENT_RATE_OWNER_AND_COST_HISTORY_SOURCE_GATES',
                        write='CURRENT_RATE_FIELDS_VERSION_AND_PERIOD',
                        export='NO_EXPORT_ENTRY_IN_REVIEWED_CONTROLLERS',
                        download='NO_DOWNLOAD_ENTRY_IN_REVIEWED_CONTROLLERS')
        elif resource == 'fin-cost':
            uses.update(query='CURRENT_FINANCE_VIEW_AND_RATE_SOURCE_GATES',
                        write='CURRENT_FINANCE_COLLECT_AND_SOURCE_GATES',
                        export='NO_EXPORT_ENTRY_IN_REVIEWED_CONTROLLERS',
                        download='NO_DOWNLOAD_ENTRY_IN_REVIEWED_CONTROLLERS')
        records.append({**entry, 'review_group': group, 'uses': uses,
                        'lexical_source_candidates': refs, 'permission_attribute_route_ids': direct})
    args.output.mkdir(parents=True, exist_ok=True)
    def write(name, values):
        with (args.output / name).open('w', encoding='utf-8', newline='\n') as stream:
            for value in values:
                stream.write(json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n')
    write('resources.jsonl', records)
    write('routes.jsonl', ({'id': i, 'controller': r['controller'], 'method': r['method'],
                            'route': r['route'], 'verbs': r['verbs'], 'attributes': r['attributes']}
                           for i, r in enumerate(routes, 1)))
    print(json.dumps({'resources': len(records), 'controlled_fields': sum(len(r['fields'] or []) for r in records),
                      'mvc_descriptors': len(routes), 'groups': {g: sum(r['review_group'] == g for r in records)
                      for g in sorted({r['review_group'] for r in records})}}))


if __name__ == '__main__':
    main()
