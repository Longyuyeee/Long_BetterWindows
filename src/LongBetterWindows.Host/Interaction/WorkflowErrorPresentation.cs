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
                WorkflowErrorCode.TemplateCatalogUnavailable
                    or WorkflowErrorCode.TemplateOpenFailed => "workflow.error.templateCatalogUnavailable",
                WorkflowErrorCode.TemplateLimitExceeded => "workflow.error.templateLimitExceeded",
                WorkflowErrorCode.TemplateDuplicateId
                    or WorkflowErrorCode.TemplateKeyInvalid
                    or WorkflowErrorCode.TemplatePathRejected => "workflow.error.templateInvalid",
                WorkflowErrorCode.TemplateCatalogNotFound => "workflow.error.templateCatalogNotFound",
                WorkflowErrorCode.TemplateChanged => "workflow.error.templateChanged",
                WorkflowErrorCode.ImportReviewInvalid => "workflow.error.importReviewInvalid",
                WorkflowErrorCode.EditorIdentityConflict => "workflow.error.editorIdentityConflict",
                WorkflowErrorCode.EditorLimitExceeded => "workflow.error.editorLimitExceeded",
                WorkflowErrorCode.EditorCommandUnavailable
                    or WorkflowErrorCode.PreflightCommandInvalid => "workflow.error.commandUnavailable",
                WorkflowErrorCode.EditorInputRejected
                    or WorkflowErrorCode.PreflightInputInvalid => "workflow.error.inputRejected",
                WorkflowErrorCode.EditorTargetUnavailable => "workflow.error.editorTargetUnavailable",
                WorkflowErrorCode.PreflightDefinitionInvalid => "workflow.error.preflightDefinitionInvalid",
                WorkflowErrorCode.PreflightPluginUnavailable => "workflow.error.preflightPluginUnavailable",
                WorkflowErrorCode.PreflightArgumentInvalid => "workflow.error.preflightArgumentInvalid",
                WorkflowErrorCode.PreflightBindingInvalid => "workflow.error.preflightBindingInvalid",
                WorkflowErrorCode.PreflightCompensationRequired => "workflow.error.preflightCompensationRequired",
                WorkflowErrorCode.PreflightCatalogChanged => "workflow.error.preflightCatalogChanged",
                _ => "workflow.list.readError",
            };
    }
}
