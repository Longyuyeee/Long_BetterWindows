namespace LongBetterWindows.Host.Interaction
{
    internal static class WorkflowErrorPresentation
    {
        public static string GetResourceKey(WorkflowErrorCode code)
            => code switch
            {
                WorkflowErrorCode.DocumentEmpty => "workflow.error.documentEmpty",
                WorkflowErrorCode.SchemaMissing
                    or WorkflowErrorCode.SchemaUnsupported => "workflow.error.schemaInvalid",
                WorkflowErrorCode.DocumentIncomplete
                    or WorkflowErrorCode.SourceInvalid
                    or WorkflowErrorCode.StructureInvalid
                    or WorkflowErrorCode.JsonInvalid
                    or WorkflowErrorCode.ValidationFailed => "workflow.error.documentInvalid",
                WorkflowErrorCode.PathInvalid
                    or WorkflowErrorCode.ExistingHashRequired
                    or WorkflowErrorCode.ExpectedHashInvalid => "workflow.error.requestInvalid",
                WorkflowErrorCode.DocumentNotFound => "workflow.error.notFound",
                WorkflowErrorCode.ReparsePointRejected => "workflow.error.pathRejected",
                WorkflowErrorCode.DocumentTooLarge => "workflow.error.documentTooLarge",
                WorkflowErrorCode.ReadFailed => "workflow.error.readFailed",
                WorkflowErrorCode.StorageUnavailable => "workflow.error.storageUnavailable",
                WorkflowErrorCode.SensitiveInputApprovalRequired => "workflow.error.sensitiveApproval",
                WorkflowErrorCode.StaleWriteConflict
                    or WorkflowErrorCode.ExpectedVersionMissing => "workflow.error.conflict",
                WorkflowErrorCode.SaveFailed => "workflow.error.saveFailed",
                WorkflowErrorCode.DeleteFailed => "workflow.error.deleteFailed",
                WorkflowErrorCode.ExportPathInvalid
                    or WorkflowErrorCode.ExportLocationRejected => "workflow.error.exportPathInvalid",
                WorkflowErrorCode.ExportFailed => "workflow.error.exportFailed",
                _ => "workflow.list.readError",
            };
    }
}
