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
}
