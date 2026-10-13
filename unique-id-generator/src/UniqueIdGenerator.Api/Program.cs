using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using UniqueIdGenerator.Api;
using UniqueIdGenerator.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ClockMovedBackwardsExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services =>
{
    // Read once: changing the node identity while running could produce duplicate IDs.
    var options = services.GetRequiredService<IConfiguration>().GetRequiredSection("Snowflake").Get<SnowflakeOptions>()
        ?? throw new InvalidOperationException("The 'Snowflake' configuration section is empty.");
    return new SnowflakeIdGenerator(options, services.GetRequiredService<TimeProvider>());
});

var app = builder.Build();

// Resolved before Run so that invalid configuration or an out-of-range clock fails the startup, not the first request.
var generator = app.Services.GetRequiredService<SnowflakeIdGenerator>();
var maxBatchSize = app.Configuration.GetValue<int?>("IdApi:MaxBatchSize")
    ?? throw new InvalidOperationException("'IdApi:MaxBatchSize' is required.");
if (maxBatchSize < 1)
{
    throw new InvalidOperationException($"'IdApi:MaxBatchSize' must be at least 1, but was {maxBatchSize}.");
}

app.Logger.LogInformation("ID generator started for datacenter {DatacenterId}, machine {MachineId}.",
    generator.DatacenterId, generator.MachineId);

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Malformed requests (e.g. a non-numeric count) are client errors, not server errors.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapPost("/ids", () => new IdResponse(Format(generator.NextId())));

app.MapPost("/ids/batch", Results<Ok<IdBatchResponse>, ProblemHttpResult> (int count) =>
{
    if (count < 1 || count > maxBatchSize)
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid batch size",
            detail: $"count must be between 1 and {maxBatchSize}.");
    }

    var ids = new string[count];
    for (var i = 0; i < count; i++)
    {
        ids[i] = Format(generator.NextId());
    }

    return TypedResults.Ok(new IdBatchResponse(ids));
});

app.MapGet("/ids/{id}", Results<Ok<DecodedIdResponse>, ProblemHttpResult> (string id) =>
{
    // NumberStyles.None rejects signs, spaces and separators: only plain digits are valid IDs.
    if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid ID",
            detail: "The ID must be a non-negative 64-bit integer.");
    }

    var decoded = SnowflakeId.Decode(value);
    return TypedResults.Ok(new DecodedIdResponse(
        Format(decoded.Value), decoded.Timestamp, decoded.DatacenterId, decoded.MachineId, decoded.Sequence));
});

app.Run();

static string Format(long id) => id.ToString(CultureInfo.InvariantCulture);
