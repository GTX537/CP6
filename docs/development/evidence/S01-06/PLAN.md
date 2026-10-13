# S01-06 ATP StableId

Task baseline and clean actual main: `8569b669fed56ddc7b53b7bb24666c7fb93d544a`. Fixed input baseline: `d7a210256783e45291a5e724ea068ed4fc165ecc`; the later main increment is coordinator documentation only. S01-02 is delivered; shared Wire and both project configurations stay read-only.

1. Verify the fixed profile/vector/source-index Git bytes and all four composed ATP source hashes, original hashes and full registered patch bundle pins. Record the five PostgreSQL UOM columns, nullability and complete PLAN-IF-004 plus PLAN-IF-005 input; never execute DDL.
2. Test then implement a C# StableId value/converter and TS branded string parser using the accepted R1 absolute-end patterns. Keep the original text and case, no coercion/trim/truncation. C# JSON strings round-trip, wrong tokens reject, and carriers use delivered StrictJson before conversion.
3. Extract only `$defs/StableId` into a separately identified local Schema fragment, with exact source/version/hash attribution. The source full ATP wire identity remains distinct. No full ATP contract or Owner adoption is claimed.
4. Both language tests must discover exactly all 22 assigned fixed IDs; TS executes the fragment's type/minLength/maxLength/pattern assertions against those same inputs. Add explicit 16/17/128/129, null/wrong token, ASCII/Unicode, leading punctuation, whitespace/LF/CRLF and serialization tests. Keep each endpoint's observations explicit.
5. Retain effective failing runs, final task-local test/type-check output, source/input/dependency/build hashes and one complete task-level review. Reuse unchanged upstream results with original provenance; do not rerun broad business tests.
6. Publish a candidate and fixed DELIVERY through Handoff after checking candidate/protected-main workflow events. COORD serializes integration, shared status and remote-main receipt. No DB/migration/package release/deployment or Actions.
