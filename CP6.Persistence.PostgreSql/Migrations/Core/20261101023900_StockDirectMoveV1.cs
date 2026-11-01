using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101023900_StockDirectMoveV1")]
public sealed class StockDirectMoveV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_stock_move_v1;
        CREATE TABLE cp6_stock_move_v1.openings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_stock_move_v1.locations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id text COLLATE "C" NOT NULL,native_id uuid NOT NULL,
          opening_id text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,native_id),
          FOREIGN KEY(tenant_id,environment_id,opening_id) REFERENCES cp6_stock_move_v1.openings);
        CREATE TABLE cp6_stock_move_v1.writer_bindings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,location_id text COLLATE "C" NOT NULL,
          warehouse_code text COLLATE "C" NOT NULL,location_code text COLLATE "C" NOT NULL,product_code text COLLATE "C" NOT NULL,
          PRIMARY KEY(tenant_id,warehouse_code,location_code,product_code),
          FOREIGN KEY(tenant_id,environment_id,location_id) REFERENCES cp6_stock_move_v1.locations);
        CREATE TABLE cp6_stock_move_v1.roots(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id text COLLATE "C" NOT NULL,opening_id text COLLATE "C" NOT NULL,
          quantity numeric(21,8) NOT NULL CHECK(quantity>0),body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,opening_id) REFERENCES cp6_stock_move_v1.openings);
        CREATE TABLE cp6_stock_move_v1.slices(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id text COLLATE "C" NOT NULL,
          from_qty numeric(21,8) NOT NULL,to_qty numeric(21,8) NOT NULL,location_id text COLLATE "C" NOT NULL,
          CHECK(from_qty>=0 AND to_qty>from_qty),PRIMARY KEY(tenant_id,environment_id,root_id,from_qty),
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_stock_move_v1.roots,
          FOREIGN KEY(tenant_id,environment_id,location_id) REFERENCES cp6_stock_move_v1.locations);
        CREATE TABLE cp6_stock_move_v1.root_heads(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id text COLLATE "C" NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,environment_id,root_id),FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_stock_move_v1.roots);
        CREATE TABLE cp6_stock_move_v1.claim_bindings(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id text COLLATE "C" NOT NULL,root_id text COLLATE "C" NOT NULL,
          from_qty numeric(21,8) NOT NULL,to_qty numeric(21,8) NOT NULL,body bytea NOT NULL,
          CHECK(from_qty>=0 AND to_qty>from_qty),PRIMARY KEY(tenant_id,environment_id,id),
          FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_stock_move_v1.roots);
        CREATE TABLE cp6_stock_move_v1.capacities(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id text COLLATE "C" NOT NULL,body bytea NOT NULL,
          occupied numeric(38,8) NOT NULL CHECK(occupied>=0),revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_stock_move_v1.decisions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,operation_id uuid NOT NULL,
          input_digest text COLLATE "C" NOT NULL,state text NOT NULL CHECK(state IN('POSTED','SEALED_NO_EFFECT')),body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,canonical),UNIQUE(tenant_id,environment_id,operation_id));
        CREATE TABLE cp6_stock_move_v1.events(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,canonical text COLLATE "C" NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,canonical),FOREIGN KEY(tenant_id,environment_id,canonical) REFERENCES cp6_stock_move_v1.decisions);
        CREATE FUNCTION cp6_stock_move_v1.reject_legacy_writer() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP <> 'INSERT' AND EXISTS(SELECT 1 FROM cp6_stock_move_v1.writer_bindings b WHERE b.tenant_id=OLD."TenantId"
            AND b.warehouse_code=OLD."WarehouseCd" COLLATE "C" AND b.location_code=OLD."LocationCd" COLLATE "C" AND b.product_code=OLD."ProductCd" COLLATE "C") THEN
            RAISE EXCEPTION 'STOCK_ROOT_WRITER_REQUIRED' USING ERRCODE='23514';
          END IF;
          IF TG_OP <> 'DELETE' AND EXISTS(SELECT 1 FROM cp6_stock_move_v1.writer_bindings b WHERE b.tenant_id=NEW."TenantId"
            AND b.warehouse_code=NEW."WarehouseCd" COLLATE "C" AND b.location_code=NEW."LocationCd" COLLATE "C" AND b.product_code=NEW."ProductCd" COLLATE "C") THEN
            RAISE EXCEPTION 'STOCK_ROOT_WRITER_REQUIRED' USING ERRCODE='23514';
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END $$;
        CREATE TRIGGER stock_root_writer BEFORE INSERT OR UPDATE OR DELETE ON public."T_Stock" FOR EACH ROW EXECUTE FUNCTION cp6_stock_move_v1.reject_legacy_writer();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_stock_move_v1.openings FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_stock_move_v1.roots FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_stock_move_v1.decisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_stock_move_v1.events FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_stock_move_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only physical inventory history.");
}
