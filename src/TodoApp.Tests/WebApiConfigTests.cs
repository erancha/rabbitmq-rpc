using System.Text.Json;
using TodoApp.WebApi.Configuration;
using Xunit;

namespace TodoApp.Tests;

/// <summary>
/// Pins the WebApi configuration fallbacks to the values shipped in appsettings.json,
/// so the code-level default and the deployed configuration cannot drift apart.
/// </summary>
public class WebApiConfigTests
{
    [Fact]
    public void RpcTimeoutSeconds_fallback_matches_shipped_appsettings()
    {
        // Test binaries run from src/TodoApp.Tests/bin/<Configuration>/<tfm>.
        var appsettingsPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "TodoApp.WebApi", "appsettings.json");

        using var appsettings = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
        var shipped = appsettings.RootElement
            .GetProperty("WebApi")
            .GetProperty("RpcTimeoutSeconds")
            .GetInt32();

        Assert.Equal(shipped, new WebApiConfig().RpcTimeoutSeconds);
    }
}
