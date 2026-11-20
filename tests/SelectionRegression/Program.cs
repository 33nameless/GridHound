using ObjectDetectTrackApp.Models;
using ObjectDetectTrackApp.Services;
using static ObjectDetectTrackApp.Services.SelectionLogic;
int count=0;
void Test(string name,Action run) { run(); count++; Console.WriteLine("PASS "+name); }
void Check(bool value) { if(!value) throw new Exception("Assertion failed"); }
Detection D(int x=0,int? id=null,string cls="person")=>new Detection { ClassName=cls,X=x,Y=0,Width=100,Height=100,TrackId=id,FrameIndex=12,Appearance=new float[]{1,0} };
Test("ID on first image",()=> {var d=D(); new TrackerService(null).Update(new(){d}); Check(d.TrackId==1);});
Test("ID survives movement",()=> {var t=new TrackerService(null);t.Update(new(){D()});var d=D(10);t.Update(new(){d});Check(d.TrackId==1);});
Test("Class isolation",()=> {var t=new TrackerService(null);t.Update(new(){D()});var d=D(0,null,"car");t.Update(new(){d});Check(d.TrackId==2);});
Test("Reset IDs",()=> {var t=new TrackerService(null);t.Update(new(){D()});t.Reset();var d=D();t.Update(new(){d});Check(d.TrackId==1);});
Test("Set is idempotent",()=> {var list=new List<SelectionItem>();Check(Set(list,D(0,1),true));Check(!Set(list,D(0,1),true));Check(list.Count==1);});
Test("Remove selection",()=> {var list=new List<SelectionItem>{SelectionItem.From(D(0,1))};Check(Set(list,D(0,1),false));Check(list.Count==0);});
Test("Only selected drawn candidates",()=> {var items=new List<SelectionItem>{SelectionItem.From(D(0,1))};var all=new List<Detection>{D(0,1),D(200,2)};Check(Apply(all,items)==1);Check(all[0].IsSelected&&!all[1].IsSelected);});
Test("Lost does not show neighbour",()=> {var items=new List<SelectionItem>{SelectionItem.From(D(0,1))};var neighbour=D(400,2);Check(Apply(new[]{neighbour},items)==0);Check(!items[0].LastMatched&&!neighbour.IsSelected);});
Test("Reacquire ID requires distinct observations",()=> {
 var items=new List<SelectionItem>{SelectionItem.From(D(0,1))};Apply(Array.Empty<Detection>(),items);
 for(int f=13;f<=16;f++){var d=D(10,1);d.FrameIndex=f;Check(Apply(new[]{d},items)==(f==16?1:0));}
});
Test("No SORT selection",()=> {var items=new List<SelectionItem>{SelectionItem.From(D())};Check(Apply(new[]{D(10),D(250)},items)==1);});
Test("Distinct IDs do not collapse",()=> {var items=new List<SelectionItem>();Set(items,D(0,1),true);Set(items,D(10,2),true);Check(items.Count==2);});
Test("One-to-one selection",()=> {var items=new List<SelectionItem>{SelectionItem.From(D(0,1)),SelectionItem.From(D(20,2))};Check(Apply(new[]{D(10,1)},items)==1);});
Test("Prefer exact ID over first overlap",()=> {var items=new List<SelectionItem>{SelectionItem.From(D(0,1))};var all=new[]{D(10,2),D(20,1)};Apply(all,items);Check(!all[0].IsSelected&&all[1].IsSelected);});
Test("Appearance rejects colour mismatch in tracker",()=> {var t=new TrackerService(null);t.Update(new(){D()});var d=D(5);d.Appearance=new float[]{0,1};t.Update(new(){d});Check(d.TrackId==2);});
Test("Appearance score",()=>Check(AppearanceSimilarity(new float[]{1,0},new float[]{1,0})==1 && AppearanceSimilarity(new float[]{1,0},new float[]{0,1})==0));
Test("Hungarian beats greedy",()=> {var m=HungarianAlgorithm.FindMatches(new float[,]{{.1f,.2f},{.15f,9}},.7f);Check(m.Count==2 && m.Contains((0,1)) && m.Contains((1,0)));});
Test("Rectangular unmatched",()=>Check(HungarianAlgorithm.FindMatches(new float[,]{{9},{.1f},{9}},.7f).SequenceEqual(new[]{(1,0)})));
Test("Threshold inclusive",()=>Check(HungarianAlgorithm.FindMatches(new float[,]{{.7f}},.7f).Count==1));
Test("Empty assignment",()=>Check(HungarianAlgorithm.FindMatches(new float[0,3],.7f).Count==0));
Test("Clone preserves lost state and anchor",()=> {var it=SelectionItem.From(D());it.LastMatched=false;var copy=it.Clone();Check(!copy.LastMatched && copy.AnchorFrame==12 && copy.AnchorW==100);});
Test("Observable checkbox",()=> {var d=D();bool notified=false;d.PropertyChanged+=(o,e)=>notified|=e.PropertyName=="IsSelected";d.IsSelected=true;Check(notified);});
Test("Observable row update",()=> {var d=D();bool notified=false;d.PropertyChanged+=(o,e)=>notified|=e.PropertyName=="DisplayLine";d.UpdateFrom(D(10,3));Check(notified&&d.TrackId==3&&d.X==10);});
Test("Ctrl multiple selection",()=> {var items=new List<SelectionItem>();Toggle(items,D(0,1),false);Toggle(items,D(200,2),true);Check(items.Count==2);Toggle(items,D(0,1),true);Check(items.Count==1);});
Test("Selection flags reset between passes",()=> {var d=D(0,1);var items=new List<SelectionItem>{SelectionItem.From(d)};Apply(new[]{d},items);items.Clear();Apply(new[]{d},items);Check(!d.IsSelected);});
Test("Crossing different clothes",()=> {
    var t=new TrackerService(null);
    for(int i=0;i<=4;i++) {
        var a=D(i*30); var b=D(200-i*30); b.Appearance=new float[]{0,1};
        t.Update(new(){b,a});
        Check(a.TrackId==2 && b.TrackId==1);
    }
});
Test("Short detection gap",()=> {
    var t=new TrackerService(null);t.Update(new(){D()});t.Update(new());
    var d=D(10);t.Update(new(){d});Check(d.TrackId==1);
});
Test("Skipped modest motion",()=> {
    var t=new TrackerService(null);t.Update(new(){D()});t.Update(new(){D(20)});
    var d=D(60);t.Update(new(){d});Check(d.TrackId==1);
});
Test("Selection key preserved on clone",()=> {
    var it=SelectionItem.From(D());Check(it.Key==it.Clone().Key);
});
Test("Apply tags selected identity",()=> {
    var it=SelectionItem.From(D(0,1));var d=D(0,1);
    Apply(new[]{d},new[]{it});Check(d.SelectionKey==it.Key);
});
Test("Numbers independent from track IDs",()=> {
 var a=SelectionItem.From(D(0,999));var b=SelectionItem.From(D(0,999));Check(a.Number!=b.Number && a.Clone().Number==a.Number);
});
Test("Rebind keeps key and number",()=> {
 var a=SelectionItem.From(D(0,1));var key=a.Key;int number=a.Number;Rebind(a,D(300,9));Check(a.Key==key&&a.Number==number&&a.LastTrackId==9&&a.X==300);
});
Test("Known neighbour cannot take selection",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(new[]{D(0,1),D(200,2)},items);
 for(int f=13;f<22;f++){var d=D(0,2);d.FrameIndex=f;Check(Apply(new[]{d},items)==0);}
 Check(a.LastTrackId==1);
});
Test("Merged tall box rejected even same ID",()=> {
 var a=SelectionItem.From(D(0,1));var d=D(0,1);d.Height=190;Check(Apply(new[]{d},new[]{a})==0&&!d.IsSelected);
});
Test("Same ID cannot teleport by colour",()=> {
 var a=SelectionItem.From(D(0,1));Check(Apply(new[]{D(900,1)},new[]{a})==0);
});
Test("Recovery repeated frame does not confirm",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 for(int n=0;n<10;n++){var d=D(0,1);d.FrameIndex=13;Check(Apply(new[]{d},items)==0);}
 Check(a.PendingCount==1);
});
Test("Repaint does not advance recovery",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 var d=D(0,1);d.FrameIndex=13;Apply(new[]{d},items);
 for(int n=0;n<10;n++)Apply(new[]{d},items,advance:false);
 Check(a.PendingCount==1 && !a.LastMatched && !d.IsSelected);
});
Test("Repaint never loses accepted frame",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};var d=D(0,1);Apply(new[]{d},items);
 Apply(new[]{d},items,advance:false);Check(d.IsSelected&&a.LastMatched&&d.SelectionNumber==a.Number);
});
Test("Competing unknown candidates wait",()=> {
 var a=SelectionItem.From(D());var ds=new[]{D(0),D(5)};Check(Apply(ds,new[]{a})==0&&a.State==SelectionState.Ambiguous);
});
Test("Shared candidate never binds two selections",()=> {
 var a=SelectionItem.From(D());var b=SelectionItem.From(D(1));var d=D();Check(Apply(new[]{d},new[]{a,b})==0);
 Check(a.State==SelectionState.Ambiguous&&b.State==SelectionState.Ambiguous);
});
Test("Missing selection remains active but invisible",()=> {
 var items=new List<SelectionItem>{SelectionItem.From(D(0,1))};var ds=new[]{D(500,2)};
 Apply(ds,items);Check(items.Count==1 && items[0].State==SelectionState.TemporarilyLost && ds.All(d=>!d.IsSelected));
});
Test("Long gap requires explicit correction",()=> {
 var a=SelectionItem.From(D(0,1));var d=D(0,1);d.FrameIndex=103;Check(Apply(new[]{d},new[]{a})==0 && a.State==SelectionState.AwaitingReselection);
 d.FrameIndex=104;Check(Apply(new[]{d},new[]{a},true)==0);Rebind(a,d);Check(Apply(new[]{d},new[]{a})==1);
});
Test("New ID confirmation preserves selection number",()=> {
 var a=SelectionItem.From(D(0,1));int number=a.Number;var key=a.Key;var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 for(int f=13;f<=16;f++){var d=D(5,9);d.FrameIndex=f;Check(Apply(new[]{d},items)==(f==16?1:0));if(f==16)Check(d.SelectionNumber==number&&d.SelectionKey==key);}
 Check(a.LastTrackId==9);
});
Test("Active mode still needs three observations",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 for(int f=13;f<=15;f++){var d=D(5,9);d.FrameIndex=f;Check(Apply(new[]{d},items,true)==(f==15?1:0));}
});
Test("Manual correction permits formerly excluded track",()=> {
 var a=SelectionItem.From(D(0,1));a.KnownOtherIds.Add(2);Rebind(a,D(0,2));Check(Apply(new[]{D(0,2)},new[]{a})==1);
});
Test("Replay reset retains user identity but clears evidence",()=> {
 var a=SelectionItem.From(D(0,1));int n=a.Number;var key=a.Key;a.PendingCount=3;a.LastObservedFrame=50;a.State=SelectionState.AwaitingReselection;
 ResetForReplay(a);Check(a.Number==n&&a.Key==key&&a.PendingCount==0&&a.LastObservedFrame==-1&&a.KnownOtherIds.Count==0);
});
Test("Class mismatch does not steal selection",()=> {
 var a=SelectionItem.From(D(0,1));Check(Apply(new[]{D(0,1,"car")},new[]{a})==0);
});
Test("Clone evidence is independent",()=> {
 var a=SelectionItem.From(D());a.KnownOtherIds.Add(7);var b=a.Clone();b.KnownOtherIds.Add(8);Check(!a.KnownOtherIds.Contains(8));
});
Test("Backward observations cannot confirm recovery",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 var d=D(0,1);d.FrameIndex=15;Apply(new[]{d},items);d.FrameIndex=14;Apply(new[]{d},items);Check(a.PendingCount==1);
});
Test("Recovery evidence interrupted by loss resets",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};Apply(Array.Empty<Detection>(),items);
 var d=D(0,1);d.FrameIndex=13;Apply(new[]{d},items);Apply(Array.Empty<Detection>(),items);d.FrameIndex=14;Apply(new[]{d},items);Check(a.PendingCount==1);
});
Test("New tracker epoch cannot reuse old ID history",()=> {
 var a=SelectionItem.From(D(0,1));a.KnownOtherIds.Add(5);a.LastObservedFrame=50;a.PendingCount=2;
 int n=a.Number;StartNewTrackingEpoch(a,50);Check(a.Number==n&&a.LastTrackId==-1&&a.LastObservedFrame==-1&&a.KnownOtherIds.Count==0&&a.PendingCount==0&&!a.LastMatched);
});
Test("Empty observations also expire the local recovery window",()=> {
 var a=SelectionItem.From(D(0,1));var items=new[]{a};
 for(int f=13;f<=104;f++)Apply(Array.Empty<Detection>(),items,observationFrame:f);
 Check(a.State==SelectionState.AwaitingReselection && a.LastObservedFrame==104);
});
Test("Ski fixture rejects recorded neighbour transfer",()=> {
 var path=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json");
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(path));
 foreach(int step in new[]{1,2,4,8}) {
  var tracker=new TrackerService(null);var items=new List<SelectionItem>();int visible=0;bool recovered=false;
  for(int f=0;f<frames.Count;f+=step) {
   var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,FrameIndex=f,Appearance=d.Appearance,SourceSeconds=f/24.0}).ToList();
   ds=tracker.Update(ds,.45f,.10f,f/24.0);
   if(f==0) {var target=ds.Single(d=>d.X==383&&d.Y==718);items.Add(SelectionItem.From(target));}
   Apply(ds,items,observationFrame:f,sourceSeconds:f/24.0);
   foreach(var d in ds.Where(d=>d.IsSelected)) {visible++;if(f>=136&&f<=176) recovered=true;Check(d.TrackId!=1 && d.TrackId!=4 && d.SelectionNumber==items[0].Number);}
  }
  Check(visible>0 && recovered);
 }
});
Test("Preview landscape centre maps to source pixels",()=> {
 Check(PreviewCoordinates.TryMapUniform(480,270,960,540,3840,2160,out var x,out var y)&&Math.Abs(x-1920)<.001&&Math.Abs(y-1080)<.001);
});
Test("Preview portrait letterbox maps centre",()=> {
 Check(PreviewCoordinates.TryMapUniform(500,300,1000,600,720,1280,out var x,out var y)&&Math.Abs(x-360)<.001&&Math.Abs(y-640)<.001);
});
Test("Preview empty side margin cannot select",()=> {
 Check(!PreviewCoordinates.TryMapUniform(100,300,1000,600,720,1280,out _,out _));
});
Test("Preview empty top margin cannot select",()=> {
 Check(!PreviewCoordinates.TryMapUniform(300,50,600,1000,1920,1080,out _,out _));
});
Test("Preview invalid dimensions rejected",()=> {
 Check(!PreviewCoordinates.TryMapUniform(1,1,0,100,720,1280,out _,out _));
 Check(!PreviewCoordinates.TryMapUniform(double.NaN,1,100,100,720,1280,out _,out _));
});
Test("Preview far edge is outside source",()=> {
 Check(!PreviewCoordinates.TryMapUniform(960,540,960,540,3840,2160,out _,out _));
 Check(PreviewCoordinates.TryMapUniform(0,0,960,540,3840,2160,out var x,out var y)&&x==0&&y==0);
});
Test("Weak detections never create tracks",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.2f;Check(t.Update(new(){d},.45f,.1f,0).Count==0&&t.GetActiveTrackCount()==0);
});
Test("Weak detection continues existing track",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 var weak=D(5);weak.Confidence=.2f;var result=t.Update(new(){weak},.45f,.1f,.04);Check(result.Count==1&&weak.TrackId==1);
});
Test("Strong candidates have first association priority",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 var strong=D(5);strong.Confidence=.7f;var weak=D(2);weak.Confidence=.2f;
 var result=t.Update(new(){weak,strong},.45f,.1f,.04);Check(strong.TrackId==1 && !weak.TrackId.HasValue && result.Count==1);
});
Test("Below continuation threshold cannot support track",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 var weak=D(2);weak.Confidence=.05f;Check(t.Update(new(){weak},.45f,.1f,.04).Count==0);
});
Test("Timed track expires by seconds not processed steps",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 var weak=D();weak.Confidence=.2f;Check(t.Update(new(){weak},.45f,.1f,3.1).Count==0&&t.GetActiveTrackCount()==0);
});
Test("Tracker rejects repeated timestamp",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 Check(t.Update(new(){d},.45f,.1f,0).Count==0&&t.GetActiveTrackCount()==1);
});
Test("Manual reanchor refreshes track appearance",()=> {
 var t=new TrackerService(null);var d=D();d.Confidence=.8f;t.Update(new(){d},.45f,.1f,0);
 var anchor=D(0,1);anchor.Appearance=new float[]{0,1};anchor.SourceSeconds=.04;t.Reanchor(anchor);
 var next=D(1);next.Confidence=.8f;next.Appearance=new float[]{0,1};t.Update(new(){next},.45f,.1f,.08);Check(next.TrackId==1);
});
Test("Manual correction clears old motion and expiry",()=> {
 var it=SelectionItem.From(D(0,1));int n=it.Number;it.State=SelectionState.AwaitingReselection;it.VelocityX=900;it.HasVelocity=true;it.PendingCount=3;
 var d=D(500,7);d.SourceSeconds=20;Rebind(it,d);Check(it.Number==n && !it.HasVelocity && it.VelocityX==0 && it.PendingCount==0 && double.IsNaN(it.PendingClock) && it.LastConfirmedClock==20 && it.State==SelectionState.Tracked);
});
Test("Source seconds replace fixed frame expiry",()=> {
 var d=D(0,1);d.SourceSeconds=0;var it=SelectionItem.From(d);var next=D(1,1);next.SourceSeconds=.5;next.FrameIndex=1000;
 Check(Apply(new[]{next},new[]{it},observationFrame:1000,sourceSeconds:.5)==1);
});
Test("Local motion allows recovery beyond frozen last box",()=> {
 var d=D(0,1);d.SourceSeconds=0;var it=SelectionItem.From(d);var items=new[]{it};Apply(new[]{d},items,observationFrame:0,sourceSeconds:0);
 var moving=D(10,1);moving.FrameIndex=1;moving.SourceSeconds=.1;Apply(new[]{moving},items,observationFrame:1,sourceSeconds:.1);
 Apply(Array.Empty<Detection>(),items,observationFrame:2,sourceSeconds:.2);
 for(int i=0;i<4;i++){var next=D(80+i*10,1);next.FrameIndex=8+i;next.SourceSeconds=.8+i*.1;Check(Apply(new[]{next},items,observationFrame:8+i,sourceSeconds:.8+i*.1)==(i==3?1:0));}
 Check(it.X==110);
});
Test("Timing uses fractional FPS when source PTS is missing",()=> {
 var timing=new SourceTiming();Check(timing.ResolveMilliseconds(0,0,23.976)==0);
 double ms=timing.ResolveMilliseconds(24,0,23.976);Check(Math.Abs(ms-24*1000/23.976)<.001);
});
Test("Timing prefers valid PTS and resets on seek",()=> {
 var timing=new SourceTiming();timing.ResolveMilliseconds(0,0,25);Check(timing.ResolveMilliseconds(1,70,25)==70);
 Check(timing.ResolveMilliseconds(0,0,25)==0);
});
Test("Timing handles duplicate nonzero PTS",()=> {
 var timing=new SourceTiming();timing.ResolveMilliseconds(0,100,25);Check(timing.ResolveMilliseconds(2,100,25)==180);
});
Test("Weak continuation does not update recent appearance",()=> {
 var it=SelectionItem.From(D(0,1));var before=it.RecentAppearance;
 var d=D(0,1);d.FrameIndex=13;d.IsWeakContinuation=true;d.Appearance=new float[]{.95f,.05f};
 Check(Apply(new[]{d},new[]{it})==1 && ReferenceEquals(before,it.RecentAppearance));
});
Test("Replay resets adaptive appearance to anchor reference",()=> {
 var it=SelectionItem.From(D());it.RecentAppearance=new float[]{0,1};ResetForReplay(it);Check(ReferenceEquals(it.RecentAppearance,it.Appearance));
});
Test("Manual correction on ski fixture preserves number across later ID change",()=> {
 var path=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json");
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(path));
 var tracker=new TrackerService(null);var items=new List<SelectionItem>();int number=0;bool selectedAt240=false;
 for(int f=0;f<=280;f++) {
  var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,FrameIndex=f,Appearance=d.Appearance,SourceSeconds=f/24.0}).ToList();
  ds=tracker.Update(ds,.45f,.1f,f/24.0);
  if(f==0){items.Add(SelectionItem.From(ds.Single(d=>d.X==383&&d.Y==718)));number=items[0].Number;}
  if(f==203){var correct=ds.Single(d=>d.X==169&&d.Y==841);Rebind(items[0],correct);tracker.Reanchor(correct);Check(items[0].Number==number);}
  Apply(ds,items,observationFrame:f,sourceSeconds:f/24.0);
  foreach(var d in ds.Where(d=>d.IsSelected)){Check(d.SelectionNumber==number && d.TrackId!=1 && d.TrackId!=4);if(f==240)selectedAt240=true;}
 }
 Check(selectedAt240);
});
Test("Auto request waits one source second and asks only once",()=> {
 var a=new AutoCorrectionRequests();var key=Guid.NewGuid();
 Check(!a.Observe(key,false,1.99,1));Check(a.Observe(key,false,2,1));Check(!a.Observe(key,false,4,1));
});
Test("Auto request re-arms only after actual recovery",()=> {
 var a=new AutoCorrectionRequests();var key=Guid.NewGuid();a.Suppress(key);
 Check(!a.Observe(key,false,4,1));Check(!a.Observe(key,true,4,4));Check(a.Observe(key,false,5,4));
});
Test("Auto requests are independent per selected identity",()=> {
 var a=new AutoCorrectionRequests();Check(a.Observe(Guid.NewGuid(),false,3,0)&&a.Observe(Guid.NewGuid(),false,3,0));
});
Test("Auto request ignores invalid clocks and resets",()=> {
 var a=new AutoCorrectionRequests();var key=Guid.NewGuid();Check(!a.Observe(key,false,double.NaN,0));
 a.Suppress(key);a.Clear();Check(a.Observe(key,false,2,0));
});
Test("Manual appearance memory owns its data and survives replay",()=> {
 var d=D();var it=SelectionItem.From(d);d.Appearance[0]=0;Check(it.ConfirmedAppearances[0][0]==1);
 var v=D();v.Appearance=new float[]{0,1};Rebind(it,v);ResetForReplay(it);Check(it.ConfirmedAppearances.Count==2);
 var copy=it.Clone();copy.ConfirmedAppearances.Clear();Check(it.ConfirmedAppearances.Count==2);
});
Test("Only explicit confirmations add memory samples",()=> {
 var it=SelectionItem.From(D(0,1));for(int f=13;f<30;f++){var d=D(0,1);d.FrameIndex=f;Apply(new[]{d},new[]{it});}
 Check(it.ConfirmedAppearances.Count==1);
});
Test("Confirmed memory recognises earlier user-approved appearance",()=> {
 var it=SelectionItem.From(D(0,1));var otherView=D(0,1);otherView.Appearance=new float[]{0,1};Rebind(it,otherView);
 var original=D(0,1);original.FrameIndex=13;Check(Apply(new[]{original},new[]{it})==1);
});
Test("Memory does not bypass spatial exclusion",()=> {
 var it=SelectionItem.From(D(0,1));Check(Apply(new[]{D(1000,1)},new[]{it})==0);
});
Test("Confirmation gallery bounded and class change clears it",()=> {
 var it=SelectionItem.From(D());for(int i=1;i<30;i++) {var d=D();d.Appearance=new float[]{(float)Math.Cos(i*.15),(float)Math.Sin(i*.15)};Rebind(it,d);}
 Check(it.ConfirmedAppearances.Count<=8);Rebind(it,D(0,null,"car"));Check(it.ConfirmedAppearances.Count==1);
});
Test("Export with no selection history retains all detections",()=> {
 var timeline=new SelectionExportTimeline();var ds=new List<Detection>{D(0,1),D(200,2)};
 Check(timeline.Snapshot().Select(ds,0,0,false).Count==2);
});
Test("Export retains full frame span and hides boxes before first selection",()=> {
 var t=new SelectionExportTimeline();var d=D(0,1);d.FrameIndex=20;var it=SelectionItem.From(d);t.Record(20,new[]{it});var replay=t.Snapshot();
 for(int f=0;f<20;f++)Check(replay.Select(new(){D(0,5)},f,f/24.0,false).Count==0);
 var anchor=D(0,5);anchor.SourceSeconds=20/24.0;
 Check(replay.Select(new(){anchor},20,20/24.0,false).Single().SelectionNumber==it.Number);
});
Test("Export corrections keep S and are not applied before their frame",()=> {
 var t=new SelectionExportTimeline();var d=D(0,1);d.FrameIndex=0;var it=SelectionItem.From(d);t.Record(0,new[]{it});
 var correction=D(300,7);correction.FrameIndex=10;Rebind(it,correction);t.Record(10,new[]{it});
 var replay=t.Snapshot();Check(replay.Advance(0).Single().AnchorX==0);
 Check(replay.Advance(9).Count==0 && replay.Items.Single().AnchorX==0);
 var current=D(300,88);current.SourceSeconds=10/24.0;
 var selected=replay.Select(new(){current},10,10/24.0,false);
 Check(selected.Count==1&&selected[0].SelectionNumber==it.Number&&selected[0].TrackId==88);
});
Test("Export remembers removal without leaking unselected objects",()=> {
 var t=new SelectionExportTimeline();t.Record(0,new[]{SelectionItem.From(D())});t.Record(10,Array.Empty<SelectionItem>());
 var replay=t.Snapshot();Check(replay.Select(new(){D(0,1)},10,1,false).Count==0);
});
Test("Export snapshot independent of later edits and other exports",()=> {
 var t=new SelectionExportTimeline();var it=SelectionItem.From(D());t.Record(12,new[]{it});var a=t.Snapshot();var b=t.Snapshot();
 t.Clear();Check(a.Advance(12).Count==1&&b.Items.Count==0);Check(b.Advance(12).Count==1);
 a.Items[0].X=999;Check(b.Items[0].X==0);
});
Test("Export earlier-frame edit replaces future branch",()=> {
 var t=new SelectionExportTimeline();var a=SelectionItem.From(D());t.Record(12,new[]{a});t.Record(30,Array.Empty<SelectionItem>());
 var b=SelectionItem.From(D(200));t.Record(20,new[]{b});var replay=t.Snapshot();replay.Advance(40);
 Check(replay.Items.Single().Key==b.Key);
});
Test("Export adding another S does not reset continuing motion",()=> {
 var t=new SelectionExportTimeline();var a=SelectionItem.From(D());t.Record(12,new[]{a});t.Record(20,new[]{a,SelectionItem.From(D(200))});
 var replay=t.Snapshot();replay.Advance(12);replay.Items[0].VelocityX=23;replay.Items[0].X=44;
 Check(replay.Advance(20).Count==1 && replay.Items.Single(i=>i.Key==a.Key).VelocityX==23 && replay.Items.Single(i=>i.Key==a.Key).X==44);
});
Test("Export ambiguous correction does not bind a guessed box immediately",()=> {
 var t=new SelectionExportTimeline();var d=D(0,1);d.FrameIndex=12;t.Record(12,new[]{SelectionItem.From(d)});
 var replay=t.Snapshot();int anchors=0;replay.Select(new(){D(0,4),D(1,5)},12,.5,false,_=>anchors++);Check(anchors==0);
});
Test("Export correction reanchors replay tracker only on action frame",()=> {
 var t=new SelectionExportTimeline();t.Record(12,new[]{SelectionItem.From(D())});var replay=t.Snapshot();int anchors=0;
 replay.Select(new(){D(0,9)},12,.5,false,_=>anchors++);replay.Select(new(){D(0,9)},13,.6,false,_=>anchors++);Check(anchors==1);
});
Test("CSV has source time, invariant decimals and separate observation clock",()=> {
 var before=System.Globalization.CultureInfo.CurrentCulture;
 try {System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("ru-RU");
 var d=D();d.SourceSeconds=61.25;d.Confidence=.875f;d.Timestamp=new DateTime(2026,1,2,10,20,30);
 var csv=ExportSerialization.Csv(new[]{d});Check(csv.Contains("00:01:01.250")&&csv.Contains(";0.875;")&&csv.Contains(";61.250000;")&&csv.Contains("2026-01-02T10:20:30"));
 }finally{System.Globalization.CultureInfo.CurrentCulture=before;}
});
Test("CSV correctly quotes special class names",()=> {
 var d=D();d.ClassName="a;\"b\"";Check(ExportSerialization.Csv(new[]{d}).Contains("\"a;\"\"b\"\"\""));
});
Test("JSON source time is nullable for photos not a fake midnight",()=> {
 var d=D();using var doc=System.Text.Json.JsonDocument.Parse(ExportSerialization.Json(new[]{d}));var row=doc.RootElement[0];
 Check(row.GetProperty("source_seconds").ValueKind==System.Text.Json.JsonValueKind.Null&&row.GetProperty("time").GetString()==""&&row.GetProperty("analysis_mode").GetString()=="view_observations");
});
Test("JSON preserves source frame and S separately from internal track",()=> {
 var d=D(0,42);d.FrameIndex=100;d.SourceSeconds=100/29.97;d.SelectionNumber=7;
 using var doc=System.Text.Json.JsonDocument.Parse(ExportSerialization.Json(new[]{d}));var row=doc.RootElement[0];
 Check(row.GetProperty("frame").GetInt32()==100&&row.GetProperty("selection_number").GetInt32()==7&&row.GetProperty("track").GetInt32()==42&&Math.Abs(row.GetProperty("source_seconds").GetDouble()-d.SourceSeconds)<1e-10);
});
Test("Source time handles durations over 24h",()=>Check(ExportSerialization.SourceTime(90061.25)=="25:01:01.250"));
Test("Export ski replay applies actual manual correction at f203",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json")));
 var timeline=new SelectionExportTimeline();var initial=frames[0].Single(d=>d.X==383&&d.Y==718);initial.FrameIndex=0;initial.SourceSeconds=0;
 var item=SelectionItem.From(initial);timeline.Record(0,new[]{item});
 var correction=frames[203].Single(d=>d.X==169&&d.Y==841);correction.FrameIndex=203;correction.SourceSeconds=203/24.0;
 Rebind(item,correction);timeline.Record(203,new[]{item});var replay=timeline.Snapshot();var tracker=new TrackerService(null);bool early=false,late=false;
 for(int f=0;f<=280;f++) {
  var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,FrameIndex=f,SourceSeconds=f/24.0,Appearance=d.Appearance}).ToList();
  ds=tracker.Update(ds,.45f,.1f,f/24.0);var selected=replay.Select(ds,f,f/24.0,false,d=>tracker.Reanchor(d));
  foreach(var d in selected){Check(d.SelectionNumber==item.Number&&d.TrackId!=1&&d.TrackId!=4);if(f==20)early=true;if(f==240)late=true;}
 }
 Check(early&&late);
});
Test("Empty selection click without prior history does not hide every export box",()=> {
 var t=new SelectionExportTimeline();t.Record(12,Array.Empty<SelectionItem>());
 Check(!t.HasHistory && t.Snapshot().Select(new(){D(0,1)},0,0,false).Count==1);
});
Test("Later action in same frame replaces earlier action",()=> {
 var t=new SelectionExportTimeline();var a=SelectionItem.From(D());t.Record(12,new[]{a});
 var b=SelectionItem.From(D(200));t.Record(12,new[]{b});Check(t.Snapshot().Advance(12).Single().Key==b.Key);
});
Test("Future appearance confirmation is not used in earlier export frames",()=> {
 var t=new SelectionExportTimeline();var a=SelectionItem.From(D());t.Record(12,new[]{a});
 var v=D();v.FrameIndex=30;v.Appearance=new float[]{0,1};Rebind(a,v);t.Record(30,new[]{a});
 var replay=t.Snapshot();replay.Advance(12);Check(replay.Items.Single().ConfirmedAppearances.Count==1);
 replay.Advance(30);Check(replay.Items.Single().ConfirmedAppearances.Count==2);
});
Test("Timed Kalman uncertainty composes across elapsed intervals",()=> {
 var a=new KalmanFilter();var b=new KalmanFilter();a.Init(0,0,100,100,true);b.Init(0,0,100,100,true);
 a.Predict(.2f);b.Predict(.1f);b.Predict(.1f);
 var field=typeof(KalmanFilter).GetField("_P",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
 var ap=(float[,])field.GetValue(a);var bp=(float[,])field.GetValue(b);
 for(int i=0;i<7;i++)for(int j=0;j<7;j++)Check(Math.Abs(ap[i,j]-bp[i,j])<.002f);
});
Test("Kalman can seed bounded measured turn velocity",()=> {
 var k=new KalmanFilter();k.Init(0,0,50,100,true,-60,90);k.Predict(.25f);var s=k.GetState();
 Check(Math.Abs(s.x+15)<.001&&Math.Abs(s.y-22.5)<.001);
});
TrackerService LocalTurnTracker() {
 var t=new TrackerService(null);
 for(int n=0;n<3;n++){var d=D();d.Confidence=.9f;d.SourceSeconds=n*.1;t.Update(new(){d},.45f,.1f,n*.1);}
 var tracks=(List<SortTrack>)typeof(TrackerService).GetField("_activeTracks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(t);
 tracks.Single().KalmanFilter.Init(1000,1000,100,100,true);
 return t;
}
Test("Isolated strong local turn preserves tracker ID despite bad prediction",()=> {
 var t=LocalTurnTracker();var d=D(20);d.Confidence=.9f;d.SourceSeconds=.3;
 var ds=t.Update(new(){d},.45f,.1f,.3);Check(ds.Single().TrackId==1&&!d.IsWeakContinuation);
});
Test("Local fallback cannot rescue a weak observation",()=> {
 var t=LocalTurnTracker();var d=D(20);d.Confidence=.2f;
 Check(t.Update(new(){d},.45f,.1f,.3).Count==0);
});
Test("Local fallback refuses overlapping competitor boxes",()=> {
 var t=LocalTurnTracker();var a=D(20);a.Confidence=.9f;var b=D(25);b.Confidence=.9f;
 var ds=t.Update(new(){a,b},.45f,.1f,.3);Check(ds.All(d=>d.TrackId!=1));
});
Test("Local fallback refuses stale track and wrong class",()=> {
 var a=LocalTurnTracker();var d=D(20);d.Confidence=.9f;Check(a.Update(new(){d},.45f,.1f,.8).Single().TrackId!=1);
 var b=LocalTurnTracker();var car=D(20,null,"car");car.Confidence=.9f;Check(b.Update(new(){car},.45f,.1f,.3).Single().TrackId!=1);
});
Test("Local fallback refuses appearance mismatch and distant boxes",()=> {
 var a=LocalTurnTracker();var d=D(20);d.Confidence=.9f;d.Appearance=new float[]{0,1};Check(a.Update(new(){d},.45f,.1f,.3).Single().TrackId!=1);
 var b=LocalTurnTracker();var far=D(150);far.Confidence=.9f;Check(b.Update(new(){far},.45f,.1f,.3).Single().TrackId!=1);
});
Test("Local fallback is reciprocal and refuses two eligible tracks",()=> {
 var t=new TrackerService(null);
 for(int n=0;n<3;n++){var a=D();var b=D(5);a.Confidence=b.Confidence=.9f;t.Update(new(){a,b},.45f,.1f,n*.1);}
 var tracks=(List<SortTrack>)typeof(TrackerService).GetField("_activeTracks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(t);
 foreach(var track in tracks)track.KalmanFilter.Init(1000,1000,100,100,true);
 var d=D(2);d.Confidence=.9f;Check(t.Update(new(){d},.45f,.1f,.3).Single().TrackId==3);
});
SelectionItem PendingRecovery() {
 var anchor=D(0,1);anchor.FrameIndex=0;anchor.SourceSeconds=0;
 var it=SelectionItem.From(anchor);
 for(int f=1;f<=3;f++) {var d=D(0,2);d.FrameIndex=f;d.SourceSeconds=f*.1;Apply(new[]{d},new[]{it},sourceSeconds:f*.1,observationFrame:f);}
 Check(it.PendingCount==3);return it;
}
Detection Borderline(int f,float similarity=.91f,bool weak=false,int id=2) {
 var d=D(0,id);d.FrameIndex=f;d.SourceSeconds=f*.1;d.IsWeakContinuation=weak;
 d.Appearance=new float[]{similarity*similarity,1-similarity*similarity};return d;
}
Test("One borderline frame holds evidence but never completes recovery",()=> {
 var it=PendingRecovery();var d=Borderline(4);
 Check(Apply(new[]{d},new[]{it},observationFrame:4,sourceSeconds:.4)==0&&it.PendingCount==3&&it.PendingBorderlineUsed);
 var good=Borderline(5,1);Check(Apply(new[]{good},new[]{it},observationFrame:5,sourceSeconds:.5)==1&&it.LastTrackId==2);
});
Test("Second borderline frame resets recovery instead of confirming",()=> {
 var it=PendingRecovery();Apply(new[]{Borderline(4)},new[]{it},observationFrame:4,sourceSeconds:.4);
 Check(Apply(new[]{Borderline(5)},new[]{it},observationFrame:5,sourceSeconds:.5)==0&&it.PendingCount==0);
});
Test("Weak or poor appearance cannot hold pending recovery",()=> {
 var a=PendingRecovery();Check(Apply(new[]{Borderline(4,.91f,true)},new[]{a},observationFrame:4,sourceSeconds:.4)==0&&a.PendingCount==0);
 var b=PendingRecovery();Check(Apply(new[]{Borderline(4,.8f)},new[]{b},observationFrame:4,sourceSeconds:.4)==0&&b.PendingCount==0);
});
Test("Other ID and expired borderline cannot hold evidence",()=> {
 var a=PendingRecovery();Apply(new[]{Borderline(4,.91f,false,3)},new[]{a},observationFrame:4,sourceSeconds:.4);Check(a.PendingCount==0);
 var b=PendingRecovery();Apply(new[]{Borderline(10)},new[]{b},observationFrame:10,sourceSeconds:1);Check(b.PendingCount==0);
});
Test("Manual correction clears held borderline evidence",()=> {
 var it=PendingRecovery();Apply(new[]{Borderline(4)},new[]{it},observationFrame:4,sourceSeconds:.4);
 Rebind(it,D(0,3));Check(!it.PendingBorderlineUsed&&it.PendingCount==0);
});
Test("All 15 ski phases and 3 variable schedules retain late target",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json")));
 var schedules=new List<int[]>();
 foreach(int step in new[]{1,2,4,8})for(int phase=0;phase<step;phase++)schedules.Add(new[]{0}.Concat(Enumerable.Range(1,frames.Count-1).Where(f=>f%step==phase)).ToArray());
 foreach(var pattern in new[]{new[]{1,2,4,8},new[]{2,4},new[]{4,8}}){var list=new List<int>();int f=0,n=0;while(f<frames.Count){list.Add(f);f+=pattern[n++%pattern.Length];}schedules.Add(list.ToArray());}
 var dense=new Dictionary<int,Detection>();
 foreach(var schedule in schedules) {
  var tracker=new TrackerService(null);SelectionItem it=null;int last=-1;
  foreach(int f in schedule){
   var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance,FrameIndex=f,SourceSeconds=f/24.0}).ToList();
   ds=tracker.Update(ds,.45f,.1f,f/24.0);
   if(f==0)it=SelectionItem.From(ds.Single(d=>d.X==383&&d.Y==718));
   Apply(ds,new[]{it},observationFrame:f,sourceSeconds:f/24.0);
   var accepted=ds.Where(d=>d.IsSelected).ToList();
   if(f>=184&&f<=270)Check(accepted.Count==1);
   foreach(var d in accepted){last=f;Check(d.SelectionNumber==it.Number&&d.TrackId!=1&&d.TrackId!=4);
    if(ReferenceEquals(schedule,schedules[0]))dense[f]=d;
    else if(f>=160&&dense.TryGetValue(f,out var reference))Check(Iou(d.X,d.Y,d.Width,d.Height,reference.X,reference.Y,reference.Width,reference.Height)>.9f);
   }
  }
  Check(last>=270);
 }
});

Test("Local fallback refuses abrupt merged-box size growth",()=> {
 var t=LocalTurnTracker();var d=D();d.Width=180;d.Confidence=.9f;
 Check(t.Update(new(){d},.45f,.1f,.3).Single().TrackId!=1);
});
Test("A borderline cannot preserve an unestablished sequence",()=> {
 var anchor=D(0,1);anchor.FrameIndex=0;anchor.SourceSeconds=0;var it=SelectionItem.From(anchor);
 Apply(new[]{Borderline(1,1)},new[]{it},observationFrame:1,sourceSeconds:.1);Check(it.PendingCount==1);
 Apply(new[]{Borderline(2)},new[]{it},observationFrame:2,sourceSeconds:.2);Check(it.PendingCount==0);
});
Test("Competing recovery candidates override borderline holding",()=> {
 var it=PendingRecovery();var a=Borderline(4);var b=Borderline(4,1,false,3);b.X=1;
 Check(Apply(new[]{a,b},new[]{it},observationFrame:4,sourceSeconds:.4)==0&&it.PendingCount==0&&it.State==SelectionState.Ambiguous);
});
Detection E(int x,int y,int w=100,int h=120,int? id=1)=>new Detection {ClassName="person",X=x,Y=y,Width=w,Height=h,FrameWidth=1000,FrameHeight=1000,Appearance=new float[]{1,0},Confidence=.9f,TrackId=id,SourceSeconds=0,FrameIndex=0};
Test("Edge geometry supports all four sides",()=> {
 Check(EdgeGeometry.TryMatch(E(10,300),E(0,310,35,120),100,120,.2,out _));
 Check(EdgeGeometry.TryMatch(E(890,300),E(965,310,35,120),100,120,.2,out _));
 Check(EdgeGeometry.TryMatch(E(300,10),E(310,0,100,30),100,120,.2,out _));
 Check(EdgeGeometry.TryMatch(E(300,870),E(310,965,100,35),100,120,.2,out _));
});
Test("Edge geometry supports adjacent corner clipping",()=> {
 Check(EdgeGeometry.TryMatch(E(0,850,70,120),E(0,965,35,35),100,120,.2,out _));
});
Test("Interior shrink and almost-border boxes receive no edge exception",()=> {
 Check(!EdgeGeometry.TryMatch(E(300,300),E(310,310,35,120),100,120,.2,out _));
 Check(!EdgeGeometry.TryMatch(E(10,300),E(3,310,35,120),100,120,.2,out _));
});
Test("Edge exception cannot explain unrelated-axis shrink or merged growth",()=> {
 Check(!EdgeGeometry.TryMatch(E(10,300),E(0,310,35,30),100,120,.2,out _));
 Check(!EdgeGeometry.TryMatch(E(10,300),E(0,310,180,120),100,120,.2,out _));
});
Test("Edge geometry refuses stale, missing or changed source bounds",()=> {
 Check(!EdgeGeometry.TryMatch(E(10,300),E(0,310,35,120),100,120,.61,out _));
 var no=E(0,310,35,120);no.FrameWidth=0;Check(!EdgeGeometry.TryMatch(E(10,300),no,100,120,.2,out _));
 var different=E(0,310,35,120);different.FrameHeight=1200;Check(!EdgeGeometry.TryMatch(E(10,300),different,100,120,.2,out _));
});
Test("Edge geometry refuses opposite-side jumps and tiny fragments",()=> {
 Check(!EdgeGeometry.TryMatch(E(0,300),E(965,310,35,120),100,120,.2,out _));
 Check(!EdgeGeometry.TryMatch(E(0,300),E(0,310,8,120),100,120,.2,out _));
});
Test("Nearby non-overlapping competitor blocks edge isolation",()=> {
 var a=E(0,300,35,120);var b=E(40,310,35,120);Check(!EdgeGeometry.Isolated(a,new[]{a,b},100,120));
 b.ClassName="car";Check(EdgeGeometry.Isolated(a,new[]{a,b},100,120));
});
TrackerService EdgeTracker() {
 var t=new TrackerService(null);
 for(int f=0;f<3;f++){var d=E(30-f*10,300);d.SourceSeconds=f*.1;d.FrameIndex=f;t.Update(new(){d},.45f,.1f,f*.1);}
 var track=((List<SortTrack>)typeof(TrackerService).GetField("_activeTracks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(t)).Single();
 track.KalmanFilter.Init(-200,360,100,120,true);return t;
}
Test("Weak clipped box continues established edge track and does not duplicate",()=> {
 var t=EdgeTracker();var d=E(0,310,35,120);d.Confidence=.2f;d.SourceSeconds=.3;
 var ds=t.Update(new(){d},.45f,.1f,.3);Check(ds.Count==1&&d.TrackId==1&&d.IsWeakContinuation&&d.IsEdgeContinuation);
});
Test("Weak edge detection never starts a new track",()=> {
 var t=new TrackerService(null);var d=E(0,310,35,120);d.Confidence=.2f;Check(t.Update(new(){d},.45f,.1f,.3).Count==0);
});
Test("Edge tracker rejects competitor and dissimilar fragment",()=> {
 var t=EdgeTracker();var a=E(0,310,35,120);var b=E(40,310,35,120);a.Confidence=b.Confidence=.2f;
 Check(t.Update(new(){a,b},.45f,.1f,.3).Count==0);
 t=EdgeTracker();a=E(0,310,35,120);a.Appearance=new float[]{0,1};a.Confidence=.2f;Check(t.Update(new(){a},.45f,.1f,.3).Count==0);
});
Test("Selection edge continuation preserves S, full size and frozen appearance",()=> {
 var it=SelectionItem.From(E(10,300));var before=it.RecentAppearance;int number=it.Number;
 var d=E(0,310,35,120);d.Appearance=new float[]{.96f,.04f};d.SourceSeconds=.1;d.FrameIndex=1;
 Check(Apply(new[]{d},new[]{it},observationFrame:1,sourceSeconds:.1)==1);
 Check(d.SelectionNumber==number&&it.FullWidth==100&&it.FullHeight==120&&ReferenceEquals(before,it.RecentAppearance));
});
Test("Short edge gap resumes only real detections, not empty observations",()=> {
 var it=SelectionItem.From(E(10,300));var d=E(0,310,35,120);d.FrameIndex=1;d.SourceSeconds=.1;
 Apply(new[]{d},new[]{it},observationFrame:1,sourceSeconds:.1);
 Check(Apply(Array.Empty<Detection>(),new[]{it},observationFrame:2,sourceSeconds:.2)==0);
 d=E(0,320,30,120);d.FrameIndex=3;d.SourceSeconds=.3;d.IsWeakContinuation=true;
 Check(Apply(new[]{d},new[]{it},observationFrame:3,sourceSeconds:.3)==1);
});
Test("Edge selection cannot steal a detection owned by another S",()=> {
 var a=SelectionItem.From(E(10,300));var owned=E(0,310,35,120,2);var b=SelectionItem.From(owned);
 owned.FrameIndex=1;owned.SourceSeconds=.1;Apply(new[]{owned},new[]{a,b},observationFrame:1,sourceSeconds:.1);
 Check(owned.SelectionKey!=a.Key);
});
Test("Detection row update preserves source bounds and edge flag",()=> {
 var a=D();var b=E(0,300);b.IsEdgeContinuation=true;a.UpdateFrom(b);
 Check(a.FrameWidth==1000&&a.FrameHeight==1000&&a.IsEdgeContinuation);
});
Test("Dense ski edge replay follows returned fragments without invented boxes",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json")));
 var tracker=new TrackerService(null);SelectionItem it=null;
 for(int f=0;f<frames.Count;f++) {
  var raw=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance,FrameWidth=720,FrameHeight=1280,FrameIndex=f,SourceSeconds=f/24.0}).ToList();
  var ds=tracker.Update(raw,.45f,.1f,f/24.0);Check(ds.Distinct().Count()==ds.Count);
  if(f==0)it=SelectionItem.From(ds.Single(d=>d.X==383&&d.Y==718));
  Apply(ds,new[]{it},observationFrame:f,sourceSeconds:f/24.0);var accepted=ds.Where(d=>d.IsSelected).ToList();
  if(f>=284) {
   var edge=raw.Where(d=>d.X<150&&d.Y>1050).ToList();
   Check(accepted.Count==(edge.Count>0 ? 1 : 0));
   foreach(var d in accepted)Check(raw.Contains(d)&&d.SelectionNumber==it.Number&&d.TrackId==3);
  }
 }
});
Test("Sparse ski edge follows f288 and export replay shares source bounds",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","ski-baseline-detections.json")));
 var tracker=new TrackerService(null);var timeline=new SelectionExportTimeline();SelectionExportTimeline.Replay replay=null;bool at288=false;
 for(int f=0;f<frames.Count;f+=8) {
  var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance,FrameWidth=720,FrameHeight=1280,FrameIndex=f,SourceSeconds=f/24.0}).ToList();
  ds=tracker.Update(ds,.45f,.1f,f/24.0);
  if(f==0){timeline.Record(0,new[]{SelectionItem.From(ds.Single(d=>d.X==383&&d.Y==718))});replay=timeline.Snapshot();}
  var accepted=replay.Select(ds,f,f/24.0,false);
  if(f==288)at288=accepted.Any(d=>d.X==0&&d.Y==1113&&d.Width==30);
  if(f>=315)Check(accepted.Count==0);
 }
 Check(at288);
});

Test("Two eligible edge tracks remain ambiguous rather than stealing a weak fragment",()=> {
 var t=EdgeTracker();var tracks=(List<SortTrack>)typeof(TrackerService).GetField("_activeTracks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(t);
 var original=tracks.Single();var other=new SortTrack{Id=2,ClassName="person",LastDetection=original.LastDetection,ReferenceAppearance=original.ReferenceAppearance,FullWidth=100,FullHeight=120,HitStreak=3};
 other.KalmanFilter.Init(-200,360,100,120,true);tracks.Add(other);
 var d=E(0,310,35,120);d.Confidence=.2f;Check(t.Update(new(){d},.45f,.1f,.3).Count==0);
});
Test("Replay clears border reference and restores full anchor geometry",()=> {
 var it=SelectionItem.From(E(10,300));var d=E(0,310,35,120);d.FrameIndex=1;d.SourceSeconds=.1;
 Apply(new[]{d},new[]{it},observationFrame:1,sourceSeconds:.1);Check(it.EdgeAppearance!=null);
 ResetForReplay(it);Check(it.EdgeAppearance==null&&it.FullWidth==100&&it.FullHeight==120&&it.W==100);
});
Detection C(int x=300,int y=300,double seconds=0,int frame=0,float confidence=.9f) {
 var d=E(x,y,100,100);d.ClassName="car";d.SourceSeconds=seconds;d.FrameIndex=frame;d.Confidence=confidence;return d;
}
Test("Strong sparse bootstrap preserves ID and seeds observed velocity",()=> {
 var tracker=new TrackerService(null);var first=C();tracker.Update(new(){first},.45f,.1f,0);
 var next=C(245,255,.32,8);Check(tracker.Update(new(){next},.45f,.1f,.32).Single().TrackId==1);
 var third=C(190,210,.64,16);Check(tracker.Update(new(){third},.45f,.1f,.64).Single().TrackId==1);
});
Test("Sparse selection initialisation accepts verified same ID before velocity exists",()=> {
 var it=SelectionItem.From(C());var next=C(245,255,.32,8);next.TrackId=1;
 Check(Apply(new[]{next},new[]{it},observationFrame:8,sourceSeconds:.32)==1&&it.HasVelocity&&it.VelocityX<0);
});
Test("Weak observations cannot bootstrap a sparse track",()=> {
 var tracker=new TrackerService(null);tracker.Update(new(){C()},.45f,.1f,0);
 Check(tracker.Update(new(){C(245,255,.32,8,.2f)},.45f,.1f,.32).Count==0);
});
Test("Bootstrap refuses stale, distant and wrong-colour observations",()=> {
 foreach(int variant in new[]{0,1,2}) {
  var tracker=new TrackerService(null);tracker.Update(new(){C()},.45f,.1f,0);
  var d=C(variant==1?100:245,255,variant==0?.41:.32,8);if(variant==2)d.Appearance=new float[]{0,1};
  Check(tracker.Update(new(){d},.45f,.1f,d.SourceSeconds).Single().TrackId!=1);
 }
});
Test("Bootstrap refuses overlapping competing detections",()=> {
 var tracker=new TrackerService(null);tracker.Update(new(){C()},.45f,.1f,0);
 var a=C(245,255,.32,8);var b=C(246,256,.32,8);
 Check(tracker.Update(new(){a,b},.45f,.1f,.32).All(d=>d.TrackId!=1));
});
Test("Bootstrap refuses two plausible tracks for one candidate",()=> {
 var tracker=new TrackerService(null);tracker.Update(new(){C(),C(301,301)},.45f,.1f,0);
 var d=C(245,255,.32,8);Check(tracker.Update(new(){d},.45f,.1f,.32).Single().TrackId==3);
});
Test("Bootstrap cannot use abrupt size growth or a border fragment",()=> {
 var tracker=new TrackerService(null);tracker.Update(new(){C()},.45f,.1f,0);var large=C(245,255,.32,8);large.Width=160;
 Check(tracker.Update(new(){large},.45f,.1f,.32).Single().TrackId!=1);
 tracker=new TrackerService(null);tracker.Update(new(){C(55,300)},.45f,.1f,0);var border=C(0,255,.32,8);
 Check(tracker.Update(new(){border},.45f,.1f,.32).Single().TrackId!=1);
});
Test("Selection bootstrap refuses weak and mismatched appearance",()=> {
 var a=SelectionItem.From(C());var weak=C(245,255,.32,8);weak.TrackId=1;weak.IsWeakContinuation=true;
 Check(Apply(new[]{weak},new[]{a},observationFrame:8,sourceSeconds:.32)==0);
 var b=SelectionItem.From(C());var wrong=C(245,255,.32,8);wrong.TrackId=1;wrong.Appearance=new float[]{.8f,.2f};
 Check(Apply(new[]{wrong},new[]{b},observationFrame:8,sourceSeconds:.32)==0);
});
SelectionItem ShortLostCar() {
 var it=SelectionItem.From(C());var steady=C(300,300,.04,1);steady.TrackId=1;
 Apply(new[]{steady},new[]{it},observationFrame:1,sourceSeconds:.04);
 Apply(Array.Empty<Detection>(),new[]{it},observationFrame:2,sourceSeconds:.08);return it;
}
Detection ReturnedCar(int frame=3,double seconds=.12,int id=1) {
 var d=C(300,300,seconds,frame,.2f);d.TrackId=id;d.IsWeakContinuation=true;return d;
}
Test("Verified short same-ID gap needs two actual observations, not one",()=> {
 var it=ShortLostCar();var first=ReturnedCar();
 Check(Apply(new[]{first},new[]{it},observationFrame:3,sourceSeconds:.12)==0&&it.PendingCount==1);
 Check(Apply(new[]{ReturnedCar(4,.16)},new[]{it},observationFrame:4,sourceSeconds:.16)==1);
});
Test("Repeated observations never complete short recovery",()=> {
 var it=ShortLostCar();var first=ReturnedCar();Apply(new[]{first},new[]{it},observationFrame:3,sourceSeconds:.12);
 Check(Apply(new[]{first},new[]{it},observationFrame:3,sourceSeconds:.12)==0&&it.PendingCount==1);
});
Test("New ID and longer gap keep full confirmation policy",()=> {
 var a=ShortLostCar();Apply(new[]{ReturnedCar(3,.12,2)},new[]{a},observationFrame:3,sourceSeconds:.12);
 Check(Apply(new[]{ReturnedCar(4,.16,2)},new[]{a},observationFrame:4,sourceSeconds:.16)==0&&a.PendingCount==2);
 var b=ShortLostCar();Apply(new[]{ReturnedCar(15,.60)},new[]{b},observationFrame:15,sourceSeconds:.60);
 Check(Apply(new[]{ReturnedCar(16,.64)},new[]{b},observationFrame:16,sourceSeconds:.64)==0&&b.PendingCount==2);
});
Test("Neighbour and reduced similarity cannot use two-observation recovery",()=> {
 var a=ShortLostCar();
 for(int f=3;f<=4;f++){var d=ReturnedCar(f,f*.04);var neighbour=ReturnedCar(f,f*.04,2);neighbour.X=301;Apply(new[]{d,neighbour},new[]{a},observationFrame:f,sourceSeconds:f*.04);}
 Check(a.PendingCount==2&&!a.LastMatched);
 var b=ShortLostCar();for(int f=3;f<=4;f++){var d=ReturnedCar(f,f*.04);d.Appearance=new float[]{.72f,.28f};Apply(new[]{d},new[]{b},observationFrame:f,sourceSeconds:f*.04);}
 Check(b.PendingCount==2&&!b.LastMatched);
});
Test("Short recovery cannot bypass ownership by another S",()=> {
 var a=ShortLostCar();var b=SelectionItem.From(ReturnedCar());
 for(int f=3;f<=4;f++){var d=ReturnedCar(f,f*.04);Apply(new[]{d},new[]{a,b},observationFrame:f,sourceSeconds:f*.04);Check(d.SelectionKey!=a.Key);}
});
Test("Road sparse initial car stays selected across all eight starting phases",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","road-baseline-detections.json")));
 for(int phase=0;phase<8;phase++) {
  var tracker=new TrackerService(null);SelectionItem it=null;int last=-1;
  foreach(int f in new[]{0}.Concat(Enumerable.Range(1,frames.Count-1).Where(f=>f%8==phase))) {
   var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance,FrameWidth=3840,FrameHeight=2160,FrameIndex=f,SourceSeconds=f/25.0}).ToList();
   ds=tracker.Update(ds,.45f,.1f,f/25.0);Check(ds.Distinct().Count()==ds.Count);
   if(f==0)it=SelectionItem.From(ds.Single(d=>d.X==2940));
   Apply(ds,new[]{it},observationFrame:f,sourceSeconds:f/25.0);
   var selected=ds.Where(d=>d.IsSelected).ToList();if(f<=150)Check(selected.Count==1);
   foreach(var d in selected){Check(d.TrackId==1&&d.SelectionNumber==it.Number);last=f;}
  }
  Check(last>=150);
 }
});
Test("Road same-ID recovery extends both cars without sharing a detection",()=> {
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","road-baseline-detections.json")));
 var tracker=new TrackerService(null);var items=new List<SelectionItem>();SelectionItem a=null,b=null;bool at166=false,at229=false;
 for(int f=0;f<frames.Count;f++) {
  var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance,FrameWidth=3840,FrameHeight=2160,FrameIndex=f,SourceSeconds=f/25.0}).ToList();
  ds=tracker.Update(ds,.45f,.1f,f/25.0);
  if(f==0){a=SelectionItem.From(ds.Single(d=>d.X==2940));items.Add(a);}
  if(f==54){var target=ds.Single(d=>d.X==3075&&d.Y==1389);Check(Set(items,target,true));b=items.Last();}
  Apply(ds,items,observationFrame:f,sourceSeconds:f/25.0);var accepted=ds.Where(d=>d.IsSelected).ToList();
  Check(accepted.Select(d=>d.SelectionKey).Distinct().Count()==accepted.Count);
  foreach(var d in accepted){if(d.SelectionKey==a.Key){Check(d.TrackId==1);if(f==166)at166=true;}else{Check(b!=null&&d.SelectionKey==b.Key&&d.TrackId==8);if(f==229)at229=true;}}
  if(accepted.Count==2){var aa=accepted.Single(d=>d.SelectionKey==a.Key);var bb=accepted.Single(d=>d.SelectionKey==b.Key);Check(aa.X<bb.X&&aa.Y<bb.Y);}
  if(f==168||f==300)Check(!accepted.Any(d=>d.SelectionKey==a.Key));
 }
 Check(at166&&at229);
});

Test("ROI OFF never schedules extra work and enabling starts fresh",()=> {
 var s=new RoiSchedule();Check(!s.Due(false,true,0,0));Check(s.Due(true,true,10,.4));
 Check(!s.Due(false,true,11,.44));Check(s.Due(true,true,12,.48));
});
Test("ROI source-time cadence is five extra passes per source second at 25fps",()=> {
 var s=new RoiSchedule();var run=new List<int>();for(int f=0;f<25;f++)if(s.Due(true,true,f,f/25.0))run.Add(f);
 Check(run.SequenceEqual(new[]{0,5,10,15,20}));
});
Test("ROI sparse processing runs one pass per eligible observation, no catch-up burst",()=> {
 var s=new RoiSchedule();Check(s.Due(true,true,0,0));Check(s.Due(true,true,8,.32));Check(s.Due(true,true,80,3.2));
 Check(!s.Due(true,true,81,3.24));
});
Test("ROI repeated frame is not scheduled twice",()=> {
 var s=new RoiSchedule();Check(s.Due(true,true,0,0));Check(!s.Due(true,true,0,0));Check(!s.Due(true,true,1,.04));
});
Test("ROI seek backwards and reset restart the schedule",()=> {
 var s=new RoiSchedule();Check(s.Due(true,true,30,1.2));Check(!s.Due(true,true,31,1.24));
 Check(s.Due(true,true,4,.16));s.Reset();Check(s.Due(true,true,4,.16));
});
Test("ROI photo and paused preview run without advancing video schedule",()=> {
 var s=new RoiSchedule();Check(s.Due(true,true,0,0));Check(s.Due(true,true,1,.04,true));
 Check(!s.Due(true,true,1,.04));Check(s.Due(true,false,0,double.NaN));
 Check(!s.Due(false,false,0,double.NaN,true));
});
Test("ROI invalid video clocks cannot create unbounded repeated passes",()=> {
 var s=new RoiSchedule();Check(!s.Due(true,true,1,double.NaN));Check(!s.Due(true,true,-1,0));Check(!s.Due(true,true,1,-.1));Check(!s.Due(true,true,1,double.PositiveInfinity));
});
Test("ROI export has its own cadence independent of playback state",()=> {
 var live=new RoiSchedule();var export=new RoiSchedule();Check(live.Due(true,true,100,4));
 Check(export.Due(true,true,0,0));Check(!export.Due(true,true,1,.04));Check(live.Due(true,true,105,4.2));
});
Test("ROI 4K crop equals diagnosed region; portrait is bounded and small sources skip",()=> {
 Check(RoiPolicy.Region(3840,2160)==new RoiPolicy.Window(1300,300,1400,950));
 var p=RoiPolicy.Region(1080,1920);Check(p.Width>0&&p.Height>0&&p.X+p.Width<=1080&&p.Y+p.Height<=1920);
 Check(RoiPolicy.Region(320,240)==default);
});
Detection CropBox(int x=100,int y=100,float confidence=.8f) => new Detection{ClassName="car",X=x,Y=y,Width=50,Height=40,Confidence=confidence};
var region=RoiPolicy.Region(3840,2160);
Test("ROI translation returns source coordinates without mutating crop",()=> {
 var d=CropBox();d.Timestamp=new DateTime(2026,9,15,12,0,0);var m=RoiPolicy.MapCrop(new[]{d},region,3840,2160).Single();
 Check(m.Timestamp==d.Timestamp);
 Check(m.X==1400&&m.Y==400&&m.Width==50&&m.Height==40&&m.FrameWidth==3840&&m.FrameHeight==2160);
 Check(d.X==100&&d.Y==100&&m.TrackId==null&&m.AnchorIdx==-1);
});
Test("ROI seg polygon is translated once and no crop buffer is shared",()=> {
 var d=CropBox();d.MaskPolygon=new float[]{100,100,150,100,150,140};d.AnchorIdx=7;
 var m=RoiPolicy.MapCrop(new[]{d},region,3840,2160).Single();
 Check(m.MaskPolygon.SequenceEqual(new float[]{1400,400,1450,400,1450,440}));
 Check(d.MaskPolygon[0]==100&&m.AnchorIdx==-1&&!ReferenceEquals(m.MaskPolygon,d.MaskPolygon));
});
Test("ROI corrupt polygons do not enter renderer",()=> {
 foreach(var p in new[]{new float[]{1,2,3},new float[]{1,2,3,4,5,float.NaN},new float[]{1,2,3,4,5,6,7}}) {
  var d=CropBox();d.MaskPolygon=p;Check(RoiPolicy.MapCrop(new[]{d},region,3840,2160).Single().MaskPolygon.Length==0);
 }
});
Test("ROI invalid boxes and invalid crop geometry are dropped",()=> {
 var a=CropBox(-1);var b=CropBox();b.Width=0;var c=CropBox(confidence:float.NaN);var d=CropBox(1390);
 Check(RoiPolicy.MapCrop(new[]{a,b,c,d},region,3840,2160).Count==0);
 Check(RoiPolicy.MapCrop(new[]{CropBox()},new(3800,300,100,100),3840,2160).Count==0);
});
Test("ROI artificial crop boundaries cannot shrink a full source object",()=> {
 foreach(var d in new[]{CropBox(2),CropBox(y:2),CropBox(1348),CropBox(y:908)})
  Check(RoiPolicy.MapCrop(new[]{d},region,3840,2160).Count==0);
 Check(RoiPolicy.MapCrop(new[]{CropBox(3,3)},region,3840,2160).Count==1);
});
Test("ROI physical source boundary is distinct from artificial crop border",()=> {
 var d=CropBox(0,0);Check(RoiPolicy.MapCrop(new[]{d},new(0,0,200,200),200,200).Count==1);
});
Test("ROI empty result preserves original order and list identity",()=> {
 var all=new List<Detection>{CropBox(confidence:.2f),CropBox(400,100,.9f)};
 Check(ReferenceEquals(all,RoiPolicy.Merge(all,new(),.45f))&&all[0].Confidence==.2f);
});
Test("ROI NMS retains one high-confidence observation and its mask",()=> {
 var full=CropBox(1400,400,.2f);var crop=CropBox();crop.MaskPolygon=new float[]{100,100,150,100,150,140};
 var mapped=RoiPolicy.MapCrop(new[]{crop},region,3840,2160);var m=RoiPolicy.Merge(new(){full},mapped,.45f);
 Check(m.Count==1&&m[0].Confidence==.8f&&m[0].MaskPolygon[0]==1400);
});
Test("ROI NMS uses original class-agnostic overlap policy across passes",()=> {
 var a=CropBox();var b=CropBox(confidence:.9f);b.ClassName="truck";
 Check(RoiPolicy.Merge(new(){a},new(){b},.45f).Single().ClassName=="truck");
});
Test("ROI adjacent cars remain separate",()=> {
 Check(RoiPolicy.Merge(new(){CropBox()},new(){CropBox(180)},.45f).Count==2);
});
Test("ROI equal confidence prefers full-frame geometry deterministically",()=> {
 var full=CropBox();var extra=CropBox();Check(ReferenceEquals(full,RoiPolicy.Merge(new(){full},new(){extra},.45f).Single()));
});
Test("ROI merged detection gets appearance from source before tracking",()=> {
 var tracker=new TrackerService(null);var d=CropBox(1400,400,.9f);d.Appearance=new float[]{1,0};
 tracker.Update(new(){d},.45f,.1f,0);
 var crop=CropBox();var mapped=RoiPolicy.MapCrop(new[]{crop},region,3840,2160);
 var all=RoiPolicy.Merge(new(),mapped,.45f);foreach(var q in all)q.Appearance=new float[]{1,0};
 Check(tracker.Update(all,.45f,.1f,.2).Single().TrackId==1);
});

Test("ROI every-frame mode still refuses repeat frame and respects OFF",()=> {
 var s=new RoiSchedule();Check(s.Due(true,true,0,0,everyFrame:true));Check(s.Due(true,true,1,.04,everyFrame:true));
 Check(!s.Due(true,true,1,.04,everyFrame:true));Check(!s.Due(false,true,2,.08,everyFrame:true));
});
(int R,int B,int Extra) ReplayRoi(bool enabled,bool every) {
 var fixture=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures");
 var frames=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(fixture,"road-baseline-detections.json")));
 var crops=System.Text.Json.JsonSerializer.Deserialize<List<List<Detection>>>(System.IO.File.ReadAllText(System.IO.Path.Combine(fixture,"road-roi-crop-detections.json")));
 var tracker=new TrackerService(null);var schedule=new RoiSchedule();var items=new List<SelectionItem>();Guid r=Guid.Empty,b=Guid.Empty;int lastR=-1,lastB=-1,extra=0;
 for(int f=0;f<frames.Count;f++) {
  var ds=frames[f].Select(d=>new Detection{ClassName=d.ClassName,Confidence=d.Confidence,X=d.X,Y=d.Y,Width=d.Width,Height=d.Height,Appearance=d.Appearance}).ToList();
  if(schedule.Due(enabled,true,f,f/25.0,everyFrame:every)) {
   extra++;var mapped=RoiPolicy.MapCrop(crops[f],region,3840,2160);
   foreach(var d in mapped)d.Appearance=crops[f].Single(c=>c.X+region.X==d.X&&c.Y+region.Y==d.Y&&c.Width==d.Width&&c.Height==d.Height&&c.Confidence==d.Confidence).Appearance;
   ds=RoiPolicy.Merge(ds,mapped,.45f);
  }
  foreach(var d in ds){d.FrameIndex=f;d.SourceSeconds=f/25.0;d.FrameWidth=3840;d.FrameHeight=2160;}
  ds=tracker.Update(ds,.45f,.1f,f/25.0);
  if(f==0){var it=SelectionItem.From(ds.Single(d=>d.X==2940));items.Add(it);r=it.Key;}
  if(f==54){var d=ds.Where(d=>d.ClassName=="car"&&d.X>2700&&d.Y>1100&&d.Confidence>=.45f).OrderByDescending(d=>d.Confidence).First();Check(Set(items,d,true));b=items.Last().Key;}
  Apply(ds,items,observationFrame:f,sourceSeconds:f/25.0);
  var accepted=ds.Where(d=>d.IsSelected).ToList();Check(accepted.Select(d=>d.SelectionKey).Distinct().Count()==accepted.Count);
  Check(accepted.Select(d=>d.TrackId).Distinct().Count()==accepted.Count);
  foreach(var d in accepted){Check(d.FrameIndex==f&&d.FrameWidth==3840&&d.FrameHeight==2160);if(d.SelectionKey==r){lastR=f;Check(d.TrackId==1);}else{Check(d.SelectionKey==b);lastB=f;}}
  if(accepted.Count==2){var a=accepted.Single(d=>d.SelectionKey==r);var bb=accepted.Single(d=>d.SelectionKey==b);Check(a.X<bb.X&&a.Y<bb.Y);}
 }
 return(lastR,lastB,extra);
}
Test("ROI OFF reproduces last road package and makes zero extra passes",()=> {
 var q=ReplayRoi(false,false);Check(q.R==167&&q.B==229&&q.Extra==0);
});
Test("ROI economic replay is bounded and does not imply dense continuous recovery",()=> {
 var q=ReplayRoi(true,false);Check(q.R==167&&q.B==230&&q.Extra==108);
});
Test("ROI full-frequency replay extends two distinct selected cars without invented observations",()=> {
 var q=ReplayRoi(true,true);Check(q.R==402&&q.B==410&&q.Extra==538);
});

Console.WriteLine($"TOTAL {count}/{count} PASS");
