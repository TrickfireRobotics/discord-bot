using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DiscordBot.Services.Discord;
using DSharpPlus.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DiscordBot.Services.Api;

/// <summary>
/// Read-only JSON endpoints the TrickFire dashboard polls to show what the bot
/// is doing. Served off the same listener as the Notion webhooks, so every
/// route here requires a token even though it is only published on loopback.
/// </summary>
public class DashboardApi(
    ILogger<DashboardApi> logger,
    IConfiguration configuration,
    WebhookListener listener,
    ActivityLog activity,
    LogBuffer logs,
    BotState botState,
    DiscordService discord
) : IHostedService, IAutoRegisteredService
{
    private static readonly DateTimeOffset StartedAt = Process
        .GetCurrentProcess()
        .StartTime.ToUniversalTime();

    private string? Token => configuration["DASHBOARD_TOKEN"];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            logger.LogWarning("No DASHBOARD_TOKEN set, dashboard API is disabled");
            return Task.CompletedTask;
        }

        listener.MapRoute("/api/status", Authorized(_ => Json(Status())));
        listener.MapRoute(
            "/api/activity",
            Authorized(req => Json(activity.Recent(Limit(req, 50))))
        );
        listener.MapRoute("/api/logs", Authorized(req => Json(logs.Recent(Limit(req, 200)))));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private object Status()
    {
        bool connected = discord.Client.AllShardsConnected;
        discord.Client.Guilds.TryGetValue(discord.Options.MainGuildId, out DiscordGuild? guild);

        return new
        {
            Connected = connected,
            StartedAt,
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
            Guild = guild is null
                ? null
                : new
                {
                    guild.Name,
                    guild.MemberCount,
                    RoleCount = guild.Roles.Count,
                },
            CheckedIn = botState
                .Members.Select(entry => new
                {
                    Name = entry.member.DisplayName,
                    Since = entry.time,
                })
                .ToList(),
            Health = new
            {
                activity.LastNotionWebhookAt,
                activity.LastRoleSyncAt,
                activity.LastErrorAt,
                activity.LastError,
            },
        };
    }

    private static int Limit(HttpListenerRequest request, int fallback)
    {
        return int.TryParse(request.QueryString["limit"], out int limit) && limit > 0
            ? Math.Min(limit, 500)
            : fallback;
    }

    private RouteResponse Json(object payload)
    {
        return new RouteResponse(
            HttpStatusCode.OK,
            "application/json",
            JsonConvert.SerializeObject(payload)
        );
    }

    /// <summary>
    /// Wraps a handler so it only runs for callers presenting the right token.
    /// </summary>
    private Func<HttpListenerRequest, RouteResponse> Authorized(
        Func<HttpListenerRequest, RouteResponse> handler
    )
    {
        return request =>
            IsAuthorized(request)
                ? handler(request)
                : new RouteResponse(HttpStatusCode.Unauthorized, "application/json", "{}");
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        string? provided = request.Headers["X-Dashboard-Token"];
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(Token))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(Token)
        );
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddInjectableHostedService<DashboardApi>();
    }
}
