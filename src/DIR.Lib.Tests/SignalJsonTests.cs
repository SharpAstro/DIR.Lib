using System.Text.Json;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="SignalJson"/> binds an inspector payload onto a signal. A key that binds nothing used to leave
/// its parameter at the default with no error: TianWen's pin signal takes a parameter named <c>RA</c>, the
/// generator emitted its key as <c>rA</c>, and a payload spelling it <c>ra</c> pinned the target at RA 0.
/// </summary>
public class SignalJsonTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("ra")]
    [InlineData("RA")]
    [InlineData("rA")]
    [InlineData("Ra")]
    public void AKeyBindsItsParameterInAnyCase(string key)
    {
        SignalJson.Double(Payload($$"""{"{{key}}": 22.4937}"""), "ra", 0d).ShouldBe(22.4937);
    }

    [Fact]
    public void AKeyThatNamesNoParameterIsRefusedWithTheKeysTheSignalTakes()
    {
        var refused = Should.Throw<ArgumentException>(() =>
            SignalJson.RequireKnownKeys(Payload("""{"ra": 22.49, "decl": -20.8}"""), "Pin", "name", "ra", "dec"));

        refused.Message.ShouldContain("'decl'", Case.Sensitive);
        refused.Message.ShouldContain("name, ra, dec", Case.Sensitive, "the refusal says what the call should have been");
    }

    [Fact]
    public void AKnownKeyPassesInAnyCase()
    {
        Should.NotThrow(() => SignalJson.RequireKnownKeys(Payload("""{"RA": 22.49, "Dec": -20.8}"""), "Pin", "name", "ra", "dec"));
    }

    [Fact]
    public void NoPayloadIsEveryParameterAtItsDefault()
    {
        Should.NotThrow(() => SignalJson.RequireKnownKeys(default, "Pin", "ra"));
        Should.NotThrow(() => SignalJson.RequireKnownKeys(Payload("null"), "Pin", "ra"));
        Should.NotThrow(() => SignalJson.RequireKnownKeys(Payload("{}"), "Pin", "ra"));
    }

    [Fact]
    public void ASignalThatTakesNothingRefusesAnyKey()
    {
        Should.Throw<ArgumentException>(() => SignalJson.RequireKnownKeys(Payload("""{"now": true}"""), "Refresh"))
            .Message.ShouldContain("takes no arguments", Case.Sensitive);
    }

    [Fact]
    public void APayloadThatIsNotAnObjectIsRefused()
    {
        Should.Throw<ArgumentException>(() => SignalJson.RequireKnownKeys(Payload("[22.49, -20.8]"), "Pin", "ra", "dec"));
    }
}
