using System.Windows;
using System.Windows.Media;

namespace Parvathi.Windows.Views;

public sealed class Waveform : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(Waveform), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(Waveform), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(Waveform), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var brush = new SolidColorBrush(Active ? Color.FromRgb(97, 223, 229) : Color.FromRgb(62, 105, 114)); brush.Freeze();
        const int bars = 35; const double width = 4, gap = 5;
        double start = (ActualWidth - bars * (width + gap)) / 2;
        for (int i = 0; i < bars; i++) {
            double shape = ReducedMotion ? 0.65 : 0.35 + 0.65 * Math.Abs(Math.Sin(i * 0.72));
            double height = Active ? 5 + Math.Clamp(Level, 0, 1) * Math.Max(0, ActualHeight - 12) * shape : 4 + i % 5 * 2;
            dc.DrawRoundedRectangle(brush, null, new Rect(start + i * (width + gap), (ActualHeight - height) / 2, width, height), 2, 2);
        }
    }
}
