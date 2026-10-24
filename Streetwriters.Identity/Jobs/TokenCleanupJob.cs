using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Jobs
{
    /// <summary>Removes expired grants every 12 hours, from a minute after startup.</summary>
    public class TokenCleanupJob(IServiceProvider services, ILogger<TokenCleanupJob> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
            do
            {
                try
                {
                    await using var scope = services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<TokenCleanup>().RemoveExpiredGrantsAsync();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Failed to remove expired grants");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
