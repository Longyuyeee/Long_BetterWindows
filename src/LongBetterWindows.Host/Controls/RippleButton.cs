using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace LongBetterWindows.Host.Controls;

/// <summary>
/// 涟漪效果按钮
/// </summary>
public class RippleButton : Button
{
    private Ellipse? _ripple;
    private Canvas? _rippleCanvas;

    public RippleButton()
    {
        // 设置按钮样式
        Template = CreateTemplate();
        Cursor = Cursors.Hand;
    }

    private ControlTemplate CreateTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        factory.SetValue(Border.ClipToBoundsProperty, true);

        var grid = new FrameworkElementFactory(typeof(Grid));

        // 内容层
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(contentPresenter);

        // 涟漪层
        var canvas = new FrameworkElementFactory(typeof(Canvas));
        canvas.SetValue(FrameworkElement.NameProperty, "RippleCanvas");
        canvas.SetValue(Panel.IsHitTestVisibleProperty, false);
        grid.AppendChild(canvas);

        factory.AppendChild(grid);

        var template = new ControlTemplate(typeof(RippleButton)) { VisualTree = factory };
        return template;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (Template.FindName("RippleCanvas", this) is Canvas canvas)
        {
            _rippleCanvas = canvas;
            var position = e.GetPosition(this);
            CreateRipple(position);
        }

        // 按钮按下动画
        AnimateButtonPress();
    }

    private void CreateRipple(Point position)
    {
        if (_rippleCanvas == null) return;

        var maxSize = Math.Max(ActualWidth, ActualHeight) * 2;

        _ripple = new Ellipse
        {
            Width = 0,
            Height = 0,
            Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        Canvas.SetLeft(_ripple, position.X);
        Canvas.SetTop(_ripple, position.Y);

        _rippleCanvas.Children.Add(_ripple);

        // 涟漪扩散动画
        var expandAnimation = new DoubleAnimation
        {
            From = 0,
            To = maxSize,
            Duration = TimeSpan.FromMilliseconds(600),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var fadeAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(600),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        expandAnimation.Completed += (s, e) =>
        {
            _rippleCanvas.Children.Remove(_ripple);
        };

        _ripple.BeginAnimation(WidthProperty, expandAnimation);
        _ripple.BeginAnimation(HeightProperty, expandAnimation);
        _ripple.BeginAnimation(OpacityProperty, fadeAnimation);

        // 更新位置使其居中
        var translateTransform = new TranslateTransform(-maxSize / 2, -maxSize / 2);
        _ripple.RenderTransform = translateTransform;
    }

    private void AnimateButtonPress()
    {
        var scaleTransform = RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        RenderTransform = scaleTransform;
        RenderTransformOrigin = new Point(0.5, 0.5);

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
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDown);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDown);

        scaleDown.Completed += (s, e) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUp);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUp);
        };
    }
}
