using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Services;

public class WebhookListener(
    ILogger<WebhookListener> logger,
    IOptions<WebhookListenerOptions> options
) : BackgroundService, IAutoRegisteredService
{
    public event Action<HttpListenerRequest>? OnWebhookReceived;

    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, Func<HttpListenerRequest, RouteResponse>> _routes = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Registers a handler that answers requests for the given path itself,
    /// instead of being broadcast to <see cref="OnWebhookReceived"/>.
    /// </summary>
    public void MapRoute(string path, Func<HttpListenerRequest, RouteResponse> handler)
    {
        _routes[path] = handler;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (string prefix in options.Value.Prefixes)
        {
            _listener.Prefixes.Add(prefix);
        }
        _listener.Start();

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        _listener.Stop();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for request
                HttpListenerContext ctx;
                ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken);

                string path = ctx.Request.Url?.AbsolutePath ?? "";
                RouteResponse response;
                if (_routes.TryGetValue(path, out var handler))
                {
                    response = handler(ctx.Request);
                }
                else
                {
                    // Read request body
                    OnWebhookReceived?.Invoke(ctx.Request);
                    response = new RouteResponse(HttpStatusCode.OK, "text/plain", "Ok");
                }

                ctx.Response.StatusCode = (int)response.StatusCode;
                ctx.Response.ContentType = response.ContentType;
                ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes(response.Body));
                ctx.Response.OutputStream.Close();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in WebhookListener main loop: ");
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        GC.SuppressFinalize(this);
        _listener.Close();
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            // I'm going to assume most people can't listen on every ip on their
            // dev machine, nor receive data from trickfirediscordbot.fly.dev
            // So just initialize it as a singleton, which doesn't receive start
            // events
            builder.Services.AddSingleton<WebhookListener>();
            return;
        }

        builder
            .Services.AddInjectableHostedService<WebhookListener>()
            .ConfigureTypeSection<WebhookListenerOptions>(builder.Configuration);
    }
}

/// <summary>
/// What a <see cref="WebhookListener"/> route sends back to the caller.
/// </summary>
public record RouteResponse(HttpStatusCode StatusCode, string ContentType, string Body);

public class WebhookListenerOptions()
{
    public string[] Prefixes { get; set; } = ["http://*:8080/"];
}
