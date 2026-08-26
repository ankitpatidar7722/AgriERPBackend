namespace AgriERP.Application.Common.Models;

/// <summary>
/// Result of a bulk (Excel) import: how many rows landed, and — for a partial
/// import — the row number and reason for each one that did not.
/// </summary>
public class BulkImportResultDto
{
    public int Imported { get; set; }
    public int Failed { get; set; }
    public List<BulkImportError> Errors { get; set; } = new();
}

public class BulkImportError
{
    /// <summary>1-based index of the row within the uploaded data (header excluded).</summary>
    public int Row { get; set; }
    public string Message { get; set; } = string.Empty;
}
