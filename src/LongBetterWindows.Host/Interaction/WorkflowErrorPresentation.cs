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
                WorkflowErrorCode.ExecutionPreflightRejected => "workflow.error.executionPreflightRejected",
                WorkflowErrorCode.ExecutionAuthorizationRejected
                    or WorkflowErrorCode.ExecutionReviewMissing => "workflow.error.executionApprovalInvalid",
                WorkflowErrorCode.ExecutionStateChanged => "workflow.error.executionStateChanged",
                WorkflowErrorCode.ExecutionBindingFailed => "workflow.error.executionBindingFailed",
                WorkflowErrorCode.ExecutionArgumentInvalid => "workflow.error.preflightArgumentInvalid",
                WorkflowErrorCode.ExecutionCommandFailed => "workflow.error.executionCommandFailed",
                WorkflowErrorCode.ExecutionCancelled => "workflow.error.executionCancelled",
                WorkflowErrorCode.ExecutionOutputInvalid => "workflow.error.executionOutputInvalid",
                WorkflowErrorCode.ExecutionCompensationBlocked
                    or WorkflowErrorCode.ExecutionCompensationFailed => "workflow.error.executionCompensationFailed",
                WorkflowErrorCode.ExecutionBusy => "workflow.error.executionBusy",
                WorkflowErrorCode.ReportEmpty
                    or WorkflowErrorCode.ReportSchemaUnsupported
                    or WorkflowErrorCode.ReportInvalid
                    or WorkflowErrorCode.ReportJsonInvalid => "workflow.error.reportInvalid",
                WorkflowErrorCode.ReportSensitiveApprovalRequired => "workflow.error.reportSensitiveApproval",
                WorkflowErrorCode.ReportTooLarge => "workflow.error.reportTooLarge",
                WorkflowErrorCode.ReportStorageUnavailable => "workflow.error.reportStorageUnavailable",
                WorkflowErrorCode.ReportAlreadyExists => "workflow.error.reportAlreadyExists",
                WorkflowErrorCode.ReportSaveFailed => "workflow.error.reportSaveFailed",
                WorkflowErrorCode.ReportIdInvalid => "workflow.error.reportIdInvalid",
                WorkflowErrorCode.ReportNotFound => "workflow.error.reportNotFound",
                WorkflowErrorCode.ReportReparsePointRejected => "workflow.error.reportPathRejected",
                WorkflowErrorCode.ReportReadFailed => "workflow.error.reportReadFailed",
                WorkflowErrorCode.TerminalOutputInvalid => "workflow.error.terminalOutputInvalid",
                WorkflowErrorCode.TerminalExportPathInvalid => "workflow.error.terminalPathInvalid",
                WorkflowErrorCode.TerminalApprovalMissing => "workflow.error.terminalApprovalMissing",
                WorkflowErrorCode.TerminalReviewInvalid => "workflow.error.terminalReviewInvalid",
                WorkflowErrorCode.TerminalReviewChanged => "workflow.error.terminalReviewChanged",
                WorkflowErrorCode.TerminalDestinationChanged => "workflow.error.terminalDestinationChanged",
                WorkflowErrorCode.TerminalExportCancelled => "workflow.error.terminalCancelled",
                WorkflowErrorCode.TerminalAccessDenied => "workflow.error.terminalAccessDenied",
                WorkflowErrorCode.TerminalIoFailure => "workflow.error.terminalIoFailure",
                _ => "workflow.list.readError",
            };
    }
}
