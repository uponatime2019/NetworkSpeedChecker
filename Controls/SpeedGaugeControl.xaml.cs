using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;

namespace NetworkSpeedChecker.Controls
{
    /// <summary>Circular speedometer gauge with a 240° arc driven by live test progress.</summary>
    public sealed partial class SpeedGaugeControl : UserControl
    {
        private const double StartAngle = 150.0;   // degrees, screen coords (y down)
        private const double TotalSweep = 240.0;
        private const double Center = 185.0;
        private const double Radius = 146.0;

        public SpeedGaugeControl()
        {
            InitializeComponent();
            Loaded += (_, _) => BuildArcs(0);
            SizeChanged += (_, _) => BuildArcs(_lastPercent);
        }

        private double _lastPercent;

        private static Point Polar(double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(Center + Radius * Math.Cos(rad), Center + Radius * Math.Sin(rad));
        }

        private static PathFigure BuildFigure(double sweepDegrees)
        {
            var start = Polar(StartAngle);
            var figure = new PathFigure { StartPoint = start };
            if (sweepDegrees > 0.05)
            {
                var endAngle = StartAngle + sweepDegrees;
                var segment = new ArcSegment
                {
                    Point = Polar(endAngle),
                    Size = new Size(Radius, Radius),
                    IsLargeArc = sweepDegrees > 180,
                    SweepDirection = SweepDirection.Clockwise
                };
                figure.Segments.Add(segment);
            }
            else
            {
                // Tiny stub keeps round line caps visible even at ~0 progress.
                figure.Segments.Add(new LineSegment { Point = new Point(start.X + 0.01, start.Y) });
            }
            return figure;
        }

        private void BuildArcs(double percent)
        {
            _lastPercent = percent = Math.Clamp(percent, 0, 100);

            // Each Path needs its own Geometry instance — a geometry cannot be shared.
            TrackPath.Data = BuildGeometry(TotalSweep);
            HaloPath.Data = BuildGeometry(TotalSweep);
            ProgressPath.Data = BuildGeometry(TotalSweep * percent / 100.0);
            ProgressPath.Opacity = percent > 0.2 ? 1 : 0.35;
        }

        private static PathGeometry BuildGeometry(double sweepDegrees)
        {
            var geometry = new PathGeometry();
            geometry.Figures.Add(BuildFigure(sweepDegrees));
            return geometry;
        }

        /// <summary>Gauge with no active test (or before the first byte arrives).</summary>
        public void ShowIdle(string phase, string value, string unit, string sub)
        {
            PhaseText.Text = phase;
            ValueText.Text = value;
            UnitText.Text = unit;
            SubText.Text = sub;
            BuildArcs(0);
        }

        /// <summary>Live phase update. Rate is displayed with one decimal.</summary>
        public void ShowPhase(string phase, double rate, string unit, double percent, string sub)
        {
            PhaseText.Text = phase;
            ValueText.Text = rate >= 100 ? rate.ToString("0") : rate.ToString("0.0");
            UnitText.Text = unit;
            SubText.Text = sub;
            BuildArcs(percent);
        }

        /// <summary>Completed state: full ring, final headline number.</summary>
        public void ShowComplete(double rate, string unit, string sub)
        {
            PhaseText.Text = "COMPLETE";
            ValueText.Text = rate >= 100 ? rate.ToString("0") : rate.ToString("0.0");
            UnitText.Text = unit;
            SubText.Text = sub;
            BuildArcs(100);
        }
    }
}
