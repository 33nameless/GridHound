namespace ObjectDetectTrackApp.Services
{
    /// <summary>Uniform Image coordinates (before LayoutTransform) to source pixels.</summary>
    public static class PreviewCoordinates
    {
        public static bool TryMapUniform(double x, double y, double viewWidth, double viewHeight,
                                         int pixelWidth, int pixelHeight, out double px, out double py)
        {
            px = py = 0;
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(viewWidth) ||
                !double.IsFinite(viewHeight) || viewWidth <= 0 || viewHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
                return false;
            double scale = System.Math.Min(viewWidth / pixelWidth, viewHeight / pixelHeight);
            double ox = (viewWidth - pixelWidth * scale) / 2;
            double oy = (viewHeight - pixelHeight * scale) / 2;
            px = (x - ox) / scale;
            py = (y - oy) / scale;
            return px >= 0 && py >= 0 && px < pixelWidth && py < pixelHeight;
        }
    }
}
