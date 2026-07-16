using System.Timers;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;
using Timer = System.Timers.Timer;

namespace LongBetterWindows.Host.Services
{
    public class ScheduleService : IScheduleService
    {
        private readonly Dictionary<string, ScheduleTask> _tasks = new();
        private readonly Dictionary<string, Timer> _timers = new();

        public Task<HostApiResponse<string>> CreateTaskAsync(ScheduleTask task)
        {
            return Task.Run(() =>
            {
                try
                {
                    task.Id = Guid.NewGuid().ToString("N");
                    task.CreatedAt = DateTime.Now;
                    task.Enabled = true;

                    _tasks[task.Id] = task;

                    if (task.TriggerType == "interval" && task.IntervalMinutes.HasValue)
                    {
                        SetupIntervalTimer(task);
                    }
                    else if (task.TriggerTime.HasValue)
                    {
                        SetupOnceTimer(task);
                    }

                    return HostApiResponse<string>.Success(task.Id);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> DeleteTaskAsync(string taskId)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_timers.ContainsKey(taskId))
                    {
                        _timers[taskId].Stop();
                        _timers[taskId].Dispose();
                        _timers.Remove(taskId);
                    }

                    _tasks.Remove(taskId);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse<List<ScheduleTask>>> GetAllTasksAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var tasks = _tasks.Values.ToList();
                    return HostApiResponse<List<ScheduleTask>>.Success(tasks);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<List<ScheduleTask>>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetTaskEnabledAsync(string taskId, bool enabled)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!_tasks.ContainsKey(taskId))
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "任务不存在");

                    _tasks[taskId].Enabled = enabled;

                    if (_timers.ContainsKey(taskId))
                    {
                        if (enabled)
                            _timers[taskId].Start();
                        else
                            _timers[taskId].Stop();
                    }

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> RunTaskNowAsync(string taskId)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!_tasks.ContainsKey(taskId))
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "任务不存在");

                    ExecuteTask(_tasks[taskId]);
                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        private void SetupIntervalTimer(ScheduleTask task)
        {
            if (!task.IntervalMinutes.HasValue) return;

            var timer = new Timer(task.IntervalMinutes.Value * 60 * 1000);
            timer.Elapsed += (sender, e) => ExecuteTask(task);
            timer.AutoReset = true;
            timer.Start();

            _timers[task.Id] = timer;
            task.NextRunAt = DateTime.Now.AddMinutes(task.IntervalMinutes.Value);
        }

        private void SetupOnceTimer(ScheduleTask task)
        {
            if (!task.TriggerTime.HasValue) return;

            var delay = (task.TriggerTime.Value - DateTime.Now).TotalMilliseconds;
            if (delay <= 0) return;

            var timer = new Timer(delay);
            timer.Elapsed += (sender, e) =>
            {
                ExecuteTask(task);
                timer.Stop();
                timer.Dispose();
                _timers.Remove(task.Id);
            };
            timer.AutoReset = false;
            timer.Start();

            _timers[task.Id] = timer;
            task.NextRunAt = task.TriggerTime.Value;
        }

        private void ExecuteTask(ScheduleTask task)
        {
            if (!task.Enabled) return;

            task.LastRunAt = DateTime.Now;

            try
            {
                switch (task.ActionType)
                {
                    case "command":
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c {task.ActionData}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        break;

                    case "notification":
                        // 触发通知
                        break;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "执行定时任务失败: {TaskId}", task.Id);
            }
        }
    }
}
