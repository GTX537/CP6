"""Concrete candidate relational constraints; not executable DDL or migration."""
import re


def snake(value):
    return re.sub(r'(?<!^)(?=[A-Z])','_',value).lower()


def enrich(entities, module, original):
    entities_by_name={e['entity']:e for e in entities}
    tenancy=module('PLM-ITER-01',37,48)
    registry={'entity':'OwnerKey','target':'SHARED_OWNER_INFRASTRUCTURE','owner':'original business Owner',
      'fields':[],'physical_location':'shared Owner identity domain; not per-tenant copies of a global unique domain',
      'columns':[{'name':n,'postgresql':t,'nullable':null,'source_pin':tenancy} for n,t,null in [
        ('storage_id','uuid',False),('tenant_id','uuid',False),('domain_key','bytea',False),('scope_bytes','bytea',False),
        ('schema_bytes','bytea',False),('identity_bytes','bytea',False),('bucket_digest','bytea',False)]],
      'primary_key':{'columns':['storage_id'],'scope':'global internal generated identity, never a conversion of original bytes'},
      'unique_constraints':[{'name':'owner_key_tenant_id','columns':['tenant_id','storage_id'],'scope':'FK support; does not narrow global storage_id PK','enforcement':'POSTGRESQL_UNIQUE'}],
      'foreign_keys':[],
      'indexes':[{'name':'owner_key_bucket','columns':['bucket_digest'],'unique':False}],
      'checks':[{'name':'owner_key_exact_intern','columns':['tenant_id','domain_key','scope_bytes','schema_bytes','identity_bytes','bucket_digest'],
        'enforcement':'OWNER_SERIALIZED_FULL_BYTE_COMPARISON','rule':'Length-prefixed tuple domain/schema/scope/original bytes determines bucket. Lock bucket; compare complete tuple, return existing key or append distinct UUID. All writers/import/recovery use the same transaction lock. Hash collision never means same identity; no unbounded bytea btree UQ.'}],
      'cas':{'mode':'INSERT_ONLY','conflict_resolution':'Under bucket lock return exact existing tuple only; never change identity bytes'},
      'delete_rule':'RESTRICT','runtime':'NOT_RUN'}
    entities.append(registry);entities_by_name['OwnerKey']=registry

    def columns(e):return {c['name']:c for c in e['columns']}
    def add_column(e,name,typ='uuid',nullable=False,source=None):
        if name not in columns(e):
            e['columns'].append({'name':name,'postgresql':typ,'nullable':nullable,'source_pin':source or e['constraint_source'],
                'origin':'NEW_CANDIDATE_PHYSICAL_COLUMN; source specifies logical duty, not this SQL column name'})
    def uq(e,name,cols,scope,predicate=None):
        item={'name':name,'columns':cols,'scope':scope,'enforcement':'POSTGRESQL_UNIQUE','source_pin':e['constraint_source']}
        if predicate:item.update(predicate=predicate,predicate_columns=['outcome'])
        e['unique_constraints'].append(item)
    def fk(e,column,target,nullable=False,extra_scope=False):
        add_column(e,column,nullable=nullable)
        cols=['tenant_id',column];targetcols=['tenant_id','storage_id']
        if extra_scope:
            add_column(e,'iteration_storage_id')
            cols=['tenant_id','iteration_storage_id',column];targetcols=['tenant_id','iteration_storage_id','storage_id']
        e['foreign_keys'].append({'columns':cols,'target_entity':target,'target_columns':targetcols,
            'nullable':nullable,'on_delete':'RESTRICT','deferrable':'INITIALLY_DEFERRED only for atomic creation cycles; checked at commit',
            'scope':'same TenantId; exact stored immutable parent row','source_pin':e['constraint_source']})
    def key(e,name,domain):
        fk(e,name,'OwnerKey')
        columns(e)[name]['identity_binding']={'domain':domain,'original_bytes':'retained in OwnerKey.identity_bytes with exact schema/scope',
            'admission':'Trusted Owner resolves exact raw identity under registry lock; external Owner truth still needs its original provider proof'}

    for e in entities[:-1]:
        e['constraint_source']=next(f['source_pin'] for f in e['fields'] if f.get('field')!='storage_id')
        e['columns']=[];e['foreign_keys']=[];e['indexes']=[];e['unique_constraints']=[];e['checks']=[]
        for field in e['fields']:
            name=field.get('candidate_column') or snake(field['field']);field['candidate_column']=name
            profile=field['storage_profile']
            typ={'tenant':'uuid','local_id':'uuid','cas':'uuid'}.get(profile,'bytea')
            add_column(e,name,typ,nullable=field.get('nullability')!='NOT NULL',source=field['source_pin'])
            field['physical_columns']=[name]
            if profile=='exact_number':
                for suffix,typ2 in [('sign','smallint'),('coefficient','bytea'),('exponent','bytea'),('numeric_projection','numeric')]:
                    add_column(e,name+'_'+suffix,typ2,nullable=True,source=field['source_pin']);field['physical_columns'].append(name+'_'+suffix)
        for name in ['storage_id','tenant_id']:
            add_column(e,name);columns(e)[name].update(postgresql='uuid',nullable=False)
        e['primary_key']={'columns':['storage_id'],'scope':'global internal row identity; original identity stays in separately scoped OwnerKey'}
        uq(e,e['entity'].lower()+'_tenant_row',['tenant_id','storage_id'],'Composite FK support without narrowing global PK')
        add_column(e,'concurrency_token','uuid')
        e['cas']={'mode':'OWNER_CURRENT_ROW_CAS_WITH_IMMUTABLE_PAYLOAD_GUARD','predicate_columns':['tenant_id','storage_id','concurrency_token'],
            'new_token':'fresh Owner UUID only after current authority/expected token passes',
            'immutable_columns':'Original signed/reviewed payload, identities and versions never UPDATE. Successor creates a new storage_id; token governs only mutable control/projection columns.',
            'zero_rows':'stale/conflict; no success receipt; unknown commit queries original operation'}
        e['physical_location']='Shared Owner domain until every original global PK/UQ domain is proven; only proven tenant-local tables may move to tenant schema under cp6-pg-tenancy-v1.'
        e['external_reference_checks']='Original external Owner/schema/ref/version bytes remain opaque; local FK to OwnerKey only proves local identity binding, not remote existence or adoption.'

    def entity(name):return entities_by_name[name]
    # Parent columns are individually declared and target concrete keys.
    for name in ['IterationInput','Candidate','EngineeringTask','ReviewRound','Evaluation','ExternalRequestReceipt','AuditClosureAmendment']:
        fk(entity(name),'iteration_storage_id','Iteration')
        uq(entity(name),name.lower()+'_iteration_row',['tenant_id','iteration_storage_id','storage_id'],'Exact same-iteration FK support')
    fk(entity('Iteration'),'project_storage_id','Project')
    fk(entity('Iteration'),'predecessor_storage_id','Iteration',nullable=True)
    fk(entity('CandidateRef'),'candidate_storage_id','Candidate')
    fk(entity('ReviewIssue'),'review_round_storage_id','ReviewRound')
    fk(entity('ReviewCandidateAggregate'),'review_round_storage_id','ReviewRound')
    fk(entity('RoundBinding'),'review_round_storage_id','ReviewRound')
    fk(entity('RoundBinding'),'aggregate_storage_id','ReviewCandidateAggregate')
    fk(entity('ReviewSnapshot'),'binding_storage_id','RoundBinding')
    fk(entity('OwnerApplication'),'binding_storage_id','RoundBinding')
    fk(entity('OwnerApplication'),'inbox_storage_id','OwnerInbox')
    fk(entity('OwnerApplicationReceipt'),'application_storage_id','OwnerApplication')
    for name,parent,target in [
        ('UseAuthorization','request_storage_id','UseRequest'),('Reservation','execution_intent_storage_id','ExecutionIntent'),
        ('Reservation','authorization_storage_id','UseAuthorization'),('MeasurementFact','execution_intent_storage_id','ExecutionIntent'),
        ('ExceptionCorrection','reservation_storage_id','Reservation'),('NoFurtherExecutionProof','execution_intent_storage_id','ExecutionIntent'),
        ('ReleaseTarget','release_storage_id','ReleaseRecord'),('TargetReceipt','release_target_storage_id','ReleaseTarget'),
        ('DeliveryAttempt','release_target_storage_id','ReleaseTarget'),('ActualAdoption','release_target_storage_id','ReleaseTarget')]:fk(entity(name),parent,target)

    unique_specs={
      'Project':(['project_no_key'],'TenantId + exact ProjectNo; original tenant-wide business uniqueness'),
      'Iteration':(['iteration_no_key'],'TenantId + exact IterationNo, not narrowed to Project'),
      'IterationInput':(['iteration_storage_id','input_version_key'],'One immutable input version per iteration'),
      'Candidate':(['iteration_storage_id','candidate_identity_key','candidate_version_key'],'Exact candidate identity/version within iteration'),
      'CandidateRef':(['candidate_storage_id','reference_tuple_key'],'Owner/object/revision/Occurrence/applicability tuple; same material in different Occurrences remains distinct'),
      'EngineeringTask':(['iteration_storage_id','task_identity_key'],'Task identity within iteration'),
      'ReviewRound':(['iteration_storage_id','round_no_key'],'RoundNo unique within iteration; append monotonic exact integer under iteration head lock, no MAX+1 race'),
      'ReviewIssue':(['review_round_storage_id','issue_identity_key'],'Issue identity bound to exact review round'),
      'Evaluation':(['iteration_storage_id','evaluation_identity_key'],'Immutable evaluation identity; successor evaluations remain history, current evaluation uniqueness lives on Iteration pointer'),
      'ExternalRequestReceipt':(['operation_type_key','aggregate_source_scope_key','request_key_id'],'Original (TenantId,OperationType,Aggregate/SourceScope,RequestKey); not narrowed to iteration'),
      'AuditClosureAmendment':(['event_identity_key'],'Original immutable Owner event identity'),
      'UseRequest':(['source_owner_key','request_scope_key','request_key_id'],'Original Owner request scope/key; identical body replays, changed body conflicts'),
      'UseAuthorization':(['authorization_identity_key','authorization_version_key'],'Stable authorization identity plus immutable version; successor does not reset lineage balances'),
      'ExecutionIntent':(['source_owner_key','execution_scope_key','execution_request_key_id'],'Original execution request scope/key; quantity/body cannot change on replay'),
      'Reservation':(['reservation_identity_key','reservation_version_key'],'Reservation identity/version, retains authorization lineage and exact execution intent'),
      'MeasurementFact':(['source_owner_key','fact_identity_key','fact_version_key'],'Same Owner fact/version ingested once; corrected version appends, cumulative values do not sum'),
      'ExceptionCorrection':(['correction_identity_key','correction_version_key'],'Append-only correction identity/version; original fact/settlement remains'),
      'NoFurtherExecutionProof':(['execution_intent_storage_id','source_owner_key','proof_identity_key','proof_version_key'],'Proof must bind original execution and exact coverage/version; root-only status cannot become proof'),
      'ReleaseRecord':(['release_identity_key','release_revision_key'],'ReleaseId stable; each signed ReleaseRevision immutable'),
      'ReleaseTarget':(['release_storage_id','target_identity_key','target_scope_version_key'],'Exact ReleaseRevision + TargetId + ScopeVersion; changed obligations are successors; old obligations retained'),
      'TargetReceipt':(['release_target_storage_id','source_owner_key','owner_receipt_identity_key','owner_receipt_version_key'],'Deduplicate exact Owner receipt/version for exact release/target/scope; arrival time never replaces Owner version'),
      'DeliveryAttempt':(['attempt_identity_key'],'Each retry creates new attempt under the same target/release, never another release or obligation'),
      'ActualAdoption':(['release_target_storage_id','source_owner_key','applied_fact_identity_key','applied_fact_version_key'],'Same fact/version cannot be counted twice within obligation; cross-target allocation is separately checked'),
      'ControlRecovery':(['case_identity_key'],'One original RecoveryCase identity; cross-page links do not create new master records'),
      'ReviewCandidateAggregate':(['review_round_storage_id'],'One exact frozen full aggregate per original review round; generated aggregate ID/version remain independently preserved'),
      'RoundBinding':(['review_round_storage_id'],'One immutable binding of round/aggregate/package/policy; no re-resolution on retry'),
      'ReviewSnapshot':(['binding_storage_id'],'Exact immutable canonical snapshot for the frozen binding'),
      'OwnerApplication':(['binding_storage_id','application_unit_key'],'One registered application unit for bound decision; receipt revisions are append-only child records, not new applications'),
      'OwnerInbox':(['scope_key','producer_key','event_key'],'R1 Scope+Producer+EventId; same event/different body quarantines before application'),
      'OwnerApplicationReceipt':(['application_storage_id','receipt_revision_key'],'Immutable receipt revision per application; BLOCKED recovery appends successor referencing exact previousReceiptId'),
    }
    for name,(parts,scope) in unique_specs.items():
        e=entity(name)
        for col in parts:
            if col.endswith(('_key','_key_id')):key(e,col,name+'.'+col)
        uq(e,name.lower()+'_business',['tenant_id',*parts],scope)

    iteration=entity('Iteration');evaluation=entity('Evaluation')
    add_column(iteration,'current_evaluation_storage_id',nullable=True)
    iteration['foreign_keys'].append({'columns':['tenant_id','storage_id','current_evaluation_storage_id'],
        'target_entity':'Evaluation','target_columns':['tenant_id','iteration_storage_id','storage_id'],'nullable':True,'on_delete':'RESTRICT',
        'deferrable':'INITIALLY_DEFERRED','scope':'Only evaluation of this exact iteration can be current','source_pin':original('ITER',1628,1628)})
    fk(evaluation,'selected_candidate_storage_id','Candidate',nullable=True,extra_scope=True)
    fk(evaluation,'review_round_storage_id','ReviewRound',nullable=True,extra_scope=True)
    iteration['checks'].append({'name':'single_current_evaluation_and_selection','columns':['tenant_id','storage_id','current_evaluation_storage_id'],
        'enforcement':'SINGLE_NULLABLE_FK_POINTER_AND_OWNER_CAS','rule':'At most one current Evaluation per iteration (一轮迭代). ActiveSelection derives solely from this pointed evaluation and its single selected candidate. Re-selection appends a new evaluation then CAS-replaces pointer; history persists. ReviewRoundId is optional, so no false UNIQUE(ReviewRoundId).',
        'source_pins':[original('ITER',445,449),original('ITER',1092,1096),original('ITER',1722,1728)]})
    evaluation['checks'].append({'name':'evaluation_outcome_dependencies','columns':['outcome','selected_candidate_storage_id','handover_ref'],
        'enforcement':'OWNER_EXACT_ENUM_VALIDATION_BEFORE_COMMIT','rule':'ADOPT requires exactly one selected candidate; CONTINUE requires HandoverRef. Original outcome bytes are preserved; immutable evaluation cannot be edited.',
        'source_pin':original('ITER',1722,1736)})
    entity('ExternalRequestReceipt')['checks'].append({'name':'original_command_scope','columns':['operation_type_key','aggregate_source_scope_key','request_key','payload_digest'],
        'enforcement':'OWNER_COMMAND_UQ_AND_FULL_BODY_COMPARISON','rule':'Unique original command tuple plus full immutable payload compare; same key/different body 409; unknown outcome original key query only.',
        'source_pin':original('ITER',522,526)})

    intent=entity('ExecutionIntent');fk(intent,'root_work_order_key','OwnerKey',nullable=True)
    intent['checks'].append({'name':'one_original_key_one_root','columns':['source_owner_key','execution_scope_key','execution_request_key','immutable_payload','root_work_order_key'],
        'enforcement':'OWNER_KEY_UQ_AND_ROOT_BINDING_CAS','rule':'Each original intent has one nullable root reference. Only authentic MES fact may bind null to original root; any different root or changed payload conflicts. Timeout/NOT_FOUND cannot clear root or change key.',
        'source_pins':[original('REL',566,566),original('REL',681,681)]})
    reservation=entity('Reservation')
    reservation['checks'].append({'name':'exact_partition_and_no_double_allocation','columns':['authorization_storage_id','execution_intent_storage_id','authorization_lineage','consumption_basis','basis_version','nonoverlapping_share','q','c','r','p'],
        'enforcement':'OWNER_EXACT_ARITHMETIC_UNDER_SORTED_LINEAGE_AND_RESERVATION_LOCKS','rule':'Normal covered partition Q=C+R+P, all nonnegative and mutually exclusive. Complete serialized lineage check prevents overlapping shares and exceeds available capacity. No SQL numeric rounding; exact byte/coefficient model evaluates arithmetic. Missing coverage/safe termination keeps P.',
        'source_pin':module('PLM-REL-01',40,43)})
    entity('ActualAdoption')['checks'].append({'name':'fact_allocation_not_double_counted','columns':['release_target_storage_id','source_owner_key','applied_fact_identity_key','applied_fact_version_key','covered','remaining','completeness'],
        'enforcement':'OWNER_SERIALIZED_COVERAGE_ALLOCATION','rule':'Exact AdoptionDefinition scope/window controls allocation; the same fact cannot falsely fill several independent commitments. Legitimate links retain explicit disjoint coverage, no duplicate count.',
        'source_pin':original('REL',1088,1088)})
    for name in ['ReleaseRecord','ReleaseTarget','TargetReceipt','DeliveryAttempt']:
        entity(name)['source_identity_constraints']=[original('TARGET',31,33),original('TARGET',111,111),original('TARGET',238,238)]
    entity('ControlRecovery')['source_identity_constraints']=[original('REL',1239,1239)]
    application=entity('OwnerApplication')
    for name in ['instance_key','subject_revision_key','decision_key']:key(application,name,'OwnerApplication.'+name)
    uq(application,'owner_application_decision',['tenant_id','instance_key','subject_revision_key','decision_key'],'R1 separate Instance+SubjectRevision+Decision dedup; action string cannot create a second application')
    receipt=entity('OwnerApplicationReceipt');add_column(receipt,'outcome','bytea')
    receipt['checks'].append({'name':'one_applied_receipt','columns':['application_storage_id','outcome'],
        'enforcement':'POSTGRESQL_PARTIAL_UNIQUE_AFTER_REGISTERED_OUTCOME_MAPPING','rule':'One APPLIED receipt per registered application unit; original outcome encoding is fixed by OA mapping, never free text/action renaming.'})
    uq(receipt,'owner_receipt_applied',['tenant_id','application_storage_id'],'Only one APPLIED revision per application unit',"outcome = convert_to('APPLIED','UTF8')")
    # Original row identity is independent of business numbers/version/command keys.
    # These are global original identity domains, even though every row retains TenantId.
    original_ids = {
        'Project': ('Project', 1580),
        'Iteration': ('Iteration', 1602),
        'IterationInput': ('IterationInput', 1632),
        'CandidateRef': ('CandidateObjectRef', 1664),
        'ReviewRound': ('ReviewRound', 1692),
        'ExternalRequestReceipt': ('ExternalRequestReceipt', 1738),
    }
    interning = {
        'tuple_columns': ['domain_key', 'schema_bytes', 'scope_bytes', 'identity_bytes'],
        'equality': 'FULL_TUPLE_BYTES',
        'bucket_digest_unique': False,
        'same_tuple_same_tenant': 'RETURN_EXISTING_UUID',
        'same_tuple_other_tenant': 'SAFE_IDENTITY_CONFLICT_NO_NEW_UUID',
        'different_tuple_same_bucket': 'COMPARE_FULL_TUPLE_THEN_ALLOCATE_DISTINCT_UUID',
        'atomicity': 'ONE_OWNER_TRANSACTION_BUCKET_LOCK_COMPARE_INSERT',
    }
    for name, (source_entity, line) in original_ids.items():
        e = entity(name)
        source = original('ITER', line, line)
        field = next(f for f in e['fields'] if f['field'] == 'Id')
        field.update(nullability='NOT NULL', source_pin=source,
                     identity_scope='Original global Owner/entity Id domain; TenantId is not part of identity')
        columns(e)['id'].update(postgresql='bytea', nullable=False, source_pin=source)
        domain = 'ITER/' + source_entity + '.Id'
        key(e, 'original_id_key', domain)
        columns(e)['original_id_key']['identity_binding'].update(
            scope='ORIGINAL_GLOBAL_IDENTITY', scope_bytes_hex='',
            raw_column='id', schema_binding='PINNED_ORIGINAL_OWNER_SCHEMA_NOT_CALLER_SELECTED',
            source_pin=source)
        uq(e, name.lower() + '_original_id', ['original_id_key'],
           'Original global Id uniqueness, independent of tenant/parent/business number/version')
        e['unique_constraints'][-1]['source_pin'] = source
        e['original_identity'] = {
            'source_field': 'Id', 'source_entity': source_entity, 'source_pin': source,
            'raw_column': 'id', 'key_column': 'original_id_key',
            'registry_domain': domain, 'scope': 'ORIGINAL_GLOBAL_IDENTITY',
            'scope_bytes_hex': '', 'raw_binding': 'EXACT_ORIGINAL_BYTES',
            'schema_binding': 'PINNED_ORIGINAL_OWNER_SCHEMA_NOT_CALLER_SELECTED',
            'interning': dict(interning),
            'rule': 'Fix the original Owner/entity and accepted Id schema from the cited source; '
                    'do not accept a caller-selected schema/domain. The scope is fixed empty bytes: '
                    'TenantId, parent, business number and version never enter the interning tuple '
                    'or its lock bucket. Keep every original Id byte without normalization/casts. '
                    'A global identity already assigned to another tenant is a safe conflict: '
                    'do not disclose the row or allocate another UUID. Same-tenant reuse resolves '
                    'the same UUID, and global UNIQUE(original_id_key) rejects a second entity row. '
                    'A digest collision compares all tuple bytes and permits a distinct UUID only '
                    'for a distinct original identity under the same Owner transaction lock.',
        }
        e['checks'].append({
            'name': 'original_id_authority_binding',
            'columns': ['tenant_id', 'id', 'original_id_key'],
            'enforcement': 'OWNER_GLOBAL_INTERN_AND_EXACT_RAW_BINDING_BEFORE_COMMIT',
            'comparison': 'EXACT_RAW_SCHEMA_DOMAIN_AND_IMMUTABLE_TENANT',
            'rule': 'Resolve original_id_key through the declared global tuple and compare '
                    'id with OwnerKey.identity_bytes exactly; require the fixed original '
                    'Owner/schema/empty scope and the immutable tenant assignment before commit. '
                    'Every writer/import/recovery uses this same lock and comparison. '
                    'The composite tenant FK and global key UQ are additional constraints, '
                    'not substitutes for exact interning or raw/key consistency.',
            'source_pin': source,
        })
    registry['checks'].append({
        'name': 'original_global_id_interning',
        'columns': ['domain_key', 'schema_bytes', 'scope_bytes', 'identity_bytes',
                    'tenant_id', 'bucket_digest'],
        'enforcement': 'OWNER_SERIALIZED_FULL_BYTE_COMPARISON',
        'domains': [entity(name)['original_identity']['registry_domain'] for name in original_ids],
        'interning': dict(interning),
        'scope_bytes_hex': '',
        'rule': 'For these original global Id domains, TenantId is checked only after the '
                'full global identity lookup; it never enters tuple equality, scope bytes '
                'or the bucket-lock key. An existing other-tenant tuple conflicts without '
                'allocating a second UUID. Distinct tuple bytes sharing a digest remain '
                'distinct identities. Original Owner/schema/domain selection is trusted '
                'and fixed from the cited contracts, never normalized or caller-selected.',
        'source_pins': [entity(name)['original_identity']['source_pin'] for name in original_ids],
    })
    for e in entities[:-1]:
        for index,f in enumerate(e['foreign_keys']):
            e['indexes'].append({'name':e['entity'].lower()+'_fk_'+str(index),'columns':f['columns'],'unique':False})
    return entities
