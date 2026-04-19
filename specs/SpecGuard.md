# SpecGuard — Implementation Spec

## Goal

A .NET class library that validates incoming ASP.NET API request bodies by extracting JSON schemas from an OpenAPI 3.1 spec and validating request payloads against them.

## Core responsibilities

1. **Schema extraction** — Accept an OpenAPI 3.1 document (parsed via `Microsoft.OpenApi`), walk every operation that defines a `requestBody` with a `application/json` media type, and extract or resolve its JSON schema.
2. **Request matching** — Given an incoming HTTP request (method + path), match it to the correct OpenAPI operation, accounting for path templates (e.g. `/pets/{petId}`).
3. **Validation** — Validate the request's JSON body against the matched schema. Return a structured result containing any validation errors.
4. **ASP.NET integration** — Expose middleware (and/or an action filter) that plugs into the ASP.NET pipeline, performs steps 2–3 automatically, and short-circuits with `400 Bad Request` + error details when validation fails.

## Public API surface

### Registration

An `IServiceCollection` extension (e.g. `AddSpecGuard()`) and an `IApplicationBuilder` extension (e.g. `UseSpecGuard()`) that register all required services and middleware.

### Key abstractions

- A service that takes an OpenAPI document and produces a lookup of operation → JSON schema.
- A validation service that takes a JSON body and a schema and returns a validation result.
- A validation result type containing success/failure and a list of errors (each with a path/pointer and message).

Keep the public surface small. Internal implementation details should not leak.

## Dependencies

- `Microsoft.OpenApi` — for parsing and navigating OpenAPI documents
- A JSON schema validation library (e.g. `JsonSchema.Net` or similar) — for the actual validation step
- `Microsoft.AspNetCore.Http` — for middleware integration

## Constraints

- Target the same .NET version as the solution
- No external HTTP calls at runtime
- Stateless after initial schema extraction — schemas are built once at startup and reused
- Thread-safe

## What NOT to do

- No OpenAPI spec generation (that's the host app's job)
- No response validation
- No authentication or authorization concerns
- No logging framework opinions — use `ILogger<T>` if logging is needed