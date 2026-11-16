-- Metadata only. Run against an isolated Main DatabaseInit database, never a business dump.
-- The two fixture namespaces belong to pg_resource.py, not CP6.
WITH namespaces AS (
  SELECT oid, nspname, nspowner, nspacl FROM pg_namespace
  WHERE nspname <> 'information_schema' AND nspname !~ '^pg_'
    AND nspname NOT IN ('w51_guard', 'fixture')
), relations AS (
  SELECT c.*, n.nspname FROM pg_class c JOIN namespaces n ON n.oid = c.relnamespace
  WHERE c.relkind IN ('r','p','v','m','S','f')
)
SELECT jsonb_build_object(
  'server_version', current_setting('server_version'),
  'schemas', (SELECT jsonb_agg(jsonb_build_object('name',nspname,
      'owner',pg_get_userbyid(nspowner),'acl',nspacl::text) ORDER BY nspname) FROM namespaces),
  'relations', (SELECT jsonb_agg(jsonb_build_object(
    'schema',r.nspname,'name',r.relname,'kind',r.relkind,
    'owner',pg_get_userbyid(r.relowner),'acl',r.relacl::text,
    'rls_enabled',r.relrowsecurity,'rls_forced',r.relforcerowsecurity,
    'system_columns',(SELECT jsonb_agg(jsonb_build_object('name',a.attname,
      'type',format_type(a.atttypid,a.atttypmod),'not_null',a.attnotnull) ORDER BY a.attnum)
      FROM pg_attribute a WHERE a.attrelid=r.oid AND a.attnum<0 AND a.attname='xmin'),
    'columns',(SELECT coalesce(jsonb_agg(jsonb_build_object(
      'name',a.attname,'type',format_type(a.atttypid,a.atttypmod),'not_null',a.attnotnull,
      'identity',a.attidentity,'generated',a.attgenerated,
      'default',pg_get_expr(d.adbin,d.adrelid),'acl',a.attacl::text,
      'collation',CASE WHEN a.attcollation <> 0 THEN a.attcollation::regcollation::text END
      ) ORDER BY a.attnum),'[]'::jsonb) FROM pg_attribute a
      LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
      WHERE a.attrelid=r.oid AND a.attnum>0 AND NOT a.attisdropped),
    'constraints',(SELECT coalesce(jsonb_agg(jsonb_build_object(
      'name',c.conname,'kind',c.contype,'definition',pg_get_constraintdef(c.oid,true),
      'validated',c.convalidated,'deferrable',c.condeferrable,'deferred',c.condeferred,
      'columns', (SELECT jsonb_agg(a.attname ORDER BY k.ord) FROM unnest(c.conkey) WITH ORDINALITY k(num,ord)
        JOIN pg_attribute a ON a.attrelid=r.oid AND a.attnum=k.num),
      'referenced_schema',fn.nspname,'referenced_table',fc.relname
      ) ORDER BY c.conname),'[]'::jsonb) FROM pg_constraint c
      LEFT JOIN pg_class fc ON fc.oid=c.confrelid LEFT JOIN pg_namespace fn ON fn.oid=fc.relnamespace
      WHERE c.conrelid=r.oid),
    'indexes',(SELECT coalesce(jsonb_agg(jsonb_build_object(
      'name',ic.relname,'unique',i.indisunique,'primary',i.indisprimary,
      'valid',i.indisvalid,'nulls_not_distinct',i.indnullsnotdistinct,
      'definition',pg_get_indexdef(i.indexrelid),
      'predicate',pg_get_expr(i.indpred,i.indrelid),
      'key_columns',(SELECT jsonb_agg(pg_get_indexdef(i.indexrelid,k,false) ORDER BY k)
        FROM generate_series(1,i.indnkeyatts) k)
      ) ORDER BY ic.relname),'[]'::jsonb) FROM pg_index i JOIN pg_class ic ON ic.oid=i.indexrelid WHERE i.indrelid=r.oid),
    'triggers',(SELECT coalesce(jsonb_agg(jsonb_build_object(
      'name',t.tgname,'enabled',t.tgenabled,'definition',pg_get_triggerdef(t.oid,true),
      'function_schema',pn.nspname,'function',p.proname,
      'function_arguments',pg_get_function_identity_arguments(p.oid)
      ) ORDER BY t.tgname),'[]'::jsonb) FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid
      JOIN pg_namespace pn ON pn.oid=p.pronamespace WHERE t.tgrelid=r.oid AND NOT t.tgisinternal),
    'policies',(SELECT coalesce(jsonb_agg(jsonb_build_object(
      'name',p.polname,'command',p.polcmd,'permissive',p.polpermissive,
      'roles',(SELECT jsonb_agg(CASE WHEN roleid=0 THEN 'PUBLIC' ELSE pg_get_userbyid(roleid)::text END) FROM unnest(p.polroles) roleid),
      'using',pg_get_expr(p.polqual,p.polrelid),'check',pg_get_expr(p.polwithcheck,p.polrelid)
      ) ORDER BY p.polname),'[]'::jsonb) FROM pg_policy p WHERE p.polrelid=r.oid),
    'sequence', (SELECT jsonb_build_object('type',format_type(s.seqtypid,null),'start',s.seqstart,
      'increment',s.seqincrement,'min',s.seqmin,'max',s.seqmax,'cache',s.seqcache,'cycle',s.seqcycle)
      FROM pg_sequence s WHERE s.seqrelid=r.oid),
    'sequence_dependents',(SELECT coalesce(jsonb_agg(jsonb_build_object('schema',dn.nspname,'table',dc.relname,
      'column',a.attname,'dependency',d.deptype) ORDER BY dn.nspname,dc.relname,a.attname),'[]'::jsonb)
      FROM pg_depend d JOIN pg_class dc ON dc.oid=d.refobjid JOIN pg_namespace dn ON dn.oid=dc.relnamespace
      LEFT JOIN pg_attribute a ON a.attrelid=dc.oid AND a.attnum=d.refobjsubid
      WHERE r.relkind='S' AND d.classid='pg_class'::regclass AND d.objid=r.oid AND d.refclassid='pg_class'::regclass),
    'view_definition',CASE WHEN r.relkind IN ('v','m') THEN pg_get_viewdef(r.oid,true) END
  ) ORDER BY r.nspname,r.relname) FROM relations r),
  'routines',(SELECT coalesce(jsonb_agg(jsonb_build_object(
    'schema',n.nspname,'name',p.proname,'kind',p.prokind,
    'arguments',pg_get_function_identity_arguments(p.oid),'result',pg_get_function_result(p.oid),
    'owner',pg_get_userbyid(p.proowner),'acl',p.proacl::text,'security_definer',p.prosecdef,
    'settings',p.proconfig,'language',l.lanname,'definition',pg_get_functiondef(p.oid)
    ) ORDER BY n.nspname,p.proname,pg_get_function_identity_arguments(p.oid)),'[]'::jsonb)
    FROM pg_proc p JOIN namespaces n ON n.oid=p.pronamespace JOIN pg_language l ON l.oid=p.prolang
    WHERE p.prokind IN ('f','p')),
  'types',(SELECT coalesce(jsonb_agg(jsonb_build_object('schema',n.nspname,'name',t.typname,
    'kind',t.typtype,'owner',pg_get_userbyid(t.typowner),
    'base_type',CASE WHEN t.typbasetype<>0 THEN format_type(t.typbasetype,t.typtypmod) END,
    'enum_labels',(SELECT jsonb_agg(e.enumlabel ORDER BY e.enumsortorder) FROM pg_enum e WHERE e.enumtypid=t.oid)
    ) ORDER BY n.nspname,t.typname),'[]'::jsonb) FROM pg_type t JOIN namespaces n ON n.oid=t.typnamespace
    WHERE t.typtype IN ('d','e','r','m') OR t.typtype='c' AND EXISTS(SELECT 1 FROM pg_class c WHERE c.oid=t.typrelid AND c.relkind='c')),
  'default_privileges',(SELECT coalesce(jsonb_agg(jsonb_build_object('owner',pg_get_userbyid(d.defaclrole),
    'schema',n.nspname,'object_type',d.defaclobjtype,'acl',d.defaclacl::text)
    ORDER BY pg_get_userbyid(d.defaclrole),n.nspname,d.defaclobjtype),'[]'::jsonb)
    FROM pg_default_acl d LEFT JOIN pg_namespace n ON n.oid=d.defaclnamespace),
  'extensions',(SELECT jsonb_agg(jsonb_build_object('name',e.extname,'schema',n.nspname,'version',e.extversion) ORDER BY e.extname)
    FROM pg_extension e JOIN pg_namespace n ON n.oid=e.extnamespace)
);
