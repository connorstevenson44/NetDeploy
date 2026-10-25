using NetDeploy.Core.Manifest;

namespace NetDeploy.Core.Tests;

public class PlaceholdersTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase) { ["InstallDir"] = @"C:\Apps\Orders" };

    [Theory]
    [InlineData(@"{InstallDir}\appsettings.json", @"C:\Apps\Orders\appsettings.json")]
    [InlineData(@"{installdir}", @"C:\Apps\Orders")]
    [InlineData("{Unknown}", "{Unknown}")]
    [InlineData("Get-Service | Where-Object { $_.Status }", "Get-Service | Where-Object { $_.Status }")]
    public void Resolves_known_names_only(string text, string expected) =>
        Assert.Equal(expected, Placeholders.Resolve(text, Values));
}
