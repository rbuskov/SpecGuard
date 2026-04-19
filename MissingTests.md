# Missing Tests — SpecGuard

A prioritized checklist of test cases that are not currently covered by the
`SpecGuard.Test`, `SpecGuard.MuseumApi.Tests`, or `SpecGuard.TicTacToeApi.Tests`
test projects. Each item describes the behavior the test should exercise and
the kind of assertion that would demonstrate the behavior.

Organization:

1. Middleware (`SpecGuardMiddleware`)
2. Registration (`ExtensionMethods`, `SpecGuardOptions`)
3. Body validator (`JsonBodyValidator`)
4. Parameter validator (`ParameterValidator`)
5. Schema builder (`OpenApiSchemaBuilder`)
6. Read-only collector (`ReadOnlyPropertyCollector`)
7. String-numeric coercer (`StringNumericCoercer`)
8. Route matching (`RoutePatternMatcher`)
9. Validation result shape (`ValidationErrorResult`)
10. Sanitizers / schema transformers
11. End-to-end sample-app coverage (MuseumApi, TicTacToeApi)
12. Contract / documentation alignment

---

## 1. Middleware (`SpecGuardMiddleware`)

### 1.1 Content-type detection (`IsJsonRequest`)

- [ ] Request with `application/json; charset=utf-8` is treated as JSON
  (parameters on the media type must not defeat detection).
- [ ] Request with `application/vnd.company.v1+json` is treated as JSON
  (`*/*+json` suffix rule).
- [ ] Request with `application/ld+json`, `application/hal+json`,
  `application/problem+json` are treated as JSON.
- [ ] Request with `APPLICATION/JSON` uppercase is treated as JSON
  (case-insensitive).
- [ ] Request with `application/xml` is **not** treated as JSON (body is not
  parsed; downstream handler sees raw body).
- [ ] Request with `text/json` is **not** treated as JSON (only
  `application/json` and `+json` suffixes are recognized).
- [ ] Request with malformed `Content-Type` header (e.g. `application/;json`)
  passes through without 400.
- [ ] Request with no body but a JSON content type and `required: false` is
  passed through (current `Required_body_empty…` tests cover validator; add a
  middleware-level test that asserts `next` is invoked and response is 200).

### 1.2 Request stream handling / buffering

- [ ] After SpecGuard runs (validation succeeds), the request body stream is
  rewound and a downstream middleware can re-read the original bytes.
- [ ] Buffering stays enabled for handlers that call `Request.Body.ReadAsync`
  after `next(context)` runs.
- [ ] A validation failure at 422 still leaves the stream consumable (not
  disposed) for a subsequent exception handler if it tries to inspect it.
- [ ] Middleware that ran *before* SpecGuard and consumed the stream without
  buffering yields an empty-body pass-through when SpecGuard runs — document
  and verify this in an integration test so regressions show up.
- [ ] Large bodies (> default in-memory buffering threshold) still rewind
  correctly.

### 1.3 Malformed JSON 400 responses

- [ ] Problem details body includes a `type` field pointing at
  `https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1`.
- [ ] Problem details `status` is the integer `400` (not string).
- [ ] Response `Content-Type` is `application/problem+json` (verify exact
  media type, not just body shape).
- [ ] UTF-8 BOM in the body does **not** cause a 400 for otherwise valid JSON.
- [ ] Trailing newline/whitespace does not cause a 400.
- [ ] Body `null` alone (valid JSON literal) is not treated as malformed and
  flows to validators.
- [ ] Body that is a top-level scalar (`"X"`, `42`, `true`) is not malformed.
- [ ] Body that is a top-level array (`[1,2]`) is not malformed.
- [ ] Empty-only whitespace body is treated as empty, not malformed (current
  test relies on `IsNullOrWhiteSpace`; lock the behavior in for the middleware).

### 1.4 Spec loading (`EnsureInitializedAsync` / `LoadAndInitializeAsync`)

- [ ] Concurrent first-requests race on initialization and only ONE HTTP
  fetch is made (two parallel tasks → single `SendAsync`).
- [ ] Spec URL returning non-JSON response (e.g. HTML 200) surfaces a
  `JsonException` and does not poison `initializationTask`.
- [ ] Spec URL returning a JSON object without `paths` initializes validators
  to an empty state and subsequent requests pass through unchanged.
- [ ] Spec URL returning `{}` (empty object) — requests are all treated as
  unmatched.
- [ ] Spec URL with a query string (e.g. `/openapi/v1.json?foo=1`) is
  dispatched as a spec request — or explicitly rejected. Lock the behavior.
- [ ] Spec URL with a fragment is handled sanely.
- [ ] Relative spec URL without a leading slash (e.g. `openapi/v1.json`) —
  document and test the resolution behavior.
- [ ] Absolute spec URL with a non-matching host is **not** treated as a spec
  request when the incoming request's host differs (e.g.
  `UseSpecGuard("https://api.com/spec.json")` and a request to
  `localhost/spec.json` — currently `IsSpecRequest` compares Scheme +
  HostAndPort + Path, so this should fail the match).
- [ ] Spec URL using `file://` or other non-http(s) schemes: verify behavior
  (rejected, or treated as relative).
- [ ] HTTP 404 or 5xx from the spec URL propagates as an exception on the
  first request and does not cache a faulted task.
- [ ] `HttpMessageHandler` registered in DI is reused rather than being
  disposed (current implementation uses `disposeHandler: false` — verify).

### 1.5 Spec-request bypass

- [ ] Relative spec URL under a `PathBase` (e.g. `/myapp`) — a request to
  `/myapp/openapi/v1.json` bypasses validation.
- [ ] Spec-request path comparison is case-insensitive
  (`/Openapi/v1.json` matches `/openapi/v1.json`).
- [ ] Absolute spec URL with different port compared to request port is
  **not** treated as spec request.

### 1.6 Operation-not-matched pass-through

- [ ] Unknown path + unknown method → passes through, no body parsing
  (already covered for JSON content type; add explicit test for NON-JSON
  content type too).
- [ ] Known path with an unknown method (e.g. `POST /special-events/{id}`
  where spec only defines GET/PATCH/DELETE) — passes through without
  validation.

### 1.7 Error aggregation across validators

- [ ] When multiple validators each produce errors, the order of errors in
  the 422 response follows the order in which validators run. Lock this so
  consumers can rely on it.
- [ ] When every validator returns no errors AND no validator `MatchesOperation`
  returns true, `next` is called once (no double-invocation regression).

### 1.8 Response serialization of 422

- [ ] 422 response `Content-Type` is `application/problem+json`.
- [ ] `errors` array element field names are lowercase
  `message`/`in`/`path` (camelCase policy is in effect from `AddSpecGuard`'s
  default `JsonOptions`).
- [ ] Each error `in` is one of {`body`, `path`, `query`, `header`, `cookie`}.
- [ ] Problem details `type` is the 15.5.21 URL.

---

## 2. Registration API (`ExtensionMethods`, `SpecGuardOptions`)

### 2.1 Option propagation

- [ ] `AddSpecGuard(o => o.RejectAdditionalProperties = true)` causes the
  registered `JsonBodyValidator` to observe `RejectAdditionalProperties = true`
  (resolve the validator from DI and exercise it against a body with
  unknown fields).
- [ ] `AddSpecGuard(o => o.AllowStringNumerics = true)` causes the registered
  `JsonBodyValidator` and `NumericSchemaTransformer` to both observe the
  flag.
- [ ] The same `SpecGuardOptions` singleton instance is injected into both
  validators and the numeric schema transformer — mutating on one path is
  seen by the other (or, if intentionally isolated, lock that behavior).
- [ ] Configuring options twice via two `AddSpecGuard` calls — second call's
  options are ignored (registration is idempotent), or the last one wins.
  Lock the chosen behavior.

### 2.2 Default `JsonOptions`

- [ ] `AddSpecGuard` does not overwrite a user-supplied `PropertyNamingPolicy`
  (current code uses `??=`; verify by pre-configuring
  `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` and asserting it
  survives).
- [ ] `AddSpecGuard` does not add a second `JsonStringEnumConverter` if one
  is already registered (current check uses `OfType<JsonStringEnumConverter>`).
- [ ] `AddSpecGuard` adds exactly one converter and one naming policy on a
  clean `IServiceCollection`.

### 2.3 Schema / operation transformer registration

- [ ] `ValidationResponseTransformer` is registered when
  `AddValidationResponses` is `true` (default) and **not** registered when
  `false`.
- [ ] All five schema transformers (`NumericSchemaTransformer`,
  `SbyteSchemaTransformer`, `HalfSchemaTransformer`,
  `TimeSpanSchemaTransformer`, `EmailAddressSchemaTransformer`) are
  registered via `ConfigureAll<OpenApiOptions>` and fire during spec
  generation.
- [ ] The transformer list is deduplicated when `AddSpecGuard` is called
  twice (today `ConfigureAll` may queue the same transformer twice —
  lock the current behavior).

### 2.4 `UseSpecGuard` overloads

- [ ] `UseSpecGuard()` with no argument uses the default `/openapi/v1.json`.
- [ ] `UseSpecGuard("/custom/spec.json")` stores the relative URL and routes
  accordingly.
- [ ] `UseSpecGuard("https://example.com/spec.json")` stores the absolute URL.
- [ ] Passing a relative URL without leading slash — document and test.

### 2.5 Option honored end-to-end

- [ ] Add a test that `SpecGuardOptions.AddValidationResponses = false`
  is honored end-to-end: the published spec contains no SpecGuard-added
  `400` / `422` entries on operations that would otherwise receive
  them.
- [ ] Add a test that `AddValidationResponses` defaults to `true` (spec
  is augmented when the option is unspecified).

---

## 3. Body validator (`JsonBodyValidator`)

### 3.1 Operation + content-type matching

- [ ] Operation whose only `content` is `application/xml` (no JSON) — body
  is not validated; `BodyRequired` handling is bypassed because the
  middleware's `IsJsonRequest` returns false. Cover both validator-level
  (fed an empty Items dict) and end-to-end (XML body).
- [ ] Operation with both `application/json` and `application/xml` content —
  a JSON body is validated, an XML body is passed through.
- [ ] Operation with `application/json; charset=utf-8` media type key in the
  spec — the validator still finds the schema. (Currently the lookup is
  `TryGetProperty("application/json")` which is case-sensitive string
  match — a `charset=` suffix in the spec key would miss. Lock whether
  SpecGuard accepts parameterized keys.)
- [ ] Operation with `requestBody` but no `content` object — passed through.
- [ ] Operation with `content` but no `schema` — passed through.

### 3.2 Body shape

- [ ] Body that is a top-level array is validated against an array-typed
  schema.
- [ ] Body that is a top-level scalar (e.g. `"X"`) is validated against a
  string/integer schema. (The TicTacToeApi happy-path covers this, but no
  dedicated JsonBodyValidator unit test does.)
- [ ] Body that is `null` against `{ "type": ["object", "null"] }` is
  accepted; against `{ "type": "object" }` is rejected with a clear message.
- [ ] Nested object property path in error output uses JSON Pointer
  conventions for keys containing `/` (escaped as `~1`) and `~` (escaped as
  `~0`).

### 3.3 `readOnly` enforcement

- [ ] `readOnly` collected through `oneOf` branches — each branch's
  readOnly-marked properties are rejected when present in a request.
- [ ] `readOnly` collected through `anyOf` branches (same as above).
- [ ] `readOnly` nested inside a composition cycle — does not infinite-loop.
- [ ] `readOnly` on a property that is itself an array — the array as a
  whole is rejected, not individual items.
- [ ] `readOnly` on a `$ref`'d component that is reused in two properties —
  both locations are rejected when sent.
- [ ] All fields in a schema's `required` list are `readOnly` → the built
  schema has no `required` list (covered indirectly; add a direct test on
  `OpenApiSchemaBuilder` and on end-to-end validation behavior).
- [ ] `readOnly` property sent under a `patternProperties` key — document
  and test (collector does not scan `patternProperties`).

### 3.4 Required body edge cases

- [ ] `requestBody: { required: true }` with `content: {}` (no media types)
  — behavior?
- [ ] A request with JSON content-type and a body that parses but is `null`
  — is `null` treated as "body provided"? (`ParsedBodyKey` will be set to a
  `JsonElement` of kind `Null`.)
- [ ] Empty JSON array `[]` against a required-body operation whose schema
  expects an object — what error is produced?

### 3.5 Combined options

- [ ] `RejectAdditionalProperties = true` AND `AllowStringNumerics = true`
  together — unknown property still rejected, string numerics still
  coerced.
- [ ] `RejectAdditionalProperties = true` on a schema that declares
  `patternProperties` (no explicit `additionalProperties`) — verify whether
  the additional-props constraint is still injected.

### 3.6 Fallback error synthesis (`CollectErrors` / `LastEvaluationKeyword`)

- [ ] `oneOf` with multiple matches under the "List" output mode and no
  leaf-level error → the synthesized error message contains `oneOf` (or
  the keyword name) and a non-empty path.
- [ ] `not` that fails without a leaf error → synthesized error mentions
  `not`.
- [ ] `const` with an object mismatch — error message includes some
  indication of the mismatch (already partly tested at the success/failure
  boundary; lock the error format).
- [ ] Evaluation path with numeric-only segments (array index) falls through
  correctly to the preceding keyword.

### 3.7 `writeOnly` stripping

- [ ] `OpenApiSchemaBuilder` strips `writeOnly` from the produced schema
  (so JSON Schema doesn't attempt enforcement). Verify directly on the
  built node.
- [ ] A `writeOnly`-required property is still enforced at request time
  (already covered) — keep this pinned.

---

## 4. Parameter validator (`ParameterValidator`)

### 4.1 Path parameter edge cases

- [ ] URL-encoded path parameter value (`%20`, Cyrillic chars) is decoded
  before validation (`ExtractPathValues` calls `Uri.UnescapeDataString`).
- [ ] Path parameter with slashes encoded as `%2F` — lock the behavior.
- [ ] Path parameter whose value is empty (e.g. `/pets/`) — ASP.NET routing
  usually 404s; if SpecGuard sees it, what happens?
- [ ] Multiple path parameters in one segment template
  (`/{year}-{month}/{name}.{ext}`) — covered by matcher but not by
  parameter validator as a full round-trip.
- [ ] Path parameter with `style: "simple"` and `explode: true` — array
  extraction (currently delimiter is always `,`; `explode:true` in path
  changes the delimiter to between segments, which SpecGuard may not
  support — document).
- [ ] Path parameter with `style: "label"` or `"matrix"` — lock "unsupported"
  behavior so downstream changes don't silently activate them.

### 4.2 Query parameter edge cases

- [ ] Multiple values sent for a scalar-typed parameter (`?limit=1&limit=2`) —
  currently the code picks only the first via `rawValues[0]` in
  `Deserialize`; lock the behavior.
- [ ] Array parameter with `explode: false` (form-style) and **only one
  value** in the query (`?tags=dog`) — single-element array, not a
  comma-split of `d`, `o`, `g`.
- [ ] `style: "form"` + `explode: true` array with fewer items than
  `minItems` — fails validation.
- [ ] `style: "spaceDelimited"` with `explode: true` — behavior (spec says
  space-delimited generally implies explode:false).
- [ ] `style: "pipeDelimited"` with repeated values (`?tags=a&tags=b`) —
  lock precedence between repeated values and pipe splitting.
- [ ] `deepObject` with bracket keys that refer to unknown properties
  against a schema with `additionalProperties: false` — error should report.
- [ ] `deepObject` when only the bracket form is present for a required
  property — accepted.
- [ ] `deepObject` with malformed bracket syntax (`filter[status`) — ignored
  (not matched against the prefix), treated as absent.
- [ ] `deepObject` without any `explode` set — default is derived from
  `form`; verify that `deepObject` is handled regardless of `explode`.
- [ ] `deepObject` with an array-typed property (`filter[tags]=a,b`) — lock
  the behavior. Coercion currently goes through `CoercePrimitive`, so an
  array-typed property under deepObject is NOT split.

### 4.3 Header parameter edge cases

- [ ] Header name matching is case-insensitive (`X-Request-Id` vs
  `x-request-id`) — `HeaderDictionary` is case-insensitive; lock the
  behavior with an explicit test.
- [ ] Header with multiple values (`X-Foo: a, b` vs two `X-Foo` headers) —
  behavior for arrays.
- [ ] Header with empty value — treated as empty string, possibly mis-coerced.
- [ ] Reserved headers (`Content-Type`, `Authorization`) declared as OpenAPI
  parameters — are they looked up successfully?

### 4.4 Cookie parameter edge cases

- [ ] Cookie parameter with multiple cookies in a single `Cookie` header
  (`session=abc; other=def`) — only the named cookie is extracted.
- [ ] Cookie parameter with URL-encoded value — lock the decoding behavior.
- [ ] Missing `Cookie` header vs `Cookie` header present but named cookie
  absent — both should produce "missing required" when required.
- [ ] Cookie parameter with `style: "form"` and `explode: true` — serializes
  as separate cookies; current code only looks at one cookie by name so this
  is a limitation worth pinning.

### 4.5 Content-encoded parameters (`content: { application/json: ... }`)

- [ ] Content-encoded parameter whose content is not `application/json`
  (e.g. `application/xml`) — skipped or failed?
- [ ] Content-encoded parameter when a header/path uses this style — the
  code supports it in `in: query`, but path/header/cookie should behave
  the same way.
- [ ] Content-encoded parameter with both `schema` and `content` sections in
  the spec — OpenAPI forbids this; verify which one SpecGuard uses.
- [ ] Content-encoded parameter whose JSON decodes to an unexpected type
  (e.g. an array when schema expects an object).

### 4.6 Coercion (`CoercePrimitive`)

- [ ] Integer coercion with `+` sign (`"+5"`) — `NumberStyles.Integer` does
  NOT allow leading `+`; document the rejection.
- [ ] Integer coercion with leading zeros (`"0005"`) — allowed.
- [ ] Integer coercion with thousand separators (`"1,000"`) — rejected.
- [ ] Number coercion with scientific notation (`"1e5"`) — currently passes
  `NumberStyles.Float` which includes exponents — lock.
- [ ] Number coercion with comma as decimal separator (`"1,5"`) — currently
  rejected because `InvariantCulture` is used — lock.
- [ ] Boolean coercion beyond `true`/`false` (`"yes"`, `"1"`, `"0"`) —
  rejected, returns schema error.
- [ ] Coercion when schema type is `"null"` or an array-form
  `["integer", "null"]` — verify the non-null branch is picked.
- [ ] Coercion when no `type` is declared on the schema — `GetSchemaType`
  returns `null`; `CoercePrimitive` falls through to string.

### 4.7 Schema ref resolution for coercion

- [ ] `$ref`'d component schema that uses `allOf: [ { type: integer } ]` —
  `ResolveSchemaRef` only unwraps refs, not compositions, so the raw
  `type` key is missing. Lock this limitation.
- [ ] Cyclic `$ref` chain is detected and not followed forever
  (current code uses a 32-step depth limit without a `visited` set at the
  `ParameterValidator` level; `StringNumericCoercer` uses visited set —
  different behavior deserves a pinned test).

### 4.8 Cross-cutting

- [ ] Path-level parameters and operation-level parameters with the same
  `(name, in)` — operation-level wins (already tested for query; add
  cases for path, header, cookie).
- [ ] `$ref` to a non-existent `#/components/parameters/Name` — the parameter
  is silently dropped or errors out? Lock behavior.
- [ ] Parameter declared but with no `schema` **and** no `content` — skipped
  (not validated); confirm no crash.

---

## 5. Schema builder (`OpenApiSchemaBuilder`)

### 5.1 `readOnly` / required interaction

- [ ] When ALL properties in `required` are `readOnly`, the builder removes
  `required` entirely from the output.
- [ ] When only SOME `required` entries are `readOnly`, only those names
  are stripped; the rest remain enforceable.
- [ ] `required` array on a `$ref`'d component has its `readOnly` entries
  stripped inside `$defs`.
- [ ] Property that is `readOnly` AND not in `required` — published schema
  retains it but it's collected for request-body rejection (collector
  test).

### 5.2 Nullable composition collapse

- [ ] `oneOf` with 3 branches, one of which is `type: null` — **NOT**
  collapsed to `anyOf` (the current collapse is 2-branch-only).
- [ ] `oneOf` with 2 branches, neither of which is `type: null` — **NOT**
  collapsed.
- [ ] `oneOf` with `{ type: ["null", "string"] }` instead of
  `{ type: "null" }` — is this shape recognized as a "null branch"?
  (Current `IsNullType`-style check would only match a pure `"null"`
  string; lock.)
- [ ] `oneOf` inside a composition (e.g. inside `allOf`) still gets
  collapsed as expected.

### 5.3 Discriminator

- [ ] Implicit mapping is inferred from `anyOf` refs (current code supports
  this; add an explicit test mirroring the `oneOf` test).
- [ ] Explicit `mapping` with keys that do NOT correspond to a `$ref` in
  `oneOf`/`anyOf` — still produces an `if/then` branch pointing at the
  `mapping` target.
- [ ] Discriminator `mapping` entry pointing to an **external** `$ref`
  (`https://…` or non-`#/components/schemas/`) — the ref is used verbatim.
- [ ] Discriminator chain terminates with `else: false` so an unknown
  value produces a validation error (currently asserted indirectly via
  body validator; add a direct test on the built `JsonObject`).
- [ ] Discriminator without `mapping` and without any `oneOf`/`anyOf`
  branches — no chain is emitted; the schema validates as-if no
  discriminator were present.
- [ ] Discriminator chain preserves `allOf` entries that existed on the
  parent schema alongside the new chain element.

### 5.4 Format handling

- [ ] `format: "int64"` on an integer: `minimum`/`maximum` become
  `long.MinValue`/`long.MaxValue` as `long`-typed JSON numbers (not
  double, so large values round-trip losslessly).
- [ ] `format: "uint64"` emits `ulong` min/max — especially
  `ulong.MaxValue = 18446744073709551615` which overflows `long`.
- [ ] `format: "double"` emits `double.MinValue`/`double.MaxValue` and
  validation correctly rejects values outside that range.
- [ ] Unknown format (e.g. `"uuid"` as a numeric format — clearly
  inappropriate) is preserved on a string type and stripped on a numeric
  type.
- [ ] Non-numeric OpenAPI formats (`binary`, `byte`, `password`) stripped
  on strings (already covered) AND do not emit `minimum`/`maximum` on
  non-numeric types (current test covers this; keep).

### 5.5 `$ref` rewriting

- [ ] `$ref: "#/components/schemas/Pet"` becomes `$ref: "#/$defs/Pet"` —
  already covered.
- [ ] External `$ref: "https://example.com/schema.json"` is preserved
  verbatim (no rewriting, no defs entry).
- [ ] `$ref` pointing to `#/$defs/...` (already rewritten) is idempotent.
- [ ] `$ref` appearing alongside sibling keywords in a 2020-12 schema
  (e.g. `{"$ref": "...", "description": "..."}`) preserves siblings.

### 5.6 Defs building

- [ ] `$defs` is emitted even when no components schemas exist (empty
  object) — already covered.
- [ ] `components.schemas` name containing characters that need JSON
  Pointer escaping (unlikely but possible) — `#/$defs/<name>` is still
  produced as-is.
- [ ] Recursive / cyclic components are copied into `$defs` without
  infinite recursion (already indirectly covered by tree/mutually-recursive
  body tests).

### 5.7 Top-level schema shape

- [ ] Boolean schema `true` at the operation level — wrapped in
  `allOf: [true]` (already covered).
- [ ] Boolean schema `false` — same wrapping; nothing validates.
- [ ] Empty object schema `{}` — passes through as `$schema`/`$defs`
  augmented empty object.

---

## 6. Read-only collector (`ReadOnlyPropertyCollector`)

No dedicated test class exists. Add unit tests for:

- [ ] Collects a top-level `readOnly` property.
- [ ] Collects a nested `readOnly` property into a path like
  `/parent/child`.
- [ ] Collects through `allOf` branches.
- [ ] Collects through `oneOf` and `anyOf` branches.
- [ ] Collects through `$ref` to a component.
- [ ] Does not blow the stack on a cyclic `$ref` chain.
- [ ] Ignores `readOnly: false` explicitly set.
- [ ] Does NOT collect `readOnly` inside `patternProperties`,
  `additionalProperties`, `items`, or `prefixItems` (current implementation
  only descends into direct `properties` and composition keywords — lock
  this).
- [ ] Returns an empty set for a schema with no properties.

---

## 7. String-numeric coercer (`StringNumericCoercer`)

### 7.1 Schema traversal

- [ ] Coerces through `$ref` to a numeric component schema.
- [ ] Coerces through `allOf` composition (already handled in
  `WalkObject`).
- [ ] Does **NOT** attempt to coerce through `oneOf` branches (only
  `nullable` 2-branch `oneOf` unwrapping is special-cased) — lock this as
  a known limitation with a test.
- [ ] Coerces through `anyOf` nullable-like shapes (currently only handled
  when pattern exactly matches the 2-branch nullable `oneOf`; confirm
  `anyOf` variant behavior).
- [ ] Nested arrays of arrays of numeric strings (`[["1","2"],["3"]]`) are
  walked correctly.
- [ ] Object property named with a `/` or `~` — the JSON Pointer segment
  is escaped in the emitted error path.

### 7.2 Pattern-based out-of-range errors

- [ ] When `pattern` is absent and the value `LooksNumericForSchema` via
  the fallback `NumericShape` regex, an out-of-range message is emitted.
- [ ] When the schema-declared `pattern` is a throwaway (`.*`) that matches
  anything, an out-of-range error is still emitted for an overflowing
  number-shaped string.
- [ ] When the schema-declared `pattern` is malformed (invalid regex), the
  coercer falls back to `NumericShape` (current behavior uses
  `catch (ArgumentException)`).
- [ ] A string that looks numeric but the schema only permits `integer` —
  the emitted error message mentions `integer` (not `number`).
- [ ] A hex-like string `"0x1F"` — rejected (not recognized by
  `NumericShape`).
- [ ] An `Infinity` or `NaN` string — parsing yields `double.IsInfinity` /
  `IsNaN` and the error path is taken.

### 7.3 Successful coercion edges

- [ ] A value at exactly `long.MaxValue` is coerced to a `long` JSON number.
- [ ] A value at `long.MaxValue + 1` falls through to `ulong` coercion and
  succeeds when the schema permits the implicit range.
- [ ] A value at `ulong.MaxValue + 1` fails with out-of-range.
- [ ] A negative value that fits in `long` is coerced as `long`.
- [ ] A number with a trailing fractional zero (`"1.0"`) against
  `type: integer` — pattern for integer `^-?(?:0|[1-9]\d*)$` rejects it, so
  no synthetic error is produced (already covered; lock the exact behavior
  for patterns that DO allow decimals against integer types).

### 7.4 Final evaluation merges coercion + schema errors

- [ ] When coercion emits one error and schema evaluation also fails, both
  sets of errors appear in the final response (currently tested at a high
  level; add a case that asserts the concatenation order).
- [ ] Coercion errors have `in = "body"` and the correct JSON Pointer path.

---

## 8. Route matching (`RoutePatternMatcher`)

- [ ] Path containing URL-encoded characters (`%20`) matches its template.
- [ ] Path is matched case-sensitively (OpenAPI paths are case-sensitive
  per spec) — e.g. `/Pets` does NOT match `/pets`.
- [ ] Trailing slash on request path: `/pets/` vs `/pets` — lock the
  behavior (ASP.NET routing is usually tolerant; SpecGuard should be
  consistent).
- [ ] Catch-all template (`/files/{*path}`) — verify whether SpecGuard
  silently accepts it (via ASP.NET `TemplateMatcher`) and how
  `LiteralSegmentCount` treats it.
- [ ] Two templates with identical `LiteralSegmentCount` but different
  parameter positions (`/users/{id}/posts/all` vs `/users/all/posts/{id}`)
  — the tie-breaker is undefined per docs; pin current behavior to detect
  regressions.
- [ ] Empty path (`/`) — matcher behavior.

---

## 9. Validation result shape (`ValidationErrorResult`)

- [ ] JSON serialization of the `errors` extension uses the default
  `JsonOptions` naming policy (camelCase fields inside each error object).
- [ ] `errors` array serializes as a JSON array (not an object/keyed map).
- [ ] An error with `path = ""` (body-level error) serializes as an empty
  string, not omitted.
- [ ] An error whose message contains a double-quote or backslash survives
  JSON serialization (escape-safety).

---

## 10. Sanitizers / schema transformers

### 10.1 `EmailAddressSchemaTransformer`

No test file exists. Cover:

- [ ] A property of type `string` decorated with `[EmailAddress]` gets
  `format: "email"`.
- [ ] A property of type `string` without `[EmailAddress]` is untouched.
- [ ] A non-string type decorated with `[EmailAddress]` is untouched.
- [ ] Inherited `[EmailAddress]` on a property in a derived class applies
  (the transformer uses `inherit: true`).

### 10.2 `TimeSpanSchemaTransformer`

No test file exists. Cover:

- [ ] `TimeSpan` with `[Duration]` — schema becomes
  `{ type: "string", format: "duration" }`.
- [ ] `TimeSpan` without `[Duration]` — schema is untouched (retains ASP.NET
  Core's default string pattern).
- [ ] `TimeSpan` with `[Duration]` and ASP.NET's default pattern — the
  pattern is stripped only when it matches the exact default regex.
- [ ] `[Duration]` on a non-`TimeSpan` property — untouched.
- [ ] `[Duration]` is not inherited (transformer uses `inherit: false`).

### 10.3 `TimeSpanConverter`

- [ ] Reads a valid ISO 8601 duration (`"PT1H30M"`) into `TimeSpan`.
- [ ] Reads a negative ISO 8601 duration (`"-P1D"`) into a negative
  `TimeSpan`.
- [ ] Reads a legacy `"01:30:00"` string (`.NET` default format).
- [ ] Throws `JsonException` on malformed ISO 8601 (`"PT"` alone).
- [ ] Throws `JsonException` on `null` JSON value.
- [ ] Writes a `TimeSpan` as an ISO 8601 string.

### 10.4 `ValidationResponseTransformer`

Already well-covered. Add:

- [ ] When both parameters **and** a non-JSON body exist (e.g. `multipart`),
  only 422 is added; 400 is not.
- [ ] When `operation.Responses` already has `"4XX"` (a wildcard), the
  explicit `"400"` / `"422"` are still added (there is no current wildcard
  handling; lock).
- [ ] Operation with `$ref`'d request body — `HasJsonRequestBody` walks
  refs or not? (Current code inspects `operation.RequestBody.Content` — if
  `RequestBody` is a reference, `Content` may be null.)

### 10.5 `NumericSchemaTransformer`

Already well-covered. Add:

- [ ] A numeric schema with a **custom** `pattern` (not in
  `GeneratedPatterns`) is preserved.
- [ ] A numeric schema with `Pattern` but `AllowStringNumerics = true`
  preserves both the pattern and the `string` type alternative.
- [ ] A schema typed `integer | number | string` (triple union) — only
  `string` is stripped under the default; the integer+number union is
  kept.

### 10.6 `SbyteSchemaTransformer` / `HalfSchemaTransformer`

- [ ] Both transformers produce correctly when the property is `sbyte?` /
  `Half?` (nullable).

---

## 11. End-to-end coverage

**Ground rule.** The sample API projects (`SpecGuard.MuseumApi`,
`SpecGuard.TicTacToeApi`) are fixtures — tests must not modify them.
Any end-to-end scenario that cannot be reproduced against the existing
sample endpoints and models should be covered by either:

- **(a) Existing-endpoint tests.** Reuse the operations already exposed
  by MuseumApi / TicTacToeApi.
- **(b) Test-local minimal hosts.** Stand up a `WebApplication` inside
  the test project itself — typically with
  `WebApplication.CreateBuilder(...)` or `TestServer` — using
  test-project-owned model classes and endpoints. This keeps
  option/model coverage isolated from the shipped samples.
- **(c) Direct unit exercises.** Call the underlying validators or
  transformers against a hand-built `JsonDocument` / `OpenApiSchema`,
  which avoids needing any host at all.

Every bullet below is tagged with **(a)**, **(b)**, or **(c)** to make
the allowed scope explicit.

### 11.1 Numeric type range enforcement (test-local hosts or unit tests)

The shipped sample APIs expose only `decimal` (`price`) and `int?`
(`limit`, `page`). Coverage for the other CLR numeric types must come
from test-owned fixtures. For each CLR type below, assert that values
outside the format-derived range documented in USAGE.md §7.1 produce a
422 (end-to-end) or a validator-level error (unit):

- [ ] **(b / c)** `byte` — value `256` rejected (uint8 range).
- [ ] **(b / c)** `sbyte` — value `-129` rejected (int8 range).
- [ ] **(b / c)** `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
  `Half`, `float`, `double`, `decimal` — one boundary test per type per
  side.
- [ ] **(a)** MuseumApi `price` field (`decimal`) — spec publishes the
  double-derived range; send a value outside it and assert 422. This
  catches regressions without touching the sample.

### 11.2 String type / attribute behavior

- [ ] **(a)** `Guid` — `eventId` path parameter with `"not-a-uuid"` is
  already partly covered; add a body-level `Guid` case using the
  `BuyMuseumTicketsRequest.EventId` field (`Guid?`), which no current
  test exercises.
- [ ] **(a)** `DateOnly` — ticket `ticketDate` is partly covered; add a
  unit/validator test using a hand-built spec to pin the format check.
- [ ] **(b / c)** `TimeOnly` — malformed time rejected.
- [ ] **(b / c)** `DateTime` / `DateTimeOffset` — malformed values
  rejected.
- [ ] **(b / c)** `Uri` — malformed URI rejected.
- [ ] **(b / c)** `byte[]` — non-base64 string rejected.
- [ ] **(b / c)** `TimeSpan` (default) — malformed time span rejected.
- [ ] **(b / c)** `TimeSpan` with `[Duration]` — malformed ISO 8601
  rejected, `"PT1H30M"` accepted, handler receives the parsed
  `TimeSpan`.
- [ ] **(a)** `[EmailAddress]` on `BuyMuseumTicketsRequest.Email` — an
  invalid address is already partly covered; add a case that asserts
  the published spec declares `format: "email"` on the field.

### 11.3 Published-spec integration

Run the sample APIs via `WebApplicationFactory` and fetch
`/openapi/v1.json`, then assert:

- [ ] **(a)** `400` / `422` responses present on operations with JSON
  bodies or parameters (MuseumApi `POST /special-events`,
  `PATCH /special-events/{id}`, `GET /museum-hours`), and absent on
  operations with neither.
- [ ] **(a)** The 422 response declares `application/problem+json` with
  a schema exposing `title`, `status`, `detail`, `type`, `errors`.
- [ ] **(b)** Hand-authored `400` / `422` in a spec are not
  overwritten — use a test-local host that defines its own operation
  responses, since the shipped samples don't hand-author these.
- [ ] **(a)** Numeric property `price` (`decimal`) carries the
  documented `double`-range `minimum`/`maximum`.
- [ ] **(a)** `eventId` (`Guid`) appears as
  `{ type: "string", format: "uuid" }`.
- [ ] **(a)** `email` on `BuyMuseumTicketsRequest` appears as
  `{ type: "string", format: "email" }`.
- [ ] **(b)** `[Duration] TimeSpan` appears as
  `{ type: "string", format: "duration" }` with no default `.NET`
  pattern — requires a test-local model with `[Duration]`.
- [ ] **(a)** `eventId` on `BuyMuseumTicketsRequest` is nullable
  (`Guid?`) — verify the published spec rewrites the generated nullable
  `oneOf` to `anyOf` per the collapse rule.
- [ ] **(b)** A numeric property with `AllowStringNumerics = true`
  retains `type: ["integer","string"]` AND a number-shape `pattern`.
- [ ] **(a)** Existing numeric properties with the option OFF (default)
  drop the `string` alternative and strip the generated pattern.

### 11.4 End-to-end options

These require spinning up a host with non-default options. The shipped
samples register defaults only, so use **(b)** test-local hosts:

- [ ] **(b)** `RejectAdditionalProperties = true`: a body with an
  unknown field returns 422.
- [ ] **(b)** `AllowStringNumerics = true`: a string-typed numeric
  (`"10"`) is accepted AND the handler sees a parsed numeric value.
- [ ] **(b)** `AddValidationResponses = false`: the published spec does
  **not** contain 400 / 422 additions.

### 11.5 TicTacToeApi coverage (use existing endpoints only)

- [ ] **(a)** `GET /board/{row}/{column}` with non-numeric column
  (`/1/abc`) is rejected — only the row variant is covered today.
- [ ] **(a)** `PUT /board/{row}/{column}` with invalid column (`/1/5`)
  returns 422 referencing `column`.
- [ ] **(a)** `GET /board` response `winner` field — assert its value is
  a member of the published enum (don't hard-code `X`/`O`/`.`; read the
  enum from the served spec).
- [ ] **(a)** `PUT /board/{row}/{column}` with valid mark `"X"` and
  valid coordinates returns 200 and a schema-valid body.

### 11.6 Middleware registration order (integration, **(b)** only)

These scenarios all require a host whose pipeline is configured
specifically for the test. Do **not** modify MuseumApi/TicTacToeApi's
`Program.cs` — stand up a minimal host inside the test project.

- [ ] **(b)** `UseAuthentication`/`UseAuthorization` before
  `UseSpecGuard` — anonymous call to a 401-required endpoint does NOT
  hit SpecGuard (no 422 for a missing body).
- [ ] **(b)** Auth after SpecGuard — anonymous callers with malformed
  bodies receive 422, not 401.
- [ ] **(b)** `UseExceptionHandler` after `UseSpecGuard` does NOT
  rewrite 400 / 422 responses.
- [ ] **(b)** Path-rewrite middleware placed after `UseSpecGuard` does
  NOT affect matching (SpecGuard already ran against the original path).
- [ ] **(b)** Body-reading middleware placed BEFORE `UseSpecGuard`
  without `EnableBuffering` leaves SpecGuard with an empty body and a
  `required: true` schema therefore 422s.

### 11.7 Malformed JSON edge cases at the API boundary

- [ ] **(a)** `application/problem+json` body sent to a JSON operation
  (e.g. `POST /special-events`) — treated as JSON via the `+json` rule
  and parsed/validated.
- [ ] **(a)** `application/json; charset=utf-16` on an existing
  MuseumApi endpoint — document the behavior.
- [ ] **(a)** `POST /special-events` with empty body and
  `Content-Type: application/json` returns 422 with the body-required
  error.
- [ ] **(a)** `POST /special-events` with empty body and NO
  `Content-Type` returns 422.

---

## 12. Contract / documentation alignment

These are not so much "missing tests" as checks that force the docs and
code to stay aligned. They read from running tests or a snapshot of the
shipped spec.

- [ ] USAGE.md §5 claims `AddValidationResponses` with default `true`.
  Add a test that asserts
  `typeof(SpecGuardOptions).GetProperty("AddValidationResponses")` exists
  and its default on a fresh instance is `true` — this will catch
  accidental renames or default flips.
- [ ] USAGE.md §7.1 numeric ranges table — add a data-driven test that
  enumerates each CLR numeric type, generates a spec via
  `AddOpenApi()`/`AddSpecGuard()`, and asserts the `minimum`/`maximum`
  match the documented numbers exactly.
- [ ] USAGE.md §7.2 string types / attributes — generate a spec from a
  small model class covering each type and assert the documented
  `{ type, format }` shape.
- [ ] USAGE.md §9.2 parameter serialization styles table — add one happy-
  path parameter test per row.
- [ ] USAGE.md §9.3 ignored formats / keywords — add assertions that
  `binary`, `byte`, `password` on a string do NOT reject non-conforming
  values and that `example`, `xml`, `externalDocs`, `deprecated`,
  `writeOnly` do NOT appear in the compiled JSON Schema.
- [ ] USAGE.md §8.2 error-shape example — assert that the real 422
  response matches the documented field names and key order.

---

## Suggested priority

1. **§2.5 / §12** — the option name mismatch between USAGE.md and
   `SpecGuardOptions`. This is a user-visible contract bug; a test that
   locks the name would surface it.
2. **§1.2** — request-body stream rewind. A silently empty downstream body
   is the nastiest possible failure mode.
3. **§10.1–§10.3** — zero test coverage on three schema transformers and
   the `TimeSpan` converter means any refactor can break them invisibly.
4. **§11.3** — published-spec integration tests. These lock the spec
   transformations that are SpecGuard's other core responsibility.
5. **§6** — `ReadOnlyPropertyCollector` has no dedicated tests and is
   exercised only transitively.
6. **§4** — parameter validator edge cases around styles, encodings, and
   case sensitivity are thin. These are the second most complex piece of
   validator code.
7. **§11.6** — middleware-order caveats in USAGE.md §6 have no integration
   coverage.
