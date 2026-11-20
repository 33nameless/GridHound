using System;
using System.Collections.Generic;
using System.Linq;
using ObjectDetectTrackApp.Models;

namespace ObjectDetectTrackApp.Services
{
    public class TrackerService
    {
        private readonly List<SortTrack> _activeTracks = new();
        private int _nextTrackId = 1;
        private double _lastTime = double.NaN;
        private readonly LogService _logService;
        public TrackerService(LogService logService) { _logService=logService; }

        // Compatibility for callers without source timestamps/thresholds (including still images).
        public List<Detection> Update(List<Detection> detections) =>
            Update(detections, float.NegativeInfinity, float.NegativeInfinity);

        /// <summary>Strong observations associate first; weak ones only continue unmatched tracks.</summary>
        public List<Detection> Update(List<Detection> detections, float highThreshold, float lowThreshold,
                                      double sourceSeconds = double.NaN)
        {
            bool timed=double.IsFinite(sourceSeconds);
            if(timed && double.IsFinite(_lastTime) && sourceSeconds<=_lastTime) return new();
            double dt=timed && double.IsFinite(_lastTime) ? sourceSeconds-_lastTime : (timed ? 1.0/25 : 1);
            if(timed) _lastTime=sourceSeconds;
            foreach(var d in detections) {d.TrackId=null;d.IsWeakContinuation=false;d.IsEdgeContinuation=false;}
            foreach(var t in _activeTracks)
            {
                t.MissedSeconds+=dt;
                t.Age++;
                t.Predict((float)Math.Min(dt,3));
            }
            _activeTracks.RemoveAll(t=>timed ? t.MissedSeconds>3.0 : t.Age>30);
            var strong=detections.Where(d=>d.Confidence>=highThreshold).ToList();
            var weak=detections.Where(d=>d.Confidence>=lowThreshold && d.Confidence<highThreshold).ToList();
            var available=_activeTracks.ToList();
            var result=new List<Detection>();
            Associate(strong,available,result,false);
            if(timed) AssociateBootstrap(strong,available,result,detections);
            if(timed) AssociateLocalStrong(strong,available,result,detections);
            if(timed) AssociateEdge(strong,available,result,detections,false);
            if(timed) AssociateEdge(weak,available,result,detections,true);
            Associate(weak,available,result,true);
            foreach(var d in strong.Where(d=>!d.TrackId.HasValue))
            {
                var t=new SortTrack {Id=_nextTrackId++,ClassName=d.ClassName,LastDetection=d,
                    ReferenceAppearance=d.Appearance,FullWidth=d.Width,FullHeight=d.Height,HitStreak=1};
                t.KalmanFilter.Init(d.X+d.Width/2f,d.Y+d.Height/2f,d.Width,d.Height,timed);
                d.TrackId=t.Id;_activeTracks.Add(t);result.Add(d);
            }
            return result;
        }

        private static void Associate(List<Detection> ds,List<SortTrack> tracks,List<Detection> result,bool weak)
        {
            ds=ds.Where(d=>!d.TrackId.HasValue).ToList();
            if(ds.Count==0 || tracks.Count==0) return;
            var costs=new float[ds.Count,tracks.Count];
            for(int i=0;i<ds.Count;i++) for(int j=0;j<tracks.Count;j++)
            {
                var d=ds[i];var t=tracks[j];var p=t.KalmanFilter.GetState();var old=t.LastDetection;
                float iou=SelectionLogic.Iou(d.X,d.Y,d.Width,d.Height,(int)(p.x-p.w/2),(int)(p.y-p.h/2),
                    Math.Max(1,(int)p.w),Math.Max(1,(int)p.h));
                float wr=(float)d.Width/Math.Max(1,old.Width),hr=(float)d.Height/Math.Max(1,old.Height);
                bool app=d.Appearance!=null && t.ReferenceAppearance!=null;
                float sim=SelectionLogic.AppearanceSimilarity(d.Appearance,t.ReferenceAppearance);
                bool allowed=string.Equals(d.ClassName,t.ClassName,StringComparison.OrdinalIgnoreCase) &&
                    wr>=0.55f && wr<=1.55f && hr>=0.55f && hr<=1.55f &&
                    iou>=(weak ? 0.20f : 0.25f) && (!app || sim>=(weak ? 0.75f : 0.60f));
                costs[i,j]=allowed ? .75f*(1-iou)+.25f*(app ? 1-sim : 1-iou) : 10f;
            }
            var assigned=new List<SortTrack>();
            foreach(var (i,j) in HungarianAlgorithm.FindMatches(costs,.8f))
            {
                var d=ds[i];var t=tracks[j];t.Update(d);d.TrackId=t.Id;d.IsWeakContinuation=weak;result.Add(d);assigned.Add(t);
            }
            foreach(var t in assigned) tracks.Remove(t);
        }
        // First sparse observations have no measured velocity yet. Resolve only an
        // isolated, reciprocal, strong match, then initialise velocity from real motion.
        private static void AssociateBootstrap(List<Detection> strong,List<SortTrack> tracks,
            List<Detection> result,List<Detection> observations)
        {
            var pairs=new List<(Detection Detection,SortTrack Track)>();
            foreach(var d in strong.Where(d=>!d.TrackId.HasValue)) foreach(var t in tracks) {
                var old=t.LastDetection;
                if(t.HitStreak<1 || t.HitStreak>=3 || t.MissedSeconds>.4 ||
                   t.ClassName!=d.ClassName || d.Appearance==null || old.Appearance==null ||
                   t.ReferenceAppearance==null || EdgeGeometry.Touches(d) || EdgeGeometry.Touches(old)) continue;
                float wr=(float)d.Width/Math.Max(1,old.Width),hr=(float)d.Height/Math.Max(1,old.Height);
                if(wr<.65f || wr>1.55f || hr<.65f || hr>1.55f ||
                   Math.Abs(d.X+d.Width/2f-old.X-old.Width/2f)>.8f*old.Width ||
                   Math.Abs(d.Y+d.Height/2f-old.Y-old.Height/2f)>.8f*old.Height ||
                   SelectionLogic.Iou(d.X,d.Y,d.Width,d.Height,old.X,old.Y,old.Width,old.Height)<.10f ||
                   SelectionLogic.AppearanceSimilarity(d.Appearance,old.Appearance)<.90f ||
                   SelectionLogic.AppearanceSimilarity(d.Appearance,t.ReferenceAppearance)<.90f ||
                   !EdgeGeometry.Isolated(d,observations,old.Width,old.Height)) continue;
                pairs.Add((d,t));
            }
            foreach(var pair in pairs) {
                if(pairs.Count(p=>ReferenceEquals(p.Detection,pair.Detection))!=1 ||
                   pairs.Count(p=>ReferenceEquals(p.Track,pair.Track))!=1) continue;
                var old=pair.Track.LastDetection;double elapsed=Math.Max(.001,pair.Track.MissedSeconds);
                float vx=(float)((pair.Detection.X+pair.Detection.Width/2f-old.X-old.Width/2f)/elapsed);
                float vy=(float)((pair.Detection.Y+pair.Detection.Height/2f-old.Y-old.Height/2f)/elapsed);
                vx=Math.Clamp(vx,-old.Width*8f,old.Width*8f);vy=Math.Clamp(vy,-old.Height*8f,old.Height*8f);
                pair.Track.Update(pair.Detection);
                pair.Track.KalmanFilter=new KalmanFilter();
                pair.Track.KalmanFilter.Init(pair.Detection.X+pair.Detection.Width/2f,
                    pair.Detection.Y+pair.Detection.Height/2f,pair.Detection.Width,pair.Detection.Height,true,vx,vy);
                pair.Detection.TrackId=pair.Track.Id;result.Add(pair.Detection);tracks.Remove(pair.Track);
            }
        }

        // A short, isolated turn can disagree with the velocity prediction while remaining
        // close to the last real observation. Never use this fallback for weak detections.
        private static void AssociateLocalStrong(List<Detection> strong,List<SortTrack> tracks,
            List<Detection> result,List<Detection> observations)
        {
            var pairs=new List<(Detection Detection,SortTrack Track)>();
            foreach(var d in strong.Where(d=>!d.TrackId.HasValue)) {
                bool isolated=!observations.Any(o=>!ReferenceEquals(o,d) && o.ClassName==d.ClassName &&
                    SelectionLogic.Iou(o.X,o.Y,o.Width,o.Height,d.X,d.Y,d.Width,d.Height)>.05f);
                if(!isolated) continue;
                foreach(var t in tracks) {
                    var old=t.LastDetection;
                    if(t.ClassName!=d.ClassName || t.HitStreak<3 || t.MissedSeconds>.5 ||
                       d.Appearance==null || old.Appearance==null || t.ReferenceAppearance==null) continue;
                    float wr=(float)d.Width/Math.Max(1,old.Width),hr=(float)d.Height/Math.Max(1,old.Height);
                    float iou=SelectionLogic.Iou(d.X,d.Y,d.Width,d.Height,old.X,old.Y,old.Width,old.Height);
                    if(wr<.6f || wr>1.55f || hr<.6f || hr>1.55f || iou<.20f ||
                       Math.Abs(d.X+d.Width/2f-old.X-old.Width/2f)>.8f*old.Width ||
                       Math.Abs(d.Y+d.Height/2f-old.Y-old.Height/2f)>.8f*old.Height ||
                       SelectionLogic.AppearanceSimilarity(d.Appearance,old.Appearance)<.90f ||
                       SelectionLogic.AppearanceSimilarity(d.Appearance,t.ReferenceAppearance)<.65f) continue;
                    if(result.Any(o=>o.ClassName==d.ClassName &&
                        SelectionLogic.Iou(o.X,o.Y,o.Width,o.Height,d.X,d.Y,d.Width,d.Height)>.05f)) continue;
                    pairs.Add((d,t));
                }
            }
            foreach(var pair in pairs) {
                if(pairs.Count(p=>ReferenceEquals(p.Detection,pair.Detection))!=1 ||
                   pairs.Count(p=>ReferenceEquals(p.Track,pair.Track))!=1) continue;
                var old=pair.Track.LastDetection;
                double elapsed=Math.Max(.001,pair.Track.MissedSeconds);
                float vx=(float)((pair.Detection.X+pair.Detection.Width/2f-old.X-old.Width/2f)/elapsed);
                float vy=(float)((pair.Detection.Y+pair.Detection.Height/2f-old.Y-old.Height/2f)/elapsed);
                vx=Math.Clamp(vx,-old.Width*8f,old.Width*8f);vy=Math.Clamp(vy,-old.Height*8f,old.Height*8f);
                pair.Track.Update(pair.Detection);
                // Replace the contradicted velocity with bounded measured local motion, not a new ID.
                pair.Track.KalmanFilter=new KalmanFilter();
                pair.Track.KalmanFilter.Init(pair.Detection.X+pair.Detection.Width/2f,
                    pair.Detection.Y+pair.Detection.Height/2f,pair.Detection.Width,pair.Detection.Height,true,vx,vy);
                pair.Detection.TrackId=pair.Track.Id;result.Add(pair.Detection);tracks.Remove(pair.Track);
            }
        }

        private static void AssociateEdge(List<Detection> candidates,List<SortTrack> tracks,
            List<Detection> result,List<Detection> observations,bool weak)
        {
            var pairs=new List<(Detection Detection,SortTrack Track)>();
            foreach(var d in candidates.Where(d=>!d.TrackId.HasValue)) foreach(var t in tracks) {
                if(t.HitStreak<3 || !EdgeGeometry.TryMatch(t.LastDetection,d,t.FullWidth,t.FullHeight,t.MissedSeconds,out _) ||
                   d.Appearance==null || t.ReferenceAppearance==null ||
                   SelectionLogic.AppearanceSimilarity(d.Appearance,t.EdgeAppearance ?? t.LastDetection.Appearance)<.85f ||
                   SelectionLogic.AppearanceSimilarity(d.Appearance,t.ReferenceAppearance)<.60f ||
                   !EdgeGeometry.Isolated(d,observations,t.FullWidth,t.FullHeight)) continue;
                pairs.Add((d,t));
            }
            foreach(var pair in pairs) {
                if(pairs.Count(p=>ReferenceEquals(p.Detection,pair.Detection))!=1 ||
                   pairs.Count(p=>ReferenceEquals(p.Track,pair.Track))!=1) continue;
                pair.Detection.IsEdgeContinuation=true;
                pair.Detection.IsWeakContinuation=weak;
                pair.Track.Update(pair.Detection);
                pair.Detection.TrackId=pair.Track.Id;
                // Visible centres are not the full object's physical centre. Do not seed
                // an artificial outward velocity from the changing clipped width/height.
                pair.Track.KalmanFilter=new KalmanFilter();
                pair.Track.KalmanFilter.Init(pair.Detection.X+pair.Detection.Width/2f,
                    pair.Detection.Y+pair.Detection.Height/2f,pair.Detection.Width,pair.Detection.Height,true);
                result.Add(pair.Detection);tracks.Remove(pair.Track);
            }
        }

        /// <summary>Explicit user correction refreshes the track's motion and appearance reference.</summary>
        public void Reanchor(Detection d)
        {
            var t=_activeTracks.FirstOrDefault(t=>t.Id==d.TrackId && t.ClassName==d.ClassName);
            if(t==null) return;
            t.LastDetection=d;t.ReferenceAppearance=d.Appearance;t.FullWidth=d.Width;t.FullHeight=d.Height;t.EdgeAppearance=null;t.Age=0;t.MissedSeconds=0;
            t.KalmanFilter=new KalmanFilter();
            t.KalmanFilter.Init(d.X+d.Width/2f,d.Y+d.Height/2f,d.Width,d.Height,double.IsFinite(d.SourceSeconds));
        }
        public int GetActiveTrackCount()=>_activeTracks.Count;
        public void Reset() { _activeTracks.Clear();_nextTrackId=1;_lastTime=double.NaN; }
    }

    public class SortTrack
    {
        public int Id {get;set;}
        public string ClassName {get;set;}
        public Detection LastDetection {get;set;}
        public float[] ReferenceAppearance {get;set;}
        public float[]? EdgeAppearance {get;set;}
        public int FullWidth {get;set;}
        public int FullHeight {get;set;}
        public int Age {get;set;}
        public double MissedSeconds {get;set;}
        public int HitStreak {get;set;}
        public KalmanFilter KalmanFilter {get;set;}=new();
        public void Predict(float dt=1)=>KalmanFilter.Predict(dt);
        public void Update(Detection detection)
        {
            if(EdgeGeometry.Touches(detection)) EdgeAppearance ??= LastDetection.Appearance;
            else {FullWidth=detection.Width;FullHeight=detection.Height;EdgeAppearance=null;}
            LastDetection=detection;HitStreak++;Age=0;MissedSeconds=0;
            KalmanFilter.Update(detection.X+detection.Width/2f,detection.Y+detection.Height/2f,
                detection.Width,detection.Height);
        }
    }
}
