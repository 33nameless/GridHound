namespace ObjectDetectTrackApp.Services
{
    /// <summary>Prefer source PTS; if the backend repeats/omits it, use elapsed source frames / FPS.</summary>
    public sealed class SourceTiming
    {
        private double _previous = double.NaN;
        private int _frame = -1;
        public void Reset() { _previous=double.NaN; _frame=-1; }
        public double ResolveMilliseconds(int frame, double pts, double fps)
        {
            if(frame<=_frame) Reset(); // explicit seek/reopen, not elapsed playback
            bool valid=double.IsFinite(pts) && pts>=0 &&
                (!double.IsFinite(_previous) || pts>_previous);
            if(!valid || (frame>0 && pts==0))
            {
                if(!double.IsFinite(fps) || fps<=0) return double.NaN;
                pts=double.IsFinite(_previous) ? _previous+(frame-_frame)*1000.0/fps : frame*1000.0/fps;
            }
            _previous=pts;_frame=frame;
            return pts;
        }
    }
}
