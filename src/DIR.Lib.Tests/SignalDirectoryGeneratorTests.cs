using DIR.Lib.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// The inspector's signal directory, as the generator emits it for a DEBUG build: which key each parameter is
/// read from, and the refusal of a key that names none. A parameter named <c>RA</c> used to be read from
/// <c>rA</c>, so the <c>ra</c> anyone would type bound nothing and the signal went out with RA 0.
/// </summary>
public class SignalDirectoryGeneratorTests
{
    private const string Signals = """
        namespace Probe;

        public readonly record struct PinSignal(string Name, double RA, double Dec, int OtaIndex = 0);

        public readonly record struct RefreshSignal();
        """;

    // Runs the generator the way a DEBUG build does, over the real DIR.Lib, and returns what it emitted with
    // the compilation it produced (the input plus the emitted directory).
    private static (string Source, Compilation Output) Generate(string signals)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: ["DEBUG"]);
        var platform = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("no trusted platform assemblies to compile the probe against");
        var references = platform.Split(Path.PathSeparator)
            .Append(typeof(SignalBus).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Probe",
            [CSharpSyntaxTree.ParseText(signals, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create([new SignalDirectoryGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        diagnostics.ShouldBeEmpty();

        var directory = output.SyntaxTrees.Single(t => t.FilePath.EndsWith("SignalDirectory.g.cs", StringComparison.Ordinal));
        return (directory.ToString(), output);
    }

    [Fact]
    public void AParameterIsReadFromTheKeyAPayloadWouldSpellIt()
    {
        var (source, _) = Generate(Signals);

        // Case-sensitive on purpose: Shouldly's string ShouldContain ignores case by default, which let "rA" pass.
        source.ShouldContain("SignalJson.Double(el, \"ra\", default)", Case.Sensitive);
        source.ShouldContain("SignalJson.Int(el, \"otaIndex\", 0)", Case.Sensitive);
        source.ShouldContain("SignalJson.StringNonNull(el, \"name\", \"\")", Case.Sensitive);
    }

    [Fact]
    public void EveryFactoryRefusesAKeyItsSignalDoesNotTake()
    {
        var (source, _) = Generate(Signals);

        source.ShouldContain("SignalJson.RequireKnownKeys(el, \"Pin\", \"name\", \"ra\", \"dec\", \"otaIndex\");", Case.Sensitive);
        source.ShouldContain("SignalJson.RequireKnownKeys(el, \"Refresh\");", Case.Sensitive);
    }

    [Fact]
    public void TheDirectoryCompilesAgainstSignalJsonAsItIs()
    {
        var (_, output) = Generate(Signals);

        using var image = new MemoryStream();
        var emitted = output.Emit(image, cancellationToken: TestContext.Current.CancellationToken);

        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        emitted.Success.ShouldBeTrue();
    }
}
