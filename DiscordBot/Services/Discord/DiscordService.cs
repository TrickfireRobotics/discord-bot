using System.Reflection;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services.Discord;

public class DiscordService(DiscordClient client, IOptions<DiscordServiceOptions> options)
    : IHostedService,
        IAutoRegisteredService,
        IEventHandler<SessionCreatedEventArgs>
{
    /// <summary>
    /// The client associated with the bot.
    /// </summary>
    public DiscordClient Client { get; } = client;

    public DiscordServiceOptions Options { get; } = options.Value;

    /// <summary>
    /// Always resolves to whatever guild object DSharpPlus's own cache
    /// currently holds, instead of a fixed snapshot, so it stays correct
    /// across reconnects without fighting the library's cache lifecycle.
    /// </summary>
    public DiscordGuild MainGuild => Client.Guilds[Options.MainGuildId];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // This tells Discord we are using slash commands
        await Client.InitializeAsync();

        // Connect our bot to the Discord API
        await Client.ConnectAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Client.AllShardsConnected)
        {
            await Client.DisconnectAsync();
        }
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder
            .Services.AddDiscordClient(
                builder.Configuration["BOT_TOKEN"]!,
                DiscordIntents.GuildMembers | DiscordIntents.Guilds
            )
            .Configure<DiscordConfiguration>(builder.Configuration.GetSection("DiscordBotConfig"))
            .AddCommandsExtension(
                (_, extension) =>
                {
                    // Configure to slash commands
                    extension.AddProcessor(new SlashCommandProcessor());

                    // Add our commands from our code (anything with the command
                    // decorator)
                    extension.AddCommands(Assembly.GetExecutingAssembly());
                }
            )
            .ConfigureEventHandlers(events =>
            {
                Type[] types = Assembly.GetExecutingAssembly().GetTypes();
                List<Type> eventHandlers = types
                    .Where(t => t.IsAssignableTo(typeof(IEventHandler)))
                    .ToList();
                events.AddEventHandlers(eventHandlers);
            })
            .AddInjectableHostedService<DiscordService>()
            .ConfigureTypeSection<DiscordServiceOptions>(builder.Configuration);
    }

    public async Task HandleEventAsync(DiscordClient sender, SessionCreatedEventArgs eventArgs)
    {
        // Make sure CurrentMember is cached after (re)connecting. GetMemberAsync
        // populates the guild's own member cache as part of its normal,
        // supported behavior, so no manual cache poking is needed here.
        await MainGuild.GetMemberAsync(Client.CurrentUser.Id);

        sender.Logger.LogInformation("Set current member in cache");
    }
}

public class DiscordServiceOptions
{
    /// <summary>
    /// The id of the main discord guild of the bot.
    /// </summary>
    public ulong MainGuildId { get; set; } = 0;
}
