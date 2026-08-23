using System.Text.Json.Serialization;

namespace CloudLightBlizzard.Models;

public sealed class AnnouncementDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonPropertyName("announcements")] public List<Announcement> Announcements { get; set; } = new();
}

public sealed class Announcement
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("revision")] public int Revision { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("publishedAt")] public DateTimeOffset PublishedAt { get; set; }
    [JsonPropertyName("minVersion")] public string? MinVersion { get; set; }
    [JsonPropertyName("maxVersion")] public string? MaxVersion { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public sealed class AnnouncementLocalState
{
    public AnnouncementDocument? Cache { get; set; }
    public Dictionary<string, int> ReadRevisions { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset? LastSuccessfulCheck { get; set; }
}
