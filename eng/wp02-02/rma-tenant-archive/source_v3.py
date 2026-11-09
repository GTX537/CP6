"""Bounded RMA source projections for the optional v3 archive."""

from typing import NamedTuple

FORMAT = "cp6-rma-source-archive-v3"
HEADER_MEMBER = "outbounds.csv"
DETAIL_MEMBER = "rma-source-details.csv"
LINEAGE_MEMBER = "rma-source-lineage.csv"
MEMBERS = (DETAIL_MEMBER, LINEAGE_MEMBER)
HEADER_COLUMNS = ("tenant_id", "outbound_id", "outbound_no", "warehouse_cd", "status", "is_deleted",
                  "customer_cd", "web_order_no", "outbound_type")
DETAIL_COLUMNS = ("tenant_id", "outbound_id", "outbound_no", "outbound_line_id", "line_no", "product_cd",
                  "shipped_qty", "unit_cd", "warehouse_cd", "location_cd", "lot_no", "ship_txn_no")
LINEAGE_COLUMNS = ("tenant_id", "outbound_line_id", "outbound_no", "payload")
QUANTITY_TYPE = "numeric(21,8)"
READ_ALL_ROWS = "SET LOCAL row_security=off;"


class Slice(NamedTuple):
    key: str
    member: str
    query: str
    order: str


def slices(tenant):
    # The exporter has already parsed the trusted tenant UUID.
    headers = """
        WITH headers AS (
            SELECT o."TenantId" AS tenant_id, o."Id" AS outbound_id, o."OutboundNo" AS outbound_no,
                   o."WarehouseCd" AS warehouse_cd, o."Status" AS status, o."IsDeleted" AS is_deleted,
                   o."CustomerCd" AS customer_cd, o."WebOrderNo" AS web_order_no, o."OutboundType" AS outbound_type
            FROM public."T_OutboundOrder" o
            JOIN (SELECT DISTINCT outbound_id FROM t_{schema}.heads WHERE tenant_id='{tenant}'::uuid) h
                 ON h.outbound_id=o."Id"
            WHERE o."TenantId"='{tenant}'::uuid
        )
    """.format(schema=tenant.hex, tenant=tenant)
    details = headers.rstrip() + """,
        details AS (
            SELECT h.tenant_id, h.outbound_id, d."OutboundNo" AS outbound_no,
                   d."Id" AS outbound_line_id, d."LineNo" AS line_no, d."ProductCd" AS product_cd,
                   d."ShippedQty" AS shipped_qty, d."UnitCd" AS unit_cd, d."WarehouseCd" AS warehouse_cd,
                   d."LocationCd" AS location_cd, d."LotNo" AS lot_no, d."ShipTxnNo" AS ship_txn_no,
                   h.outbound_no AS header_no
            FROM headers h
            JOIN public."T_OutboundOrderDetail" d
                 ON d."TenantId"=h.tenant_id AND d."OutboundNo"=h.outbound_no AND NOT d."IsDeleted"
        )
    """
    # Detail uses the original BPCHAR equality. Lineage uses the native header text,
    # as OutboundOrderLineageV1.Read does, so a BPCHAR-to-text cast must not trim it.
    lineage = details + """
        SELECT l.tenant_id, l.outbound_line_id, l.outbound_no, l.payload
        FROM cp6_outbound_lineage_v1.order_lines l
        JOIN details d ON d.tenant_id=l.tenant_id AND d.outbound_line_id=l.outbound_line_id
             AND l.outbound_no=pg_catalog.convert_from(pg_catalog.bpcharsend(d.header_no),'UTF8')
    """
    return (
        Slice("headers", HEADER_MEMBER, headers + "SELECT " + ", ".join(HEADER_COLUMNS) + " FROM headers", "outbound_id"),
        Slice("details", DETAIL_MEMBER, details + "SELECT " + ", ".join(DETAIL_COLUMNS) + " FROM details",
              "outbound_id,line_no,outbound_line_id"),
        Slice("lineage", LINEAGE_MEMBER, lineage, "outbound_line_id"),
    )


def capture_counts(query, sources):
    counts = ",\n".join("'{}',(SELECT count(*) FROM ({}) source)".format(part.key, part.query) for part in sources)
    captured = query(READ_ALL_ROWS + """
        SELECT json_build_object(
            'shippedQuantityType',(SELECT format_type(atttypid,atttypmod) FROM pg_attribute
                 WHERE attrelid='public."T_OutboundOrderDetail"'::regclass AND attname='ShippedQty' AND NOT attisdropped),
            'counts',json_build_object({counts}));
    """.format(counts=counts))
    if captured["shippedQuantityType"] != QUANTITY_TYPE:
        raise ValueError("rma-source-quantity-type-rejected")
    return captured["counts"]


def header_projection():
    return {"table": "public.T_OutboundOrder", "columns": list(HEADER_COLUMNS),
            "scope": "same tenant, referenced by a head at the exported snapshot"}


def manifest_section(counts):
    return {
        "counts": counts,
        "details": {"table": "public.T_OutboundOrderDetail", "columns": list(DETAIL_COLUMNS),
                    "shippedQuantityType": QUANTITY_TYPE,
                    "scope": "active details of the referenced same-tenant headers"},
        "lineage": {"table": "cp6_outbound_lineage_v1.order_lines", "columns": list(LINEAGE_COLUMNS),
                    "scope": "optional original lineage matching a selected detail and its native header text"},
    }


def matches_manifest(manifest):
    section = manifest.get("rmaSource")
    base_counts = manifest.get("counts")
    if not isinstance(section, dict) or not isinstance(base_counts, dict):
        return False
    counts = section.get("counts")
    return isinstance(counts, dict) and set(counts) == {"headers", "details", "lineage"} \
        and all(type(value) is int and value >= 0 for value in counts.values()) \
        and counts["headers"] == base_counts.get("outbounds") and counts["lineage"] <= counts["details"] \
        and section == manifest_section(counts) and manifest.get("sharedProjection") == header_projection()
