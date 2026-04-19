namespace SpecGuard;

public sealed class SpecGuardOptions
{
    /// <summary>
    /// When <c>true</c>, object schemas that declare <c>properties</c> but omit
    /// <c>additionalProperties</c> will be treated as if
    /// <c>additionalProperties: false</c> were set, causing unknown fields to be
    /// rejected. Default is <c>false</c> (follow JSON Schema 2020-12, which
    /// allows additional properties).
    /// </summary>
    public bool RejectAdditionalProperties { get; set; }

    /// <summary>
    /// When <c>false</c> (default), SpecGuard augments each operation's
    /// <c>responses</c> with the HTTP statuses SpecGuard can produce at runtime
    /// (<c>400</c> for malformed JSON bodies, <c>422</c> for validation
    /// failures) — but only on operations that will actually be validated.
    /// Existing <c>400</c> or <c>422</c> entries are never overwritten.
    /// Set to <c>true</c> to suppress this augmentation.
    /// </summary>
    public bool SkipValidationResponses { get; set; }

    /// <summary>
    /// When <c>true</c>, numeric JSON body fields may also be sent as strings
    /// (e.g. <c>"42"</c> for an integer property). The published spec keeps
    /// <c>string</c> in the numeric <c>type</c> union and retains the
    /// auto-generated number-shape regex <c>pattern</c>. Incoming string
    /// values are validated against the pattern, coerced to their numeric
    /// value, and then checked against <c>minimum</c>, <c>maximum</c>,
    /// <c>multipleOf</c>, and <c>format</c> range constraints.
    ///
    /// When <c>false</c> (default), numeric schemas are published as pure
    /// numeric types, so bodies carrying string-encoded numbers are rejected.
    /// </summary>
    public bool AllowStringNumerics { get; set; }
}
