using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Helpers;

/// <summary>
/// 增强版动画辅助类 - 流畅动画系统
/// </summary>
public static class AnimationHelperEnhanced
{
    /// <summary>
    /// 从底部滑入 + 淡入动画
    /// </summary>
    public static void SlideInFromBottom(FrameworkElement element, int durationMs = 300, int delayMs = 0)
    {
        element.RenderTransform = new TranslateTransform(0, 30);
        element.Opacity = 0;

        var slideAnimation = new DoubleAnimation
        {
            From = 30,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        element.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
        element.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
    }

    /// <summary>
    /// 淡出 + 轻微缩小动画
    /// </summary>
    public static void FadeOutAndShrink(FrameworkElement element, int durationMs = 200, Action? onComplete = null)
    {
        var scaleTransform = new ScaleTransform(1, 1);
        element.RenderTransform = scaleTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0.95,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        if (onComplete != null)
        {
            fadeAnimation.Completed += (s, e) => onComplete();
        }

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        element.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
    }

    /// <summary>
    /// 按钮点击缩放动画
    /// </summary>
    public static void ButtonPressAnimation(FrameworkElement element)
    {
        var scaleTransform = element.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        element.RenderTransform = scaleTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleDown = new DoubleAnimation
        {
            To = 0.95,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleUp = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 3 }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);

        scaleDown.Completed += (s, e) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        };
    }

    /// <summary>
    /// 悬停放大动画
    /// </summary>
    public static void HoverScaleUp(FrameworkElement element, double scale = 1.05, int durationMs = 200)
    {
        var scaleTransform = element.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        element.RenderTransform = scaleTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleAnimation = new DoubleAnimation
        {
            To = scale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    /// <summary>
    /// 悬停结束恢复
    /// </summary>
    public static void HoverScaleDown(FrameworkElement element, int durationMs = 200)
    {
        var scaleTransform = element.RenderTransform as ScaleTransform;
        if (scaleTransform == null) return;

        var scaleAnimation = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
    }

    /// <summary>
    /// 页面左右滑动切换
    /// </summary>
    public static void SlidePageTransition(FrameworkElement oldPage, FrameworkElement newPage, bool slideLeft = true, int durationMs = 300)
    {
        var oldTransform = new TranslateTransform(0, 0);
        var newTransform = new TranslateTransform(slideLeft ? oldPage.ActualWidth : -oldPage.ActualWidth, 0);

        oldPage.RenderTransform = oldTransform;
        newPage.RenderTransform = newTransform;
        newPage.Visibility = Visibility.Visible;

        // 旧页面滑出
        var oldAnimation = new DoubleAnimation
        {
            To = slideLeft ? -oldPage.ActualWidth : oldPage.ActualWidth,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        // 新页面滑入
        var newAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        oldAnimation.Completed += (s, e) => oldPage.Visibility = Visibility.Collapsed;

        oldTransform.BeginAnimation(TranslateTransform.XProperty, oldAnimation);
        newTransform.BeginAnimation(TranslateTransform.XProperty, newAnimation);
    }

    /// <summary>
    /// 抖动动画（错误反馈）
    /// </summary>
    public static void ShakeAnimation(FrameworkElement element)
    {
        var translateTransform = new TranslateTransform(0, 0);
        element.RenderTransform = translateTransform;

        var shake = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(400)
        };

        shake.KeyFrames.Add(new LinearDoubleKeyFrame(10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
        shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));

        translateTransform.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    /// <summary>
    /// 旋转加载动画
    /// </summary>
    public static void RotateLoadingAnimation(FrameworkElement element)
    {
        var rotateTransform = new RotateTransform(0);
        element.RenderTransform = rotateTransform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);

        var rotateAnimation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(1000),
            RepeatBehavior = RepeatBehavior.Forever
        };

        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
    }

    /// <summary>
    /// 呼吸闪烁动画（骨架屏）
    /// </summary>
    public static void PulseAnimation(FrameworkElement element)
    {
        var opacityAnimation = new DoubleAnimation
        {
            From = 0.3,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(1500),
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
    }

    /// <summary>
    /// 列表项逐个延迟出现（Stagger）
    /// </summary>
    public static void StaggeredFadeIn(Panel parent, int itemDelayMs = 50)
    {
        int index = 0;
        foreach (UIElement child in parent.Children)
        {
            if (child is FrameworkElement element)
            {
                SlideInFromBottom(element, 300, index * itemDelayMs);
            }
            index++;
        }
    }
}
