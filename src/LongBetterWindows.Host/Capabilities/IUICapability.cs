using System.Windows;
using LongBetterWindows.Host.Core;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    /// <summary>
    /// UI 能力——允许插件创建自定义窗口和对话框
    /// </summary>
    public interface IUICapability
    {
        /// <summary>
        /// 创建自定义窗口
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="htmlContent">HTML 内容（支持 CSS + JS）</param>
        /// <param name="width">窗口宽度</param>
        /// <param name="height">窗口高度</param>
        /// <param name="resizable">是否可调整大小</param>
        /// <returns>窗口 ID（用于后续操作）</returns>
        Task<HostApiResponse<string>> CreateWindowAsync(
            string title,
            string htmlContent,
            int width = 600,
            int height = 400,
            bool resizable = true);

        /// <summary>
        /// 显示确认对话框
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <param name="title">对话框标题</param>
        /// <returns>用户是否点击确认</returns>
        Task<HostApiResponse<bool>> ConfirmAsync(string message, string title = "确认");

        /// <summary>
        /// 显示输入对话框
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <param name="title">对话框标题</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>用户输入的文本（取消返回 null）</returns>
        Task<HostApiResponse<string?>> PromptAsync(string message, string title = "输入", string defaultValue = "");

        /// <summary>
        /// 显示选择对话框
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <param name="options">选项列表</param>
        /// <param name="title">对话框标题</param>
        /// <returns>用户选择的索引（取消返回 -1）</returns>
        Task<HostApiResponse<int>> SelectAsync(string message, string[] options, string title = "选择");

        /// <summary>
        /// 关闭指定窗口
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        Task<HostApiResponse> CloseWindowAsync(string windowId);

        /// <summary>
        /// 向指定窗口发送消息
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <param name="message">消息内容（JSON）</param>
        Task<HostApiResponse> SendMessageToWindowAsync(string windowId, string message);
    }
}
