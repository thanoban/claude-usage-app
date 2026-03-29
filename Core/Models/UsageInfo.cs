namespace ClaudeUsage.Core.Models;

public class UsageInfo
{
    public List<LimitData> Limits { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string? Error { get; set; }
    public bool IsLoading { get; set; }
}
