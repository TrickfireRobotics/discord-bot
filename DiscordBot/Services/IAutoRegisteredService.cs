using Microsoft.Extensions.Hosting;

namespace DiscordBot.Services;

public interface IAutoRegisteredService
{
    /// <summary>
    /// A method that's called at the start of the program. This method should
    /// register the implemented service.
    /// </summary>
    /// <param name="builder">The program builder for the program</param>
    public static abstract void Register(IHostApplicationBuilder builder);
}
