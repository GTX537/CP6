# Owner adoption checklist — not executed by S01-10

The names below identify source dictionary/DDL tables, not migrated production EF entities. All real Owner adoption remains **UNPROVEN / NOT_RUN**. The adapter and disposable fixtures do not select a replacement Owner, change permissions, remove TenantId, deploy tenant schemas or migrate existing data. S02-00 `cp6-pg-tenancy-v1` is a design dependency only.

Every row must close these obligations in its own approved Owner/migration task:

1. Identify actual schema/table/model version, authoritative Owner command, every ORM/raw SQL/bulk/import writer and reader, nullability, sign and scale rules. Record legacy decimal/float/rounding entry points, not just the new happy-path writer.
2. Recover exact values from authoritative original wire/receipt bytes when available. Quantify old rounding/truncation; do not reconstruct supposedly exact missing digits from a rounded legacy value. Add a versioned representation alongside old storage only under an approved migration. Preserve currency, unit, business policy scale, original UTF8 and digest identities independently.
3. Validate the native destination field, write all coefficient/scale/original-wire/state columns on the same connection and transaction, retain Owner identity and authorization, and prove all writers switch atomically. Test old/new readers, concurrent mutation, duplicate replay and failed transaction rollback. Text coefficients cannot be compared or ordered lexically as numeric values, or CAST/SUMmed through finite PostgreSQL numeric.
4. Adopt S02-00's trusted tenant-to-schema mapping and transaction/connection-pool context only in the approved tenant window. Preserve TenantId and cross-tenant keys/constraints, qualify identifiers from trusted metadata, verify DB roles/RLS as applicable, and prove pool reuse cannot leak tenant context. This fixture's single schema is not a tenant isolation test.
5. Plan per-tenant forward migration, backup/restore and recovery with version/pin manifests. Prove a rollback application can read the new representation and all newly accepted values. A legacy decimal writer or scale8 column cannot be resumed for arbitrary-length/scale18 data without a proved compatible bridge; otherwise deployment rollback must stop writes. No destructive down-migration or silent narrowing.

| Source table / field set | Count | Specific adoption checks beyond the common obligations |
|---|---:|---|
| FIN-CREDIT native fields in mapping `credit.field_bindings` | 42 | signed4/nonnegative33/positive5. Native nullable/object conditions stay with the Owner. Replace each legacy decimal path only after BigInteger coefficient + scale writes/readback and transaction evidence. Validate before binding; numeric(38,0) CHECK cannot detect a fraction already rounded by typmod. |
| FIN-INV SourcePortion | 4 | SourceQty/BaseQty nonnegative numeric checks, positive ratio components; preserve conversion/basis identity and original Rational strings, not a reduced replacement. |
| SourcePortionBalance | 7 | NonNegativeQty12 wire; valuation/clearing/reservation relations and concurrent available-balance mutations must remain exact and atomic. |
| ValuationPoolHead | 2 | CurrentQty/CurrentUnroundedValue must refer to the same accepted revision; coordinate head CAS with revision append. |
| ValueLayer | 3 | OriginalQty nonnegative; unit Money policy and unrounded original value remain distinct; preserve provisional/legal-zero exclusivity. |
| ValueLayerBalance | 5 | Nonnegative numeric balances (without silently tightening signed Qty12 lexical zero); exact Available=Original−Consumed+Reversed−Reserved, concurrent reservations/consumption and reversal. |
| MovingAveragePoolRevision | 11 | ACTIVE five optional triples present and afterQty positive; EMPTY triples all null and quantity/value zero. Preserve raw `00`/`-0` numerator and unreduced positive denominators. Separately prove native exact-average, quantization, remainder units, revision/CAS and carry arithmetic. |
| ValueLine | 6 | Intermediate rounded-before-residue/residue/final mappings are dictionary-derived, not invented native properties. Preserve Final=Before+Residue and line/order allocation rules; pending scale18 supplement affects three fields. |
| LayerConsumption | 7 | TakeQty positive; After=Before−Take; coordinate layer balance/effect append and reversals; pending scale18 supplement affects three Money amounts. |
| RoundingTrace | 5 | Preserve mode, policy scale, exact raw total, assignments and FinalTotal=TargetTotal. Policy 0..8 does not narrow native Decimal18; four fields require the supplement. |
| ReconciliationLine | 10 | Preserve absence versus zero for all ten nullable values; optional Money/Quantity object identity, coverage/watermark/version and pagination must remain native. Nine amounts require the supplement. |
| SourcePortionRange | 2 | NonNegativeQty12 endpoints; exact To>From and coverage/overlap rules without finite CAST. |
| FxRateFact | 2 | Positive exact numerator/denominator decimal values, source Owner/version/digest/effective-time identity. Do not misrepresent this dictionary-derived binding as a complete native FxRate codec. |
| ProvisionalValueFact | 2 | Keep approval/cost evidence, expiry/true-up/correction Owner; maxAmount policy scale is independent, pending supplement permits all native Decimal18 digits. |
| ATP evaluation.result_uom | 1 nullable | All terminal-state result/digest/time bindings from the full composed DDL; validate raw text before varchar narrowing. |
| ATP segment.uom | 1 required | Organization/evaluation FK, allocation≤capacity, scale and native result identity; all raw/bulk writers use a reviewed text ingress guard. |
| ATP basis.native_uom/result_uom | 2 required | Both UOMs, representation predecessor/proof relation and deduction sum; organization/evaluation FK. |
| ATP reservation_ledger.uom | 1 required | Self-exclusion/deduction relation, organization/evaluation FK and original allocation/demand binding. |
| ATP replay/recovery entry points | five composed patches | In each real path, ordered replay locks precede evaluation locks; actor/delegation/operation/request and both digests bind replay. Prove full authorization/fencing, validator, receipt, CAS, recovery and publish behavior separately; the fixture executes only the selected lock/binding critical section. |

Known legacy consumers include `CreditControlService` (`decimal` amounts), `StockFinBridge` (rounding to two places) and `ISupplyService` (`decimal` quantities). Their presence is a migration inventory item, not permission to switch them in this task. Existing DB/Owner tests, the 247 full runtime scenarios, 41 historical REFERENCE_ONLY cases, missing ITER/REL originals and WP05 PROPOSED evidence retain their prior scope/status.
