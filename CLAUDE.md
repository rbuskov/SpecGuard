# SpecGuard

## Keep implementation hidden

Don't bleed out details bout JSON schema validation or other internals. Types 
and members should be declared internal where possible. Make exposed types sealed
where possible.

## Test-first Red Green Refactor

When developing new features or fixing bugs in SpecGuard, follow the test-first 
approach. Start by writing a failing test that captures the desired behavior or 
the bug you want to fix. Then, implement the necessary code to make the test pass. 
Finally, refactor the code as needed to improve readability and maintainability 
while ensuring that all tests still pass.

## Documentation style

When writing user-facing documentation for SpecGuard, focus on observable
behavior, inputs, and outputs — including validation rules, validation logic,
and API spec transformations that the user can see in the published OpenAPI
document or in HTTP responses. Do not describe internal classes, internal
method names, or implementation details; they are not part of the public
contract.

## Keep documentation in USAGE.md up-to-date

Ensure that any changes to the library's behavior or features are reflected 
accurately in the USAGE.md file. This includes updates to usage patterns 
and any new features or breaking changes.