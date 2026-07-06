namespace StoreShared.Print;

public sealed record EnqueueLabelPrintRequest(string Barcode, int Count = 1);

public sealed record EnqueueLabelPrintResponse(Guid JobId, string Message);

public sealed record LabelPrintJobDto(
    Guid Id,
    string Barcode,
    int Count,
    DateTimeOffset CreatedAt,
    string RequestedBy);

public sealed record LabelPrintQueueStatusDto(int PendingCount);
