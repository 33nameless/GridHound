using System;
using System.Collections.Generic;

namespace ObjectDetectTrackApp.Services
{
    /// <summary>At most one request per uninterrupted loss; input clocks are source seconds.</summary>
    public sealed class AutoCorrectionRequests
    {
        private readonly HashSet<Guid> _suppressed=new();
        public bool Observe(Guid key, bool tracked, double observed, double confirmed)
        {
            if(tracked) { _suppressed.Remove(key); return false; }
            if(!double.IsFinite(observed) || !double.IsFinite(confirmed) || observed-confirmed<1.0)
                return false;
            return _suppressed.Add(key);
        }
        public void Suppress(Guid key) => _suppressed.Add(key);
        public void Clear() => _suppressed.Clear();
    }
}
