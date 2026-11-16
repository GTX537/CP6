"""Render SQL to canonicalize EF partial-index predicates in an isolated PostgreSQL database.

Only empty temporary tables/indexes are created; all are rolled back. Input must be
the adopted ModelProbe output. Pipe the generated SQL to psql -X -q -At with
ON_ERROR_STOP=1 and save its single JSON result for build_inventory.py --predicates.
"""
import json
from pathlib import Path
import sys


def quote(value):
    return '"'+value.replace('"','""')+'"'


def literal(value):
    return "'"+value.replace("'","''")+"'"


def render(models):
    yield 'BEGIN; SET LOCAL standard_conforming_strings=on;'
    yield 'CREATE TEMP TABLE inventory_predicates(schema_name text, table_name text, index_name text, predicate text) ON COMMIT DROP;'
    seen=set()
    for context in models['contexts']:
        if context['profile']!='fixed-shared':
            continue
        for table in context['tables']:
            for index in table['unique_indexes']:
                key=(table['schema'],table['name'],index['name'])
                if not index['filter'] or key in seen:
                    continue
                seen.add(key)
                yield 'CREATE TEMP TABLE inventory_subject (LIKE '+quote(table['schema'])+'.'+quote(table['name'])+');'
                yield 'CREATE UNIQUE INDEX inventory_subject_index ON inventory_subject ('+','.join(map(quote,index['columns']))+') WHERE '+index['filter']+';'
                yield 'INSERT INTO inventory_predicates SELECT '+','.join(map(literal,key))+",pg_get_expr(indpred,indrelid) FROM pg_index WHERE indexrelid='pg_temp.inventory_subject_index'::regclass;"
                yield 'DROP TABLE inventory_subject;'
    yield "SELECT coalesce(json_agg(json_build_object('schema',schema_name,'table',table_name,'index',index_name,'predicate',predicate) ORDER BY schema_name,table_name,index_name),'[]'::json) FROM inventory_predicates;"
    yield 'ROLLBACK;'


if __name__=='__main__':
    models=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    print('\n'.join(render(models)))
