using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal class Monitoring
    {
        private static List<Task> tasks = new List<Task>();

        public static void Run(Func<Task> action, TimeSpan delay, CancellationToken token, Func<Exception, Task> catchCallback = null)
        {
            var task = Task.Delay(delay).ContinueWith(async _ =>
            {
                while (true)
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        await catchCallback(ex);
                    }

                    await Task.Delay(delay, token);
                }
            }, token);

            tasks.Add(task);
        }
    }
}
