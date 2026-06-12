namespace LongBetterWindows.Host.Core
{
    /// <summary>
    /// 插件提供主 UI 界面。
    /// 实现此接口后，ToolCenter 插件卡片显示「打开」按钮。
    /// </summary>
    public interface IHasMainUI
    {
        /// <summary>打开插件的主界面</summary>
        void ShowMainUI();
    }
}
