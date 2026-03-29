namespace ClaudeUsage.Core.Models;

public class LimitData
{
    public string Label { get; set; } = string.Empty;

    /// <summary>0-100 from utilization field</summary>
    public double Percentage { get; set; }

    public DateTime? ResetsAt { get; set; }
}
