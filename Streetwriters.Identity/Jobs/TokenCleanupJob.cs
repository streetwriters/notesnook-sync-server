using System.Threading.Tasks;
using Quartz;
using Streetwriters.Identity.Services;

namespace Streetwriters.Identity.Jobs
{
    public class TokenCleanupJob : IJob
    {
        private TokenCleanup TokenCleanup { get; set; }
        public TokenCleanupJob(TokenCleanup tokenCleanup)
        {
            TokenCleanup = tokenCleanup;
        }

        public Task Execute(IJobExecutionContext context)
        {
            return TokenCleanup.RemoveExpiredGrantsAsync();
        }
    }
}