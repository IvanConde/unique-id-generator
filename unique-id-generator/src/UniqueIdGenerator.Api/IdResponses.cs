namespace UniqueIdGenerator.Api;

// IDs travel as strings: JavaScript clients cannot represent integers above 2^53 exactly.
public sealed record IdResponse(string Id);

public sealed record IdBatchResponse(IReadOnlyList<string> Ids);

public sealed record DecodedIdResponse(string Id, DateTimeOffset Timestamp, int DatacenterId, int MachineId, int Sequence);
