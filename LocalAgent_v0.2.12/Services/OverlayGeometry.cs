using System;
using System.Drawing;
using System.Linq;

namespace VisionQC.LocalAgent.Services
{
    internal static class OverlayGeometry
    {
        // WPF/ViDi Matrix maps (x,y) to (x*M11+y*M21+OffsetX, x*M12+y*M22+OffsetY).
        internal static PointF[] Corners(double[] v)
        {
            if (v == null || v.Length != 8 || v.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || v[6] <= 0 || v[7] <= 0) return null;
            if (Math.Abs(v[0] * v[3] - v[1] * v[2]) < 1e-12) return null;
            double[] coordinates = { v[4], v[5], v[4] + v[6] * v[0], v[5] + v[6] * v[1], v[4] + v[7] * v[2], v[5] + v[7] * v[3] };
            if (coordinates.Any(x => double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > 1e8)) return null;
            return new[] { new PointF((float)coordinates[0], (float)coordinates[1]), new PointF((float)coordinates[2], (float)coordinates[3]), new PointF((float)coordinates[4], (float)coordinates[5]) };
        }
    }
}
