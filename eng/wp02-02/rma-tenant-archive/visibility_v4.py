"""Original source visibility facts in the optional v4 RMA archive."""

FORMAT = "cp6-rma-source-archive-v4"
MEMBER = "rma-source-visibility.csv"
COLUMNS = ("kind", "tenant_id", "id", "facts")
ORDER = 'kind COLLATE "C", id'
FACTS = {
    "outbound": ("T_OutboundOrder", ("outbound_no", "creator", "warehouse_cd", "web_order_no", "work_order_no", "is_deleted")),
    "outbound_detail": ("T_OutboundOrderDetail", ("outbound_id", "outbound_no", "creator", "warehouse_cd", "location_cd", "is_deleted")),
    "order": ("T_Order", ("web_order_no", "order_department", "is_deleted")),
    "work_order": ("T_WorkOrder", ("work_order_no", "web_order_no", "is_deleted")),
    "location": ("T_Location", ("warehouse_cd", "location_cd", "area_cd", "is_deleted", "is_initialization_quarantined")),
}


def rows_sql(tenant):
    # Project BPCHAR wire text only after resolving the source keys with their native equality.
    def wire(column):
        return f"pg_catalog.convert_from(pg_catalog.bpcharsend({column}), 'UTF8')"

    return f"""
        WITH headers AS (
            SELECT o."TenantId", o."Id", o."OutboundNo", o."Creator", o."WarehouseCd", o."WebOrderNo",
                   o."WorkOrderNo", o."IsDeleted"
            FROM public."T_OutboundOrder" o
            JOIN (SELECT DISTINCT outbound_id FROM t_{tenant.hex}.heads WHERE tenant_id='{tenant}'::uuid) h
              ON h.outbound_id=o."Id"
            WHERE o."TenantId"='{tenant}'::uuid
        ), details AS (
            -- Both the self and warehouse predicates inspect deleted details too.
            SELECT d."TenantId", d."Id", d."OutboundNo", d."Creator", d."WarehouseCd", d."LocationCd",
                   d."IsDeleted", h."Id" AS outbound_id, coalesce(d."WarehouseCd", h."WarehouseCd") AS effective_warehouse
            FROM headers h
            JOIN public."T_OutboundOrderDetail" d ON d."TenantId"=h."TenantId" AND d."OutboundNo"=h."OutboundNo"
        ), work_orders AS (
            SELECT w."TenantId", w."Id", w."WorkOrderNo", w."WebOrderNo", w."IsDeleted"
            FROM public."T_WorkOrder" w
            WHERE w."TenantId"='{tenant}'::uuid AND EXISTS (
                SELECT FROM headers h WHERE h."WorkOrderNo"=w."WorkOrderNo"
                  AND (h."WorkOrderNo"<>'' OR h."WebOrderNo" IS NULL OR h."WebOrderNo"=''))
        ), order_refs AS (
            SELECT h."WebOrderNo" FROM headers h WHERE h."WebOrderNo"<>''
            UNION ALL
            SELECT w."WebOrderNo"
            FROM headers h JOIN work_orders w ON w."WorkOrderNo"=h."WorkOrderNo"
            WHERE (h."WebOrderNo" IS NULL OR h."WebOrderNo"='') AND NOT w."IsDeleted" AND w."WebOrderNo" IS NOT NULL
        ), visibility AS (
            SELECT 'outbound' AS kind, h."TenantId" AS tenant_id, h."Id" AS id,
                jsonb_build_object('outbound_no',{wire('h."OutboundNo"')},'creator',{wire('h."Creator"')},
                    'warehouse_cd',{wire('h."WarehouseCd"')},'web_order_no',{wire('h."WebOrderNo"')},
                    'work_order_no',{wire('h."WorkOrderNo"')},'is_deleted',h."IsDeleted") AS facts
            FROM headers h
            UNION ALL
            SELECT 'outbound_detail', d."TenantId", d."Id",
                jsonb_build_object('outbound_id',d.outbound_id,'outbound_no',{wire('d."OutboundNo"')},
                    'creator',{wire('d."Creator"')},'warehouse_cd',{wire('d."WarehouseCd"')},
                    'location_cd',{wire('d."LocationCd"')},'is_deleted',d."IsDeleted")
            FROM details d
            UNION ALL
            SELECT 'work_order', w."TenantId", w."Id",
                jsonb_build_object('work_order_no',{wire('w."WorkOrderNo"')},'web_order_no',{wire('w."WebOrderNo"')},
                    'is_deleted',w."IsDeleted")
            FROM work_orders w
            UNION ALL
            SELECT 'order', p."TenantId", p."Id",
                jsonb_build_object('web_order_no',{wire('p."WebOrderNo"')},
                    'order_department',{wire('p."OrderDepartment"')},'is_deleted',p."IsDeleted")
            FROM public."T_Order" p
            WHERE p."TenantId"='{tenant}'::uuid AND EXISTS (
                SELECT FROM order_refs r WHERE r."WebOrderNo"=p."WebOrderNo")
            UNION ALL
            SELECT 'location', l."TenantId", l."Id",
                jsonb_build_object('warehouse_cd',{wire('l."WarehouseCd"')},'location_cd',{wire('l."LocationCd"')},
                    'area_cd',{wire('l."AreaCd"')},'is_deleted',l."IsDeleted",
                    'is_initialization_quarantined',l."IsInitializationQuarantined")
            FROM public."T_Location" l
            WHERE l."TenantId"='{tenant}'::uuid AND EXISTS (
                SELECT FROM details d WHERE d.effective_warehouse=l."WarehouseCd" AND d."LocationCd"=l."LocationCd")
        )
        SELECT kind, tenant_id, id, facts FROM visibility
    """


def capture_counts(query, rows):
    captured = query(f"""
        SELECT coalesce(json_object_agg(kind, row_count), '{{}}'::json)
        FROM (SELECT kind, count(*) AS row_count FROM ({rows}) facts GROUP BY kind) totals;
    """)
    return {kind: captured.get(kind, 0) for kind in FACTS}


def covers_source(counts, source_counts):
    return counts["outbound"] == source_counts["headers"] and counts["outbound_detail"] >= source_counts["details"]


def manifest_section(counts):
    return {
        "member": MEMBER,
        "columns": list(COLUMNS),
        "scope": "same-tenant source visibility facts for outbound headers referenced at the exported snapshot",
        "facts": {kind: {"table": "public." + table, "fields": list(fields)} for kind, (table, fields) in FACTS.items()},
        "counts": counts,
    }


def matches_manifest(manifest):
    section = manifest.get("rmaVisibility")
    if not isinstance(section, dict):
        return False
    counts = section.get("counts")
    source = manifest["rmaSource"]["counts"]
    return isinstance(counts, dict) and counts.keys() == FACTS.keys() \
        and all(type(value) is int and value >= 0 for value in counts.values()) \
        and covers_source(counts, source) \
        and section == manifest_section(counts)
