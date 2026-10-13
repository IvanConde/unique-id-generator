# Unique ID Generator

A Snowflake-style generator of unique 64-bit IDs for distributed systems: each node creates IDs on its own, without coordination, and the IDs are ordered by time.

```
| 1 bit | 41 bits                              | 5 bits        | 5 bits     | 12 bits  |
|   0   | ms since 2026-01-01T00:00:00Z        | datacenter ID | machine ID | sequence |
```

- `src/UniqueIdGenerator.Core`: the generator as a library, with no ASP.NET dependency (`SnowflakeIdGenerator`, `SnowflakeId`).
- `src/UniqueIdGenerator.Api`: a thin HTTP API on top of it (generate, generate a batch, decode).
- `tests/UniqueIdGenerator.Tests`: unit tests for the core and integration tests for the API.

Design decisions, trade-offs and how AI was used: see [DESIGN.md](DESIGN.md).

## Requirements

Only the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (any 10.0.x version; `global.json` pins the major version). It runs on Windows, macOS and Linux, with no database or other services.

## Build and test

All commands run from this folder (`unique-id-generator/`):

```bash
dotnet build
dotnet test
```

## Run the API

```bash
dotnet run --project src/UniqueIdGenerator.Api
```

It runs in the `Development` environment and listens on `http://localhost:5176`. Open **http://localhost:5176/swagger** in the browser. (Visual Studio, Rider and VS Code open it automatically; from the terminal, `dotnet watch --project src/UniqueIdGenerator.Api` also does.)

### Swagger

Swagger UI is at **http://localhost:5176/swagger**, and the OpenAPI document at `http://localhost:5176/openapi/v1.json`. Every endpoint can be tried from there, and each one documents its success response and its error responses (`application/problem+json`).

Swagger is only available in the `Development` environment. In any other environment both URLs return 404.

## Endpoints

IDs are returned as **strings**: JavaScript clients cannot represent integers above 2^53 exactly, and Snowflake IDs are around 2^62.

| Method | Path | Description |
|---|---|---|
| `POST` | `/ids` | Generates one ID. |
| `POST` | `/ids/batch?count=N` | Generates `N` IDs (1 to `IdApi:MaxBatchSize`, 1000 by default). |
| `GET` | `/ids/{id}` | Decodes an ID into its timestamp, datacenter, machine and sequence. |

Errors use [Problem Details](https://www.rfc-editor.org/rfc/rfc9457):

| Status | When |
|---|---|
| `400` | Invalid `count` or invalid ID (not a non-negative 64-bit integer). |
| `503` + `Retry-After` | The server clock moved backwards. The node refuses to generate IDs (to avoid duplicates) until its clock catches up; retry after the given seconds. |
| `500` | Unexpected error. The response does not include internal details. |

### Examples

```bash
curl -X POST http://localhost:5176/ids
# {"id":"103544663004151808"}

curl -X POST "http://localhost:5176/ids/batch?count=3"
# {"ids":["103544663092232192","103544663092232193","103544663092232194"]}

curl http://localhost:5176/ids/21393410
# {"id":"21393410","timestamp":"2026-01-01T00:00:00.005+00:00","datacenterId":3,"machineId":7,"sequence":2}

curl -X POST "http://localhost:5176/ids/batch?count=5000"
# {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Invalid batch size","status":400,
#  "detail":"count must be between 1 and 1000.","traceId":"00-..."}
```

On Windows PowerShell 5.1, `curl` is an alias of `Invoke-WebRequest`: use `curl.exe` instead.

## Configuration

| Key | Required | Description |
|---|---|---|
| `Snowflake:DatacenterId` | Yes | 0 to 31. |
| `Snowflake:MachineId` | Yes | 0 to 31. Each running node must have a unique `(DatacenterId, MachineId)` pair. |
| `IdApi:MaxBatchSize` | Yes (default in `appsettings.json`: 1000) | Maximum `count` for `/ids/batch`. |

The node identity has **no default** outside `Development` (where `appsettings.Development.json` sets 0/0). This is on purpose: a node that starts without an explicit identity could collide with another one, so the service refuses to start instead. As environment variables, use a double underscore:

```bash
# macOS / Linux
ASPNETCORE_ENVIRONMENT=Production Snowflake__DatacenterId=1 Snowflake__MachineId=2 \
  dotnet run --project src/UniqueIdGenerator.Api --no-launch-profile
```

```powershell
# Windows PowerShell
$env:ASPNETCORE_ENVIRONMENT = "Production"; $env:Snowflake__DatacenterId = "1"; $env:Snowflake__MachineId = "2"
dotnet run --project src/UniqueIdGenerator.Api --no-launch-profile
```

Without a launch profile the API listens on `http://localhost:5000`. On startup it logs the node identity (`ID generator started for datacenter 1, machine 2.`). If the identity is missing or out of range, the process exits with an error.
