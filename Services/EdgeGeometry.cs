using System;
using System.Collections.Generic;
using ObjectDetectTrackApp.Models;

namespace ObjectDetectTrackApp.Services
{
    /// <summary>Visible border fragments only; never produces or renders a predicted box.</summary>
    public static class EdgeGeometry
    {
        public static bool Touches(Detection d) => Valid(d) &&
            (d.X<=2 || d.Y<=2 || d.X+d.Width>=d.FrameWidth-2 || d.Y+d.Height>=d.FrameHeight-2);
        private static bool Valid(Detection d) => d.FrameWidth>0 && d.FrameHeight>0 &&
            d.Width>0 && d.Height>0 && d.X>=0 && d.Y>=0 &&
            (long)d.X+d.Width<=d.FrameWidth && (long)d.Y+d.Height<=d.FrameHeight;
        public static bool TryMatch(Detection previous,Detection current,int fullWidth,int fullHeight,double gap,out float score)
        {
            score=0;
            if(!Valid(previous) || !Valid(current) || previous.FrameWidth!=current.FrameWidth ||
                previous.FrameHeight!=current.FrameHeight || previous.ClassName!=current.ClassName ||
                !double.IsFinite(gap) || gap<=0 || gap>.6 || fullWidth<8 || fullHeight<8 ||
                fullWidth>current.FrameWidth*.6 || fullHeight>current.FrameHeight*.6) return false;
            bool left=current.X<=2 && previous.X<=fullWidth*.75;
            bool right=current.X+current.Width>=current.FrameWidth-2 &&
                previous.X+previous.Width>=current.FrameWidth-fullWidth*.75;
            bool top=current.Y<=2 && previous.Y<=fullHeight*.75;
            bool bottom=current.Y+current.Height>=current.FrameHeight-2 &&
                previous.Y+previous.Height>=current.FrameHeight-fullHeight*.75;
            if(!(left||right||top||bottom)) return false;
            float wr=(float)current.Width/fullWidth,hr=(float)current.Height/fullHeight;
            if(wr<(left||right ? .15f : .55f) || wr>1.3f ||
               hr<(top||bottom ? .15f : .55f) || hr>1.3f) return false;
            // Only clipping explains a large shrink: the opposite axis remains constrained.
            float dx=Math.Abs(current.X+current.Width/2f-previous.X-previous.Width/2f)/fullWidth;
            float dy=Math.Abs(current.Y+current.Height/2f-previous.Y-previous.Height/2f)/fullHeight;
            float limit=(float)Math.Min(1.5,.65+1.5*gap);
            if(dx>limit || dy>limit) return false;
            score=Math.Clamp(1-.35f*(dx+dy),.35f,1);
            return true;
        }
        public static bool Isolated(Detection d,IEnumerable<Detection> observations,int fullWidth,int fullHeight)
        {
            foreach(var other in observations) {
                if(ReferenceEquals(d,other) || d.ClassName!=other.ClassName) continue;
                float dx=Math.Abs(d.X+d.Width/2f-other.X-other.Width/2f);
                float dy=Math.Abs(d.Y+d.Height/2f-other.Y-other.Height/2f);
                if(SelectionLogic.Iou(d.X,d.Y,d.Width,d.Height,other.X,other.Y,other.Width,other.Height)>.05f ||
                    (dx<fullWidth*.65f && dy<fullHeight*.65f)) return false;
            }
            return true;
        }
    }
}
