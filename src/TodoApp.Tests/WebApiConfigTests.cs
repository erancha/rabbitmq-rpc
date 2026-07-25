using Microsoft.Extensions.Configuration;
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
        var webApiDirectory = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "TodoApp.WebApi");

        // Reading through the configuration provider applies the same parsing the WebApi host
        // does, so annotations appsettings.json carries for operators cannot break this test.
        var appsettings = new ConfigurationBuilder()
            .SetBasePath(webApiDirectory)
            .AddJsonFile("appsettings.json")
            .Build();
        var shipped = int.Parse(appsettings["WebApi:RpcTimeoutSeconds"]
            ?? throw new InvalidOperationException("appsettings.json omits WebApi:RpcTimeoutSeconds"));

        Assert.Equal(shipped, new WebApiConfig().RpcTimeoutSeconds);
    }
}
