using System.Text.Json.Serialization;

namespace ClaudeUsage.Core.Models;

public class UsagePeriod
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }
}

public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsagePeriod? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsagePeriod? SevenDay { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsagePeriod? SevenDaySonnet { get; set; }
}
