"""Apply pinned original RMA facts to the native journal of a closed Core candidate."""

import archive


PROFILE = "cp6-rma-core-candidate-original-journal-v1"
RECEIPT = "journal_receipt"
EXECUTOR = "cp6_rma_original_journal_executor_v1"
LEGACY_GATE = "rma_core_legacy_disabled"
REJECT_LEGACY = "cp6_rma_core_commission_v1.reject_legacy()"
JOURNAL = archive.JOURNAL_TABLE
INSTALLATION = archive.JOURNAL_INSTALLATION
STORE = "cp6_rma_original_journal_v1.store(uuid,uuid,uuid,text,bytea,text,bytea,uuid[],jsonb)"
COLUMNS = ", ".join(archive.JOURNAL_COLUMNS)
ROWS = "pg_temp.rma_original_import"


def layout_sql(plan):
    # Recompute the protected W361 JournalCatalogSha256 on the connection that will write.
    return f"""
    LOCK TABLE {JOURNAL} IN ACCESS EXCLUSIVE MODE;
    DO $journal_layout$
    DECLARE
        p jsonb := current_setting('{plan}')::jsonb;
        db_owner oid := to_regrole(quote_ident(current_user));
        journal oid := to_regclass('{JOURNAL}');
        installation oid := to_regclass('{INSTALLATION}');
        executor oid;
        fingerprint text;
    BEGIN
        IF current_setting('session_replication_role')<>'origin'
           OR NOT EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
               WHERE c.oid=installation AND c.relkind='r' AND c.relowner=db_owner AND n.nspowner=db_owner
                 AND NOT c.relrowsecurity
                 AND NOT EXISTS (SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>db_owner
                     AND (a.grantee=0 OR a.grantor<>db_owner OR a.is_grantable OR a.privilege_type<>'USAGE'))
                 AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>db_owner)
                 AND NOT EXISTS (SELECT FROM pg_attribute t,LATERAL aclexplode(t.attacl) a
                     WHERE t.attrelid=c.oid AND a.grantee<>db_owner)) THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-layout-rejected';
        END IF;
        LOCK TABLE {INSTALLATION} IN SHARE MODE;
        SELECT r.oid,encode(sha256(convert_to(jsonb_build_object(
            'relation',jsonb_build_array(c.oid,c.relowner,c.relacl,j.nspacl,s.nspacl),
            'columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attname,a.attacl) ORDER BY a.attnum)
                FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
            'policies',(SELECT jsonb_agg(jsonb_build_array(q.oid,q.polname,q.polcmd,q.polpermissive,q.polroles,
                pg_get_expr(q.polqual,c.oid),pg_get_expr(q.polwithcheck,c.oid)) ORDER BY q.polname)
                FROM pg_policy q WHERE q.polrelid=c.oid),
            'triggers',(SELECT jsonb_agg(jsonb_build_array(t.oid,t.tgenabled,pg_get_triggerdef(t.oid)) ORDER BY t.tgname)
                FROM pg_trigger t WHERE t.tgrelid=c.oid AND t.tgname IN ('authority_gate','immutable','{LEGACY_GATE}')),
            'store',jsonb_build_array(f.oid,f.proowner,f.proacl,pg_get_functiondef(f.oid)))::text,'UTF8')),'hex')
        INTO executor,fingerprint
        FROM pg_class c JOIN pg_namespace j ON j.oid=c.relnamespace
        JOIN pg_proc f ON f.oid=to_regprocedure('{STORE}')
        JOIN pg_namespace s ON s.oid=f.pronamespace JOIN pg_roles r ON r.oid=f.proowner
        WHERE c.oid=journal AND c.relowner=db_owner AND j.nspowner=db_owner AND s.nspowner=db_owner
          AND c.relkind='r' AND c.relpersistence='p' AND c.relrowsecurity AND c.relforcerowsecurity AND f.prosecdef
          AND r.rolname='{EXECUTOR}' AND NOT (r.rolcanlogin OR r.rolsuper OR r.rolbypassrls
              OR r.rolinherit OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
          AND NOT EXISTS (SELECT FROM pg_auth_members m WHERE m.roleid=r.oid OR m.member=r.oid);
        IF executor IS NULL OR NOT EXISTS (
            SELECT FROM {INSTALLATION} i CROSS JOIN cp6_rma_core_commission_v1.installation c
            WHERE i.singleton AND c.singleton AND i.commission_json=c.request_json
              AND i.request_json::jsonb-'OperationId'=c.request_json::jsonb-'OperationId'
              AND i.catalog_sha256=fingerprint
              AND i.result_json::jsonb->>'CatalogSha256'=fingerprint
              AND i.result_json::jsonb->>'Outcome'='INSTALLED'
              AND i.result_json::jsonb->>'Profile'='{archive.JOURNAL_PROFILE}'
              AND i.result_json::jsonb->>'InstallationId'=p->>'installationId'
              AND i.result_json::jsonb->>'DatabaseOid'=p->'key'->'target'->>'DatabaseOid'
              AND i.result_json::jsonb->>'SystemIdentifier'=p->'key'->'target'->>'SystemIdentifier'
              AND i.result_json::jsonb->>'OperationId'=i.request_json::jsonb->>'OperationId'
              AND i.result_json::jsonb->>'CommissionOperationId'=c.commission_receipt::jsonb->>'OperationId')
           OR NOT EXISTS (SELECT FROM pg_trigger t JOIN pg_proc f ON f.oid=t.tgfoid
               WHERE t.tgrelid=journal AND t.tgname='{LEGACY_GATE}' AND t.tgenabled='A'
                 AND NOT t.tgisinternal AND t.tgtype=7 AND t.tgfoid=to_regprocedure('{REJECT_LEGACY}')
                 AND f.proowner=db_owner AND NOT f.prosecdef
                 AND f.proconfig=ARRAY['search_path=pg_catalog']
                 AND btrim(regexp_replace(f.prosrc,'[[:space:]]+',' ','g'))=
                     'BEGIN RAISE EXCEPTION ''RMA_SHARED_AUTHORITY_RETIRED'' USING ERRCODE=''42501''; END'
                 AND pg_get_triggerdef(t.oid)=format(
                     'CREATE TRIGGER {LEGACY_GATE} BEFORE INSERT ON {JOURNAL} FOR EACH ROW '
                     'WHEN (((new.owner = %L::text) AND ((to_regrole(quote_ident((CURRENT_USER)::text)))::oid '
                     'IS DISTINCT FROM (%s)::oid))) EXECUTE FUNCTION {REJECT_LEGACY}',
                     '{archive.JOURNAL_OWNER}',executor))
           OR (SELECT count(*) FROM pg_trigger t WHERE t.tgrelid=journal AND NOT t.tgisinternal)<>6
           OR EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=journal AND t.tgisinternal AND (t.tgtype&4)<>0)
           OR (SELECT count(*) FROM (VALUES
                 ('authority_gate','cp6_authentication_v1.before_change()',62,false),
                 ('immutable','cp6_main_owner_v1.immutable()',58,false),
                 ('document_availability_complete','cp6_controlled_document_v1.availability_complete()',5,true),
                 ('requirement_impact_effect_complete','cp6_requirement_impact_v1.complete()',5,true),
                 ('location_rule_withdrawal_command_complete','cp6_location_rule_review_control_v1.command_complete()',5,true)
               ) expected(name,procedure,trigger_type,deferred) JOIN pg_trigger t
                 ON t.tgrelid=journal AND t.tgname=expected.name AND t.tgfoid=to_regprocedure(expected.procedure)
                 AND t.tgtype=expected.trigger_type AND t.tgenabled IN ('O','A') AND t.tgqual IS NULL
                 AND t.tgdeferrable=expected.deferred AND t.tginitdeferred=expected.deferred AND NOT t.tgisinternal)<>5
           OR EXISTS (SELECT FROM pg_rewrite WHERE ev_class=journal)
           OR EXISTS (SELECT FROM pg_inherits WHERE inhrelid=journal OR inhparent=journal)
           OR EXISTS (SELECT FROM pg_index WHERE indrelid=journal AND (indexprs IS NOT NULL OR indpred IS NOT NULL))
           OR (SELECT array_agg(attname::text||':'||atttypid::regtype::text ORDER BY attnum) FROM pg_attribute
               WHERE attrelid=journal AND attnum>0 AND NOT attisdropped) IS DISTINCT FROM
               ARRAY['tenant_id:uuid','owner:text','operation_id:uuid','actor_id:uuid','contract:text','state:text',
                     'input_bytes:bytea','input_sha256:text','result_bytes:bytea','object_ids:uuid[]','audit_json:jsonb',
                     'committed_at:timestamp with time zone']
           OR NOT EXISTS (SELECT FROM pg_constraint WHERE conrelid=journal AND contype='p' AND convalidated
                     AND conkey=ARRAY[1,2,3]::smallint[]) THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-layout-rejected';
        END IF;
    END
    $journal_layout$;
    """


def import_sql(schema, plan, receipts):
    return layout_sql(plan) + f"""
    CREATE TEMP TABLE rma_original_import (LIKE {JOURNAL} INCLUDING CONSTRAINTS,
        PRIMARY KEY (tenant_id,owner,operation_id)) ON COMMIT DROP;
    \\copy {ROWS} ({COLUMNS}) FROM 'bundle/{archive.JOURNAL_MEMBER}' WITH (FORMAT csv, HEADER MATCH)
    -- Keep the original Owner-specific AFTER INSERT validators active. They must finish before ALTER TABLE.
    SET CONSTRAINTS ALL IMMEDIATE;
    DO $journal_import$
    DECLARE
        p jsonb := current_setting('{plan}')::jsonb;
        tenant uuid := (p->'key'->>'tenantId')::uuid;
        counts jsonb := p->'journalCounts';
        original_gate oid;
        original_definition text;
    BEGIN
        IF EXISTS (SELECT FROM {JOURNAL} WHERE tenant_id=tenant AND owner='{archive.JOURNAL_OWNER}') THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-not-empty';
        END IF;
        IF (SELECT ARRAY[count(*),count(*) FILTER (WHERE state='COMMITTED'),count(*) FILTER (WHERE state='CLOSED')]
            FROM {ROWS}) IS DISTINCT FROM ARRAY[(counts->>'rows')::bigint,(counts->>'committed')::bigint,
                                               (counts->>'closed')::bigint]
           OR EXISTS (SELECT FROM {ROWS} WHERE tenant_id IS DISTINCT FROM tenant
                        OR owner IS DISTINCT FROM '{archive.JOURNAL_OWNER}'
                        OR contract IS DISTINCT FROM '{archive.JOURNAL_CONTRACT}') THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-rows-rejected';
        END IF;
        IF EXISTS (
            WITH originals AS MATERIALIZED (
                SELECT j.*,convert_from(result_bytes,'UTF8')::jsonb AS result FROM {ROWS} j)
            SELECT FROM originals j
            WHERE jsonb_typeof(result) IS DISTINCT FROM 'object'
               OR result->>'OperationId' IS DISTINCT FROM operation_id::text
               OR CASE WHEN j.state='CLOSED' THEN
                      result->>'State' IS DISTINCT FROM 'CLOSED' OR result->>'Result' IS NOT NULL
                      OR cardinality(object_ids)<>0
                  ELSE input_sha256 IS DISTINCT FROM encode(sha256(input_bytes),'hex')
                      OR NOT EXISTS (SELECT FROM {schema}.versions v WHERE v.tenant_id=j.tenant_id
                          AND j.object_ids=ARRAY[v.id] AND result @> jsonb_build_object(
                              'ApplicationId',v.id,'Version',v.version,'State',v.state,'Etag',
                              format('"rma-application-%s-%s"',replace(v.id::text,'-',''),v.version)))
                  END) THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-graph-rejected';
        END IF;
        SELECT t.oid,pg_get_triggerdef(t.oid) INTO STRICT original_gate,original_definition FROM pg_trigger t
            WHERE t.tgrelid='{JOURNAL}'::regclass AND t.tgname='{LEGACY_GATE}';
        -- Only the exact legacy INSERT rejection is suspended, under the exclusive journal lock.
        ALTER TABLE ONLY {JOURNAL} DISABLE TRIGGER {LEGACY_GATE};
        INSERT INTO {JOURNAL} ({COLUMNS}) SELECT {COLUMNS} FROM {ROWS};
        ALTER TABLE ONLY {JOURNAL} ENABLE ALWAYS TRIGGER {LEGACY_GATE};
        IF NOT EXISTS (SELECT FROM pg_trigger t WHERE t.oid=original_gate AND t.tgenabled='A'
                         AND pg_get_triggerdef(t.oid)=original_definition)
           OR EXISTS (SELECT {COLUMNS} FROM {ROWS} EXCEPT
               SELECT {COLUMNS} FROM {JOURNAL} WHERE tenant_id=tenant AND owner='{archive.JOURNAL_OWNER}') THEN
            RAISE EXCEPTION 'rma-stage-candidate-journal-import-rejected';
        END IF;
    END
    $journal_import$;
    CREATE TABLE {receipts}.{RECEIPT} (
        singleton boolean PRIMARY KEY CHECK (singleton), input jsonb NOT NULL, result_json text NOT NULL);
    REVOKE ALL ON {receipts}.{RECEIPT} FROM PUBLIC;
    INSERT INTO {receipts}.{RECEIPT}
    SELECT true,p->'key',json_build_object('outcome','IMPORTED','recoveryReady',false,'profile','{PROFILE}',
        'binding',p->'key','format',p->>'format','journalCounts',p->'journalCounts')::text
    FROM (SELECT current_setting('{plan}')::jsonb p) s;
    SELECT result_json FROM {receipts}.{RECEIPT};
    """
