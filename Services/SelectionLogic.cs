using System;
using System.Collections.Generic;
using ObjectDetectTrackApp.Models;

namespace ObjectDetectTrackApp.Services
{
    /// <summary>
    /// Чистая логика ручного выбора объектов (клик по превью) и «захвата» выбора
    /// на последующих кадрах. Без WPF/OpenCV — чтобы тестировалась на любой платформе.
    /// </summary>
    public static class SelectionLogic
    {
        /// <summary>Порог IoU для повторного сопоставления выбранного объекта (без трека).</summary>
        public const float IouThresh = 0.30f;

        public enum SelectionState { Tracked, TemporarilyLost, Ambiguous, AwaitingReselection }
        private static int _nextNumber;
        public static string StateText(SelectionState state) => state switch {
            SelectionState.Tracked => "сопровождается",
            SelectionState.TemporarilyLost => "временно потерян",
            SelectionState.Ambiguous => "неоднозначность",
            _ => "нужен повторный выбор"
        };

        public sealed class SelectionItem
        {
            public Guid Key = Guid.NewGuid();
            public int Number = System.Threading.Interlocked.Increment(ref _nextNumber);
            public string ClassName;
            public int X, Y, W, H;
            public int FrameWidth,FrameHeight,FullWidth,FullHeight;
            public float[]? EdgeAppearance;
            public int LastTrackId = -1;
            public bool LastMatched = true;
            public SelectionState State = SelectionState.Tracked;
            public int AnchorFrame;
            public int AnchorX, AnchorY, AnchorW, AnchorH;
            public float[] Appearance;
            public float[] RecentAppearance;
            public List<float[]> ConfirmedAppearances = new();
            public int LastObservedFrame = -1, LastConfirmedFrame = -1;
            public double LastConfirmedClock=double.NaN, LastObservedClock=double.NaN;
            public double VelocityX, VelocityY;
            public bool HasVelocity;
            public int PendingTrackId = -1, PendingCount, PendingFrame = -1;
            public bool PendingBorderlineUsed;
            public int PendingX, PendingY, PendingW, PendingH;
            public double PendingClock=double.NaN, PendingVelocityX, PendingVelocityY;
            public HashSet<int> KnownOtherIds = new();
            public string Summary => $"S{Number} · {ClassName}: {StateText(State)}";

            public static SelectionItem From(Detection d)
            {
                var item = new SelectionItem();
                Rebind(item, d);
                return item;
            }

            public SelectionItem Clone()
            {
                var copy = (SelectionItem)MemberwiseClone();
                copy.KnownOtherIds = new HashSet<int>(KnownOtherIds);
                copy.ConfirmedAppearances = new List<float[]>(ConfirmedAppearances);
                return copy;
            }
        }

        /// <summary>Explicit user correction; preserves the user's number and key.</summary>
        public static void Rebind(SelectionItem it, Detection d)
        {
            if(it.ClassName!=null && it.ClassName!=d.ClassName) it.ConfirmedAppearances.Clear();
            if(d.Appearance!=null) {
                var sample=(float[])d.Appearance.Clone();
                if(it.ConfirmedAppearances.Count==0 || it.ConfirmedAppearances.TrueForAll(a=>AppearanceSimilarity(a,sample)<.995f))
                    it.ConfirmedAppearances.Add(sample);
                if(it.ConfirmedAppearances.Count>8) it.ConfirmedAppearances.RemoveAt(1);
            }
            it.FrameWidth=d.FrameWidth;it.FrameHeight=d.FrameHeight;
            it.FullWidth=d.Width;it.FullHeight=d.Height;it.EdgeAppearance=null;
            it.ClassName=d.ClassName; it.X=d.X; it.Y=d.Y; it.W=d.Width; it.H=d.Height;
            it.AnchorX=d.X; it.AnchorY=d.Y; it.AnchorW=d.Width; it.AnchorH=d.Height;
            it.AnchorFrame=d.FrameIndex; it.Appearance=d.Appearance; it.RecentAppearance=d.Appearance;
            it.LastTrackId=d.TrackId ?? -1; it.LastMatched=true; it.State=SelectionState.Tracked;
            it.LastObservedFrame=-1; it.LastConfirmedFrame=d.FrameIndex;
            it.LastConfirmedClock=double.IsFinite(d.SourceSeconds) ? d.SourceSeconds : d.FrameIndex;
            it.LastObservedClock=double.NaN;it.VelocityX=it.VelocityY=0;it.HasVelocity=false;
            it.PendingCount=0; it.PendingBorderlineUsed=false; it.PendingTrackId=-1; it.PendingFrame=-1;
            it.PendingClock=double.NaN;it.PendingVelocityX=it.PendingVelocityY=0;
            it.KnownOtherIds.Clear();
        }

        public static void StartNewTrackingEpoch(SelectionItem it, int frame)
        {
            it.EdgeAppearance=null;
            it.LastTrackId=-1; it.LastMatched=false; it.State=SelectionState.TemporarilyLost;
            it.LastObservedFrame=-1; it.LastConfirmedFrame=frame;
            it.LastConfirmedClock=double.NaN;it.LastObservedClock=double.NaN;
            it.VelocityX=it.VelocityY=0;it.HasVelocity=false;
            it.PendingCount=0; it.PendingBorderlineUsed=false; it.PendingTrackId=-1; it.PendingFrame=-1;
            it.PendingClock=double.NaN;it.PendingVelocityX=it.PendingVelocityY=0;
            it.KnownOtherIds.Clear();
        }

        public static void ResetForReplay(SelectionItem it)
        {
            it.RecentAppearance=it.Appearance;it.EdgeAppearance=null;
            it.FullWidth=it.AnchorW;it.FullHeight=it.AnchorH;
            it.X=it.AnchorX; it.Y=it.AnchorY; it.W=it.AnchorW; it.H=it.AnchorH;
            it.LastTrackId=-1; it.LastMatched=true; it.State=SelectionState.Tracked;
            it.LastObservedFrame=-1; it.LastConfirmedFrame=it.AnchorFrame;
            it.LastConfirmedClock=double.NaN;it.LastObservedClock=double.NaN;
            it.VelocityX=it.VelocityY=0;it.HasVelocity=false;
            it.PendingCount=0; it.PendingBorderlineUsed=false; it.PendingTrackId=-1; it.PendingFrame=-1;
            it.PendingClock=double.NaN;it.PendingVelocityX=it.PendingVelocityY=0;
            it.KnownOtherIds.Clear();
        }

        /// <summary>Луч-кастинг: poly — плоский массив пар x,y. Полигон «дырявый» не важен — внешние границы.</summary>
        public static bool PointInPolygon(float[] poly, double px, double py)
        {
            if (poly == null || poly.Length < 6) return false;
            bool inside = false;
            int n = poly.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[2 * i], yi = poly[2 * i + 1];
                double xj = poly[2 * j], yj = poly[2 * j + 1];
                if (((yi > py) != (yj > py)) &&
                    (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Хит-тест детекции: сначала маска (полигон), иначе — бокс.</summary>
        public static bool HitDetection(Detection d, double px, double py)
        {
            if (d == null) return false;
            if (d.MaskPolygon != null && PointInPolygon(d.MaskPolygon, px, py)) return true;
            return px >= d.X && px <= d.X + d.Width && py >= d.Y && py <= d.Y + d.Height;
        }

        private static bool InPoly(Detection d, double px, double py) =>
            d.MaskPolygon != null && PointInPolygon(d.MaskPolygon, px, py);

        /// <summary>
        /// Индекс объекта под точкой. Приоритет: (1) курсор внутри полигона маски,
        /// (2) меньшая площадь бокса (верхний объект), (3) выше confidence. -1 — мимо.
        /// </summary>
        public static int HitTest(IReadOnlyList<Detection> dets, double px, double py)
        {
            if (dets == null) return -1;
            int best = -1;
            int bestScore = 2;
            float bestArea = float.MaxValue;
            float bestConf = -1f;
            for (int i = 0; i < dets.Count; i++)
            {
                var d = dets[i];
                if (!HitDetection(d, px, py)) continue;
                int score = InPoly(d, px, py) ? 0 : 1;
                float area = (float)Math.Max(1, d.Width) * Math.Max(1, d.Height);
                bool better = best == -1
                    || score < bestScore
                    || (score == bestScore && area < bestArea)
                    || (score == bestScore && Math.Abs(area - bestArea) < 1e-6f && d.Confidence > bestConf);
                if (better) { best = i; bestScore = score; bestArea = area; bestConf = d.Confidence; }
            }
            return best;
        }

        public static float Iou(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            long ix = Math.Max(0, Math.Min(ax + aw, bx + bw) - Math.Max(ax, bx));
            long iy = Math.Max(0, Math.Min(ay + ah, by + bh) - Math.Max(ay, by));
            long inter = ix * iy;
            long uni = (long)aw * ah + (long)bw * bh - inter;
            return uni > 0 ? (float)(inter / (double)uni) : 0f;
        }

        /// <summary>Тот же ли это объект, что и сохранённый выбор: класс + (TrackId или IoU).</summary>
        public static bool SameItem(Detection d, SelectionItem it)
        {
            if (it == null || d == null) return false;
            if (!string.Equals(d.ClassName, it.ClassName, StringComparison.OrdinalIgnoreCase)) return false;
            if (it.LastTrackId > 0 && d.TrackId is int id && id > 0)
                return id == it.LastTrackId;
            return Iou(d.X, d.Y, d.Width, d.Height, it.X, it.Y, it.W, it.H) >= IouThresh;
        }

        /// <summary>
        /// Пометить IsSelected те детекции, что совпали с выбором; обновить в items
        /// бокс/TrackId (захват на ходу). Один item = максимум одна детекция за кадр.
        /// </summary>
        public static int Apply(IReadOnlyList<Detection> dets, IList<SelectionItem> items,
                                bool activeReacquisition = false, bool advance = true, int observationFrame = -1, double sourceSeconds = double.NaN)
        {
            if (dets == null || items == null) return 0;
            foreach (var d in dets) { d.IsSelected=false; d.SelectionKey=null; d.SelectionNumber=null; }
            // Repaint/re-analysis must neither add evidence nor change the last accepted identity.
            if (!advance)
            {
                int visible=0;
                foreach (var it in items)
                    if (it.LastMatched && it.State==SelectionState.Tracked)
                        foreach (var d in dets)
                            if (!d.IsSelected && SameItem(d,it) &&
                                Iou(d.X,d.Y,d.Width,d.Height,it.X,it.Y,it.W,it.H)>=0.5f)
                            { Tag(d,it); visible++; break; }
                return visible;
            }
            int frame=observationFrame;
            if(frame<0) foreach(var d in dets) frame=Math.Max(frame,d.FrameIndex);
            if(!double.IsFinite(sourceSeconds))
                foreach(var d in dets) if(double.IsFinite(d.SourceSeconds)) {sourceSeconds=d.SourceSeconds;break;}
            bool timed=double.IsFinite(sourceSeconds);
            double clock=timed ? sourceSeconds : frame;
            var proposed=new int[items.Count];
            Array.Fill(proposed,-1);
            var fresh=new bool[items.Count];
            var borderline=new bool[items.Count];
            var edgeContinuations=new bool[items.Count];
            var shortRecoveries=new bool[items.Count];
            for(int i=0;i<items.Count;i++)
            {
                var it=items[i];
                // Empty observations are losses; repeated nonempty frames are not new evidence.
                if(frame>=0 && frame<=it.LastObservedFrame) continue;
                fresh[i]=true;
                if(frame>=0) it.LastObservedFrame=frame;
                if(timed && double.IsFinite(it.LastObservedClock) && clock<=it.LastObservedClock) {fresh[i]=false;continue;}
                if(clock>=0) it.LastObservedClock=clock;
                if(!double.IsFinite(it.LastConfirmedClock)) it.LastConfirmedClock=clock;
                bool wasTracked=it.LastMatched && it.State==SelectionState.Tracked;
                it.LastMatched=false;
                if(it.State==SelectionState.AwaitingReselection) continue;
                double gap=Math.Max(0,clock-it.LastConfirmedClock);
                if(clock>=0 && gap>(timed ? 3.0 : 90))
                { it.State=SelectionState.AwaitingReselection; it.PendingCount=0; continue; }
                // IDs already observed alongside the selected track are neighbours, not a new identity.
                Detection originalTrack=null;
                foreach(var d in dets) if(d.TrackId==it.LastTrackId && d.ClassName==it.ClassName) originalTrack=d;
                if(wasTracked && it.LastTrackId>0 && originalTrack!=null)
                    foreach(var d in dets)
                        if(d.TrackId is int id && id!=it.LastTrackId && d.ClassName==it.ClassName &&
                           Iou(d.X,d.Y,d.Width,d.Height,originalTrack.X,originalTrack.Y,originalTrack.Width,originalTrack.Height)<.05f)
                            it.KnownOtherIds.Add(id);
                double horizon=Math.Min(gap,timed ? 1.0 : 30);
                int predictedX=it.X+(int)Math.Round(it.VelocityX*horizon);
                int predictedY=it.Y+(int)Math.Round(it.VelocityY*horizon);
                int best=-1; bool bestBorderline=false,bestEdge=false,bestShort=false; float bestScore=-1, second=-1;
                for(int k=0;k<dets.Count;k++)
                {
                    var d=dets[k];
                    if(!string.Equals(d.ClassName,it.ClassName,StringComparison.OrdinalIgnoreCase)) continue;
                    bool same=it.LastTrackId>0 && d.TrackId==it.LastTrackId;
                    bool ownedByOther=false;
                    foreach(var owner in items)
                        if(owner.Key!=it.Key && owner.LastTrackId>0 && d.TrackId==owner.LastTrackId) ownedByOther=true;
                    if(ownedByOther) continue;
                    if(!same && d.TrackId is int tid && it.KnownOtherIds.Contains(tid)) continue;
                    int searchX=predictedX, searchY=predictedY;
                    if(it.PendingCount>0 && (d.TrackId ?? -1)==it.PendingTrackId && double.IsFinite(it.PendingClock))
                    {
                        double pendingDt=Math.Clamp(clock-it.PendingClock,0,timed ? .5 : 15);
                        int localX=it.PendingX+(int)Math.Round(it.PendingVelocityX*pendingDt);
                        int localY=it.PendingY+(int)Math.Round(it.PendingVelocityY*pendingDt);
                        if(Math.Abs(localX-predictedX)<=it.W*2 && Math.Abs(localY-predictedY)<=it.H*2)
                        {searchX=localX;searchY=localY;}
                    }
                    float iou=Iou(d.X,d.Y,d.Width,d.Height,searchX,searchY,it.W,it.H);
                    float wr=(float)d.Width/Math.Max(1,it.W), hr=(float)d.Height/Math.Max(1,it.H);
                    float dx=Math.Abs((d.X+d.Width/2f)-(searchX+it.W/2f))/Math.Max(1,it.W);
                    float dy=Math.Abs((d.Y+d.Height/2f)-(searchY+it.H/2f))/Math.Max(1,it.H);
                    var previous=new Detection {ClassName=it.ClassName,X=it.X,Y=it.Y,Width=it.W,Height=it.H,
                        FrameWidth=it.FrameWidth,FrameHeight=it.FrameHeight};
                    bool edge=same && timed && EdgeGeometry.TryMatch(previous,d,it.FullWidth,it.FullHeight,gap,out _) &&
                        EdgeGeometry.Isolated(d,dets,it.FullWidth,it.FullHeight) &&
                        d.Appearance!=null && AppearanceSimilarity(d.Appearance,it.EdgeAppearance ?? it.RecentAppearance ?? it.Appearance)>=.85f;
                    // Normal geometry is unchanged away from a verified border continuation.
                    if(!edge && (wr<0.60f || wr>1.55f || hr<0.60f || hr>1.55f || dx>0.8f || dy>0.8f)) continue;
                    bool appearance=d.Appearance!=null && it.Appearance!=null;
                    float originalSim=AppearanceSimilarity(d.Appearance,it.Appearance);
                    float sim=AppearanceSimilarity(d.Appearance,it.RecentAppearance ?? it.Appearance);
                    // Only explicit user confirmations supply alternative appearance anchors.
                    foreach(var reference in it.ConfirmedAppearances) {
                        float confirmed=AppearanceSimilarity(d.Appearance,reference);
                        originalSim=Math.Max(originalSim,confirmed);
                        sim=Math.Max(sim,confirmed);
                    }
                    bool initialising=same && timed && !it.HasVelocity &&
                        it.LastConfirmedFrame==it.AnchorFrame && gap>0 && gap<=.4 &&
                        !d.IsWeakContinuation && appearance && originalSim>=.90f && sim>=.90f &&
                        wr>=.65f && hr>=.65f && !EdgeGeometry.Touches(d) &&
                        EdgeGeometry.Isolated(d,dets,it.W,it.H);
                    float requiredIou=same ? (initialising ? .10f : .15f) : (activeReacquisition ? .30f : .40f);
                    if(!edge && iou<requiredIou) continue;
                    float requiredSim=same ? .75f : (activeReacquisition ? .88f : .92f);
                    bool marginal=false;
                    if(appearance && originalSim<.60f) continue;
                    if(appearance && sim<requiredSim) {
                        // One near-threshold observation may hold (never advance/finish) an
                        // already established recovery sequence. A second miss resets it.
                        marginal=!same && !d.IsWeakContinuation && it.PendingCount>=2 &&
                            !it.PendingBorderlineUsed && d.TrackId==it.PendingTrackId &&
                            clock-it.PendingClock>0 && clock-it.PendingClock<=(timed ? .5 : 15) &&
                            sim>=requiredSim-.015f &&
                            Iou(d.X,d.Y,d.Width,d.Height,it.PendingX,it.PendingY,it.PendingW,it.PendingH)>=.30f;
                        if(!marginal) continue;
                    }
                    if(!same && it.LastTrackId>0 && !appearance) continue;
                    bool shortRecovery=same && timed && gap>0 && gap<=.5 &&
                        iou>=.45f && wr>=.75f && wr<=1.33f && hr>=.75f && hr<=1.33f &&
                        appearance && originalSim>=.65f && sim>=.90f &&
                        EdgeGeometry.Isolated(d,dets,it.W,it.H);
                    float score=0.65f*iou+0.35f*(appearance ? sim : iou)+(same ? 0.30f : 0);
                    if(score>bestScore) {second=bestScore;bestScore=score;best=k;bestBorderline=marginal;bestEdge=edge;bestShort=shortRecovery;}
                    else second=Math.Max(second,score);
                }
                if(best<0)
                { it.State=SelectionState.TemporarilyLost; it.PendingCount=0; it.PendingFrame=-1; continue; }
                if(second>=0 && bestScore-second<0.12f)
                { it.State=SelectionState.Ambiguous; it.PendingCount=0; it.PendingFrame=-1; continue; }
                proposed[i]=best;
                borderline[i]=bestBorderline;
                edgeContinuations[i]=bestEdge;
                shortRecoveries[i]=bestShort;
            }
            int matched=0;
            for(int i=0;i<items.Count;i++)
            {
                var it=items[i];
                if(!fresh[i])
                {
                    // Repeated frames can display the accepted observation, never confirm recovery.
                    if(it.LastMatched && it.State==SelectionState.Tracked)
                        foreach(var d in dets)
                            if(!d.IsSelected && SameItem(d,it) && Iou(d.X,d.Y,d.Width,d.Height,it.X,it.Y,it.W,it.H)>=0.5f)
                            {Tag(d,it);matched++;break;}
                    continue;
                }
                int k=proposed[i]; if(k<0) continue;
                bool contested=false;
                for(int j=0;j<items.Count;j++) if(j!=i && proposed[j]==k) contested=true;
                if(contested) {it.State=SelectionState.Ambiguous;it.PendingCount=0;continue;}
                var candidate=dets[k];
                bool continuing=(it.State==SelectionState.Tracked || edgeContinuations[i]) &&
                    (it.LastTrackId<=0 || candidate.TrackId==it.LastTrackId);
                if(!continuing)
                {
                    int id=candidate.TrackId ?? -1;
                    int previousCount=it.PendingCount;
                    bool consistent=it.PendingCount>0 && it.PendingTrackId==id &&
                        Iou(candidate.X,candidate.Y,candidate.Width,candidate.Height,
                            it.PendingX,it.PendingY,it.PendingW,it.PendingH)>=0.30f;
                    double pendingElapsed=clock-it.PendingClock;
                    if(consistent && pendingElapsed>0)
                    {
                        it.PendingVelocityX=(candidate.X-it.PendingX)/pendingElapsed;
                        it.PendingVelocityY=(candidate.Y-it.PendingY)/pendingElapsed;
                    }
                    else {it.PendingVelocityX=it.PendingVelocityY=0;}
                    if(!consistent) it.PendingBorderlineUsed=false;
                    it.PendingClock=clock;
                    it.PendingCount=consistent ? it.PendingCount+1 : 1;
                    it.PendingTrackId=id; it.PendingFrame=frame;
                    it.PendingX=candidate.X;it.PendingY=candidate.Y;it.PendingW=candidate.Width;it.PendingH=candidate.Height;
                    it.State=SelectionState.Ambiguous;
                    if(borderline[i]) {
                        it.PendingCount=previousCount;
                        it.PendingBorderlineUsed=true;
                        continue;
                    }
                    // Two independent observations only for a tightly verified short gap
                    // of the SAME ID. New IDs and uncertain candidates keep normal policy.
                    int required=shortRecoveries[i] ? 2 : (activeReacquisition ? 3 : 4);
                    if(it.PendingCount<required) continue;
                }
                Tag(candidate,it);
                double elapsed=clock-it.LastConfirmedClock;
                if(elapsed>0 && elapsed<=(timed ? 1.0 : 30))
                {
                    double vx=(candidate.X+candidate.Width/2.0-it.X-it.W/2.0)/elapsed;
                    double vy=(candidate.Y+candidate.Height/2.0-it.Y-it.H/2.0)/elapsed;
                    // Implausible measurements never seed a long extrapolation.
                    double maxSpeedX=Math.Max(1,it.W)*(timed ? 8 : 1);
                    double maxSpeedY=Math.Max(1,it.H)*(timed ? 8 : 1);
                    vx=Math.Clamp(vx,-maxSpeedX,maxSpeedX);vy=Math.Clamp(vy,-maxSpeedY,maxSpeedY);
                    double motionAlpha=timed ? 1-Math.Exp(-elapsed/.12) : .5;
                    it.VelocityX=it.HasVelocity ? (1-motionAlpha)*it.VelocityX+motionAlpha*vx : vx;
                    it.VelocityY=it.HasVelocity ? (1-motionAlpha)*it.VelocityY+motionAlpha*vy : vy;
                    it.HasVelocity=true;
                }
                // Adapt slowly only on an isolated, strong, geometrically accepted observation.
                bool isolated=true;
                foreach(var other in dets)
                    if(!ReferenceEquals(other,candidate) && other.ClassName==candidate.ClassName &&
                       Iou(candidate.X,candidate.Y,candidate.Width,candidate.Height,other.X,other.Y,other.Width,other.Height)>.05f)
                        isolated=false;
                if(continuing && isolated && !EdgeGeometry.Touches(candidate) && !candidate.IsWeakContinuation && candidate.Appearance!=null &&
                    AppearanceSimilarity(candidate.Appearance,it.RecentAppearance ?? it.Appearance)>.85f)
                {
                    var reference=it.RecentAppearance ?? it.Appearance;
                    if(reference!=null && reference.Length==candidate.Appearance.Length)
                    {
                        var next=new float[reference.Length];
                        float alpha=(float)Math.Clamp(1-Math.Exp(-Math.Max(0,elapsed)/(timed ? .5 : 12)),0,.35);
                        for(int h=0;h<next.Length;h++) next[h]=(1-alpha)*reference[h]+alpha*candidate.Appearance[h];
                        it.RecentAppearance=next;
                    }
                }
                if(EdgeGeometry.Touches(candidate)) it.EdgeAppearance ??= it.RecentAppearance ?? it.Appearance;
                else {it.FullWidth=candidate.Width;it.FullHeight=candidate.Height;it.EdgeAppearance=null;}
                it.FrameWidth=candidate.FrameWidth;it.FrameHeight=candidate.FrameHeight;
                it.LastConfirmedClock=clock;
                it.X=candidate.X;it.Y=candidate.Y;it.W=candidate.Width;it.H=candidate.Height;
                it.LastTrackId=candidate.TrackId ?? -1;
                it.LastMatched=true;it.State=SelectionState.Tracked;
                it.LastConfirmedFrame=frame;it.PendingCount=0;it.PendingBorderlineUsed=false;
                matched++;
            }
            return matched;
        }

        private static void Tag(Detection d, SelectionItem it)
        { d.IsSelected=true; d.SelectionKey=it.Key; d.SelectionNumber=it.Number; }

        public static float AppearanceSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;
            double sum=0; for (int i=0;i<a.Length;i++) sum += Math.Sqrt(a[i]*b[i]);
            return (float)Math.Min(1,sum);
        }

        /// <summary>Явная установка флага «следить» для объекта (чекбокс в списке DETECTIONS).</summary>
        public static bool Set(IList<SelectionItem> items, Detection d, bool on)
        {
            if (items == null || d == null) return false;
            int idx = -1;
            for (int i = 0; i < items.Count; i++)
                if (SameItem(d, items[i])) { idx = i; break; }
            if (on)
            {
                if (idx >= 0) return false;
                items.Add(SelectionItem.From(d));
                return true;
            }
            if (idx < 0) return false;
            items.RemoveAt(idx);
            return true;
        }

        /// <summary>
        /// Логика клика: обычный клик — «только этот объект» (повторный клик по
        /// единственному выбранному — сброс), Ctrl+клик — добавить/убрать.
        /// Возвращает размер выбора после действия.
        /// </summary>
        public static int Toggle(IList<SelectionItem> items, Detection d, bool additive)
        {
            if (items == null || d == null) return items?.Count ?? 0;
            int idx = -1;
            for (int i = 0; i < items.Count; i++)
                if (SameItem(d, items[i])) { idx = i; break; }

            // обычный клик по единственному выбранному = снять выбор
            if (!additive && idx >= 0 && items.Count == 1) { items.Clear(); return 0; }
            if (!additive) items.Clear();
            if (additive && idx >= 0) items.RemoveAt(idx);   // ctrl+клик по выбранному — убрать
            else items.Add(SelectionItem.From(d));           // добавить / зафиксировать
            return items.Count;
        }
    }
}
