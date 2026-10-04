# WP6 task review: fixtures, runtime selection and required results

This is one independent partition of the single WP6 task review, relative to base `cbbb7fc8e99290f6aba7589830a98a98726280e9`. Reviewed the complete scoped diff including new files. No .NET build, tests, database access, credential reads, process launch, remote operation or production edit was performed by this reviewer.

## Finding

**P2 — explicit WP6 scope can bypass the required fixture selection when its provider/connection aliases are missing.** `CP6.Space.IntegrationTests/SpaceRelationalFixture.cs:31-36`, `CP6.Space.IntegrationTests/SpaceMigrationTestDatabase.cs:39-48`, `CP6.Tests/DatabaseCompatibility/CoreBusinessRelationalFixture.cs:26-47`, `CP6.Tests/DatabaseCompatibility/Wp5ReportsRelationalFixture.cs:28-49`, and `CP6.Tests/Infra/WmsProductionFactAttribute.cs:18-20` only select on their old per-fixture aliases. Set `CP6_COMPAT_SCOPE=WP6` and a matching `CP6_COMPAT_DATABASE_NAME`, omit those aliases, and the constructors can return or the WMS initializer (`CP6.Tests/WmsProductionSqlServerTests.cs:36,70-90`) can take its legacy SQL path. With a legacy SQL connection also present, ownership/role verification is bypassed before migration; without it, selected modern cases can skip. The formal Entries module supplies the aliases, so this does not identify a failure in a normally wired current matrix. It does violate the selected-WP6 fail-closed entry contract. Fix selection for the corresponding WP6 role while keeping genuinely unselected legacy runs unchanged; do not globally activate all migration attributes, whose discovery validates their inputs before unrelated filtered business cases run. Root authorized this narrow correction after this finding; its follow-up source/test results must be recorded separately.

No additional substantive blocking finding was found in the reviewed scope.

## Reviewed contracts and source checks

- The helper uses explicit provider selection, exact date/name/owner/role parsing, literal loopback, SQL MARS/attach and PostgreSQL multiplex/search-path checks. Its read-only verification reads actual database name and task/owner from the same verification connection. Canonical connection strings preserve the pinned database; no creation, marker mutation or removal is performed. Partial scope/name inputs reach strict Parse when a fixture invokes it; the finding above concerns fixture preselection.
- All ten adapted fixture entrances keep their legacy branches and verify modern owner metadata before migration or fixture writes. Seven project links resolve to the same test-only helper. Runtime/Migration entry wrappers retain the mode and named check contracts; this review excludes the separately authored inner migration guards.
- The two SQL-only OIDC historical facts each declare `SqlServer`, `sqlupgrade`, `FreshPerEntry`, exact method/filter and one result. The fixture independently requires an empty user-table catalog before its historical DDL and never drops runner-owned databases. PostgreSQL does not execute or skip-substitute these SQL facts.
- The required manifest contains 62 entries: 54 SQL-eligible and 51 PostgreSQL-eligible. There are 110 historical source references / 103 distinct immutable files; all 103 source hashes match and every SourceDefinitions path exists. Required TRX display names are present in the union of their own provider-specific historical sources for all 30 TRX entries (68 source bindings); CaseCommand exactly equals each declared filter. Native required names likewise have no missing historical union member where sources exist. Grouped evidence unions establish names/provenance only; they are not relabelled as one current successful run.
- Results parsing requires a real caller-supplied integer exit code zero, exact eligible provider, exact ordinal name set/count, distinct identities/names, every outcome Passed and no skipped/nonexecuted/failed counters. Native assertions require correctly typed boolean/status values; ApplicationChecks additionally bind task/schema/command/role, required counts/hashes and paired equality. Application actual database name is returned and compared with the receipt by Entries. Missing/extra/wrong/duplicate names and failed process results cannot satisfy this contract. Parser fixtures are explicitly offline synthetic inputs, not native business evidence.
- Runtime startup guard removal is limited to accepting the two providers already constrained by DatabaseOptions. WP4 PR149 / WP5 PR152 are delivered in the accepted baseline, and their recorded native business results remain source-scoped historical evidence. The existing identity recording-publisher checks retain component boundaries; the new SignalR command is a distinct required entry, not an assertion that an external Dapr/Kafka/CRM transport has been accepted.

## Evidence applicability and pending execution

Read root public `wp6-owned-contract-green.json` (45/45, zero other, process exit 0; hash BA7D871EA1074B6997A46950B789CD2E1D93BCF46A98718FA9753E8F699CA4C1). Its helper/test hashes match the reviewed input, but it does not exercise the fixture-selection gap. Read `wp6-runtime-mode-green.json` (7/7, process exit 0; hash 512A6E6B3F47F317AC235171A6C01F06F01251B2C31D93412FBE3A482D42C698); its three runtime support/test source hashes match the reviewed files. `wp6-same-runtime-both-providers.json` binds a common 261-file published API to both provider observations (API 445E9B73EB39277C8D395677DCDDE5E14B48AB834890ACB79C985A2AC4F6B9A5), but this is a binding record, not a new test performed by this reviewer.

Read the existing parser offline proof `wp6-results-restore-seed-contract-offline.json`: 433 offline assertions / 57 manifest entries at that earlier input, not a claim of current 62-entry execution. The current self-test source contains targeted SignalR/replay assertions; native execution and latest offline validation belong to root.

Root reported PostgreSQL and SQL application/init/recovery cutouts passed and the formal PostgreSQL matrix was running (session 32411) when this review was assigned. No pending matrix result, later SignalR run, final SQL136 initializer or remote delivery is declared passed here. Root subsequently reported two ERP concurrency failures under investigation; neither the ERP production handlers nor their unchanged business tests are altered by this review. Final delivery remains blocked on actual required results and the scoped P2 correction. ApplicationProbe, inner MigrationOwnership/gates, Baseline, lifecycle/main runner and deployment documents are reviewed separately by root/other assigned partitions; no full repository or independent security/schema-race review is claimed.

## Source input fingerprint

RecordedUtc: 2026-10-03T16:25:38.3867236Z. Scoped files: 30. Canonical sorted path|bytes|SHA256 UTF-8 (LF between rows, no final LF) hash: 08A10723A4807BEA4DFC96D9387671AB7BC52492CF46E53464BF3E29016ACC2D.

| Path | Bytes | SHA-256 |
| --- | ---: | --- |
| CP6.Core/Persistence/DatabaseOptions.cs | 1567 | 976DE28B08802A3BE2C97118767215AE0A96FB8CF23F6F53451A74533105A2DD |
| CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj | 758 | 40D4764811E5C6FA99FDCD1A6971B3E4E777BAEB2CD860A0149873DB8E6ED7B5 |
| CP6.Oidc.IntegrationTests/GrantStoreSqlTests.cs | 17487 | 3C45E6CB98414A281C296B554A2B2B008A2728F9446343994994A7F3BDB34A00 |
| CP6.Oidc.IntegrationTests/OidcRelationalFixture.cs | 7798 | D8142119F07D372B86F5A4ECA5B31E29C3675E878E27FF200CCA9D4CE2649280 |
| CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj | 1347 | 5167FF6BF2335ED78106439808774136D5BF5E268C26333D3C3CBB699C92351B |
| CP6.Space.IntegrationTests/SpaceMigrationTestDatabase.cs | 13110 | FB4C4911564BBA62D0C6EAD5D0A7146338DF09A21B879BAB18A6D042F07F44B9 |
| CP6.Space.IntegrationTests/SpaceRelationalFixture.cs | 11670 | 9E558DA98B0724E35B3ED7A8C2771F86AEE214F812A02C03B8B4D772FF743E45 |
| CP6.Space.IntegrationTests/SqlServerFactAttribute.cs | 1061 | C1487CA98E2276188390FD0C5BDBEA299D9A5405FE2EFDFA59341FEF3EE41DD7 |
| CP6.Tests/CP6.Tests.csproj | 2022 | A965279AFEB64C590EB571D7042A8197C83EE2394E26FB9A2DAF1449FE9944D8 |
| CP6.Tests/DatabaseCompatibility/CoreBusinessRelationalFixture.cs | 8503 | F9D139AAF9FC78F52ABA055B9F31F7DC99354F0833DCE72E0D6D765098AA5B80 |
| CP6.Tests/DatabaseCompatibility/Wp5ReportsRelationalFixture.cs | 10818 | 6BAC92BCAFC366E719F7BF369A4B2C7BE140A01E62604E0C9DCC6E900C8C9629 |
| CP6.Tests/Infra/WmsProductionFactAttribute.cs | 813 | 7CFA4F5B8AA02F6E0ECFB18DDAFECC3296805A7721528EC8AB99300A4AB84EF2 |
| CP6.Tests/Persistence/DatabaseInitializationModeTests.cs | 1201 | 71C5680B87DE2CAC6EBE010B006431C4F05385973432A5F11A2BB3ED04C50B36 |
| CP6.Tests/Persistence/DatabaseWiringTests.cs | 17047 | 75C380D633CC199ACBC89D26CD96F4BDB84AC48B37A51DCFE9B6D6E6411F2843 |
| CP6.Tests/Persistence/OwnedTestDatabaseTests.cs | 8945 | CFA3FA9BB38FDBCB4A840FBF3CC6E0609B84AD0F6210E544A93449C575477DE5 |
| CP6.Tests/WmsProductionSqlServerTests.cs | 46334 | 2CA6C53FA675BD5671477E8C0A85A33A91A1257D9781D7D41391EAB588493F86 |
| CP6.WebApi/Configuration/DatabaseRuntimeSupport.cs | 456 | EF982662792231D3A5C5B9D85A6AC8D138813EF76C491BAABC19B7054548BC64 |
| eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj | 1025 | 088EFA338BF5CD2B8D62CA256498893D4F7EDA8CC0D632901E11F1014D734420 |
| eng/crm/erp-integration-tests/ErpRelationalFixture.cs | 9103 | 20E680B29AFD4F7B7D247A873330E6803C18F7BDCFC9843D396A15D318312C3D |
| eng/crm/identity-events-fixture/CP6.IdentityEvents.Fixture.csproj | 524 | 313D924654EB25FD406BE5B1A7BFF08BF1E5289B5BF3DB9DEC441C909A6C000F |
| eng/crm/identity-events-fixture/ProviderCases.cs | 11489 | EDF3991B0C43D627346ED6CEE1220BCE6F8515316560B6C0341ACC37C3FB8534 |
| eng/database-compatibility/OwnedTestDatabase.cs | 8968 | 637248865D9FBA1FEB63E011040B57817E78DE62AD3B4E03BDAEF6485FAED4A1 |
| eng/database-compatibility/required-cases.json | 238983 | 706D889A29D3BDF02A43B2D9D5DD01115DEC66A9E0881A87DB4694FD4DF65330 |
| scripts/database-compatibility/DatabaseCompatibilityResults.psm1 | 20078 | AD66A8EDA85E1283649AF010043D85B1D1744008837EB01E529FB7D79491D9C1 |
| scripts/database-compatibility/Test-DatabaseCompatibilityResults.ps1 | 17466 | 875152820D5715FDAD57408B8D856261BAFDAC769715971721703324337E17EB |
| tools/CP6.DatabaseCompatibility.MigrationProbe/CP6.DatabaseCompatibility.MigrationProbe.csproj | 783 | D09A897F0B9E0201D96BD96EE4A95EABB28168F70C2503DD6DFE31DA50123DEC |
| tools/CP6.DatabaseCompatibility.MigrationProbe/Program.cs | 22548 | 95A3B0558C6C0E4FD0980A819B1D3D26C480594BD8B9D2C050E5269420D6E6C0 |
| tools/CP6.DatabaseCompatibility.RuntimeProbe/CP6.DatabaseCompatibility.RuntimeProbe.csproj | 624 | DFD5B493DEBCF6F3450B3825F5E26DE918D80DC3FBC4A5981B22B72C08E890E6 |
| tools/CP6.DatabaseCompatibility.RuntimeProbe/Program.cs | 8218 | FBFCEE96E301272598A58E6977973CDE4C351B26591D8BAC8B7EB424A1566876 |
| tools/CP6.DatabaseCompatibility.RuntimeProbe/RuntimeFixture.cs | 6553 | 5F6D1BC56BE58BB7C79F40AB02C4C31B8C69356F2C952AF49FE0C508DB2B454D |
