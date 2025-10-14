using System;
using System.IO;
using System.Threading.Tasks;
using Quartz;

namespace Notesnook.API.Jobs
{
    public class DeviceCleanupJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = 100,
                CancellationToken = context.CancellationToken,
            };
            Parallel.ForEach(Directory.EnumerateDirectories("sync"), parallelOptions, (userDir, ct) =>
            {
                foreach (var device in Directory.EnumerateDirectories(userDir))
                {
                    string lastAccessFile = Path.Combine(device, "LastAccessTime");

                    try
                    {
                        if (!File.Exists(lastAccessFile))
                        {
                            Directory.Delete(device, true);
                            continue;
                        }

                        string content = File.ReadAllText(lastAccessFile);
                        if (!long.TryParse(content, out long lastAccessTime) || lastAccessTime <= 0)
                        {
                            Directory.Delete(device, true);
                            continue;
                        }

                        DateTimeOffset accessTime;
                        try
                        {
                            accessTime = DateTimeOffset.FromUnixTimeMilliseconds(lastAccessTime);
                        }
                        catch (Exception)
                        {
                            Directory.Delete(device, true);
                            continue;
                        }

                        // If the device hasn't been accessed for more than one month, delete it.
                        if (accessTime.AddMonths(1) < DateTimeOffset.UtcNow)
                        {
                            Directory.Delete(device, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log the error and continue processing other directories.
                        Console.Error.WriteLine($"Error processing device '{device}': {ex.Message}");
                    }
                }
            });
            return Task.CompletedTask;
        }
    }
}