# WP2 real text boundary probe

This executable compares SQL Server's recorded `Chinese_PRC_CI_AS` contract with the PostgreSQL 18 candidate using the repository's exact EF 8.0.30 / Npgsql EF 8.0.11 / Npgsql 8.0.8 pins. It links the actual consumer text adapters, without constructing replacement implementations of those adapters.

Use a dedicated, owner-marked `CP6Compat_WP2_YYYYMMDD_XXXXXXXX` database on loopback. Supply connection strings only through `CP6_TEST_SQLSERVER` or `CP6_TEST_POSTGRES`, and the task receipt's 32-hex identity through `CP6_TEST_DATABASE_OWNER`. The executable checks server/database/owner before any DDL. Its schema, collation, functions and rows share a single transaction that always rolls back; cleanup verifies schema and collation absence. Each individual assertion uses a savepoint so an expected PostgreSQL constraint error does not poison later checks. It does not create/drop databases, adopt unmarked databases, use business tables, or commit probe data.

```powershell
dotnet build tools/CP6.DatabaseCompatibility.TextProbe/CP6.DatabaseCompatibility.TextProbe.csproj -p:RestoreLockedMode=true
# Run only after a successful build; choose an absolute report destination.
dotnet tools/CP6.DatabaseCompatibility.TextProbe/bin/Debug/net8.0/CP6.DatabaseCompatibility.TextProbe.dll --provider SqlServer --output <report-path>
dotnet tools/CP6.DatabaseCompatibility.TextProbe/bin/Debug/net8.0/CP6.DatabaseCompatibility.TextProbe.dll --provider PostgreSql --adapted --output <report-path>
```

To capture source input hashes, run the executable from this tool's directory. Reports also identify the actual executable hash. A successful applicable build log is required to associate source hashes with that executable; changing source and running an old binary is not valid evidence. Without `--adapted`, PostgreSQL intentionally uses ordinary varchar and the provider's original services: this negative control must expose PAD SPACE keys/parameters and UTF-16 length differences. Its nonzero exit is an actual failure, never relabeled as a successful business test.

Assertions cover raw whitespace/Unicode round trips, fixed ASCII padding, scalar/array parameters, case/width/accent/NFC, interior spaces, substring/contains/concatenation/trim, UTF-16 LEN, native PK/FK/uniqueness and managed CP936/range/NULL rules. These are isolated text assertions. They do not prove all linguistic equivalences, all caller normalization, EF navigation fixup, full context migration installation, HTTP/business acceptance or production readiness. Fixed ASCII codes and varchar storage capacity do not imply that PostgreSQL should silently discard Unicode using SQL Server's historical lossy encoding.

Primary implementation references: [PostgreSQL 18 character storage](https://www.postgresql.org/docs/18/datatype-character.html), [pinned provider's fixed-character mapper](https://github.com/npgsql/efcore.pg/blob/v8.0.11/src/EFCore.PG/Storage/Internal/Mapping/NpgsqlCharacterStringTypeMapping.cs), [pinned provider's type mapping source](https://github.com/npgsql/efcore.pg/blob/v8.0.11/src/EFCore.PG/Storage/Internal/NpgsqlTypeMappingSource.cs). Unbounded `bpchar` preserves stored text and PAD SPACE equality; implicit `bpchar::text` conversion trims trailing U+0020, so the actual consumer query adapter obtains raw text through `bpcharsend`/UTF-8 conversion before string operations. The custom services use provider internals only in two locally warning-scoped files; upgrades require repeating this real regression gate.
