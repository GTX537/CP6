# Wave7 dispatch independent review

ACCEPT. No open findings remain. This decision covers the final 29-file documentation-only staged candidate on `codex/wave7-dispatch-20261014`, based on Main `1129953156b7775152a74ad2f51cb95ef430ee56`. It approves the dispatch documentation for the normal protected delivery flow; it is not implementation acceptance, formal package trust, business adoption, or permission to run Actions/deploy.

One P2 was found and resolved: WP02-26 originally required cluster system identity and database OID before the first setup of a new, unprovisioned cluster. That condition could not be satisfied. The final policy separates the initial owner/path/free-port checks from post-creation identity capture and verification, and requires all recorded identities for subsequent test/crash/restart/stop operations. Failed initial provisioning retains ownership markers and partial evidence; recovery cannot treat a missing OID as valid runtime identity. The corrected policy is identical in `WP02-26.md:43`, `dispatch.json:382`, and `roadmap.json:161100`. The task card explicitly clarifies the earlier recommendation while preserving the raw admission reports.

The complete initial candidate received one concentrated review. The follow-up covered only those three changed files and their impact; the other 26 frozen raw files remained identical. No broad product verification was restarted.

Fresh static verification established:

- All 29 final files match the final freeze; staged blobs match their worktree bytes, allowing only the existing CRLF-to-LF normalization where applicable. The worktree has no unstaged diff when `core.longpaths=true` is used. `git diff --cached --check` exits 0.
- All 76 recorded source pins match their actual Git blobs or explicitly designated raw archive bytes. Nine direct prerequisite deliveries are DONE and their source commits are ancestors of the correct fixed repository main.
- All 48 recorded added relative Markdown file links resolve. This checks file targets, not remote URL availability or every heading anchor.
- Only WP01-01, WP02-02, WP02-12 and WP02-26 task rows change. The other 371 tasks, all 125 targets and all 288 revision/historical acceptance entries remain unchanged. A/B's complete old assignments move to history and their prior preparation deliveries remain unchanged. DONE remains 22/375; business delivery remains 0/125, with 247 NOT_RUN and 41 REFERENCE_ONLY.
- The four worker worktrees are clean and remain at their assigned Main/Platform baselines. Declared write paths are pairwise disjoint within each repository. Ports and resource roots are separated, with runtime identity/availability rechecks required before use.
- Ten received admission/intake/setup files match the external originals byte-for-byte. The evidence directory preserves CRLF; the one terminal-blank exception applies only to the received admission-platform.md. Existing formatting failures and their raw log hashes are retained.

Substantive scope conclusions:

- A is an incremental technical decision, lossless PostgreSQL mapping and independently disabled gate candidate, not another unchanged unsigned template. Its task stays PLANNED / NOT_CHECKED; actual adoption/completion remain false.
- B explicitly limits four source ProjectReference substitutions, generated locks and build output to fresh owner snapshots. It exercises full Main build roots and a private synthetic-authority Kestrel probe while preserving external package signature/source policy. It neither grants a production source-adoption exception nor releases formal trust; WP02-02 stays BLOCKED / NOT_CHECKED and WP02-28 remains unopened.
- C/D are READY / CONFIRMED for bounded library implementation. Actual broker producer entitlement, original metadata/bytes, durable persistence claims and native PostgreSQL evidence have explicit gates. Missing capabilities remain UNPROVEN/NOT_RUN and cannot certify corresponding positive guarantees. Fixture success does not become Main/CRM Owner adoption or production SLO acceptance.
- No worker START is fabricated. Resource provisioning is still future work. No Actions, formal package publication, tag push, business data migration or production deployment is authorized by this dispatch.

Reviewed identities:

- Initial raw aggregate: `be4bfe9a368c8936bcaae8e7275ec45fc912737b251e8ef80fa8d72a74a57287`.
- Final raw aggregate: `fb592ec8800b243aaaec50d0597934c623d2ee5912bd8220b413380b799be87f`.
- Final staged-blob aggregate: `f9466b9f4bf748d2155a5e571dcc591888a5ac2dad7c5582b1f9fb0e28f07ffa`.
- Final staged binary diff SHA-256: `4ce29111f4e20a359d9d669a12fda67b297aa6a430f972c945116cac35649a78`.

Raw and staged aggregates use SHA-256 of Python `json.dumps(hashes, sort_keys=True).encode()` in the frozen path order. Per-file identities are included in review.json. The binary diff hash is the final observation only; the earlier static scan overlapped the coordinator's correction and is not presented as an independently frozen initial diff hash.

Limitations: this review performed local static reads only. No services, databases, product builds/tests, remote queries/writes or Actions were run. Historical runtime and remote-main evidence retain their original scope; the coordinator must perform the already-required fresh workflow guard, protected merge and remote containment checks during delivery. Runtime resource observations are not persistent reservations and are not rerun here.
