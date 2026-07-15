using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Services;

/// <summary>
/// 主题管理服务
/// </summary>
public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    private bool _isLightTheme = false;

    public bool IsLightTheme => _isLightTheme;

    private ThemeService()
    {
        // 从配置加载主题偏好
        LoadThemePreference();
    }

    /// <summary>
    /// 切换主题
    /// </summary>
    public void ToggleTheme()
    {
        _isLightTheme = !_isLightTheme;
        ApplyTheme(_isLightTheme);
        SaveThemePreference();
    }

    /// <summary>
    /// 应用主题
    /// </summary>
    public void ApplyTheme(bool isLight)
    {
        _isLightTheme = isLight;

        var resources = Application.Current.Resources;

        // 平滑过渡所有颜色
        AnimateColorTransition(resources, "SurfaceBackgroundBrush",
            isLight ? Color.FromRgb(248, 250, 252) : Color.FromRgb(30, 31, 34));

        AnimateColorTransition(resources, "CardBackgroundBrush",
            isLight ? Color.FromRgb(255, 255, 255) : Color.FromRgb(45, 45, 48));

        AnimateColorTransition(resources, "TextPrimaryBrush",
            isLight ? Color.FromRgb(15, 23, 42) : Color.FromRgb(232, 232, 232));

        AnimateColorTransition(resources, "TextSecondaryBrush",
            isLight ? Color.FromRgb(100, 116, 139) : Color.FromRgb(153, 153, 153));

        AnimateColorTransition(resources, "TitleBarBrush",
            isLight ? Color.FromRgb(255, 255, 255) : Color.FromRgb(45, 45, 48));

        AnimateColorTransition(resources, "DividerBrush",
            isLight ? Color.FromRgb(226, 232, 240) : Color.FromRgb(58, 58, 61));
    }

    private void AnimateColorTransition(ResourceDictionary resources, string brushKey, Color targetColor)
    {
        if (resources[brushKey] is SolidColorBrush brush)
        {
            var animation = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }
    }

    private void LoadThemePreference()
    {
        var storage = new StorageService();
        var result = storage.GetAsync("theme.isLight").Result;
        if (result.Success && result.Data != null && bool.TryParse(result.Data, out var isLight))
        {
            _isLightTheme = isLight;
        }
        ApplyTheme(_isLightTheme);
    }

    private void SaveThemePreference()
    {
        var storage = new StorageService();
        storage.SetAsync("theme.isLight", _isLightTheme.ToString()).Wait();
    }

    /// <summary>
    /// 根据系统主题自动切换
    /// </summary>
    public void ApplySystemTheme()
    {
        // TODO: 检测 Windows 系统主题
        // 当前简单实现：默认深色
        ApplyTheme(false);
    }
}
