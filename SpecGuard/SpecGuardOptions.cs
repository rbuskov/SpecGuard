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
}
