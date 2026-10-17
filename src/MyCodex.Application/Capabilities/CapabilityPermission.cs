namespace MyCodex.Application.Capabilities
{
    public enum CapabilityPermission
    {
        ReadOfficeRoot,
        WriteOfficeRoot,
        CreateEditedCopy,
        OverwriteWithConfirmation,
        DeleteWithConfirmation,
        WriteOutsideOfficeRootWithConfirmation,
        BulkModifyWithConfirmation,
        RunShell,
        UseLocalOfficeAutomation,
        UseLibreOffice,
        UseBrowserAutomation,
        UseMcpTools,
        WebSearch,
        DownloadAssets,
        UploadDocuments,
        CallExternalDocumentServices,
        GenerateImages,
        GenerateDocuments,
        GenerateSpreadsheets,
        GeneratePresentations
    }
}
