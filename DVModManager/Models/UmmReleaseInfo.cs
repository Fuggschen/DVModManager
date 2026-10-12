namespace DVModManager.Models;

/// <summary>
/// One Unity Mod Manager release entry parsed from UMM's self-update feed
/// (Repository.json / Repository_beta_13.json).
/// </summary>
public class UmmReleaseInfo
{
    public string Version { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FeedUrl { get; init; } = "";
}
