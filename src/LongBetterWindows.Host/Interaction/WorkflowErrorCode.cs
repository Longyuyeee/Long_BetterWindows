namespace LongBetterWindows.Host.Interaction
{
    public enum WorkflowErrorCode
    {
        None = 0,
        DocumentEmpty = 5000,
        SchemaMissing = 5001,
        SchemaUnsupported = 5002,
        DocumentIncomplete = 5003,
        SourceInvalid = 5004,
        StructureInvalid = 5005,
        JsonInvalid = 5006,
        PathInvalid = 5100,
        DocumentNotFound = 5101,
        ReparsePointRejected = 5102,
        DocumentTooLarge = 5103,
        ReadFailed = 5104,
        StorageUnavailable = 5105,
        SensitiveInputApprovalRequired = 5110,
        ValidationFailed = 5111,
        ExistingHashRequired = 5112,
        ExpectedHashInvalid = 5113,
        StaleWriteConflict = 5114,
        ExpectedVersionMissing = 5115,
        SaveFailed = 5116,
        DeleteFailed = 5117,
        ExportPathInvalid = 5118,
        ExportLocationRejected = 5119,
        ExportFailed = 5120,
    }
}
