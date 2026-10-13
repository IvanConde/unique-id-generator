using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UniqueIdGenerator.Core;

namespace UniqueIdGenerator.Tests.Api;

public class IdEndpointsTests
{
    private const int DatacenterId = 1;
    private const int MachineId = 2;
    private const int MaxBatchSize = 10;

    private static WebApplicationFactory<Program> CreateFactory(TimeProvider? clock = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Snowflake:DatacenterId", DatacenterId.ToString());
            builder.UseSetting("Snowflake:MachineId", MachineId.ToString());
            builder.UseSetting("IdApi:MaxBatchSize", MaxBatchSize.ToString());
            if (clock is not null)
            {
                builder.ConfigureServices(services => services.AddSingleton(clock));
            }
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Should_ReturnIdAsJsonString_When_GeneratingAnId()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ids", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = (await ReadJsonAsync(response)).GetProperty("id");
        Assert.Equal(JsonValueKind.String, id.ValueKind);
        var decoded = SnowflakeId.Decode(long.Parse(id.GetString()!));
        Assert.Equal(DatacenterId, decoded.DatacenterId);
        Assert.Equal(MachineId, decoded.MachineId);
    }

    [Fact]
    public async Task Should_ReturnDistinctIncreasingIdsAsStrings_When_GeneratingABatch()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/ids/batch?count={MaxBatchSize}", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = (await ReadJsonAsync(response)).GetProperty("ids").EnumerateArray().ToList();
        Assert.Equal(MaxBatchSize, ids.Count);
        Assert.All(ids, id => Assert.Equal(JsonValueKind.String, id.ValueKind));
        var values = ids.Select(id => long.Parse(id.GetString()!)).ToList();
        Assert.Equal(values.Order(), values);
        Assert.Equal(MaxBatchSize, values.Distinct().Count());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("abc")]
    public async Task Should_ReturnBadRequestProblem_When_BatchCountIsInvalid(string count)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/ids/batch?count={count}", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Should_ReturnParts_When_DecodingAValidId()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ids/21393410");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("21393410", json.GetProperty("id").GetString());
        Assert.Equal(SnowflakeId.Epoch.AddMilliseconds(5), json.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(3, json.GetProperty("datacenterId").GetInt32());
        Assert.Equal(7, json.GetProperty("machineId").GetInt32());
        Assert.Equal(2, json.GetProperty("sequence").GetInt32());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("99999999999999999999")] // above long.MaxValue
    public async Task Should_ReturnBadRequestProblem_When_DecodingAnInvalidId(string id)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/ids/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Should_ReturnServiceUnavailableWithRetryAfter_When_ClockMovesBackwards()
    {
        var clock = new ManualTimeProvider(SnowflakeId.Epoch.AddDays(1));
        await using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();
        await client.PostAsync("/ids", content: null);

        clock.Now = clock.Now.AddMilliseconds(-1500);
        var response = await client.PostAsync("/ids", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        // 1500 ms rounded up to whole seconds.
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task Should_ReturnGenericServerErrorProblem_When_AnUnexpectedErrorOccurs()
    {
        var clock = new ManualTimeProvider(SnowflakeId.Epoch.AddDays(1));
        await using var factory = CreateFactory(clock);
        using var client = factory.CreateClient();

        // Past the 41-bit range: NextId throws an InvalidOperationException that the API does not expect.
        clock.Now = SnowflakeId.Epoch.AddMilliseconds((1L << 41) + 1);
        var response = await client.PostAsync("/ids", content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("representable", body);
        Assert.DoesNotContain("at UniqueIdGenerator", body);
    }
}
