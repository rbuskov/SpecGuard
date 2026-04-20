namespace SpecGuard.Test;

public class SpecGuardOptionsTests
{
    [Fact]
    public void RejectAdditionalProperties_defaults_to_false()
    {
        Assert.False(new SpecGuardOptions().RejectAdditionalProperties);
    }

    [Fact]
    public void AddValidationResponses_defaults_to_true()
    {
        Assert.True(new SpecGuardOptions().AddValidationResponses);
    }

    [Fact]
    public void AllowStringNumerics_defaults_to_false()
    {
        Assert.False(new SpecGuardOptions().AllowStringNumerics);
    }

    [Fact]
    public void Exposes_exactly_the_documented_properties()
    {
        var expected = new[]
        {
            nameof(SpecGuardOptions.RejectAdditionalProperties),
            nameof(SpecGuardOptions.AddValidationResponses),
            nameof(SpecGuardOptions.AllowStringNumerics),
        };

        var actual = typeof(SpecGuardOptions)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n).ToArray(), actual);
    }
}
