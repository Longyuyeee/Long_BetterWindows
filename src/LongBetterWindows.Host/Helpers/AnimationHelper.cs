using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LongBetterWindows.Host.Helpers
{
    /// <summary>
    /// 常用动画辅助方法 — 统一 Apple 风格弹性/缓动曲线
    /// </summary>
    public static class AnimationHelper
    {
        private static TimeSpan Motion(string key, int fallbackMs)
            => Application.Current?.Resources[key] is Duration token
                ? token.TimeSpan
                : TimeSpan.FromMilliseconds(fallbackMs);

        /// <summary>窗口淡入 (Opacity 0→1)</summary>
        public static void FadeIn(Window window, int durationMs = 300)
        {
            var duration = Motion("Long.Motion.Normal", durationMs);
            if (duration == TimeSpan.Zero)
            {
                window.BeginAnimation(UIElement.OpacityProperty, null);
                window.Opacity = 1;
                return;
            }

            window.Opacity = 0;
            var anim = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            anim.Completed += (_, _) => window.Opacity = 1;
            window.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>窗口淡出并关闭 (Opacity 1→0)</summary>
        public static void FadeOut(Window window, int durationMs = 200)
        {
            var duration = Motion("Long.Motion.Fast", durationMs);
            if (duration == TimeSpan.Zero)
            {
                window.Close();
                return;
            }

            var anim = new DoubleAnimation(1, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) => window.Close();
            window.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>弹性缩放弹入 (Scale 0.85→1)</summary>
        public static void ScaleBounce(FrameworkElement element,
            double from = 0.85, double to = 1.0, int durationMs = 400)
        {
            var duration = Motion("Long.Motion.Slow", durationMs);
            if (duration == TimeSpan.Zero)
            {
                element.RenderTransform = Transform.Identity;
                return;
            }

            var scale = new ScaleTransform(from, from);
            element.RenderTransform = scale;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            var animX = new DoubleAnimation(from, to, duration)
            {
                EasingFunction = new ElasticEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Oscillations = 2,
                    Springiness = 5
                }
            };
            var animY = new DoubleAnimation(from, to, duration)
            {
                EasingFunction = new ElasticEase
                {
                    EasingMode = EasingMode.EaseOut,
                    Oscillations = 2,
                    Springiness = 5
                }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
        }

        /// <summary>元素淡入 (Opacity 0→1)</summary>
        public static void FadeInElement(FrameworkElement element, int durationMs = 250)
        {
            var duration = Motion("Long.Motion.Normal", durationMs);
            if (duration == TimeSpan.Zero)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1;
                return;
            }

            element.Opacity = 0;
            var anim = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>从上方滑入 (TranslateTransform Y) + 淡入</summary>
        public static void SlideDown(FrameworkElement element,
            double fromY = -20, double toY = 0, int durationMs = 350)
        {
            var duration = Motion("Long.Motion.Normal", durationMs);
            if (duration == TimeSpan.Zero)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.RenderTransform = Transform.Identity;
                element.Opacity = 1;
                return;
            }

            var translate = new TranslateTransform(0, fromY);
            element.RenderTransform = translate;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.Opacity = 0;

            var slideAnim = new DoubleAnimation(fromY, toY, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeAnim = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            translate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }
    }
}
