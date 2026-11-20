using System;
using System.Collections.Generic;
using System.Linq;
using ObjectDetectTrackApp.Models;
using static ObjectDetectTrackApp.Services.SelectionLogic;

namespace ObjectDetectTrackApp.Services
{
    /// <summary>Explicit user actions only. Editing an earlier frame replaces the future branch.</summary>
    public sealed class SelectionExportTimeline
    {
        private readonly List<(int Frame, List<SelectionItem> Items)> _events=new();
        public bool HasHistory => _events.Count>0;
        public void Clear() => _events.Clear();
        public void Record(int frame, IEnumerable<SelectionItem> items)
        {
            var snapshot=items.Select(i=>i.Clone()).ToList();
            if(snapshot.Count==0 && !HasHistory) return;
            frame=Math.Max(0,frame);
            _events.RemoveAll(e=>e.Frame>=frame);
            _events.Add((frame,snapshot));
        }
        public Replay Snapshot() => new(_events.Select(e=>(e.Frame,e.Items.Select(i=>i.Clone()).ToList())).ToList());
        public sealed class Replay
        {
            private readonly List<(int Frame,List<SelectionItem> Items)> _events;
            private int _cursor;
            private readonly Dictionary<Guid,SelectionItem> _anchors=new();
            public bool HasHistory => _events.Count>0;
            public List<SelectionItem> Items {get;}=new();
            public object[] Describe() => _events.Select(e=>(object)new {
                frame=e.Frame, selections=e.Items.Select(i=>new {
                    selection_number=i.Number, cls=i.ClassName,
                    anchor_frame=i.AnchorFrame, box=new {x=i.AnchorX,y=i.AnchorY,w=i.AnchorW,h=i.AnchorH}
                }).ToArray()
            }).ToArray();
            internal Replay(List<(int Frame,List<SelectionItem> Items)> events) { _events=events; }
            public List<Detection> Select(List<Detection> all,int frame,double sourceSeconds,bool active,Action<Detection>? reanchor=null)
            {
                foreach(var d in all) d.FrameIndex=frame;
                if(!HasHistory) return all;
                var changed=Advance(frame);
                var used=new HashSet<Detection>();
                foreach(var item in changed) {
                    var ranked=all.Where(d=>d.ClassName==item.ClassName && !used.Contains(d))
                        .Select(d=>(Detection:d,Iou:SelectionLogic.Iou(d.X,d.Y,d.Width,d.Height,item.AnchorX,item.AnchorY,item.AnchorW,item.AnchorH)))
                        .OrderByDescending(p=>p.Iou).ToList();
                    if(frame!=item.AnchorFrame || ranked.Count==0 || ranked[0].Iou<.5f ||
                        (ranked.Count>1 && ranked[0].Iou-ranked[1].Iou<.12f)) continue;
                    var candidate=ranked[0].Detection;
                    used.Add(candidate);
                    var confirmed=new List<float[]>(item.ConfirmedAppearances);
                    // New tracker IDs belong to this replay, never to the original viewing run.
                    SelectionLogic.Rebind(item,candidate);
                    item.ConfirmedAppearances=confirmed;
                    reanchor?.Invoke(candidate);
                }
                SelectionLogic.Apply(all,Items,active,observationFrame:frame,sourceSeconds:sourceSeconds);
                return all.FindAll(d=>d.IsSelected);
            }

            public List<SelectionItem> Advance(int frame)
            {
                var changed=new List<SelectionItem>();
                while(_cursor<_events.Count && _events[_cursor].Frame<=frame)
                {
                    var action=_events[_cursor++];
                    Items.RemoveAll(i=>!action.Items.Any(j=>j.Key==i.Key));
                    foreach(var saved in action.Items)
                    {
                        var previous=Items.FirstOrDefault(i=>i.Key==saved.Key);
                        _anchors.TryGetValue(saved.Key,out var anchor);
                        if(previous!=null && anchor!=null && anchor.AnchorFrame==saved.AnchorFrame &&
                            anchor.AnchorX==saved.AnchorX && anchor.AnchorY==saved.AnchorY &&
                            anchor.AnchorW==saved.AnchorW && anchor.AnchorH==saved.AnchorH &&
                            ReferenceEquals(anchor.Appearance,saved.Appearance)) continue;
                        if(previous!=null) Items.Remove(previous);
                        _anchors[saved.Key]=saved.Clone();
                        var copy=saved.Clone();ResetForReplay(copy);Items.Add(copy);changed.Add(copy);
                    }
                }
                return changed.Where(i=>Items.Contains(i)).ToList();
            }
        }
    }
}
