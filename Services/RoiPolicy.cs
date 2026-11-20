using System;
using System.Collections.Generic;
using System.Linq;
using ObjectDetectTrackApp.Models;

namespace ObjectDetectTrackApp.Services
{
    /// <summary>Bounded central upper crop; no guessed detections or cached ROI observations.</summary>
    public static class RoiPolicy
    {
        public readonly record struct Window(int X,int Y,int Width,int Height);
        // Normalised diagnostic motorway crop. Fixed, not an automatically learned horizon.
        public static Window Region(int width,int height)
        {
            if(width<640 || height<480) return default;
            int x=(int)Math.Round(width*1300.0/3840), y=(int)Math.Round(height*300.0/2160);
            int w=Math.Min(width-x,(int)Math.Round(width*1400.0/3840));
            int h=Math.Min(height-y,(int)Math.Round(height*950.0/2160));
            return new(x,y,w,h);
        }

        public static List<Detection> MapCrop(IEnumerable<Detection> crop,Window region,int width,int height)
        {
            var result=new List<Detection>();
            if(region.Width<=0 || region.Height<=0 || region.X<0 || region.Y<0 ||
               region.X+(long)region.Width>width || region.Y+(long)region.Height>height) return result;
            foreach(var d in crop)
            {
                if(!float.IsFinite(d.Confidence) || d.Width<=0 || d.Height<=0 || d.X<0 || d.Y<0 ||
                   d.X+(long)d.Width>region.Width || d.Y+(long)d.Height>region.Height) continue;
                // Artificial crop edges are not physical source edges. Discard truncated objects
                // rather than shrinking a full-frame car/person and corrupting its appearance.
                if((region.X>0 && d.X<=2) || (region.Y>0 && d.Y<=2) ||
                   (region.X+region.Width<width && d.X+(long)d.Width>=region.Width-2) ||
                   (region.Y+region.Height<height && d.Y+(long)d.Height>=region.Height-2)) continue;
                float[] polygon=Array.Empty<float>();
                if(d.MaskPolygon is {Length:>=6} p && p.Length%2==0 && p.All(float.IsFinite))
                {
                    polygon=new float[p.Length];
                    for(int i=0;i<p.Length;i+=2) {
                        polygon[i]=Math.Clamp(p[i]+region.X,0,width-1);
                        polygon[i+1]=Math.Clamp(p[i+1]+region.Y,0,height-1);
                    }
                }
                result.Add(new Detection {ClassName=d.ClassName,Confidence=d.Confidence,
                    X=d.X+region.X,Y=d.Y+region.Y,Width=d.Width,Height=d.Height,
                    FrameWidth=width,FrameHeight=height,MaskPolygon=polygon,AnchorIdx=-1,Timestamp=d.Timestamp});
            }
            return result;
        }

        public static List<Detection> Merge(List<Detection> full,List<Detection> mapped,float threshold)
        {
            // OFF/no additional candidates: preserve existing postprocess order and behaviour.
            if(mapped.Count==0) return full;
            threshold=float.IsFinite(threshold) ? Math.Clamp(threshold,0,1) : .45f;
            var kept=new List<Detection>();
            foreach(var d in full.Concat(mapped).OrderByDescending(d=>d.Confidence))
            {
                if(kept.Any(k=>
                    SelectionLogic.Iou(k.X,k.Y,k.Width,k.Height,d.X,d.Y,d.Width,d.Height)>threshold)) continue;
                kept.Add(d);
            }
            return kept;
        }
    }

    /// <summary>Economy: at most one extra pass per 0.2 seconds of analysed source time.
    /// Every-frame option: at most one extra pass for each new processed frame.
    /// No wall-clock/FPS guarantee. New source/model/settings must call Reset.</summary>
    public sealed class RoiSchedule
    {
        private double _last=double.NaN;
        private double _seen=double.NaN;
        private int _frame=-1;

        public void Reset() { _last=_seen=double.NaN;_frame=-1; }
        public bool Due(bool enabled,bool video,int frame,double seconds,bool preview=false,bool everyFrame=false)
        {
            if(!enabled) {Reset();return false;}
            if(!video || preview) return true;
            // A broken/unknown source clock must not accidentally cause an extra pass every frame.
            if(frame<0 || !double.IsFinite(seconds) || seconds<0) return false;
            if(double.IsFinite(_seen) && (seconds<_seen || frame<_frame)) Reset();
            if(frame==_frame && seconds==_seen) return false;
            bool due=everyFrame || !double.IsFinite(_last) || seconds-_last>=.2-1e-7;
            _seen=seconds;_frame=frame;
            if(due) _last=seconds;
            return due;
        }
    }
}
