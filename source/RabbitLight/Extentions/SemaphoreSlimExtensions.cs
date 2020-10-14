using System;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Extensions
{
    internal static class SemaphoreSlimExtensions
    {
        public static void WaitOrThrow(this SemaphoreSlim semaphore, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var success = semaphore.Wait(timeout, cancellationToken);
            if (!success) throw new TimeoutException("SemaphoreSlim timed out");
        }

        public static async Task WaitOrThrowAsync(this SemaphoreSlim semaphore, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var success = await semaphore.WaitAsync(timeout, cancellationToken);
            if (!success) throw new TimeoutException("SemaphoreSlim timed out");
        }
    }
}
