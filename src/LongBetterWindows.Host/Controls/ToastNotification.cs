using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LongBetterWindows.Host.Controls;

/// <summary>
/// 增强版 Toast 通知组件
/// </summary>
public class ToastNotification : Border
{
    private DispatcherTimer? _timer;
    private bool _isPaused;
    private readonly int _duration;

    public enum ToastType
    {
        Success,
        Info,
        Warning,
        Error
    }

    public ToastNotification(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        _duration = durationMs;

        // 基础样式
        Width = 360;
        MinHeight = 60;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(16, 12, 16, 12);
        Margin = new Thickness(0, 0, 0, 12);
        HorizontalAlignment = HorizontalAlignment.Center;

        // 毛玻璃背景
        Background = new SolidColorBrush(Color.FromArgb(230, 30, 41, 59));
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 24,
            ShadowDepth = 8,
            Opacity = 0.3
        };

        // 内容
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 图标
        var icon = GetIcon(type);
        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 20,
            Foreground = GetIconBrush(type),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(iconText, 0);
        grid.Children.Add(iconText);

        // 消息文本
        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(messageText, 1);
        grid.Children.Add(messageText);

        Child = grid;

        // 鼠标事件
        MouseEnter += (s, e) => PauseTimer();
        MouseLeave += (s, e) => ResumeTimer();

        // 启动定时器
        StartTimer();
    }

    private string GetIcon(ToastType type) => type switch
    {
        ToastType.Success => "✓",
        ToastType.Info => "ⓘ",
        ToastType.Warning => "⚠",
        ToastType.Error => "✕",
        _ => "ⓘ"
    };

    private Brush GetIconBrush(ToastType type) => type switch
    {
        ToastType.Success => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
        ToastType.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
        ToastType.Warning => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
        ToastType.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
        _ => new SolidColorBrush(Color.FromRgb(59, 130, 246))
    };

    private void StartTimer()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_duration)
        };
        _timer.Tick += (s, e) =>
        {
            _timer?.Stop();
            Close();
        };
        _timer.Start();
    }

    private void PauseTimer()
    {
        if (_timer != null && !_isPaused)
        {
            _timer.Stop();
            _isPaused = true;
        }
    }

    private void ResumeTimer()
    {
        if (_timer != null && _isPaused)
        {
            _timer.Start();
            _isPaused = false;
        }
    }

    public void Close()
    {
        _timer?.Stop();

        var slideOut = new DoubleAnimation
        {
            To = -100,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        slideOut.Completed += (s, e) =>
        {
            if (Parent is Panel panel)
            {
                panel.Children.Remove(this);
            }
        };

        var transform = new TranslateTransform();
        RenderTransform = transform;

        transform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// 显示 Toast 通知
    /// </summary>
    public static void Show(Panel container, string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        var toast = new ToastNotification(message, type, durationMs);

        // 添加到容器
        container.Children.Add(toast);

        // 从顶部滑入动画
        var transform = new TranslateTransform(0, -100);
        toast.RenderTransform = transform;
        toast.Opacity = 0;

        var slideIn = new DoubleAnimation
        {
            From = -100,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }
        };

        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        toast.BeginAnimation(OpacityProperty, fadeIn);
    }
}
