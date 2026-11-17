using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.Metadata;
using DSharpPlus.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Discord;

/// <summary>
/// Commands for the TrickFire dashboard's health monitor. Kept separate from
/// the member-facing commands in <see cref="Commands"/>.
/// </summary>
[Command("dashboard")]
[Description("Commands for the TrickFire dashboard's health monitor")]
public static class DashboardCommands
{
    [Command("ping")]
    [Description("Check that the dashboard health monitor itself is responding")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    public static async Task Ping(SlashCommandContext context)
    {
        DashboardCommandsOptions options = Options(context);
        if (!InAlertChannel(context, options))
        {
            await RejectChannel(context, options);
            return;
        }

        await context.DeferResponseAsync();

        Stopwatch stopwatch = Stopwatch.StartNew();
        string? reason = null;
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
            HttpResponseMessage res = await http.GetAsync(options.HealthUrl);
            string body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode || !body.Contains("ok"))
            {
                reason = $"HTTP {(int)res.StatusCode} - {body[..Math.Min(200, body.Length)]}";
            }
        }
        catch (Exception ex)
        {
            reason = ex.Message;
        }
        stopwatch.Stop();

        string content = reason is null
            ? $"Health checker is fine. Dashboard responded ok in {stopwatch.ElapsedMilliseconds}ms."
            : $"Health checker ran, but the dashboard looks unhealthy: `{reason}` ({stopwatch.ElapsedMilliseconds}ms)";

        await context.RespondAsync(content);
    }

    [Command("test")]
    [Description("Send a test alert to verify formatting and the role ping")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    public static async Task Test(SlashCommandContext context)
    {
        DashboardCommandsOptions options = Options(context);
        if (!InAlertChannel(context, options))
        {
            await RejectChannel(context, options);
            return;
        }

        DiscordEmbedBuilder embed = new DiscordEmbedBuilder()
            .WithTitle("[TEST] Dashboard is down")
            .WithColor(new DiscordColor("e53935"))
            .AddField("URL", options.HealthUrl, false)
            .AddField("Reason", "`this is a test alert, triggered by /dashboard test`", false)
            .AddField("Time (UTC)", DateTimeOffset.UtcNow.ToString("o"), false)
            .WithFooter("TrickFire Dashboard Monitor")
            .WithTimestamp(DateTimeOffset.UtcNow);

        DiscordMessageBuilder message = new DiscordMessageBuilder()
            .WithContent($"<@&{options.PingRoleId}>")
            .AddEmbed(embed.Build())
            .AddMention(new RoleMention(options.PingRoleId));

        await context.RespondAsync(message);
    }

    [Command("maintenance")]
    [Description("Pause downtime alerts for a maintenance window")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    public static async Task Maintenance(
        SlashCommandContext context,
        [Parameter("minutes")]
        [Description("How long to pause alerts for, in minutes (default 60)")]
            int minutes = 60
    )
    {
        DashboardCommandsOptions options = Options(context);
        if (!InAlertChannel(context, options))
        {
            await RejectChannel(context, options);
            return;
        }

        if (minutes < 1 || minutes > 1440)
        {
            await context.RespondAsync("Minutes must be between 1 and 1440.", true);
            return;
        }

        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        DateTimeOffset until = DateTimeOffset.UtcNow.AddMinutes(minutes);

        string? token = context.ServiceProvider.GetRequiredService<IConfiguration>()[
            "DASHBOARD_TOKEN"
        ];
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    token
                );
            }
            await http.PostAsJsonAsync($"{options.MonitorUrl}/maintenance", new { minutes });
        }
        catch (Exception ex)
        {
            await context.RespondAsync(
                $"Couldn't reach the health monitor to pause alerts: `{ex.Message}`",
                true
            );
            return;
        }

        state.MaintenanceUntil = until;
        state.Save();

        await context.RespondAsync(
            $"Alerts paused for {minutes} minute{(minutes == 1 ? "" : "s")}, until <t:{until.ToUnixTimeSeconds()}:f>."
        );
    }

    private static DashboardCommandsOptions Options(SlashCommandContext context) =>
        context.ServiceProvider.GetRequiredService<IOptions<DashboardCommandsOptions>>().Value;

    private static bool InAlertChannel(
        SlashCommandContext context,
        DashboardCommandsOptions options
    ) => context.Channel.Id == options.AlertChannelId;

    private static ValueTask RejectChannel(
        SlashCommandContext context,
        DashboardCommandsOptions options
    ) =>
        context.RespondAsync(
            $"This command can only be used in <#{options.AlertChannelId}>.",
            true
        );
}

public class DashboardCommandsOptions
{
    /// <summary>
    /// The only channel these commands may be used in.
    /// </summary>
    public ulong AlertChannelId { get; set; } = 0;

    /// <summary>
    /// The role pinged on a downtime alert.
    /// </summary>
    public ulong PingRoleId { get; set; } = 0;

    /// <summary>
    /// The dashboard's health check endpoint.
    /// </summary>
    public string HealthUrl { get; set; } = "";

    /// <summary>
    /// The base URL of the Cloudflare Worker that runs the periodic health
    /// check and posts downtime alerts.
    /// </summary>
    public string MonitorUrl { get; set; } = "";
}
