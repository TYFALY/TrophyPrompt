using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace TrophyPrompt.Controls
{
    public class Ps3WaveControl : Control
    {
        private double _phase;
        private readonly DispatcherTimer _timer;

        // Render runs 60 times a second; parsing colors and allocating three
        // brushes per frame was pure GC churn. The background is relative to
        // the bounds, so one instance serves every frame.
        private static readonly IBrush FrontWaveBrush = new ImmutableSolidColorBrush(Color.Parse("#28FFFFFF"));
        private static readonly IBrush BackWaveBrush = new ImmutableSolidColorBrush(Color.Parse("#14FFFFFF"));
        private readonly RadialGradientBrush _background = CreateBackground();

        public Ps3WaveControl()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0);
            _timer.Tick += (s, e) =>
            {
                _phase += 0.03;
                InvalidateVisual();
            };
            // Tick only while on screen: the timer used to start in the
            // constructor and, once detached, never restarted on re-attach.
            AttachedToVisualTree += (s, e) => _timer.Start();
            DetachedFromVisualTree += (s, e) => _timer.Stop();
        }

        private static RadialGradientBrush CreateBackground()
        {
            // Deep radial-ish background: layered gradients (#1A2436 -> #080A10).
            RadialGradientBrush bg = new RadialGradientBrush();
            bg.Center = new RelativePoint(0.5, 0.35, RelativeUnit.Relative);
            bg.GradientOrigin = new RelativePoint(0.5, 0.35, RelativeUnit.Relative);
            bg.RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative);
            bg.RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative);
            bg.GradientStops.Add(new GradientStop(Color.Parse("#1A2436"), 0.0));
            bg.GradientStops.Add(new GradientStop(Color.Parse("#080A10"), 1.0));
            return bg;
        }

        public override void Render(DrawingContext context)
        {
            Rect bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            context.FillRectangle(_background, bounds);

            // Dual sinusoidal translucent waves (#28FFFFFF, #14FFFFFF).
            DrawWave(context, bounds, _phase, 0.55, 26.0, FrontWaveBrush);
            DrawWave(context, bounds, -_phase * 0.7 + 1.3, 0.68, 34.0, BackWaveBrush);
        }

        private void DrawWave(DrawingContext context, Rect bounds, double phase, double yBase, double amplitude, IBrush brush)
        {
            StreamGeometry geom = new StreamGeometry();
            using (StreamGeometryContext ctx = geom.Open())
            {
                double w = bounds.Width;
                double h = bounds.Height;
                double baseY = h * yBase;
                ctx.BeginFigure(new Point(0, baseY + Math.Sin(phase) * amplitude), true);
                for (double x = 0; x <= w; x += 6)
                {
                    double y = baseY
                        + Math.Sin(x / w * Math.PI * 2.0 + phase) * amplitude
                        + Math.Sin(x / w * Math.PI * 4.0 - phase * 0.6) * (amplitude * 0.35);
                    ctx.LineTo(new Point(x, y));
                }
                ctx.LineTo(new Point(w, h));
                ctx.LineTo(new Point(0, h));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geom);
        }
    }
}
