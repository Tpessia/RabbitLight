using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RabbitLight.Helpers
{
    internal class Monitoring
    {
        private static List<(Guid, Task, CancellationToken)> _tasks = new List<(Guid, Task, CancellationToken)>();

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

            _tasks.Add((Guid.NewGuid(), task, token));
        }

        public static void Stop(Guid guid)
        {
            var task = _tasks.SingleOrDefault(x => x.Item1 == guid);
            if (task != default)
            {
                CancellationTokenSource source = new CancellationTokenSource();
                task.Item3 = source.Token;
                source.Cancel();
                _tasks.Remove(task);
            }
        }
    }
}
