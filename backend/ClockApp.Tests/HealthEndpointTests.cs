using System.Net;

namespace ClockApp.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_returns_ok()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }
}
