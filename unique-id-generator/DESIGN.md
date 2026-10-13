# Design: Unique ID Generator

## 1. Problem and why I chose it

Generate unique, numeric, 64-bit IDs ordered by time, from several nodes, without coordination between them (chapter 7 of *System Design Interview*, Vol. 1).

Of the problems in the book, this is the closest to the payments ecosystem, where I have worked and where this role sits: every operation needs a unique, traceable identifier. I have also solved a similar problem in production (see section 3).

## 2. Requirements and scope

The requirements are the ones from the book, taken as given:

| Requirement | How it is met | Tests |
|---|---|---|
| Unique | Within a node, `(millisecond, sequence)` never repeats: `NextId()` runs under a lock, the sequence increases within the millisecond, and an exhausted sequence waits for the next one. If the clock moves backwards, the generator throws instead of reusing values. Across nodes, the datacenter and machine bits differ. | Concurrency test, two-node test, sequence exhaustion test, backward clock during the wait test |
| Numeric only | `NextId()` returns a `long`. The API sends it as a JSON string of digits (see 4.6). | API test for the ID as a JSON string, decoding tests |
| Fits in 64 bits | 1 + 41 + 5 + 5 + 12 bits. Node IDs are validated to 0–31 and the clock to the 41-bit range, so no field overflows into another. | ID composition test, node and clock range tests |
| Ordered by date | The timestamp occupies the highest bits. Strictly increasing per node; approximate across nodes (see 5). | Same-millisecond and new-millisecond sequence tests, concurrency test (increasing per thread) |
| Over 10,000 IDs/s | Up to 4,096 IDs per millisecond per node. | Sequence exhaustion test (4,096 IDs in one millisecond). The concurrency test generates 400,000 IDs in well under a second. |

**In scope:** the generator as a library (`UniqueIdGenerator.Core`), decoding of IDs, and a thin HTTP API.

**Out of scope:** automatic node ID assignment, persisting the last issued timestamp, tolerance for small clock jumps, Docker, metrics and benchmarks (see 6).

## 3. Options considered

| Option | Unique without coordination | 64-bit numeric | Ordered by time | Notes |
|---|---|---|---|---|
| Multi-master replication (auto-increment by `k`) | Yes, per server | Yes | No, not across servers | Hard to add or remove servers. |
| Centralized DB sequence (ticket server) | No: every ID goes to the database | Yes | Yes | Works well with a central database, but every ID requires a round trip to the database and it is a single point of failure (the book's ticket server pattern). |
| UUID v4 | Yes | No (128 bits) | No | Random. |
| UUID v7 | Yes | No (128 bits) | Yes (48-bit ms timestamp) | Available in .NET 9+ (`Guid.CreateVersion7()`). Fails only the 64-bit requirement. |
| **Snowflake** | **Yes** | **Yes** | **Yes, per node** | Depends on the clock and on unique node IDs. |

Snowflake is the only option that meets all the requirements.

**Production experience.** On a payments system I worked on, an identifier for QR payment operations was producing collisions in production, and I was assigned to fix it. The base number came from a SQL Server `SEQUENCE` (`bigint`), obtained through a stored procedure, and was obfuscated with Hashids. The cause was that the Hashids salt was generated at random on every call: the encoding stopped being a bijection, two different numbers could produce the same code, and the codes behaved as if they were random. The fix was to use a fixed salt and to configure Hashids to generate the hexadecimal format the field required directly, removing an intermediate conversion. I deployed it behind a feature flag. The lesson, which I apply in this design: uniqueness has to come from the structure of the ID, not from randomness, and obfuscation has to be reversible.

## 4. Design

### 4.1 Bit layout

```
| 1 bit | 41 bits                       | 5 bits        | 5 bits     | 12 bits  |
|   0   | ms since 2026-01-01T00:00:00Z | datacenter ID | machine ID | sequence |
```

- The sign bit is always 0, so IDs are positive `long` values (and fit a SQL Server `bigint`).
- The epoch and the bit layout are **constants** in `SnowflakeId`, not configuration: they define the format. Changing them after IDs have been issued breaks ordering and can produce duplicates.
- An epoch close to today uses almost all of the 41-bit range: the last representable millisecond is 2095-09-07.

### 4.2 Algorithm (`SnowflakeIdGenerator.NextId`)

1. Read the clock and convert it to milliseconds since the epoch (validated to the 41-bit range).
2. If it is lower than the last timestamp, throw `ClockMovedBackwardsException`.
3. If it is the same millisecond, increment the sequence (`(sequence + 1) & 4095`). If it wraps to 0, busy-wait for the next millisecond (under 1 ms; `Thread.Sleep` can oversleep). The wait also throws if the clock moves backwards.
4. If it is a new millisecond, the sequence starts at 0.
5. Store the last timestamp and sequence **only after** the ID is fully determined. If anything throws, the state is unchanged and no sequence can be reissued.
6. Return `(timestamp << 22) | (datacenterId << 17) | (machineId << 12) | sequence`.

`SnowflakeId.Decode(long)` does the reverse with shifts and masks, and returns the timestamp in UTC. Decoding is **structural**: it rejects only negative values and does not prove that the ID was issued by the system.

### 4.3 Concurrency: `lock` vs. `Interlocked`

The state is two fields that change together (last timestamp and sequence). `Interlocked` is atomic on a single value. A lock-free version would need both packed into one `long` and a `CompareExchange` loop, which is harder to read and to test. The lock is not a bottleneck for the requirement: the concurrency test generates 400,000 IDs from 8 threads in well under a second. The lock (`System.Threading.Lock`) also acts as a memory barrier, so the fields do not need `volatile`.

### 4.4 Clock handling

| Situation | Behavior |
|---|---|
| Clock before the epoch or past the 41-bit range | `InvalidOperationException`, checked at startup and on every ID. |
| Clock moves backwards (any amount) | `ClockMovedBackwardsException` with the difference in ms. The API returns `503` + `Retry-After` (seconds, rounded up). The generator recovers by itself when the clock catches up. |
| Sequence exhausted within a millisecond | Waits for the next millisecond. The caller sees a short delay, not an error. |

Failing on any backward jump is preferred over generating a possible duplicate.

### 4.5 Configuration

- Only the node identity is configurable: `Snowflake:DatacenterId` and `Snowflake:MachineId` (0–31). The properties are `int?`, so a missing key is detected instead of silently binding to 0.
- The constructor of `SnowflakeIdGenerator` validates the identity and the clock. The API resolves the generator before it starts listening, so invalid configuration stops the process at startup.
- The configuration is read once (no reload): changing the identity of a running node could produce duplicates.
- There is no default identity outside `Development`: a node without an explicit identity could collide with another one.
- The core has no logging: it reports errors with exceptions and the host logs them.

### 4.6 API

| Endpoint | Response | Errors |
|---|---|---|
| `POST /ids` | `{ "id": "..." }` | `503`, `500` |
| `POST /ids/batch?count=N` | `{ "ids": ["...", ...] }` (N between 1 and `IdApi:MaxBatchSize`) | `400`, `503`, `500` |
| `GET /ids/{id}` | `{ "id", "timestamp", "datacenterId", "machineId", "sequence" }` | `400`, `500` |

- IDs are **strings** in JSON, through the response DTOs: JavaScript represents integers exactly only up to 2^53, and these IDs are around 2^62.
- Generation uses `POST` because it is not idempotent.
- All errors are Problem Details. A `500` does not include the exception message or stack trace.
- If the clock moves backwards in the middle of a batch, the whole request returns `503` and the IDs already generated are discarded. Contiguity is not required, the same as gaps in a database sequence.
- Swagger UI and the OpenAPI document are available only in `Development`.

## 5. Trade-offs and limitations

- **Clock dependency.** Correctness depends on the clock not moving backwards. The generator detects it and refuses to generate IDs, but it cannot detect a backward jump that happened while the process was down: the last timestamp is not persisted, so a restart with the clock behind the last issued ID could reissue IDs.
- **Manual node IDs.** Uniqueness across nodes depends on each node having a unique `(DatacenterId, MachineId)` pair, assigned by configuration. Two nodes with the same pair produce duplicates.
- **Range.** 41 bits of milliseconds give about 69.7 years: until 2095-09-07. After that, a new epoch or a format change is needed.
- **The ID reveals information.** Anyone with an ID can decode its creation time and the node that created it. If that matters, the ID needs reversible obfuscation before it is exposed.
- **Ordering across nodes is approximate.** IDs from different nodes in the same millisecond are ordered by node bits, not by real time, and clock skew between nodes shifts the order.
- **Limits per node.** 1,024 nodes (32 × 32) and 4,096 IDs per millisecond per node.

## 6. Production considerations

- **Node ID assignment.** Environment variables per instance (`Snowflake__DatacenterId`, `Snowflake__MachineId`), the ordinal of a Kubernetes StatefulSet pod, or a lease in a coordination store so that two running instances can never hold the same identity.
- **Secrets.** The service has none today. If node identity came from a lease store, its connection string would come from a secret store such as Azure Key Vault (as I used in production), not from `appsettings.json`.
- **Clock monitoring.** Keep NTP running and alert on its offset. The warning logged on every `ClockMovedBackwardsException` and the rate of `503` responses are signals to watch.
- **Restart safety.** Persist the last issued timestamp and refuse to start if the clock is behind it, or wait at startup for longer than the maximum expected backward jump.
- **Deployment model.** The intended use is the library embedded in each service. A central ID service over HTTP adds a network hop and a dependency to every ID.

## 7. Testing strategy

48 tests (`dotnet test`): 37 for the core and 11 for the API contract.

- **Deterministic time.** `ManualTimeProvider` (a `TimeProvider` controlled by the test) moves the clock forwards or backwards. `AutoAdvance` lets a test leave the wait for the next millisecond. It covers sequence exhaustion, a backward clock, recovery after a backward clock, and a backward clock during the wait.
- **Independent oracle.** Tests rebuild the expected ID with their own shifts instead of reading the production constants, so a change in the layout makes them fail.
- **Concurrency.** 8 dedicated threads × 50,000 IDs with a start barrier and the real clock: no duplicates and increasing per thread. It is a smoke test; the guarantee comes from the lock.
- **Two nodes.** Same millisecond, all 4,096 sequences on both nodes, including two identities that differ in adjacent bits.
- **API contract.** `WebApplicationFactory` with the `TimeProvider` replaced: response formats (ID as a JSON string), `400` cases, `503` with `Retry-After`, and a `500` without internal details.
- **Not tested on purpose:** framework tooling (Swagger, OpenAPI). Tests cover the core logic and the API contract.

**Mutation checks.** To confirm the tests detect real bugs, the code was broken on purpose and the tests were run again:

| Mutation | Result |
|---|---|
| Remove the lock in `NextId()` | The concurrency test failed in 10 of 10 runs (about 100,000 unique IDs out of 400,000). In one run, the race appeared as a spurious `ClockMovedBackwardsException` on a worker thread, which crashed the test host instead of failing the test. **Fixed:** each thread captures its exceptions and the test asserts there are none. |
| Shift the datacenter one bit (16 instead of 17) | The composition tests failed, but the adjacent-bits case of the two-node test did not: the chosen identities, `(0, 31)` and `(1, 0)`, shared a bit but were not equal. **Fixed:** changed to `(0, 16)` and `(1, 0)`, which become identical under the mutation. |
| Store the sequence before waiting for the next millisecond | Only the "backward clock during the wait" test failed. |
| Remove the status code mapping for malformed requests | Only the `count=abc` test failed (it became a `500`). |
| Return the ID as a `long` in the DTO | Only the "ID as a JSON string" test failed. |

## 8. How I used AI

I used AI in two ways. Claude Code wrote most of the code and the tests, following rules I defined: small steps, propose options with trade-offs before each decision, explain each piece line by line, and make no commits. I used a separate AI conversation as a reviewer, to audit each proposal before approving it.

I made the decisions, and in several of them I went against the AI's recommendation: the epoch as a constant instead of configuration, `int?` instead of a record for the options, and removing the Swagger tests as over-engineering. I also asked for tests that had not been planned, such as recovery after the clock moves backwards.

I reviewed and debugged each piece, modified tests to understand the behavior and wrote some of the comments; I made the commits. The mutation checks were run by the AI and found two weaknesses in the tests, which were fixed.
