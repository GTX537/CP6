# Main storage inventory

This is metadata tooling for WP02-02. It does not reference or rebuild the product projects, migrate existing data, install tenant profiles or open a tenant route. The adopted inventory and its limits are in [OWNERSHIP.md](../../../docs/development/storage/current-main/OWNERSHIP.md).

Use an **isolated, explicitly owned local PostgreSQL database** initialized from an adopted Main publish directory. W675 reused W673's binaries after checking that the product source and dependency inputs had not changed. `DatabaseInit` is the existing application mode; normal host services must be disabled. Keep passwords in the existing local fixture/PGPASSFILE mechanism, never in the commands or committed inventory.

1. Capture the actual post-migration metadata with `psql -X -q -At -v ON_ERROR_STOP=1 -f catalog.sql -o catalog.json`, using explicit local host, port, database and username. The query reads no business rows. It excludes PostgreSQL system namespaces and the two named fixture namespaces. Preserve the raw capture outside the repository; its function bodies aid review.
2. Build only the small probe, supplying the adopted publish directory as `PackageRoot` and an external artifacts path:

   ```powershell
   dotnet build ModelProbe/ModelProbe.csproj -p:PackageRoot=<adopted-package> --artifacts-path <local-output> -p:UseSharedCompilation=false --disable-build-servers -m:1
   dotnet <local-output>/bin/ModelProbe/debug/ModelProbe.dll <adopted-package> <local-output>/ef-models.json
   ```

   The probe loads those assemblies. It creates no database connection: all model inputs use a metadata-only placeholder. Reflection enumerates the actual context types, and constructs the internal fixed models plus both RMA mappings. The sample tenant is not an installed tenant.
3. Canonicalize the model's partial-index predicates using PostgreSQL's own parser and native column types. This avoids interpreting formatting, inserted casts and index names as different uniqueness domains:

   ```powershell
   python -X utf8 -B render_predicates.py <local-output>/ef-models.json > <local-output>/predicates.sql
   # Use the same explicit isolated connection arguments and PGPASSFILE as in step 1.
   psql -X -q -At -v ON_ERROR_STOP=1 -f <local-output>/predicates.sql -o <local-output>/model-predicates.json
   ```

   Only empty temporary tables and indexes are created, then rolled back. If psql fails, do not use partial output as a successful result.
4. From the repository root, join the inputs with tracked production source:

   ```powershell
   python -X utf8 -B eng/wp02-02/storage-inventory/build_inventory.py --root . --catalog <local-output>/catalog.json --models <local-output>/ef-models.json --predicates <local-output>/model-predicates.json --source <reviewed-main-sha> --binary-source <adopted-binary-sha> --output docs/development/storage/current-main
   ```

   Inspect `model_catalog_mismatches`, direct/generated source coverage and the actual context list. The comparison checks native column types/facets and nullability, plus unique columns, partial predicates and primary-key domains. `equivalent_index_names` records name-only differences. The captured FK, trigger, function, ACL and RLS records remain available for the task review; the tool does not claim to prove their business semantics.
5. Review the Owner/transaction/retention classification against current source, existing adoption evidence and all optional installers. `sources.jsonl` is a conservative navigation index, not a static analyzer. Closed profile writers, package writers, migration-only metadata and database-side writes are called out separately. Do not turn a token match into proof of reachability, authorization or per-tenant restore support.

JSONL records omit catalog NULL/empty fields; `false` and zero remain explicit. Function definitions are linked to source instead of duplicating them in the committed inventory. The local fixture administrator is normalized to `LOCAL_MIGRATOR` before publishing metadata. After capture, stop only the owned PostgreSQL process and retain the original local logs; no credentials or business rows belong in this inventory.
