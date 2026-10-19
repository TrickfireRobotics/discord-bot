using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace DiscordBot.Services.Api;

/// <summary>
/// A single notable thing the bot did, as surfaced in the dashboard's activity
/// feed.
/// </summary>
public record ActivityEntry(
    DateTimeOffset Time,
    string Kind,
    string Summary,
    string? Detail = null
);

/// <summary>
/// Keeps a bounded, disk-backed history of notable bot actions so the dashboard
/// can show what the bot has been doing, including across redeploys.
/// </summary>
public class ActivityLog : IAutoRegisteredService
{
    public const string CheckIn = "check_in";
    public const string CheckOut = "check_out";
    public const string MemberJoined = "member_joined";
    public const string RolesSynced = "roles_synced";
    public const string InactiveNotice = "inactive_notice";
    public const string FeedbackReceived = "feedback_received";
    public const string Error = "error";

    private readonly List<ActivityEntry> _entries = [];
    private readonly ActivityLogOptions _options;
    private readonly ILogger<ActivityLog> _logger;

    public DateTimeOffset? LastNotionWebhookAt { get; private set; }
    public DateTimeOffset? LastRoleSyncAt { get; private set; }
    public DateTimeOffset? LastErrorAt { get; private set; }
    public string? LastError { get; private set; }

    public ActivityLog(IOptions<ActivityLogOptions> options, ILogger<ActivityLog> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!File.Exists(_options.FileLocation))
        {
            return;
        }

        try
        {
            PersistedState? state = JsonConvert.DeserializeObject<PersistedState>(
                File.ReadAllText(_options.FileLocation)
            );
            if (state is null)
            {
                return;
            }

            _entries.AddRange(state.Entries);
            LastNotionWebhookAt = state.LastNotionWebhookAt;
            LastRoleSyncAt = state.LastRoleSyncAt;
            LastErrorAt = state.LastErrorAt;
            LastError = state.LastError;
        }
        catch (Exception ex)
        {
            // A corrupt activity file is not worth taking the bot down for.
            _logger.LogError(ex, "Could not read activity log, starting fresh:");
        }
    }

    public void Record(string kind, string summary, string? detail = null)
    {
        lock (_entries)
        {
            _entries.Add(new ActivityEntry(DateTimeOffset.UtcNow, kind, summary, detail));
            if (_entries.Count > _options.Capacity)
            {
                _entries.RemoveRange(0, _entries.Count - _options.Capacity);
            }

            switch (kind)
            {
                case RolesSynced:
                    LastRoleSyncAt = DateTimeOffset.UtcNow;
                    break;
                case Error:
                    LastErrorAt = DateTimeOffset.UtcNow;
                    LastError = summary;
                    break;
            }

            Save();
        }
    }

    public void MarkNotionWebhook()
    {
        lock (_entries)
        {
            LastNotionWebhookAt = DateTimeOffset.UtcNow;
            Save();
        }
    }

    /// <summary>
    /// The most recent entries, newest first.
    /// </summary>
    public IReadOnlyList<ActivityEntry> Recent(int limit)
    {
        lock (_entries)
        {
            return _entries.AsEnumerable().Reverse().Take(limit).ToList();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(
                _options.FileLocation,
                JsonConvert.SerializeObject(
                    new PersistedState
                    {
                        Entries = _entries,
                        LastNotionWebhookAt = LastNotionWebhookAt,
                        LastRoleSyncAt = LastRoleSyncAt,
                        LastErrorAt = LastErrorAt,
                        LastError = LastError,
                    },
                    Formatting.Indented
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write activity log:");
        }
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        builder
            .Services.AddSingleton<ActivityLog>()
            .ConfigureTypeSection<ActivityLogOptions>(builder.Configuration);
    }

    private class PersistedState
    {
        public List<ActivityEntry> Entries { get; set; } = [];
        public DateTimeOffset? LastNotionWebhookAt { get; set; }
        public DateTimeOffset? LastRoleSyncAt { get; set; }
        public DateTimeOffset? LastErrorAt { get; set; }
        public string? LastError { get; set; }
    }
}

public class ActivityLogOptions
{
    /// <summary>
    /// Where the activity history is persisted, relative to the working
    /// directory.
    /// </summary>
    public string FileLocation { get; set; } = "activity.json";

    /// <summary>
    /// How many entries to keep before dropping the oldest.
    /// </summary>
    public int Capacity { get; set; } = 300;
}
