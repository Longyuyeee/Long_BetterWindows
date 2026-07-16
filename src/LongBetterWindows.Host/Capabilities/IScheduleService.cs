using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IScheduleService
    {
        /// <summary>创建定时任务</summary>
        Task<HostApiResponse<string>> CreateTaskAsync(ScheduleTask task);

        /// <summary>删除定时任务</summary>
        Task<HostApiResponse> DeleteTaskAsync(string taskId);

        /// <summary>获取所有定时任务</summary>
        Task<HostApiResponse<List<ScheduleTask>>> GetAllTasksAsync();

        /// <summary>启用/禁用任务</summary>
        Task<HostApiResponse> SetTaskEnabledAsync(string taskId, bool enabled);

        /// <summary>立即执行任务</summary>
        Task<HostApiResponse> RunTaskNowAsync(string taskId);
    }

    public class ScheduleTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ActionType { get; set; } = ""; // "command", "script", "notification"
        public string ActionData { get; set; } = "";
        public string TriggerType { get; set; } = ""; // "once", "daily", "weekly", "interval"
        public DateTime? TriggerTime { get; set; }
        public int? IntervalMinutes { get; set; }
        public bool Enabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime? NextRunAt { get; set; }
    }
}
