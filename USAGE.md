# SpecGuard Usage Guide

## 1. Introduction

SpecGuard is ASP.NET Core middleware that validates incoming HTTP requests
against the OpenAPI document your API publishes at runtime. For requests whose
method and path match an operation in the spec, it can:

- Reject malformed JSON with a `400 application/problem+json` response.
- Reject schema violations with a `422 application/problem+json` response that
  lists every error found across the body, path, query, header, and cookie
  parameters.
- Pass the request through unchanged when it is valid.

Requests whose method and path do not match any operation in the spec are
passed through untouched — SpecGuard does not inspect them.

It also adjusts the published OpenAPI document in a handful of targeted ways so
that the spec better describes what the API will actually accept.

For the motivation behind the library, see [README.md](./README.md).

## 2. Installation

Until the first NuGet release, add a project reference to the `SpecGuard`
project from source:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/SpecGuard/SpecGuard.csproj" />
</ItemGroup>
```

SpecGuard targets ASP.NET Core's built-in OpenAPI stack
(`Microsoft.AspNetCore.OpenApi`). Your host project must call `AddOpenApi()`
and `MapOpenApi()` so that a spec document is exposed at runtime for SpecGuard
to consume.

## 3. Quick start

The smallest working setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSpecGuard();

var app = builder.Build();

app.UseSpecGuard();
app.MapEndpoints();
app.MapOpenApi();

app.Run();
```

With this configuration:

- Every request to a route described in the published spec is validated.
- Requests to the spec URL itself (`/openapi/v1.json` by default) are passed
  through without interference.
- The published spec documents the `400` and `422` responses SpecGuard may
  return.

## 4. Registration API

```csharp
services.AddSpecGuard();                 // Defaults
services.AddSpecGuard(o => { … });       // With options

app.UseSpecGuard();                      // Default spec URL: /openapi/v1.json
app.UseSpecGuard("/custom/spec.json");   // Relative path
app.UseSpecGuard("https://example/spec"); // Absolute URL
```

## 5. Options reference

`SpecGuardOptions` has two properties:

| Option | Default | Effect |
|---|---|---|
| `RejectAdditionalProperties` | `false` | When `true`, object schemas in the spec that declare `properties` but omit `additionalProperties` behave as if `additionalProperties: false` were set. Unknown fields in request bodies are rejected. |
| `AddValidationResponses` | `true` | When `true`, SpecGuard augments the published OpenAPI document with the `400` and `422` responses it can produce. Hand-authored entries at these statuses are never overwritten. |

Example:

```csharp
services.AddSpecGuard(o =>
{
    o.RejectAdditionalProperties = true;
    o.AddValidationResponses = false;
});
```

## 6. Middleware registration order — caveats

SpecGuard is a middleware, so where you place `UseSpecGuard()` in the pipeline
matters.

- **Register before endpoint handlers you want guarded.** Anything mapped after
  `UseSpecGuard()` is protected; anything mapped before it is not.
- **Register after middleware that rewrites the request path.** SpecGuard
  matches `HttpRequest.Path` against OpenAPI path templates. If a rewrite runs
  after SpecGuard, matching will be inconsistent with the final route.
- **Register before `UseExceptionHandler` / custom error middleware.** If
  SpecGuard's `400` or `422` response flows through an exception handler, the
  handler may replace it with a generic error page.
- **Authentication / authorization.** Put auth middleware *before* SpecGuard
  to short-circuit unauthorized callers before validation runs. Put it
  *after* if you want validation errors returned for anonymous callers too.
- **Body-reading middleware ahead of SpecGuard must enable request buffering.**
  SpecGuard enables buffering itself, but only from the point it runs. A
  middleware earlier in the pipeline that reads the body without buffering
  leaves SpecGuard with an empty stream.
- **Requests to the spec URL are passed through.** When `UseSpecGuard()` is
  configured with a relative URL (the default), requests whose path matches
  that URL skip validation, so `MapOpenApi()` continues to work regardless of
  its position in the pipeline.

## 7. Attributes and type hints

Applying these to your models changes how properties are published in the spec
(and, by extension, how SpecGuard validates them):

| Attribute / type | Published as |
|---|---|
| `[OpenApiDuration]` on `TimeSpan` | `{ "type": "string", "format": "duration" }`, serialized as ISO 8601 (e.g. `"PT1H30M"`) |
| `[EmailAddress]` on `string` | `format: "email"` |
| `sbyte` | `format: "int8"` |
| `Half` | `format: "float16"` |
| All other numeric CLR types (`int`, `long`, `float`, `double`, …) | Matching numeric format, with `minimum` / `maximum` derived from the CLR range |

## 8. HTTP behavior

### 8.1 What SpecGuard inspects

- **Method + path.** The request method and path are matched against the
  `paths` section of the published spec. When multiple templates could match,
  the one with more literal (non-parameter) segments wins. **Requests that
  match no operation in the spec pass through without any inspection** —
  including without body parsing, so a malformed JSON body on an unknown URL
  does not produce a 400.
- **Content type.** Bodies sent with `application/json` or any `*/*+json`
  media type are parsed and validated. Other content types pass through
  without body validation.
- **Parameters.** Path, query, header, and cookie parameters declared on the
  matched operation are validated. Path-level and operation-level parameter
  lists are merged; operation-level entries override path-level entries with
  the same `(name, in)`.

### 8.2 What SpecGuard returns

**`400 application/problem+json`** — the request matches an operation in the
spec, the body is JSON content-type, but the body is not valid JSON:

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
  "title": "Malformed JSON",
  "detail": "The JSON value could not be converted to … Path: $ | LineNumber: 3 | BytePositionInLine: 12.",
  "status": 400
}
```

**`422 application/problem+json`** — one or more validation errors:

```json
{
  "type": "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21",
  "title": "Validation Failed",
  "detail": "One or more validation errors occurred",
  "status": 422,
  "errors": [
    {
      "message": "Required property 'title' not found",
      "in": "body",
      "path": ""
    },
    {
      "message": "Missing required query parameter 'page'",
      "in": "query",
      "path": "page"
    }
  ]
}
```

Each `errors` entry has three fields:

- `message` — human-readable description of the problem.
- `in` — where the problem was found: one of `body`, `path`, `query`,
  `header`, `cookie`.
- `path` — a JSON Pointer into the request body for body errors, or the
  parameter name for parameter errors.

**Pass-through** — when nothing fails, the next middleware runs. The request
body stream is buffered and rewound so downstream handlers see the original
bytes.

### 8.3 Error aggregation

For every request that matches an operation in the spec, all validations run
and every error is returned in a single response. Clients never have to fix
one problem, resubmit, and discover the next one.

## 9. Validation rules

### 9.1 Request body

- The body is validated against
  `requestBody.content['application/json'].schema` using JSON Schema
  draft 2020-12 semantics.
- When `requestBody.required: true` but the body is missing or empty, the
  request is rejected with:

  ```json
  { "message": "A request body is required but none was provided.", "in": "body", "path": "" }
  ```

- Properties marked `readOnly: true` in the request schema must not appear in
  request bodies. Each occurrence produces an error:

  ```json
  { "message": "Property 'id' is read-only and must not be included in request bodies", "in": "body", "path": "/id" }
  ```

- `readOnly` properties are not enforced as required even when listed in the
  schema's `required` array — they are server-assigned, so clients need not
  send them.
- When `RejectAdditionalProperties = true`, object schemas without an explicit
  `additionalProperties` reject unknown fields.

### 9.2 Parameters

- **Required parameters.** Missing required `query`, `header`, or `cookie`
  parameters produce a `Missing required <in> parameter '<name>'` error.
  Required `path` parameters are not checked; the routing layer returns a
  `404` before SpecGuard runs.
- **Empty query values.** A query parameter whose value is an empty string
  is rejected unless the parameter declares `allowEmptyValue: true`.
- **Type coercion.** Raw string values are coerced to the declared primitive
  type (`integer`, `number`, `boolean`) before being validated against the
  schema. Values that fail to coerce are passed through as strings, which
  then fail schema validation with a descriptive message.
- **Serialization styles.** The following OpenAPI `style` values are handled:

  | Style | Applies to | Behavior |
  |---|---|---|
  | `simple` | path, header (default) | Comma-separated array |
  | `form` | query, cookie (default) | Repeated key, or comma-separated when `explode: false` |
  | `spaceDelimited` | query | Space-separated array |
  | `pipeDelimited` | query | Pipe-separated array |
  | `deepObject` | query | Bracket notation: `filter[status]=active&filter[page]=2` |
  | `form` + `explode: false` (object) | query | Comma-separated `key,value,key,value` pairs |

- **Content-encoded parameters.** A parameter declared as
  `content: { application/json: { schema: … } }` has its value parsed as a
  JSON document and validated against that schema. A value that is not valid
  JSON produces `Parameter '<name>' has invalid JSON content`.
- After extraction and coercion, each parameter value is validated against
  its schema using the same JSON Schema 2020-12 semantics as the body.

### 9.3 Keyword support highlights

- **`discriminator`.** Polymorphic schemas that use `discriminator` with an
  explicit or implicit `mapping` are dispatched by the discriminator
  property's value. Values whose discriminator does not match any mapping
  entry are rejected. Implicit mappings are inferred from the last segment
  of each branch's `$ref` when `mapping` is absent.
- **Nullable composition.** Schemas of the form
  `oneOf: [ { "type": "null" }, { "$ref": "…" } ]` are accepted even when
  the referenced schema's enum already permits `null` — SpecGuard does not
  treat this as an ambiguous match.
- **Numeric `format` ranges.** The following formats map to `minimum` /
  `maximum` checks: `int8`, `uint8`, `int16`, `uint16`, `int32`, `uint32`,
  `int64`, `uint64`, `float16`, `float`, `double`.
- **Ignored formats.** `binary`, `byte`, and `password` are not enforced —
  strings carrying these formats are validated only against other keywords
  (`minLength`, `pattern`, etc.).
- **Ignored OpenAPI-only keywords.** `example`, `xml`, `externalDocs`,
  `deprecated`, and `writeOnly` never cause validation failures.

## 10. Transformations applied to your published OpenAPI document

When SpecGuard is registered, the document emitted at `/openapi/v1.json`
differs from stock ASP.NET Core output in these observable ways:

- **Numeric schemas.** Auto-generated regex `pattern` values on numeric
  schemas are removed, and numeric schemas no longer include `string` in their
  `type` union.
- **`sbyte`.** Published with `format: "int8"`.
- **`Half`.** Published with `format: "float16"`.
- **`[OpenApiDuration]` on `TimeSpan`.** Published as
  `{ "type": "string", "format": "duration" }`. The auto-generated pattern
  for `TimeSpan` (e.g. `^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$`) is
  removed.
- **`[EmailAddress]` on `string`.** Published with `format: "email"`.
- **Validation responses (when `AddValidationResponses = true`).**
  - Every operation with a JSON request body gains a `400` response unless
    one is already defined.
  - Every operation with a JSON request body or any parameters gains a `422`
    response unless one is already defined.
  - Both responses use `application/problem+json` and document the exact
    response shape shown in §8.2.
  - Any hand-authored `400` / `422` entries are left untouched.

## 11. Caveats and limitations

- **OpenAPI 3.1 only.** OpenAPI 3.0 is on the roadmap.
- **Validation is only as correct as your spec.** SpecGuard enforces exactly
  what your published document describes. If the spec is inaccurate, the
  validation will be inaccurate too.
- **First-request cost.** The first request after startup triggers the spec
  fetch and schema compilation. Subsequent requests use the cached result.
- **JSON bodies only.** Only `application/json` and `*/*+json` bodies are
  validated; other media types (form, multipart, XML, binary) pass through
  without body validation.
- **`JsonOptions` defaults.** `AddSpecGuard()` sets two defaults on
  `JsonOptions` if you have not configured them yourself: `PropertyNamingPolicy`
  is set to `CamelCase`, and a `JsonStringEnumConverter` is added. Configure
  `JsonOptions` yourself to override either.
- **Path template disambiguation.** When multiple OpenAPI path templates
  could match a request, SpecGuard picks the one with the most literal
  segments. Overlapping templates with the same literal-segment count may
  match in an undefined order.
- **`readOnly` is enforced strictly.** A client that echoes a server-assigned
  field back into a request is rejected. Strip server-owned fields on the
  client before sending.

## 12. Troubleshooting

- **"The spec isn't loading."** SpecGuard fetches the spec from the URL you
  passed to `UseSpecGuard(...)`. For a relative path it resolves against the
  current request's scheme + host. If the host cannot reach itself over HTTP
  (e.g. container networking, TLS misconfiguration), the fetch fails. Test
  the URL from inside the running process.
- **"My spec URL is being validated instead of served."** The path passed to
  `UseSpecGuard(...)` must match the path `MapOpenApi(...)` exposes the
  document at. If they differ, SpecGuard will try to validate requests for
  the spec endpoint. Align the two, or pass the spec URL explicitly.
- **"No `400` / `422` entries appear in my spec."** Check that
  `AddValidationResponses` is `true` (its default) and that the operation
  has either a JSON request body or at least one parameter; operations with
  neither are left untouched.
- **"My handler reads an empty body."** Another middleware earlier in the
  pipeline likely consumed the request stream without enabling buffering.
  Enable buffering in that middleware, or move `UseSpecGuard()` earlier so
  SpecGuard's own buffering takes effect first.
