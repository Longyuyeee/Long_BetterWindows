using System;
using System.Windows;

namespace LongBetterWindows.Host.Helpers;

/// <summary>
/// 响应式布局辅助类
/// </summary>
public static class ResponsiveHelper
{
    public enum ScreenSize
    {
        Small,   // < 800px
        Medium,  // 800px - 1200px
        Large    // >= 1200px
    }

    /// <summary>
    /// 获取当前屏幕尺寸类型
    /// </summary>
    public static ScreenSize GetScreenSize(double width)
    {
        if (width < 800)
            return ScreenSize.Small;
        if (width < 1200)
            return ScreenSize.Medium;
        return ScreenSize.Large;
    }

    /// <summary>
    /// 获取市场网格列数
    /// </summary>
    public static int GetMarketGridColumns(double width)
    {
        return GetScreenSize(width) switch
        {
            ScreenSize.Small => 1,
            ScreenSize.Medium => 2,
            ScreenSize.Large => 3,
            _ => 3
        };
    }

    /// <summary>
    /// 获取响应式字体大小
    /// </summary>
    public static double GetResponsiveFontSize(double width, double baseFontSize)
    {
        return GetScreenSize(width) switch
        {
            ScreenSize.Small => baseFontSize * 0.9,
            ScreenSize.Medium => baseFontSize,
            ScreenSize.Large => baseFontSize * 1.1,
            _ => baseFontSize
        };
    }

    /// <summary>
    /// 获取响应式间距
    /// </summary>
    public static Thickness GetResponsiveMargin(double width, Thickness baseMargin)
    {
        var factor = GetScreenSize(width) switch
        {
            ScreenSize.Small => 0.7,
            ScreenSize.Medium => 1.0,
            ScreenSize.Large => 1.0,
            _ => 1.0
        };

        return new Thickness(
            baseMargin.Left * factor,
            baseMargin.Top * factor,
            baseMargin.Right * factor,
            baseMargin.Bottom * factor
        );
    }
}
