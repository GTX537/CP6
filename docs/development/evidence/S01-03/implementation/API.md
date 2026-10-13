# S01-03 FIN-CREDIT exact amount API

`CP6.Entity.Contracts.FinCredit` provides an immutable `CreditAmount` with a `BigInteger Coefficient` and nonnegative `int Scale`. The represented value is `Coefficient × 10^-Scale`. Private construction normalizes fractional trailing zeros and gives zero scale 0. Default construction is zero; it has no implicit native profile.

The native authority is `https://cp6.example/design/fin-credit-wire-r4.schema.json`, source `numeric-0`. `CreditAmountProfile.Signed`, `.NonNegative`, and `.Positive` select `/$defs/signedDecimal`, `/$defs/nonNegativeDecimal`, and `/$defs/positiveDecimal`. Select a profile from the exact native field binding, never a property-name guess. [PROFILE-BINDINGS.json](PROFILE-BINDINGS.json) records all verified 4/33/5 bindings and their source/package hashes. In particular, `fxEvidence.rate` and `planAtpObservationInput.requestedQuantity` are positive; `planAtpObservation.promisedQuantity/oneOf/0` is nonnegative.

| API | Boundary and behavior |
| --- | --- |
| `CreditAmount.Parse(text, profile)` | Validates the original native spelling and sign before construction. Accepts canonical ASCII strings with at most 38 total digits and 8 fractional places; rejects negative zero, fractional trailing zeros, leading zeros, exponent, plus sign, whitespace and Unicode digits. |
| `CreditAmount.FromIntermediate(coefficient, scale)` | Explicitly creates an intermediate. Coefficient magnitude and fractional scale may exceed final field limits; scale must be nonnegative. Normalizes without rounding. |
| `CompareTo`, `Equals`, `GetHashCode` | Compare exact values; canonical normalization gives equal representations and hashes for equal values. |
| `Add`, `Subtract`, `Sum` | Align scales using powers of ten and perform BigInteger arithmetic. Permit negative and oversized intermediates. `Sum` streams its input and has no application term-count cap. |
| `ToFieldString(profile)` | Revalidates the normalized final sign, precision and scale for the selected target field and emits its canonical string. Every emission requires a profile, including values previously parsed under another profile. |

For example, `Parse(max38, Signed).Add(Parse("1", Signed))` is a legal intermediate and cannot emit a final field; subtracting one restores `max38` and permits emission. A negative intermediate can later produce a positive final value. No decimal/double/float intermediary, clamp, truncation, rounding or forced scale 8 is used. Coefficient capacity is arbitrary precision subject to available memory and framework limits; scale uses `int`.

Invalid field spelling or final field bounds produce `FormatException`. Null parse text produces `ArgumentNullException`. Unknown enum profiles produce `ArgumentOutOfRangeException`, validated before inspecting text or a JSON token. Invalid intermediate scale produces `ArgumentOutOfRangeException`.

For a standalone scalar, register `new CreditAmountJsonConverter(profile)` in `JsonSerializerOptions.Converters`. All required amount tokens must be JSON strings; number, null, boolean, object and array tokens produce `JsonException`. Write revalidates the final target profile and reports invalid values as `JsonException`. Unconfigured `JsonSerializer` read/write fails with `InvalidOperationException`, preventing an amount or unbounded intermediate from being serialized as an object. Its parameterless converter exists only for serializer metadata discovery.

Use `[CreditAmountField(CreditAmountProfile.Positive)]` on an amount property when a carrier contains different profiles. Property attributes select each field explicitly and take precedence over an options-wide converter. The attribute also supports `CreditAmount?`; nullability and presence remain enclosing-field rules. Mark required fields with C# `required` or `[JsonRequired]`. An optional nullable property can be missing/null. An unmarked nonnullable property can default to zero when absent, so it does not establish required presence.

For complete carriers, use unchanged production `Wire.StrictJson.Parse`/`Deserialize`, which validates the entire JSON value and rejects duplicate decoded names before any target conversion. To retain bytes, own a `StrictJsonDocument`, call its `Deserialize<T>(options)` for values, and separately call `GetOriginalUtf8()` for a defensive original-byte copy. An escaped token such as `"\u0031.\u0032"` and `"1.2"` decode to the same amount while their token/document bytes and SHA-256 differ. Neither decoded amount text nor reserialization proves byte fidelity.

This library does not adopt the existing decimal credit service, decide credit business outcomes, transfer FIN-CREDIT ownership, persist data, add database schemas, or complete full business acceptance. The illustrative test carriers validate contract boundaries only.
