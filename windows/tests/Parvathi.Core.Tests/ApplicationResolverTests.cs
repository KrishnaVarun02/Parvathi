using Parvathi.Core;
using Xunit;

namespace Parvathi.Core.Tests;

public sealed class ApplicationResolverTests
{
    private static readonly AppDescriptor Chrome = new("chrome", "Google Chrome", @"C:\Apps\Chrome.exe");
    private static readonly AppDescriptor Code = new("vscode", "Visual Studio Code", @"C:\Apps\Code.exe");

    [Theory]
    [InlineData("code")]
    [InlineData("VS Code")]
    [InlineData("visual studio code.exe")]
    public void ResolvesAliasesAndNormalizedNames(string query)
        => Assert.Equal(Code, ApplicationResolver.Resolve(query, [Chrome, Code]));

    [Fact]
    public void ExactMatchWinsOverPartial()
        => Assert.Equal(Chrome, ApplicationResolver.Resolve("Google Chrome", [Chrome, new("beta", "Google Chrome Beta", "beta.exe")]));

    [Fact]
    public void DistinctInstallationsRequireClarification()
    {
        var error = Assert.Throws<PipelineException>(() => ApplicationResolver.Resolve("Chrome", [
            new("a", "Google Chrome", "one.exe"), new("b", "Google Chrome", "two.exe")]));
        Assert.Contains("Which application", error.Message);
    }

    [Fact]
    public void DuplicateCatalogEntriesDoNotCreateFalseAmbiguity()
        => Assert.Equal(Chrome, ApplicationResolver.Resolve("Chrome", [Chrome, Chrome with { Id = "duplicate" }]));

    [Fact]
    public void MissingAppHasUsefulError()
        => Assert.Contains("install", Assert.Throws<PipelineException>(() => ApplicationResolver.Resolve("Firefox", [Chrome])).Message);

    [Fact]
    public void PrefixAmbiguityDoesNotGuess()
        => Assert.Throws<PipelineException>(() => ApplicationResolver.Resolve("Visual", [Code, new("vs", "Visual Studio 2026", "vs.exe")]));
}
