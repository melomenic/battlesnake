using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Starter.Api.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Root_returns_api_version_one()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("1", document.RootElement.GetProperty("apiversion").GetString());
        Assert.Equal("#3E338F", document.RootElement.GetProperty("color").GetString());
    }

    [Fact]
    public async Task Move_accepts_the_engine_payload_and_returns_a_direction()
    {
        var payload = new
        {
            game = new
            {
                id = "totally-unique-game-id",
                ruleset = new
                {
                    name = "standard",
                    version = "v1.1.15",
                    settings = new { foodSpawnChance = 15, minimumFood = 1, hazardDamagePerTurn = 14 }
                },
                map = "standard",
                source = "league",
                timeout = 500
            },
            turn = 14,
            board = new
            {
                height = 11,
                width = 11,
                food = new[] { new { x = 5, y = 5 }, new { x = 9, y = 0 }, new { x = 2, y = 6 } },
                hazards = new[] { new { x = 3, y = 2 } },
                snakes = new[]
                {
                    Snake("snake-508e96ac-94ad-11ea-bb37", "My Snake", 54, new[] { (0, 0), (1, 0), (2, 0) }),
                    Snake("snake-b67f4906-94ae-11ea-bb37", "Another Snake", 16, new[] { (5, 4), (5, 3), (6, 3), (6, 2) })
                }
            },
            you = Snake("snake-508e96ac-94ad-11ea-bb37", "My Snake", 54, new[] { (0, 0), (1, 0), (2, 0) })
        };

        var response = await _client.PostAsJsonAsync("/move", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("up", document.RootElement.GetProperty("move").GetString());
    }

    private static object Snake(string id, string name, int health, (int X, int Y)[] body)
    {
        return new
        {
            id,
            name,
            health,
            body = body.Select(part => new { x = part.X, y = part.Y }).ToArray(),
            latency = "0",
            head = new { x = body[0].X, y = body[0].Y },
            length = body.Length,
            shout = "",
            squad = "",
            customizations = new { color = "#3E338F", head = "default", tail = "default" }
        };
    }
}
