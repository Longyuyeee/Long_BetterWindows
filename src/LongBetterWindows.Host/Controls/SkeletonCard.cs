using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Controls;

/// <summary>
/// 骨架屏加载组件
/// </summary>
public class SkeletonCard : Border
{
    public SkeletonCard()
    {
        Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        CornerRadius = new CornerRadius(10);
        Margin = new Thickness(0, 0, 0, 12);
        Padding = new Thickness(16);
        Height = 120;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 顶部状态条
        var statusBar = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -16, 0, 0)
        };
        grid.Children.Add(statusBar);

        var content = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        // 标题行
        var titleBar = new Border
        {
            Width = 180,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(titleBar);

        // 描述行
        var descBar = new Border
        {
            Width = 240,
            Height = 12,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        content.Children.Add(descBar);

        // 标签行
        var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < 3; i++)
        {
            tagsPanel.Children.Add(new Border
            {
                Width = 50,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 0, 8, 0)
            });
        }
        content.Children.Add(tagsPanel);

        grid.Children.Add(content);
        Child = grid;

        // 呼吸动画
        StartPulseAnimation();
    }

    private void StartPulseAnimation()
    {
        var animation = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(1500),
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        BeginAnimation(OpacityProperty, animation);
    }
}
