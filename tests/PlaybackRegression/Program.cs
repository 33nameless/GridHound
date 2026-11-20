using ObjectDetectTrackApp.Services;
var files=new FileSourceService();var pump=new FramePump(files,new LogService());
int passes=0;void Check(bool b,string name){if(!b)throw new Exception(name);Console.WriteLine("PASS "+name);passes++;}
var frames=new List<int>();var gate=new object();int finished=0;
pump.FrameReady+=(_,f)=>{lock(gate)frames.Add(f.FrameIndex);};
pump.PlaybackFinished+=(_,_)=>Interlocked.Increment(ref finished);
pump.Play("clip",startFrame:0);await pump.WaitForIdleAsync();
Check(frames.SequenceEqual(Enumerable.Range(0,6)),"first pass processes every frame");
Check(!pump.IsPlaying && finished==1 && !pump.FinishedCancelled,"natural completion");
frames.Clear();pump.Play("clip",startFrame:0);await pump.WaitForIdleAsync();
Check(frames.SequenceEqual(Enumerable.Range(0,6)),"replay delivers frames from zero");
Check(files.Opens==2,"replay reopens source");
frames.Clear();var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
EventHandler<ObjectDetectTrackApp.Models.FrameData> pause=(_,f)=>{if(f.FrameIndex==0){pump.SetPaused(true);first.TrySetResult();}};
pump.FrameReady+=pause;pump.Play("clip");await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(pump.IsPaused && pump.IsPlaying,"pause preserves active run");
int opens=files.Opens;pump.FrameReady-=pause;pump.Resume();await pump.WaitForIdleAsync();
Check(files.Opens==opens && frames.SequenceEqual(Enumerable.Range(0,6)),"resume does not reopen or rewind");
var held=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var release=new ManualResetEventSlim();
EventHandler<ObjectDetectTrackApp.Models.FrameData> block=(_,f)=>{held.TrySetResult();release.Wait();};
pump.FrameReady+=block;pump.Play("clip");await held.Task.WaitAsync(TimeSpan.FromSeconds(5));pump.Stop();
var drain=pump.WaitForIdleAsync();Check(!drain.IsCompleted,"drain waits for in-flight callback");
release.Set();await drain.WaitAsync(TimeSpan.FromSeconds(5));pump.FrameReady-=block;
Check(pump.FinishedCancelled && !pump.IsPlaying,"stop drains cancelled run");
var correction=new CorrectionPauseSession();
Check(correction.Begin(true,true,true,false,1,"clip"),"correction requests pause on playing video");
Check(correction.Finish(true,true,1,"clip"),"successful selection or cancel restores own pause");
Check(!correction.Finish(true,true,1,"clip"),"completion resumes at most once");
Check(!correction.Begin(true,true,true,true,1,"clip")&&!correction.Finish(true,true,1,"clip"),"pre-existing pause is preserved");
Check(!correction.Begin(false,true,true,false,1,"clip")&&!correction.Finish(true,true,1,"clip"),"disabled setting never owns pause");
Check(!correction.Begin(true,false,true,false,1,"photo")&&!correction.Finish(true,true,1,"photo"),"photo never requests pause");
Check(!correction.Begin(true,true,false,false,1,"clip"),"stopped video does not start correction pause");
correction.Begin(true,true,true,false,1,"clip");
Check(!correction.Finish(false,false,1,"clip"),"stop or EOF cannot be resurrected");
correction.Begin(true,true,true,false,1,"clip");
Check(!correction.Finish(true,true,2,"clip"),"new run is not resumed by old correction");
correction.Begin(true,true,true,false,1,"clip");
Check(!correction.Finish(true,true,1,"other"),"other source cannot be resumed");
correction.Begin(true,true,true,false,1,"clip");correction.Cancel();
Check(!correction.Finish(true,true,1,"clip"),"manual transport cancels automatic ownership");
correction.Begin(true,true,true,false,1,"clip");
Check(!correction.Finish(true,false,1,"clip"),"already playing is not resumed twice");
// Exercise the real pump's pause/resume, using only the file source as a test double.
frames.Clear();var frozen=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
EventHandler<ObjectDetectTrackApp.Models.FrameData> autoPause=(_,f)=> {
 if(f.FrameIndex==0) {
  if(correction.Begin(true,true,pump.IsPlaying,pump.IsPaused,3,"clip"))pump.SetPaused(true);
  frozen.TrySetResult();
 }
};
pump.FrameReady+=autoPause;pump.Play("clip");await frozen.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(pump.IsPaused,"real pump paused by correction policy");
pump.FrameReady-=autoPause;
if(correction.Finish(pump.IsPlaying,pump.IsPaused,3,"clip"))pump.Resume();
await pump.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
Check(frames.SequenceEqual(Enumerable.Range(0,6)),"correction resume continues same run without rewind");
Console.WriteLine($"TOTAL {passes}/{passes} PASS");
namespace ObjectDetectTrackApp.Models {public class FrameData {public int FrameIndex;public string SourcePath;public bool IsStream;}}
namespace ObjectDetectTrackApp.Services {
 public class LogService {public void Info(string s){}}
 public class FileSourceService {
  int index;public int Opens;
  public bool IsImageFile(string s)=>false;public bool IsVideoFile(string s)=>true;
  public bool OpenImage(string s,out int w,out int h){w=h=0;return false;}
  public ObjectDetectTrackApp.Models.FrameData GetCurrentImageFrame()=>null;
  public bool OpenVideo(string s,out int n,out int fps,out int w,out int h){Opens++;n=6;fps=1000;w=h=10;index=0;return true;}
  public bool SeekFrame(int i){index=i;return true;}
  public bool ReadNextFrame(out ObjectDetectTrackApp.Models.FrameData f,bool rawOnly=false){f=new(){FrameIndex=index++};return f.FrameIndex<6;}
  public void Close(){}
 }
}
