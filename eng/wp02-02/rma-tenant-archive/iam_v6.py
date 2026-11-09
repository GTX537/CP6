"""Historical IAM references for original RMA participants in the v6 source cut."""

FORMAT = "cp6-rma-source-archive-v6"
MEMBER = "rma-iam-references.csv"
COLUMNS = ("kind", "tenant_id", "id", "facts")
ORDER = 'kind COLLATE "C", id COLLATE "C"'
RESOURCE = "wms-rma"
ACTIONS = ("query", "add", "submit", "cancel")
FACTS = {
    "tenant": ("public.Sys_Tenants", ("enabled", "expire_date", "authentication_policy_revision", "tree_revision",
                                    "order_department_mapping_revision", "standalone_department_mapping_revision")),
    "participant": ("RMA heads, original journal and public.Sys_Users", ("referenced_head", "referenced_journal",
                    "user_present", "user_name", "enabled", "locked_until", "department_id", "primary_role_id", "revision")),
    "role": ("public.Sys_Roles", ("role_present", "enabled", "revision")),
    "additional_role": ("public.Sys_UserRole", ("user_id", "role_id")),
    "menu_reference": ("public.Sys_Menus", ("menu_key",)),
    "menu_action": ("public.Sys_MenuAction", ("menu_id", "action_code")),
    "role_action": ("public.Sys_RoleAction", ("role_id", "menu_id", "action_code")),
    "role_scope": ("public.Sys_RoleDataScope", ("role_id", "resource_key", "scope_type", "custom_department_ids")),
    "role_field": ("public.Sys_RoleFieldPerm", ("role_id", "resource_key", "field_name", "access")),
    "warehouse_scope": ("public.T_WmsRoleScope", ("role_id", "warehouse_cd", "area_cd")),
    "order_department_mapping": ("cp6_order_owner_v1.department_mappings", ("code", "department_id", "enabled", "revision")),
    "department": ("public.Sys_Dept", ("parent_id", "path", "enabled")),
}


def rows_sql(tenant, journal_rows, visibility_rows):
    def wire(column):
        return f"pg_catalog.convert_from(pg_catalog.bpcharsend({column}), 'UTF8')"

    # The caller has pinned the tenant and already verified the exact original Owner and contract.
    target = f"'{tenant}'::uuid"
    actions = ",".join(f"'{action}'" for action in ACTIONS)
    return f"""
        WITH actor_references AS (
            SELECT actor_id, true AS head, false AS journal FROM t_{tenant.hex}.heads WHERE tenant_id={target}
            UNION ALL
            SELECT actor_id, false, true FROM ({journal_rows}) original
        ), participants AS (
            SELECT actor_id, bool_or(head) AS head, bool_or(journal) AS journal FROM actor_references GROUP BY actor_id
        ), users AS (
            SELECT p.*, u."Id" IS NOT NULL AS present, u."UserName", u."Enable", u."LockedUntil", u."DeptId", u."RoleId"
            FROM participants p LEFT JOIN public."Sys_Users" u ON u."TenantId"={target} AND u."Id"=p.actor_id
        ), additional_roles AS (
            SELECT a."Id", a."UserId", a."RoleId" FROM public."Sys_UserRole" a
            JOIN participants p ON p.actor_id=a."UserId" WHERE a."TenantId"={target}
        ), role_ids AS (
            SELECT "RoleId" FROM users WHERE "RoleId" IS NOT NULL
            UNION SELECT "RoleId" FROM additional_roles
        ), revisions AS (
            SELECT object_key, revision FROM cp6_authentication_v1.identity_admission_revisions WHERE tenant_id={target}
        ), menus AS (
            SELECT "MenuId", "MenuKey" FROM public."Sys_Menus" WHERE "MenuKey"='{RESOURCE}'
        ), visible_source AS (
            {visibility_rows}
        ), warehouse_keys AS (
            SELECT facts->>'warehouse_cd' AS code FROM visible_source WHERE kind='outbound'
            UNION ALL
            SELECT coalesce(d.facts->>'warehouse_cd',h.facts->>'warehouse_cd')
            FROM visible_source d JOIN visible_source h ON h.kind='outbound' AND h.id=(d.facts->>'outbound_id')::uuid
            WHERE d.kind='outbound_detail'
        ), iam_references AS (
            SELECT 'tenant' AS kind, t."Id" AS tenant_id, t."Id"::text AS id,
                jsonb_build_object('enabled',t."Enable",'expire_date',t."ExpireDate",
                    'authentication_policy_revision',t."AuthenticationPolicyRevision",
                    'tree_revision',(SELECT revision FROM revisions WHERE object_key='tree'),
                    'order_department_mapping_revision',(SELECT revision FROM revisions WHERE object_key='order-department-mapping'),
                    'standalone_department_mapping_revision',(SELECT revision FROM revisions WHERE object_key='standalone-department-mapping')) AS facts
            FROM public."Sys_Tenants" t WHERE t."Id"={target}
            UNION ALL
            SELECT 'participant', {target}, u.actor_id::text,
                jsonb_build_object('referenced_head',u.head,'referenced_journal',u.journal,'user_present',u.present,
                    'user_name',{wire('u."UserName"')},'enabled',u."Enable",'locked_until',u."LockedUntil",
                    'department_id',u."DeptId",'primary_role_id',u."RoleId",'revision',v.revision)
            FROM users u LEFT JOIN revisions v ON v.object_key='user:'||u.actor_id::text
            UNION ALL
            SELECT 'role', {target}, i."RoleId"::text,
                jsonb_build_object('role_present',r."RoleId" IS NOT NULL,'enabled',r."Enable",'revision',v.revision)
            FROM role_ids i LEFT JOIN public."Sys_Roles" r ON r."TenantId"={target} AND r."RoleId"=i."RoleId"
            LEFT JOIN revisions v ON v.object_key='role:'||i."RoleId"::text
            UNION ALL
            SELECT 'additional_role', {target}, a."Id"::text, jsonb_build_object('user_id',a."UserId",'role_id',a."RoleId")
            FROM additional_roles a
            UNION ALL
            SELECT 'menu_reference', {target}, m."MenuId"::text, jsonb_build_object('menu_key',{wire('m."MenuKey"')})
            FROM menus m
            UNION ALL
            SELECT 'menu_action', {target}, a."Id"::text,
                jsonb_build_object('menu_id',a."MenuId",'action_code',{wire('a."ActionCode"')})
            FROM public."Sys_MenuAction" a JOIN menus m USING ("MenuId")
            WHERE a."TenantId"={target} AND a."ActionCode" IN ({actions})
            UNION ALL
            SELECT 'role_action', {target}, a."Id"::text,
                jsonb_build_object('role_id',a."RoleId",'menu_id',a."MenuId",'action_code',{wire('a."ActionCode"')})
            FROM public."Sys_RoleAction" a JOIN menus m USING ("MenuId") JOIN role_ids i USING ("RoleId")
            WHERE a."TenantId"={target} AND a."ActionCode" IN ({actions})
            UNION ALL
            SELECT 'role_scope', {target}, s."Id"::text,
                jsonb_build_object('role_id',s."RoleId",'resource_key',{wire('s."ResourceKey"')},'scope_type',s."ScopeType",
                    'custom_department_ids',{wire('s."CustomDeptIds"')})
            FROM public."Sys_RoleDataScope" s JOIN role_ids i USING ("RoleId")
            WHERE s."TenantId"={target} AND s."ResourceKey"='{RESOURCE}'
            UNION ALL
            SELECT 'role_field', {target}, f."Id"::text,
                jsonb_build_object('role_id',f."RoleId",'resource_key',{wire('f."ResourceKey"')},
                    'field_name',{wire('f."FieldName"')},'access',f."Access")
            FROM public."Sys_RoleFieldPerm" f JOIN role_ids i USING ("RoleId")
            WHERE f."TenantId"={target} AND f."ResourceKey"='{RESOURCE}'
            UNION ALL
            SELECT 'warehouse_scope', {target}, w."Id"::text,
                jsonb_build_object('role_id',w."RoleId",'warehouse_cd',{wire('w."WarehouseCd"')},'area_cd',{wire('w."AreaCd"')})
            FROM public."T_WmsRoleScope" w JOIN role_ids i USING ("RoleId")
            WHERE w."TenantId"={target} AND (w."WarehouseCd"='*' OR EXISTS (
                SELECT FROM warehouse_keys k WHERE w."WarehouseCd"=k.code::bpchar COLLATE public.cp6_ci_as_provider_v1))
            UNION ALL
            SELECT 'order_department_mapping', {target}, m.id::text,
                jsonb_build_object('code',{wire('m.code')},'department_id',m.department_id,'enabled',m.enabled,'revision',m.revision)
            FROM cp6_order_owner_v1.department_mappings m WHERE m.tenant_id={target} AND EXISTS (
                SELECT FROM public."T_Order" o JOIN visible_source v ON v.kind='order' AND v.id=o."Id"
                WHERE o."TenantId"={target} AND m.code=o."OrderDepartment")
            UNION ALL
            SELECT 'department', {target}, d."Id"::text,
                jsonb_build_object('parent_id',d."ParentId",'path',{wire('d."Path"')},'enabled',d."Enable")
            FROM public."Sys_Dept" d WHERE d."TenantId"={target}
        )
        SELECT kind, tenant_id, id, facts FROM iam_references
    """


def capture_counts(query, rows):
    captured = query(f"""
        SET LOCAL row_security=off;
        SELECT coalesce(json_object_agg(kind, row_count), '{{}}'::json)
        FROM (SELECT kind,count(*) AS row_count FROM ({rows}) facts GROUP BY kind) totals;
    """, failure="rma-iam-reference-read-failed (see native logs)")
    return {kind: captured.get(kind, 0) for kind in FACTS}


def manifest_section(counts):
    return {
        "member": MEMBER, "columns": list(COLUMNS), "idType": "text",
        "scope": "historical RMA participant references and same-tenant organization facts at the exported snapshot",
        "resource": RESOURCE, "actions": list(ACTIONS),
        "facts": {kind: {"source": table, "fields": list(fields)} for kind, (table, fields) in FACTS.items()},
        "counts": counts,
    }


def matches_manifest(manifest):
    section = manifest.get("rmaIamReferences")
    if not isinstance(section, dict):
        return False
    counts = section.get("counts")
    return isinstance(counts, dict) and counts.keys() == FACTS.keys() \
        and all(type(value) is int and value >= 0 for value in counts.values()) \
        and counts["tenant"] <= 1 and section == manifest_section(counts)
