namespace ObjectDetectTrackApp.Services
{
    /// <summary>Owns only a pause introduced by this correction, never the user's transport state.</summary>
    public sealed class CorrectionPauseSession
    {
        private bool _ownsPause;
        private int _session;
        private string _source=string.Empty;
        public bool Begin(bool enabled, bool video, bool playing, bool paused, int session, string source)
        {
            Cancel();
            _ownsPause=enabled && video && playing && !paused;
            _session=session;_source=source;
            return _ownsPause;
        }
        public bool Finish(bool playing, bool paused, int session, string source)
        {
            bool resume=_ownsPause && playing && paused && session==_session &&
                string.Equals(source,_source,System.StringComparison.OrdinalIgnoreCase);
            Cancel();
            return resume;
        }
        public void Cancel() { _ownsPause=false;_source=string.Empty; }
    }
}
