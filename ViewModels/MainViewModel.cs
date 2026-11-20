using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ObjectDetectTrackApp.Models;
using ObjectDetectTrackApp.Services;
using OpenCvSharp;

namespace ObjectDetectTrackApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FileSourceService _fileSourceService;
        private readonly LogService _logService;
        private readonly FramePump _framePump;
        private DetectorService _detectorService;
        private readonly RenderService _renderService;
        private readonly TrackerService _tracker;
        private CancellationTokenSource _cancellationTokenSource;
        private volatile bool _isRecording;          // пишем в SessionDetections только во время прогона
        private bool _sessionOverflowLogged;
        private long _sessionDropped;
        [ObservableProperty] private bool exportInputsEnabled = true;
        private readonly SelectionExportTimeline _exportTimeline = new();
        private sealed record ExportSettings(bool Tracking, bool PersonOnly, float Confidence, float Low,
            float Nms, bool Active, HighlightMode Highlight, bool Roi, bool RoiEveryFrame, SelectionExportTimeline.Replay Selection);
        private ExportSettings CaptureExportSettings()
        {
            if((int)HlMode!=0 && !_detectorService.IsSegModel)
                throw new InvalidOperationException("Режим контура/маски ещё не применён: дождись загрузки seg-модели или выбери боксы и повтори экспорт.");
            lock(_selLock) return new(EnableTracking,FilterPersonOnly,ConfidenceThreshold,ContinuationThreshold,
                NmsThreshold,ActiveReacquisition,HlMode,EnableRoi,RoiEveryFrame,_exportTimeline.Snapshot());
        }
        private List<Detection> DetectForExport(Mat raw,ExportSettings settings,bool video,
            RoiSchedule schedule,int frame,double seconds)
        {
            lock(_processing) {
                _detectorService.BuildMasks=(int)settings.Highlight!=0;
                return _detectorService.DetectWithRoi(raw,settings.Tracking && video ? settings.Low : settings.Confidence,
                    settings.Nms,schedule.Due(settings.Roi,video,frame,seconds,everyFrame:settings.RoiEveryFrame));
            }
        }
        private void RecordSelectionAction(int frame)
        {
            lock(_selLock) _exportTimeline.Record(frame,_selection);
        }
        private const int MaxSessionItems = 20000;   // защита от раздувания памяти на длинном видео

        private readonly object _processing = new();
        private readonly RoiSchedule _roiSchedule = new();
        private readonly object _cacheLock = new();
        private Mat _cachedFrame;
        private List<Detection> _allCurrent = new();
        // UI-thread snapshot committed together with the visible bitmap, not the throttled list.
        private List<Detection> _previewCandidates = new();
        private double _lastPreviewSourceSeconds=double.NaN;
        private readonly System.Collections.Concurrent.ConcurrentQueue<Detection> _manualTrackAnchors=new();
        private float ContinuationThreshold => Math.Min(0.10f,ConfidenceThreshold);
        private volatile int _sourceVersion;
        private long _renderRequest;
        private long _renderCommitted;
        private int _sessionVersion;
        private int _selectionRevision;
        private int _trackerSourceVersion = -1;
        private int _lastDetectedIndex = -1;
        private long _lastListUpdate;
        private int _selectionStartFrame;
        [ObservableProperty] private int playbackModeIndex;
        partial void OnPlaybackModeIndexChanged(int value) => _framePump.ExactFrames = value == 1;
        private volatile bool _isExporting;
        private string _currentSourcePath;

        // ---- Ручной выбор объектов (клик по превью) ----
        private readonly object _selLock = new object();
        private List<SelectionLogic.SelectionItem> _selection = new();
        private volatile int _lastPreviewFrameIndex = -1;

        // ---- Плавный стрим: отрисовка по темпу помпа, инференс — отдельный потребитель ----
        private volatile List<Detection> _liveView = new List<Detection>();
        private FrameData _pendingFd;
        private readonly SemaphoreSlim _detSignal = new SemaphoreSlim(0, 1);
        private CancellationTokenSource _detCts;
        private Task _detTask;
        private volatile bool _pumpAlive;

        [ObservableProperty]
        private bool selectMode;

        [ObservableProperty]
        private int selectedCount;

        [ObservableProperty]
        private string trackingInfo = "all";
        [ObservableProperty] private bool activeReacquisition;
        [ObservableProperty] private string selectionSummary = "Выбор не задан";
        [ObservableProperty] private string correctionNumber = "";
        [ObservableProperty] private string correctionHint = "Для уточнения введи номер S и нажми кнопку";
        private int _correctionTarget;
        [ObservableProperty] private bool correctionNoticeVisible;
        [ObservableProperty] private string correctionNoticeTitle = "";
        [ObservableProperty] private string correctionNoticeText = "";

        private void ShowCorrectionNotice(bool resumesAutomatically)
        {
            CorrectionNoticeVisible=_correctionTarget>0 && _framePump.IsPaused;
            CorrectionNoticeTitle=$"Уточнение S{_correctionTarget} · видео на паузе";
            CorrectionNoticeText=resumesAutomatically
                ? "Нажми на нужный обнаруженный объект в превью — видео продолжится. Если объекта нет, выбери «Продолжить без уточнения»."
                : "Нажми на нужный обнаруженный объект в превью, чтобы подтвердить выбор. Видео останется на паузе; для продолжения нажми Play.";
        }

        [RelayCommand]
        private void CancelSelectionCorrection()
        {
            if(_correctionTarget<=0) return;
            EndSelectionCorrection(true);
            CorrectionHint="Уточнение отменено";
        }
        [ObservableProperty] private bool pauseOnCorrection = true;
        private readonly CorrectionPauseSession _correctionPause = new();
        [ObservableProperty] private bool autoRequestCorrection = true;
        private readonly AutoCorrectionRequests _autoRequests = new();

        private void CheckAutomaticCorrection()
        {
            if(!AutoRequestCorrection || _correctionTarget>0 || _isExporting ||
                !_framePump.IsVideoSource || !_framePump.IsPlaying || _framePump.IsPaused) return;
            int number=0;
            lock(_selLock) {
                foreach(var item in _selection) {
                    if(_autoRequests.Observe(item.Key,item.State==SelectionLogic.SelectionState.Tracked,
                        item.LastObservedClock,item.LastConfirmedClock)) { number=item.Number; break; }
                }
            }
            if(number==0) return;
            CorrectionNumber=number.ToString();
            ArmSelectionCorrection();
            if(_correctionTarget>0) CorrectionHint=$"S{number}: не удалось уверенно восстановить объект. Укажи его или продолжи без уточнения.";
        }

        [RelayCommand]
        private void ContinueWithoutCorrection()
        {
            if(_correctionTarget<=0) return;
            EndSelectionCorrection(true);
            CorrectionHint="Продолжаю без уточнения. Повторного запроса для этой потери не будет.";
        }

        private void EndSelectionCorrection(bool restorePlayback)
        {
            lock(_selLock) {
                var target=_selection.FirstOrDefault(i=>i.Number==_correctionTarget);
                if(target!=null) _autoRequests.Suppress(target.Key);
            }
            _correctionTarget=0;
            CorrectionNoticeVisible=false;
            bool resume=restorePlayback && _correctionPause.Finish(_framePump.IsPlaying,
                _framePump.IsPaused,_sessionVersion,_currentSourcePath);
            if(!restorePlayback) _correctionPause.Cancel();
            if(resume)
            {
                _framePump.Resume();
                IsPlaying=true;
                StatusText=$"Playing: {Path.GetFileName(_currentSourcePath)}";
            }
        }

        [RelayCommand]
        private void ArmSelectionCorrection()
        {
            if(_isExporting) return;
            if (_correctionTarget > 0) { EndSelectionCorrection(true); CorrectionHint="Уточнение отменено"; return; }
            if (!int.TryParse(CorrectionNumber.Trim().TrimStart('S','s'),out int number))
            { CorrectionHint="Введи номер, например 1 или S1"; return; }
            lock(_selLock)
                if(!_selection.Any(i=>i.Number==number)) { CorrectionHint="Такого номера выбора нет"; return; }
            _correctionTarget=number; SelectMode=true;
            bool autoPaused=_correctionPause.Begin(PauseOnCorrection,_framePump.IsVideoSource,
                _framePump.IsPlaying,_framePump.IsPaused,_sessionVersion,_currentSourcePath);
            if(autoPaused)
            {
                _framePump.SetPaused(true);
                StatusText="Paused: уточнение объекта";
            }
            ShowCorrectionNotice(autoPaused);
            CorrectionHint=$"S{number}: кликни нужный объект в превью (повтор кнопки — отмена)";
        }

        private bool CorrectSelection(Detection d)
        {
            if(_correctionTarget<=0) return false;
            lock(_selLock)
            {
                var item=_selection.FirstOrDefault(i=>i.Number==_correctionTarget);
                if(item==null) { EndSelectionCorrection(true); return false; }
                if(_selection.Any(i=>i!=item && SelectionLogic.SameItem(d,i)))
                { CorrectionHint="Этот объект уже связан с другим номером"; return true; }
                SelectionLogic.Rebind(item,d);
                RecordSelectionAction(d.FrameIndex);
                _manualTrackAnchors.Enqueue(CopyDetection(d));
                foreach(var other in _previewCandidates)
                    if(other.TrackId is int id && id!=item.LastTrackId && other.ClassName==d.ClassName &&
                       SelectionLogic.Iou(other.X,other.Y,other.Width,other.Height,d.X,d.Y,d.Width,d.Height)<.05f)
                        item.KnownOtherIds.Add(id);
                _logService.Info($"S{item.Number}: пользователь уточнил объект");
                EndSelectionCorrection(true); CorrectionHint=$"S{item.Number}: клик принят, кадр {d.FrameIndex}. Проверяю следующие наблюдения.";
                UpdateTrackingInfo();
            }
            return true;
        }
        // Отметки кадров для выборочного экспорта (SortedSet = сам дедуплицирует)
        private readonly SortedSet<int> _exportMarks = new();
        [ObservableProperty]
        private string exportMarksText = "";
        private int[] _tlHits = Array.Empty<int>(); // кол-во детекций на кадр (для мини-таймлайна)

        /// <summary>Просит окну перерисовать «шипы» таймлайна (Hits обновляем кодом, не биндингом).</summary>
        public event Action TimelineChanged;

        public LogViewModel LogViewModel { get; }
        public SourcesViewModel SourcesViewModel { get; }

        [ObservableProperty]
        public BitmapImage previewImage;

        [ObservableProperty]
        public string statusText = "Ready";

        [ObservableProperty]
        public double fps = 0.0;

        [ObservableProperty]
        public int currentFrameIndex = -1;

        // Режим подсветки: 0=Боксы, 1=Контур, 2=Боксы+Контур, 3=Маска+Контур
        [ObservableProperty]
        public int highlightModeIndex = 0;

        [ObservableProperty]
        public int timelineFrameCount = 0;

        [ObservableProperty]
        public bool hasTimeline = false;

        [ObservableProperty]
        public string resolution = "-";

        [ObservableProperty]
        public bool isPlaying;

        [ObservableProperty]
        public ObservableCollection<Detection> detections = new();

        /// <summary>Все детекции прогона (каждый кадр). Клик по строке -> переход к кадру.</summary>
        public ObservableCollection<Detection> SessionDetections { get; } = new();

        [ObservableProperty]
        public Detection selectedSessionDetection;

        [ObservableProperty]
        public float confidenceThreshold = 0.45f;

        [ObservableProperty]
        public float nmsThreshold = 0.45f;

        [ObservableProperty]
        public bool filterPersonOnly = false;

        [ObservableProperty]
        public int activeTracksCount = 0;

        [ObservableProperty]
        public bool enableTracking = true;

        [ObservableProperty]
        public bool enableRoi = false;

        [ObservableProperty]
        public bool roiEveryFrame = false;

        public ICommand PlayCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ExportJsonCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportFramesCommand { get; }
        public ICommand MarkCurrentFrameCommand { get; }
        public ICommand ClearExportMarksCommand { get; }
        public ICommand ExportVideoCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ClearSessionCommand { get; }

        public MainViewModel()
        {
            _logService = new LogService();
            _fileSourceService = new FileSourceService(_logService);
            _framePump = new FramePump(_fileSourceService, _logService);
            _renderService = new RenderService(_logService);
            _tracker = new TrackerService(_logService);

            LogViewModel = new LogViewModel(_logService);
            SourcesViewModel = new SourcesViewModel(_fileSourceService, _logService);
            SourcesViewModel.PropertyChanged += OnSourcesViewModelPropertyChanged;

            PlayCommand = new AsyncRelayCommand(PlayAsync);
            PauseCommand = new RelayCommand(() => {
                _correctionPause.Cancel(); // explicit user transport wins over automatic resume
                _framePump.Pause();
                ShowCorrectionNotice(false);
            });
            StopCommand = new RelayCommand(Stop);
            ExportJsonCommand = new RelayCommand(ExportJson);
            ExportCsvCommand = new RelayCommand(ExportCsv);
            ExportFramesCommand = new AsyncRelayCommand(ExportFramesAsync);
            MarkCurrentFrameCommand = new RelayCommand(MarkCurrentFrame);
            ClearExportMarksCommand = new RelayCommand(ClearExportMarks);
            ExportVideoCommand = new AsyncRelayCommand(ExportVideoAsync);
            ClearSessionCommand = new RelayCommand(() =>
            {
                SessionDetections.Clear();
                _sessionDropped=0;_sessionOverflowLogged=false;
                SelectedSessionDetection = null;
                _logService.Info("Результаты очищены");
            });
            ClearLogCommand = new RelayCommand(() => _logService.Clear());

            InitializeDetector();

            _framePump.FrameReady += FramePump_FrameReady;
            _framePump.PlaybackFinished += FramePump_PlaybackFinished;
            _framePump.ErrorOccurred += FramePump_ErrorOccurred;

            _logService.Info("✓ Application initialized");
        }

        private HighlightMode HlMode =>
            (HighlightMode)Math.Max(0, Math.Min(3, HighlightModeIndex));

        partial void OnHighlightModeIndexChanged(int value)
        {
            _ = Task.Run(() => { lock (_processing) ApplyHighlightMode(); });
        }
        partial void OnConfidenceThresholdChanged(float value) => ReanalyseCurrentFrame();
        partial void OnNmsThresholdChanged(float value) => ReanalyseCurrentFrame();
        partial void OnFilterPersonOnlyChanged(bool value) => ReanalyseCurrentFrame();
        partial void OnEnableTrackingChanged(bool value) => ReanalyseCurrentFrame();
        partial void OnEnableRoiChanged(bool value)
        {
            lock(_processing) _roiSchedule.Reset();
            _logService?.Info(value
                ? "ROI включён: центральный верхний участок 36%×44%. Экономный — раз в 0,2 с исходника; каждый кадр — существенно тяжелее."
                : "ROI выключен: только полный кадр.");
            ReanalyseCurrentFrame();
        }

        partial void OnRoiEveryFrameChanged(bool value)
        {
            lock(_processing) _roiSchedule.Reset();
            _logService?.Info(value ? "Частота ROI: каждый обработанный кадр, до двух запусков модели на кадр."
                : "Частота ROI: экономный, не чаще раза в 0,2 с исходника.");
            ReanalyseCurrentFrame();
        }

        /// <summary>Согласовать режим подсветки с моделью: для контуров/маски нужна
        /// seg-модель — при её отсутствии пробуем подхватить *seg*.onnx из Assets,
        /// не нашли — честно откатываемся на боксы с предупреждением.</summary>
        private void ApplyHighlightMode()
        {
            if(_isExporting) return; // never swap the model or mask policy during an export
            _roiSchedule.Reset();
            var d = _detectorService;
            if (d == null) return;
            if (HighlightModeIndex == 0) { d.BuildMasks = false; RefreshCurrentFrame(); return; }

            if (!d.IsSegModel && !TrySwitchToSegModel())
            {
                _logService.Warn("Режим требует seg-модель: положите yolov8n-seg.onnx в Assets\\ — возвращаю боксы");
                HighlightModeIndex = 0;
                return;
            }
            _detectorService.BuildMasks = true;
            ReanalyseCurrentFrame();
            _logService.Info("Подсветка: " + new[] { "боксы", "контур", "боксы+контур", "маска+контур" }[HighlightModeIndex]);
        }

        private bool TrySwitchToSegModel()
        {
            try
            {
                string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                if (!Directory.Exists(assetsDir)) return false;

                var seg = Directory.GetFiles(assetsDir, "*.onnx")
                    .Where(f => Path.GetFileName(f).ToLowerInvariant().Contains("seg"))
                    .OrderByDescending(f => Path.GetFileName(f).ToLowerInvariant().Contains("yolov8") ? 1 : 0)
                    .FirstOrDefault();
                if (seg == null) return false;

                _detectorService?.Dispose();
                _detectorService = new DetectorService(seg, _logService);
                _logService.Info($"Модель: {Path.GetFileName(seg)} (переключение под сегментацию)");
                return true;
            }
            catch (Exception ex)
            {
                _logService.Error($"Не удалось загрузить seg-модель: {ex.Message}");
                return false;
            }
        }

        private void InitializeDetector()
        {
            try
            {
                // Ищем любую .onnx в Assets\: приоритет yolov8 -> yolo11 -> yolov5 -> первая попавшаяся.
                string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                string modelPath = null;

                if (Directory.Exists(assetsDir))
                {
                    var onnx = new List<string>(Directory.GetFiles(assetsDir, "*.onnx"));
                    onnx.Sort();

                    modelPath = FindBy(onnx, "yolov8")
                             ?? FindBy(onnx, "yolo11")
                             ?? FindBy(onnx, "yolov5")
                             ?? FindBy(onnx, "yolo")
                             ?? (onnx.Count > 0 ? onnx[0] : null);
                }

                static string FindBy(List<string> files, string key)
                    => files.Find(f => Path.GetFileName(f).ToLowerInvariant().Contains(key));

                if (modelPath == null)
                {
                    _logService.Warn("Модель не найдена: положите yolov8n.onnx (или yolov5n.onnx) в Detector\\Assets\\");
                    _logService.Warn("Экспорт: yolo export model=yolov8n.pt format=onnx imgsz=640");
                    return;
                }

                _logService.Info($"Модель: {Path.GetFileName(modelPath)}");

                _detectorService = new DetectorService(modelPath, _logService);
                ApplyHighlightMode();
                _logService.Info("✓ GridHound initialized successfully");
            }
            catch (Exception ex)
            {
                _logService.Error($"GridHound init failed: {ex.Message}");
            }
        }

        private async Task PlayAsync()
        {
            if(_isExporting) { _logService.Warn("Дождись завершения экспорта"); return; }
            var source = SourcesViewModel.SelectedSource;
            if (source == null)
            {
                _logService.Warn("No source selected");
                return;
            }

            _correctionPause.Cancel(); // explicit Play takes ownership of transport
            CorrectionNoticeVisible=false;
            if (_framePump.IsPlaying)
            {
                if (string.Equals(_currentSourcePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Тот же файл: Play = возобновить, если на паузе (иначе — тихо игнор)
                    if (_framePump.IsPaused)
                    {
                        _framePump.Resume();
                        IsPlaying = true;
                        StatusText = $"Playing: {source.FileName}";
                    }
                    else
                    {
                        _logService.Info("Воспроизведение уже выполняется");
                    }
                    return;
                }

                // Другой файл — корректно останавливаем старый прогон и стартуем заново
                _logService.Info("Переключение источника: останавливаю текущий прогон");
                Stop();
                await Task.Delay(200);
            }

            // A new run is not Resume. Invalidate queued UI work before draining old workers.
            int start = Math.Max(0, _lastPreviewFrameIndex);
            bool rewind = _framePump.IsVideoSource && start >= _framePump.CurrentVideoFrameCount - 1;
            if (rewind) start = 0;
            _sourceVersion++;
            _sessionVersion++;
            int startingSession = _sessionVersion;
            _isRecording = false;
            _cancellationTokenSource?.Cancel();
            _framePump.Stop();
            StopDetConsumer();
            await _framePump.WaitForIdleAsync();
            // A final FrameReady may have started a consumer during pump shutdown.
            StopDetConsumer();
            var oldConsumer = _detTask;
            if (oldConsumer != null) await oldConsumer;
            // Source selection can change while shutdown is awaited. Do not start the stale choice.
            if (startingSession != _sessionVersion || SourcesViewModel.SelectedSource?.FilePath != source.FilePath) return;
            // Shutdown callbacks may have queued work after the first invalidation.
            _sourceVersion++;
            _sessionVersion++;
            _currentSourcePath = source.FilePath;
            lock (_processing)
            {
                while(_manualTrackAnchors.TryDequeue(out _)) { }
                _tracker.Reset();
                _trackerSourceVersion = _sourceVersion;
                _lastDetectedIndex = -1;
                _lastListUpdate = 0;
                lock (_cacheLock)
                {
                    _allCurrent = new List<Detection>();
                    _cachedFrame?.Dispose();
                    _cachedFrame = null;
                }
                lock (_selLock)
                {
                    // A rewind is a new pass, not evidence that end-of-video identities survived.
                    if (rewind) {
                        _autoRequests.Clear();
                        foreach(var item in _selection) SelectionLogic.ResetForReplay(item);
                    }
                    else foreach (var item in _selection) SelectionLogic.StartNewTrackingEpoch(item,start);
                    SelectedCount = _selection.Count;
                    UpdateTrackingInfo();
                }
            }
            _liveView = new List<Detection>();
            _lastPreviewFrameIndex = start;
            CurrentFrameIndex = start;
            Detections.Clear();
            PreviewImage = null;
            if (rewind) _logService.Info("Новый прогон с начала: треки сброшены, номера S и подтверждённые образцы сохранены");
            _cancellationTokenSource = new CancellationTokenSource();
            ActiveTracksCount = 0;
            SessionDetections.Clear();
            SelectedSessionDetection = null;
            _sessionOverflowLogged = false;
            _sessionDropped=0;
            _isRecording = true;
            if (_tlHits.Length > 0) Array.Clear(_tlHits, 0, _tlHits.Length);
            HasTimeline = false; // включится, когда помп сообщит что источник — видео с >1 кадром
            TimelineChanged?.Invoke();
            IsPlaying = true;
            StatusText = $"Playing: {source.FileName}";
            _logService.Info("Запись результатов прогона: ON");

            _framePump.Play(source.FilePath, _cancellationTokenSource.Token, start);
        }

        private void Stop()
        {
            if(_correctionTarget>0) CorrectionHint="Уточнение отменено: воспроизведение остановлено";
            EndSelectionCorrection(false);
            _sourceVersion++;
            _sessionVersion++;
            _framePump.Stop();
            StopDetConsumer();
            _cancellationTokenSource?.Cancel();
            _isRecording = false;
            _pumpAlive = false;
            ActiveTracksCount = 0;
            IsPlaying = false;
            StatusText = "Stopped";
        }

        /// <summary>Клик по превью (координаты в пикселях кадра) с режимом выбора.</summary>
        public void SelectAt(double frameX, double frameY, bool additive)
        {
            if (!SelectMode || _isExporting) return;
            _selectionRevision++;
            var snap = _previewCandidates.Select(CopyDetection).ToList();
            lock (_selLock)
            {
                int hit = SelectionLogic.HitTest(snap, frameX, frameY);
                if(_correctionTarget>0)
                {
                    if(hit>=0) CorrectSelection(snap[hit]);
                    else CorrectionHint="В этой точке нет обнаружения; попробуй другой кадр или снизь порог";
                    RefreshCurrentFrame(); return;
                }
                int count;
                if (hit < 0)
                {
                    if (!additive) _selection.Clear();
                    count = _selection.Count;
                }
                else
                {
                    bool wasEmpty = _selection.Count == 0;
                    count = SelectionLogic.Toggle(_selection, snap[hit], additive);
                    if (wasEmpty && count > 0) { _selectionStartFrame = Math.Max(0,snap[hit].FrameIndex); }
                }
                RecordSelectionAction(hit>=0 ? snap[hit].FrameIndex : (snap.FirstOrDefault()?.FrameIndex ?? _lastPreviewFrameIndex));
                SelectedCount = count;
                UpdateTrackingInfo();
                _logService?.Info(count > 0
                    ? $"Выбор: следим за {count} объект(ами) — подсвечиваем и треким только их"
                    : "Выбор: снят — рисуем все объекты");
            }
            PruneSessionSelection();
            // Repaint cached raw pixels without invoking the detector.
            RefreshCurrentFrame();
        }

        /// <summary>Чекбокс «следить» в списке DETECTIONS.</summary>
        public void SetSelectionFor(Detection d, bool on)
        {
            if (d == null || _isExporting) return;
            bool changed;
            _selectionRevision++;
            if(on && _correctionTarget>0) { CorrectSelection(d); RefreshCurrentFrame(); return; }
            lock (_selLock)
            {
                bool wasEmpty = _selection.Count == 0;
                changed = SelectionLogic.Set(_selection, d, on);
                if(changed) RecordSelectionAction(d.FrameIndex);
                if (wasEmpty && on) { _selectionStartFrame = Math.Max(0,d.FrameIndex); }
                SelectedCount = _selection.Count;
                UpdateTrackingInfo();
            }
            PruneSessionSelection();
            if (changed)
                _logService?.Info(on
                    ? $"Выбор: «{(d.TrackId is int t && t > 0 ? d.ClassName + " #" + t : d.ClassName)}» — трекинг включён 🎯"
                    : $"Выбор: {d.ClassName} — трекинг выключен");
            RefreshCurrentFrame();
        }

        private void PruneSessionSelection() { /* Historical observations are immutable across later deselection. */ }

        private void UpdateTrackingInfo()
        {
            lock(_selLock)
            {
                TrackingInfo = _selection.Count > 0 ? $"выбрано: {_selection.Count}" : "все объекты";
                SelectionSummary = _selection.Count > 0 ? string.Join("\n",_selection.Select(i=>i.Summary)) : "Выбор не задан";
                if(_selection.Count==0) EndSelectionCorrection(true);
            }
        }

        [RelayCommand]
        private void ClearSelection()
        {
            if(_isExporting) return;
            bool had;
            _selectionRevision++;
            lock (_selLock) { had = _selection.Count > 0; _selection.Clear(); _autoRequests.Clear(); RecordSelectionAction(_previewCandidates.FirstOrDefault()?.FrameIndex ?? _lastPreviewFrameIndex); }
            SelectedCount = 0;
            UpdateTrackingInfo();
            if (had) _logService?.Info("Выбор: снят — рисуем все объекты");
            RefreshCurrentFrame();
        }

        /// <summary>Repaint a cached raw frame and detections, without I/O or inference.</summary>
        private void RefreshCurrentFrame()
        {
            int source=_sourceVersion, revision=_selectionRevision;
            long request=Interlocked.Increment(ref _renderRequest);
            Mat raw;
            List<Detection> all;
            lock (_cacheLock)
            {
                if (_cachedFrame == null || _cachedFrame.IsDisposed) return;
                raw = _cachedFrame.Clone();
                all = _allCurrent.Select(CopyDetection).ToList();
            }
            using (raw)
            {
                ApplySelection(all, false);
                bool selective; lock (_selLock) selective = _selection.Count > 0;
                var draw = selective ? all.FindAll(d=>d.IsSelected) : all;
                using var ann = _renderService.DrawDetections(raw, draw, HlMode);
                ShowBitmap(ann.ToBytes(".jpg"),raw.Width,raw.Height,source,revision,request,all);
                PublishDetections(all, !_framePump.IsPlaying || _framePump.IsPaused, false);
            }
        }

        private static Detection CopyDetection(Detection d) => new Detection {
            Id=d.Id, ClassName=d.ClassName, Confidence=d.Confidence, X=d.X, Y=d.Y,
            Width=d.Width, Height=d.Height, TrackId=d.TrackId, FrameIndex=d.FrameIndex,
            Timestamp=d.Timestamp, MaskPolygon=d.MaskPolygon, Appearance=d.Appearance, IsSelected=d.IsSelected, SelectionKey=d.SelectionKey, SelectionNumber=d.SelectionNumber, SourceSeconds=d.SourceSeconds, IsWeakContinuation=d.IsWeakContinuation, FrameWidth=d.FrameWidth, FrameHeight=d.FrameHeight, IsEdgeContinuation=d.IsEdgeContinuation };

        private List<SelectionLogic.SelectionItem> SnapshotSelection()
        { lock (_selLock) return _selection.ConvertAll(it=>it.Clone()); }

        private void ApplySelection(List<Detection> all, bool advance, int observationFrame = -1, double sourceSeconds = double.NaN)
        {
            lock (_selLock)
            {
                var items = advance ? _selection : _selection.ConvertAll(it=>it.Clone());
                var previous = items.Select(i=>i.State).ToArray();
                SelectionLogic.Apply(all,items,ActiveReacquisition,advance,observationFrame,sourceSeconds);
                if (advance)
                {
                    for(int i=0;i<items.Count;i++) if(previous[i]!=items[i].State)
                        _logService.Info(items[i].Summary);
                    int version=_sourceVersion, revision=_selectionRevision;
                    string summary=string.Join("\n",items.Select(i=>i.Summary));
                    Application.Current.Dispatcher.BeginInvoke(new Action(()=> {
                        if(version==_sourceVersion && revision==_selectionRevision)
                        {
                            SelectionSummary=summary.Length>0 ? summary : "Выбор не задан";
                            CheckAutomaticCorrection();
                        }
                    }));
                }
            }
        }

        private void ReanalyseCurrentFrame()
        {
            if (_detectorService == null || _isExporting) return;
            int version = _sourceVersion;
            _ = Task.Run(() => {
                lock (_processing)
                {
                    if (version != _sourceVersion) return;
                    Mat raw; int index; double sourceSeconds;
                    lock (_cacheLock) {
                        if (_cachedFrame == null) return;
                        raw=_cachedFrame.Clone(); index=_lastPreviewFrameIndex; sourceSeconds=_lastPreviewSourceSeconds;
                    }
                    using (raw) {
                        var all=DetectFrame(raw,index,false,sourceSeconds);
                        if (version != _sourceVersion) return;
                        lock (_cacheLock) _allCurrent=all.Select(CopyDetection).ToList();
                        RefreshCurrentFrame();
                    }
                }
            });
        }

        private List<Detection> DetectFrame(Mat raw, int index, bool track, double sourceSeconds = double.NaN)
        {
            if (_trackerSourceVersion != _sourceVersion) { while(_manualTrackAnchors.TryDequeue(out _)) { } _tracker.Reset(); _roiSchedule.Reset(); _lastDetectedIndex=-1; _trackerSourceVersion=_sourceVersion; }
            bool temporal=EnableTracking && track && _framePump.IsVideoSource;
            if(!_framePump.IsVideoSource) sourceSeconds=double.NaN;
            while(_manualTrackAnchors.TryDequeue(out var anchor)) _tracker.Reanchor(anchor);
            var all = _detectorService?.DetectWithRoi(raw,temporal ? ContinuationThreshold : ConfidenceThreshold,
                NmsThreshold,_roiSchedule.Due(EnableRoi,_framePump.IsVideoSource,index,sourceSeconds,!track,RoiEveryFrame)) ?? new List<Detection>();
            if (FilterPersonOnly) all=all.FindAll(d=>string.Equals(d.ClassName,"person",StringComparison.OrdinalIgnoreCase));
            foreach (var d in all) {
                d.FrameWidth=raw.Width;d.FrameHeight=raw.Height;d.FrameIndex=index; d.SourceSeconds=sourceSeconds; d.Appearance=BuildAppearance(raw,d);
            }
            if (EnableTracking && track) all=temporal
                ? _tracker.Update(all,ConfidenceThreshold,ContinuationThreshold,sourceSeconds) : _tracker.Update(all);
            // Re-analysis of a paused frame must not advance the motion model.
            if (EnableTracking && !track) {
                List<Detection> old; lock (_cacheLock) old=_allCurrent.Select(CopyDetection).ToList();
                var cost=new float[all.Count,old.Count];
                for(int i=0;i<all.Count;i++) for(int j=0;j<old.Count;j++)
                    cost[i,j]=all[i].ClassName==old[j].ClassName ? 1-SelectionLogic.Iou(all[i].X,all[i].Y,all[i].Width,all[i].Height,old[j].X,old[j].Y,old[j].Width,old[j].Height) : 10;
                foreach(var (i,j) in HungarianAlgorithm.FindMatches(cost,0.7f)) all[i].TrackId=old[j].TrackId;
            }
            ApplySelection(all,track,index,sourceSeconds);
            if(track) _lastDetectedIndex=index;
            return all;
        }

        private static float[] BuildAppearance(Mat raw, Detection d)
        {
            var rect = new OpenCvSharp.Rect(Math.Clamp(d.X,0,raw.Width),Math.Clamp(d.Y,0,raw.Height),
                Math.Max(0,Math.Min(d.Width,raw.Width-Math.Max(0,d.X))),
                Math.Max(0,Math.Min(d.Height,raw.Height-Math.Max(0,d.Y))));
            if(rect.Width<2 || rect.Height<2) return null;
            using var roi=new Mat(raw,rect); using var small=new Mat();
            Cv2.Resize(roi,small,new OpenCvSharp.Size(24,48));
            var hist=new float[64];
            for(int y=0;y<small.Rows;y++) for(int x=0;x<small.Cols;x++) {
                var c=small.At<Vec3b>(y,x); hist[(c.Item0/64)*16+(c.Item1/64)*4+c.Item2/64]++;
            }
            for(int i=0;i<hist.Length;i++) hist[i]/=small.Rows*small.Cols;
            return hist;
        }

        private bool CacheFrame(Mat raw, int index, string path, double sourceSeconds)
        {
            lock (_cacheLock) {
                if(!string.Equals(path,_currentSourcePath,StringComparison.OrdinalIgnoreCase)) return false;
                _cachedFrame?.Dispose(); _cachedFrame=raw.Clone(); _lastPreviewFrameIndex=index; _lastPreviewSourceSeconds=sourceSeconds;
                return true;
            }
        }

        private void PublishDetections(List<Detection> all, bool force, bool record)
        {
            int version=_sourceVersion, session=_sessionVersion, revision=_selectionRevision;
            var rows=all.Select(CopyDetection).OrderBy(d=>d.TrackId ?? int.MaxValue).ThenBy(d=>d.X).ToList();
            bool selective; lock(_selLock) selective=_selection.Count>0;
            int count=EnableTracking ? (selective ? rows.Count(d=>d.IsSelected) : rows.Count) : 0;
            long now=Environment.TickCount64;
            bool update=force || now-_lastListUpdate>=200;
            if(update) _lastListUpdate=now;
            var recorded=(selective ? rows.Where(d=>d.IsSelected) : rows).Select(CopyDetection).ToList();
            Application.Current.Dispatcher.BeginInvoke(new Action(()=> {
                if(version!=_sourceVersion || session!=_sessionVersion || revision!=_selectionRevision) return;
                if(update) {
                    var unused=Detections.ToList();
                    for(int i=0;i<rows.Count;i++) {
                        var d=rows[i];
                        var existing=unused.FirstOrDefault(o=>o.ClassName==d.ClassName &&
                            (d.TrackId.HasValue ? o.TrackId==d.TrackId :
                                SelectionLogic.Iou(o.X,o.Y,o.Width,o.Height,d.X,d.Y,d.Width,d.Height)>=0.3f));
                        if(existing==null) Detections.Insert(Math.Min(i,Detections.Count),d);
                        else {
                            unused.Remove(existing); existing.UpdateFrom(d);
                            int at=Detections.IndexOf(existing); if(at!=i) Detections.Move(at,i);
                        }
                    }
                    foreach(var old in unused) Detections.Remove(old);
                    ActiveTracksCount=count;
                    if(rows.Count>0) UpdateTimeline(rows[0].FrameIndex);
                }
                // Capture recording state at processing time, not at delayed UI dispatch time.
                if(record && session==_sessionVersion) {
                    foreach(var d in recorded) {
                        if(SessionDetections.Count<MaxSessionItems) SessionDetections.Add(d);
                        else {
                            _sessionDropped++;
                            if(!_sessionOverflowLogged) {
                                _sessionOverflowLogged=true;
                                _logService.Warn("История просмотра достигла 20000 обнаружений. CSV/JSON будут неполными; число пропущенных записей указано в metadata.json. Видео/Frames выполняют независимый полный анализ.");
                                StatusText="История заполнена: CSV/JSON неполные";
                            }
                        }
                    }
                }
            }));
        }

        private void FramePump_FrameReady(object sender, FrameData fd)
        {
            if(fd?.Frame == null) return;
            if (!string.Equals(fd.SourcePath,_currentSourcePath,StringComparison.OrdinalIgnoreCase)) { fd.Frame.Dispose(); return; }
            _pumpAlive=IsPlaying;
            if(fd.IsStream) {
                if(!CacheFrame(fd.Frame,fd.FrameIndex,fd.SourcePath,fd.Timestamp/1000.0)) { if(fd.IsStream) fd.Frame.Dispose(); return; }
                int sourceVersion = _sourceVersion, sessionVersion = _sessionVersion;
                Application.Current.Dispatcher.BeginInvoke(new Action(()=> {
                    if(sourceVersion==_sourceVersion && sessionVersion==_sessionVersion) UpdateTimeline(fd.FrameIndex);
                }));
                RefreshCurrentFrame();
                EnsureDetConsumer(); SubmitToDetector(fd);
            } else {
                try {
                    lock(_processing) {
                        int version=_sourceVersion;
                        if (!string.Equals(fd.SourcePath,_currentSourcePath,StringComparison.OrdinalIgnoreCase)) return;
                        bool recording=_isRecording;
                        var all=DetectFrame(fd.Frame,fd.FrameIndex,true,fd.Timestamp/1000.0);
                        if(version!=_sourceVersion) return;
                        if(!CacheFrame(fd.Frame,fd.FrameIndex,fd.SourcePath,fd.Timestamp/1000.0)) { if(fd.IsStream) fd.Frame.Dispose(); return; }
                        lock(_cacheLock) _allCurrent=all.Select(CopyDetection).ToList();
                        RefreshCurrentFrame();
                        PublishDetections(all,true,recording);
                        Application.Current.Dispatcher.BeginInvoke(new Action(()=> {
                            if(version==_sourceVersion) UpdateTimeline(fd.FrameIndex);
                        }));
                    }
                } catch(Exception ex) { _logService.Error("Frame: "+ex.Message); }
                finally { fd.Frame.Dispose(); }
            }
        }

        private void ShowBitmap(byte[] bytes, int w, int h, int source, int revision, long request, List<Detection> candidates)
        {
            if (bytes == null || bytes.Length == 0) return;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new System.IO.MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            void Commit() {
                if(source!=_sourceVersion || revision!=_selectionRevision || request<_renderCommitted) return;
                _renderCommitted=request;
                _previewCandidates=candidates.Select(CopyDetection).ToList();
                PreviewImage=bitmap; Resolution=$"{w}x{h}";
            }
            if(Application.Current.Dispatcher.CheckAccess()) Commit();
            else Application.Current.Dispatcher.BeginInvoke(new Action(Commit));
        }

        // ---------- потребитель инференса для режима Play ----------
        private void EnsureDetConsumer()
        {
            if (_detTask != null && !_detTask.IsCompleted) return;
            var cts = new CancellationTokenSource();
            _detCts = cts;
            _detTask = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try { await _detSignal.WaitAsync(cts.Token).ConfigureAwait(false); }
                    catch { break; }
                    FrameData fd;
                    while ((fd = Interlocked.Exchange(ref _pendingFd, null)) != null)
                    {
                        // важен последний кадр; накопленные старые неактуальны
                        try { ProcessStreamFrame(fd,cts.Token); }
                        catch (Exception ex) { _logService?.Error("inference: " + ex.Message); }
                        finally { try { fd.Frame?.Dispose(); } catch { } }
                        if (cts.IsCancellationRequested) break;
                    }
                }
                _liveView = new List<Detection>();
            }, cts.Token);
        }

        private void SubmitToDetector(FrameData fd)
        {
            var old = Interlocked.Exchange(ref _pendingFd, fd);
            if (old != null) { try { old.Frame?.Dispose(); } catch { } } // сеть не обязана видеть каждый кадр
            try { _detSignal.Release(); } catch (SemaphoreFullException) { }
        }

        private void ProcessStreamFrame(FrameData fd, CancellationToken token)
        {
            lock(_processing) {
                int version=_sourceVersion;
                if(fd.FrameIndex < _lastDetectedIndex || _framePump.ExactFrames || token.IsCancellationRequested || !string.Equals(fd.SourcePath,_currentSourcePath,StringComparison.OrdinalIgnoreCase)) return;
                bool recording=_isRecording;
                var all=DetectFrame(fd.Frame,fd.FrameIndex,true,fd.Timestamp/1000.0);
                if(version!=_sourceVersion || token.IsCancellationRequested) return;
                lock(_cacheLock) _allCurrent=all.Select(CopyDetection).ToList();
                PublishDetections(all,false,recording);
            }
        }

        private void StopDetConsumer()
        {
            try { _detCts?.Cancel(); } catch { }
            _detCts = null;
            var old = Interlocked.Exchange(ref _pendingFd, null);
            if (old != null) { try { old.Frame?.Dispose(); } catch { } }
            _liveView = new List<Detection>();
        }

        private async void FramePump_PlaybackFinished(object sender, EventArgs e)
        {
            if(_framePump.FinishedCancelled) return;
            int version=_sourceVersion, session=_sessionVersion;
            bool recording=_isRecording;
            var task=_detTask;
            StopDetConsumer();
            try {
                if(task!=null) await task.ConfigureAwait(false);
                await Task.Run(()=> {
                    lock(_processing) {
                        if(version!=_sourceVersion || session!=_sessionVersion) return;
                        Mat raw; int index; double sourceSeconds;
                        lock(_cacheLock) {
                            if(_cachedFrame==null) return;
                            raw=_cachedFrame.Clone(); index=_lastPreviewFrameIndex; sourceSeconds=_lastPreviewSourceSeconds;
                        }
                        using(raw) if(index!=_lastDetectedIndex) {
                            var all=DetectFrame(raw,index,true,sourceSeconds);
                            lock(_cacheLock) _allCurrent=all.Select(CopyDetection).ToList();
                            PublishDetections(all,true,recording);
                        }
                    }
                });
            } catch(Exception ex) { _logService.Warn("Завершение: "+ex.Message); }
            if(version!=_sourceVersion || session!=_sessionVersion) return;
            _isRecording=false; _pumpAlive=false; IsPlaying=false;
            RefreshCurrentFrame(); StatusText="Playback finished";
            Application.Current.Dispatcher.BeginInvoke(new Action(()=>
                _logService.Info($"Результаты сохранены в сессии: {SessionDetections.Count} детекций")));
        }

        /// <summary>Клик по строке RESULTS: показать тот кадр (видео: переоткрыть + seek).</summary>
        partial void OnSelectedSessionDetectionChanged(Detection value)
        {
            if (value == null) return;
            JumpToFrame(value.FrameIndex, $"RESULTS: {value.ClassName}");
        }

        /// <summary>Клик по мини-таймлайну.</summary>
        public void JumpToTimelineFrame(int frameIndex) => JumpToFrame(frameIndex, "TIMELINE");

        // ---------- Превью по выбору источника (без Play) ----------
        private void OnSourcesViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SourcesViewModel.SelectedSource))
                ShowPreviewForSelected();
        }

        /// <summary>
        /// Выбор видео/картинки в SOURCES сразу показывает его в PREVIEW: картинку —
        /// как есть, видео — нулевой кадр с детекциями/боксами. Прогон при этом НЕ стартует.
        /// Если играет выбранный же файл — прогону не мешаем; если другой — Stop() (он
        /// неблокирующий) и показ нового.
        /// </summary>
        private void ShowPreviewForSelected()
        {
            var src = SourcesViewModel.SelectedSource;
            if (_isExporting) return;
            if (src == null) {
                Stop(); _sourceVersion++; _sessionVersion++;
                _currentSourcePath=null;
                _sessionDropped=0;_sessionOverflowLogged=false;
                _exportMarks.Clear(); UpdateMarksUi();
                lock(_selLock) { _selection.Clear(); _autoRequests.Clear(); _exportTimeline.Clear(); }
                lock(_cacheLock) { _cachedFrame?.Dispose(); _cachedFrame=null; _allCurrent=new(); }
                PreviewImage=null; Detections.Clear(); SessionDetections.Clear();
                SelectedCount=0; UpdateTrackingInfo(); return;
            }

            if (_framePump.IsPlaying)
            {
                if (string.Equals(_currentSourcePath, src.FilePath, StringComparison.OrdinalIgnoreCase))
                    return; // это он и так на экране, в движении
                Stop();
            }

            if (!string.Equals(_currentSourcePath,src.FilePath,StringComparison.OrdinalIgnoreCase)) {
                EndSelectionCorrection(false);
                _sourceVersion++; _sessionVersion++;
                _currentSourcePath=src.FilePath;
                _sessionDropped=0;_sessionOverflowLogged=false;
                _exportMarks.Clear(); UpdateMarksUi();
                lock(_selLock) { _selection.Clear(); _autoRequests.Clear(); _exportTimeline.Clear(); }
                SelectedCount=0; UpdateTrackingInfo();
                lock(_cacheLock) { _cachedFrame?.Dispose(); _cachedFrame=null; _allCurrent=new(); }
                SessionDetections.Clear(); Detections.Clear();
                _selectionStartFrame=0;
                // Queue reset before the new preview's inference; never block UI on an ONNX call.
                // Reset is consumed under _processing by the first frame of the new source.
            }
            _framePump.ShowFrameAt(src.FilePath, 0);
            StatusText = $"Preview: {src.FileName}";
            _logService.Info($"Превью {src.FileName} без прогона (Play — для детекции в движении)");
        }

        private void JumpToFrame(int frameIndex, string reason)
        {
            var src = SourcesViewModel.SelectedSource;
            if (src == null)
            {
                _logService.Warn("Источник не выбран — перейти к кадру нельзя");
                return;
            }

            if (_framePump.IsPlaying)
                Stop();

            _framePump.ShowFrameAt(src.FilePath, frameIndex);
            _logService.Info($"Переход к кадру {frameIndex} ({reason})");
        }

        private void UpdateTimeline(int frameIndex)
        {
            CurrentFrameIndex = frameIndex;

            if (!_framePump.IsVideoSource) return;

            int fc = _framePump.CurrentVideoFrameCount;
            if (fc > 1)
            {
                HasTimeline = true;
                TimelineFrameCount = fc;
            }

            int idx = frameIndex;
            if (idx < 0) return;

            if (_isRecording && idx >= _tlHits.Length)
                Array.Resize(ref _tlHits, Math.Max(64, Math.Max(_tlHits.Length * 2, idx + 1)));

            if (_isRecording && idx < _tlHits.Length)
            {
                _tlHits[idx]++;
                TimelineChanged?.Invoke();
            }
        }

        public int[] GetTimelineHitsSnapshot() => (int[])_tlHits.Clone();


        // ---------- Отметки кадров для выборочного экспорта ----------
        private void MarkCurrentFrame()
        {
            if (CurrentFrameIndex < 0)
            {
                _logService.Warn("Нет текущего кадра — сначала откройте источник");
                return;
            }
            // toggle: повторное нажатие на тот же кадр снимает отметку (без дублей)
            if (!_exportMarks.Add(CurrentFrameIndex)) _exportMarks.Remove(CurrentFrameIndex);
            UpdateMarksUi();
        }

        private void ClearExportMarks()
        {
            _exportMarks.Clear();
            UpdateMarksUi();
        }

        private void UpdateMarksUi()
        {
            string text = _exportMarks.Count == 0 ? "" :
                $"marked {_exportMarks.Count}: " + string.Join(", ", _exportMarks.Take(10)) +
                (_exportMarks.Count > 10 ? " …" : "");
            try { Application.Current?.Dispatcher?.BeginInvoke(new Action(() => ExportMarksText = text)); }
            catch { ExportMarksText = text; }
            _logService.Info(_exportMarks.Count == 0
                ? "Отметки кадров сняты — экспорт будет по всем кадрам с детекциями"
                : $"Кадр {CurrentFrameIndex}: всего отмечено {_exportMarks.Count} — экспорт сохранит только их");
        }

        /// <summary>Экспорт кадров с боксами в выбранную папку. Если есть отметки — только они
        /// (по одному файлу на кадр, без повторов); иначе — все кадры с детекциями. Прогресс —
        /// в строке статуса; экспорт не блокирует UI и не ждёт помп синхронно.</summary>
        private async Task ExportFramesAsync()
        {
            var src = SourcesViewModel.SelectedSource;
            if (src == null) { _logService.Warn("Экспорт кадров: источник не выбран"); return; }
            if (_detectorService == null) { _logService.Error("Экспорт: модель не загружена"); return; }
            if (_isExporting) { _logService.Warn("Экспорт уже выполняется"); return; }
            ExportInputsEnabled=false;
            _isExporting = true; // со ЩИТА до диалога: двойной клик не откроет два окна

            try
            {
                if (_framePump.IsPlaying)
                {
                    _logService.Info("Останавливаю воспроизведение ради экспорта");
                    Stop();
                    await _framePump.WaitForIdleAsync();
                }

                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Куда сохранить кадры" };
                if (dlg.ShowDialog() != true) return;
                string folder = dlg.FolderName;

                var settings=CaptureExportSettings();
                _logService.Info("Frames: полный повторный анализ, не копия наблюдений просмотра; история выбора применяется по исходным кадрам.");
                var wanted = _exportMarks.ToList(); // копия: набор может меняться с UI
                bool selective = wanted.Count > 0;
                _logService.Info(selective
                    ? $"Экспорт кадров начат: выборочно {wanted.Count} шт…"
                    : "Экспорт кадров начат: все кадры с детекциями…");

                await Task.Run(() =>
                {
                    var fss = new FileSourceService(_logService); // свой capture — не мешает UI-потоку
                    var tracker = settings.Tracking ? new TrackerService(null) : null;
                    var roiSchedule = new RoiSchedule();
                    var selected=settings.Selection;
                    int first=0;
                    int saved = 0, processed = 0;
                    int total = selective ? wanted.Count : Math.Max(1, _framePump.CurrentVideoFrameCount);
                    var swTick = System.Diagnostics.Stopwatch.StartNew();
                    string baseName = Path.GetFileNameWithoutExtension(src.FileName);

                    void Report()
                    {
                        _ = Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            StatusText = $"export {processed}/{total} · saved {saved}"));
                    }

                    void Process(FrameData fd, bool forceSave)
                    {
                        processed++;
                        try
                        {
                            List<Detection> dets;
                            dets = DetectForExport(fd.Frame,settings,fss.IsVideoFile(src.FilePath),roiSchedule,fd.FrameIndex,fd.Timestamp/1000.0);
                            if (settings.PersonOnly)
                                dets = dets.FindAll(d => string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase));
                            foreach(var d in dets) { d.FrameWidth=fd.Frame.Width;d.FrameHeight=fd.Frame.Height;d.FrameIndex=fd.FrameIndex; d.SourceSeconds=fss.IsVideoFile(src.FilePath) ? fd.Timestamp/1000.0 : double.NaN; d.Appearance=BuildAppearance(fd.Frame,d); }
                            if (tracker != null) dets = tracker.Update(dets,settings.Confidence,settings.Low,fd.Timestamp/1000.0);
                            dets=selected.Select(dets,fd.FrameIndex,fd.Timestamp/1000.0,settings.Active,d=>tracker?.Reanchor(d));

                            if (dets.Count > 0 || forceSave)
                            {
                                using Mat ann = _renderService.DrawDetections(fd.Frame, dets, settings.Highlight);
                                string path = Path.Combine(folder, $"{baseName}_f{fd.FrameIndex:00000}.jpg");
                                if (ann.SaveImage(path, new ImageEncodingParam(ImwriteFlags.JpegQuality, 92)))
                                    saved++;
                            }
                        }
                        finally { fd.Frame?.Dispose(); }

                        if (processed % 50 == 0 || swTick.ElapsedMilliseconds > 400)
                        {
                            swTick.Restart();
                            Report();
                            if (processed % 250 == 0)
                                _logService.Info($"Экспорт кадров: обработано {processed}/{total}, сохранено {saved}");
                        }
                    }

                    try
                    {
                        if (fss.IsImageFile(src.FilePath))
                        {
                            if (fss.OpenImage(src.FilePath, out _, out _))
                            {
                                var fd = fss.GetCurrentImageFrame();
                                if (fd != null) Process(fd, true);
                            }
                            else throw new Exception("Не удалось открыть изображение");
                        }
                        else
                        {
                            if (!fss.OpenVideo(src.FilePath, out int fc, out _, out _, out _))
                                throw new Exception("Не удалось открыть видео");
                            if (!selective && fc > 0) total = fc; // честный знаменатель прогресса

                            fss.SeekFrame(first);
                            // Walk intervening frames as well: sparse seeks must not corrupt tracking.
                            while(fss.ReadNextFrame(out var fd)) {
                                bool save=!selective || wanted.Contains(fd.FrameIndex);
                                if(save) Process(fd,selective);
                                else {
                                    try {
                                        List<Detection> all;
                                        all=DetectForExport(fd.Frame,settings,true,roiSchedule,fd.FrameIndex,fd.Timestamp/1000.0);
                                        if(settings.PersonOnly) all=all.FindAll(d=>d.ClassName=="person");
                                        foreach(var d in all) { d.FrameWidth=fd.Frame.Width;d.FrameHeight=fd.Frame.Height;d.FrameIndex=fd.FrameIndex; d.SourceSeconds=fss.IsVideoFile(src.FilePath) ? fd.Timestamp/1000.0 : double.NaN; d.Appearance=BuildAppearance(fd.Frame,d); }
                                        if(tracker!=null) all=tracker.Update(all,settings.Confidence,settings.Low,fd.Timestamp/1000.0);
                                        selected.Select(all,fd.FrameIndex,fd.Timestamp/1000.0,settings.Active,d=>tracker?.Reanchor(d));
                                    } finally { fd.Frame.Dispose(); }
                                }
                            }

                            _logService.Info($"Экспорт: видео {fc} кадров, обработано {processed}");
                        }

                        var manifest=new {analysis_mode="full_reanalysis", source=src.FilePath,
                            saved_files=saved, marked_frames=wanted, selection_actions=selected.Describe(),
                            settings=new {settings.Tracking,settings.PersonOnly,settings.Confidence,settings.Low,settings.Nms,settings.Active,settings.Roi,settings.RoiEveryFrame},
                            note="Selected periods only if selection history exists. Marked frames are saved even without accepted detections. Not identical to view observations."};
                        File.WriteAllText(Path.Combine(folder,baseName+"_frames.metadata.json"),
                            System.Text.Json.JsonSerializer.Serialize(manifest,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),new UTF8Encoding(false));
                        _logService.Info($"✓ Кадры сохранены: {saved} шт. в {folder}");
                        Report();
                        _ = Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            StatusText = $"export done: {saved} files"));
                    }
                    finally { fss.Close(); }
                });
            }
            catch (Exception ex)
            {
                _logService.Error($"Ошибка экспорта кадров: {ex.Message}");
                _ = Application.Current?.Dispatcher?.BeginInvoke(new Action(() => StatusText = "export failed"));
            }
            finally
            {
                _isExporting = false;
                ExportInputsEnabled=true;
                _ = Task.Run(()=> {lock(_processing) ApplyHighlightMode();});
            }
        }

        // ---------- Экспорт видео с обводкой ----------
        private async Task ExportVideoAsync()
        {
            var src = SourcesViewModel.SelectedSource;
            if (src == null) { _logService.Warn("Экспорт видео: источник не выбран"); return; }
            if (_detectorService == null) { _logService.Error("Экспорт видео: модель не загружена"); return; }
            if (_isExporting) { _logService.Warn("Экспорт уже выполняется"); return; }
            if (_fileSourceService.IsImageFile(src.FilePath))
            {
                _logService.Warn("Экспорт видео: выбранное изображение — возьмите «Frames»");
                return;
            }
            ExportInputsEnabled=false;
            _isExporting = true;

            try
            {
                if (_framePump.IsPlaying)
                {
                    _logService.Info("Останавливаю воспроизведение ради экспорта видео");
                    Stop();
                    await _framePump.WaitForIdleAsync();
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Сохранить видео с обводкой",
                    Filter = "MP4 (H.264 при доступности, иначе MPEG-4)|*.mp4|AVI (Motion JPEG — гарантированно)|*.avi",
                    FileName = Path.GetFileNameWithoutExtension(src.FileName) + "_annotated",
                    DefaultExt = ".mp4",
                    AddExtension = true
                };
                if (dlg.ShowDialog() != true) return;
                string outPath = dlg.FileName;
                if(string.Equals(Path.GetFullPath(outPath),Path.GetFullPath(src.FilePath),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Нельзя записывать экспорт поверх исходного видео. Выбери другое имя.");

                _logService.Info("Экспорт видео начат… (режим подсветки: " +
                                 new[] { "боксы", "контур", "боксы+контур", "маска+контур" }[(int)HlMode] + ")");
                StatusText = "video export: preparing…";

                var settings=CaptureExportSettings();
                _logService.Info("Видео: полный повторный анализ с начала исходника, история выбора по кадрам; без звука. CSV/JSON — отдельные наблюдения просмотра.");
                int written = await Task.Run(() => ExportVideoCore(src.FilePath, outPath,settings));
                _logService.Info(written > 0
                    ? $"✓ Видео сохранено: {written} кадров → {outPath}"
                    : "Экспорт видео не выполнен (см. сообщения выше)");
                StatusText = written > 0 ? $"video export done: {written} frames" : "video export failed";
            }
            catch (Exception ex)
            {
                _logService.Error($"Ошибка экспорта видео: {ex.Message}");
                StatusText = "video export failed";
            }
            finally
            {
                _isExporting = false;
                ExportInputsEnabled=true;
                _ = Task.Run(()=> {lock(_processing) ApplyHighlightMode();});
            }
        }

        private int ExportVideoCore(string srcPath, string outPath, ExportSettings settings)
        {
            var fss = new FileSourceService(_logService); // свой capture, не мешаем UI
            var tracker = settings.Tracking ? new TrackerService(null) : null;
            var roiSchedule = new RoiSchedule();
            // выбор переносим СНИПШОТОМ (класс+бокс) — ре-захват по первому кадру экспорта
            var selExport = settings.Selection;
            int written = 0;
            VideoWriter writer = null;
            try
            {
                if (!fss.OpenVideo(srcPath, out int fc, out _, out _, out _))
                    throw new Exception("Не удалось открыть видео");
                double fps=fss.ExactFps;
                fss.SeekFrame(0);

                bool avi = string.Equals(Path.GetExtension(outPath), ".avi", StringComparison.OrdinalIgnoreCase);
                // mp4v/MJPG гарантированно пишет встроенный ffmpeg-бэкенд OpenCvSharp4;
                // avc1 — вторым номером (если H.264-энкодер в сборке есть).
                string[] codecIds = avi ? new[] { "MJPG" } : new[] { "mp4v", "avc1" };

                var swTick = System.Diagnostics.Stopwatch.StartNew();
                long lastUi = 0;
                int processed = 0;
                try
                {
                    while (fss.ReadNextFrame(out var fd))
                    {
                        try
                        {
                            var dets = new List<Detection>();
                            if (fd.Frame != null && !fd.Frame.IsDisposed)
                            {
                                dets = DetectForExport(fd.Frame,settings,true,roiSchedule,fd.FrameIndex,fd.Timestamp/1000.0);
                                if (settings.PersonOnly)
                                    dets = dets.FindAll(d => string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase));
                                foreach(var d in dets) { d.FrameWidth=fd.Frame.Width;d.FrameHeight=fd.Frame.Height;d.FrameIndex=fd.FrameIndex; d.SourceSeconds=fd.Timestamp/1000.0; d.Appearance=BuildAppearance(fd.Frame,d); }
                                if (tracker != null) dets = tracker.Update(dets,settings.Confidence,settings.Low,fd.Timestamp/1000.0);
                            }
                            dets=selExport.Select(dets,fd.FrameIndex,fd.Timestamp/1000.0,settings.Active,d=>tracker?.Reanchor(d));
                            using Mat ann = _renderService.DrawDetections(fd.Frame ?? new Mat(), dets, settings.Highlight);
                            if (ann.Empty()) continue;

                            if (writer == null)
                            {
                                // размер берём из ПЕРВОГО реального кадра, не из свойств потока:
                                // рассогласование размера с пикселями = «битый» MP4
                                OpenCvSharp.Size fsize = new OpenCvSharp.Size(ann.Width, ann.Height);
                                foreach (var id in codecIds)
                                {
                                    // FourCC руками — как cv::VideoWriter::fourcc, без P/Invoke при пробe
                                    int fcc = id[0] | (id[1] << 8) | (id[2] << 16) | (id[3] << 24);
                                    var tw = new VideoWriter(outPath, fcc, fps, fsize, true);
                                    if (tw.IsOpened())
                                    {
                                        writer = tw;
                                        _logService.Info($"Видеоэкспорт: кодек {id}, {fsize.Width}x{fsize.Height} @ {fps} fps");
                                        break;
                                    }
                                    tw.Dispose();
                                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                                }
                                if (writer == null)
                                    throw new Exception("Ни один видео-кодек не открылся (mp4v/avc1 или MJPG для .avi)");
                            }

                            writer.Write(ann);
                            written++;
                        }
                        finally { fd.Frame?.Dispose(); }

                        processed++;
                        if (processed % 50 == 0 || swTick.ElapsedMilliseconds - lastUi > 400)
                        {
                            lastUi = swTick.ElapsedMilliseconds;
                            int done = processed, totalF = Math.Max(fc, processed + 1);
                            _ = Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                                StatusText = $"video export {done}/{totalF}"));
                        }
                    }
                }
                finally
                {
                    writer?.Release();   // дописать атом-бокс mp4 — без этого файл «битый»
                    writer?.Dispose();
                }

                // если не записалось ни кадра — не оставлять битый файл
                if (written == 0)
                {
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                    throw new Exception("Не записано ни одного кадра — пустой файл удалён");
                }
                try
                {
                    long len = new FileInfo(outPath).Length;
                    if (len < 4096)
                        _logService.Warn($"Файл подозрительно маленький ({len} Б) — попробуйте AVI/MJPG");
                }
                catch { }
                var manifest=new { analysis_mode="full_reanalysis", source=srcPath,
                    first_source_frame=0, frames_written=written, output_fps=fss.ExactFps, audio_included=false, selection_actions=selExport.Describe(),
                    settings=new {settings.Tracking,settings.PersonOnly,settings.Confidence,settings.Low,settings.Nms,settings.Active,settings.Roi,settings.RoiEveryFrame},
                    note="Constant frame rate output. Selection actions replayed at original frame indices; not identical to view observations." };
                File.WriteAllText(outPath+".metadata.json",System.Text.Json.JsonSerializer.Serialize(manifest,
                    new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),new UTF8Encoding(false));
            }
            finally { fss.Close(); }
            return written;
        }

        // ---------- Экспорт результатов ----------
        private string ExportBaseName() =>
            Path.GetFileNameWithoutExtension(SourcesViewModel.SelectedSource?.FileName ?? "detections") + "_detections";

        private List<Detection> ExportSnapshot()
        {
            bool selective; lock(_selLock) selective=_exportTimeline.HasHistory;
            var data=SessionDetections.Count>0 ? SessionDetections.ToList() : Detections.ToList();
            return (selective ? data.Where(d=>d.IsSelected) : data).Select(CopyDetection)
                .OrderBy(d=>d.FrameIndex).ThenBy(d=>d.SelectionNumber).ToList();
        }

        private void WriteObservationMetadata(string path,int count,long dropped,string source)
        {
            var metadata=new { analysis_mode="view_observations", source=source,
                records=count, history_truncated=dropped>0, dropped_records=dropped,
                note="Observed processing frames only; not identical to full video/Frames reanalysis. time/source_seconds refer to source; observed_at is processing wall clock." };
            File.WriteAllText(path+".metadata.json",System.Text.Json.JsonSerializer.Serialize(metadata,
                new System.Text.Json.JsonSerializerOptions {WriteIndented=true}),new UTF8Encoding(false));
            if(dropped>0) System.Windows.MessageBox.Show(
                $"История неполная: не сохранено обнаружений {dropped}. Подробности в файле .metadata.json. Видео/Frames выполняют независимый полный анализ.",
                "Неполная история просмотра",MessageBoxButton.OK,MessageBoxImage.Warning);
        }

        private void ExportCsv()
        {
            var data = ExportSnapshot();
            long dropped=_sessionDropped;
            string observedSource=_currentSourcePath ?? "";
            if (data.Count == 0) { _logService.Warn("Экспорт: нечего сохранять (прогон ещё не выполнен)"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV файл|*.csv|Все файлы|*.*",
                FileName = ExportBaseName() + ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dlg.FileName, ExportSerialization.Csv(data), new UTF8Encoding(true));
                WriteObservationMetadata(dlg.FileName,data.Count,dropped,observedSource);
                _logService.Info($"CSV сохранён: {dlg.FileName} ({data.Count} записей)");
            }
            catch (Exception ex) { _logService.Error($"Ошибка CSV: {ex.Message}"); }
        }

        private void ExportJson()
        {
            var data = ExportSnapshot();
            long dropped=_sessionDropped;
            string observedSource=_currentSourcePath ?? "";
            if (data.Count == 0) { _logService.Warn("Экспорт: нечего сохранять (прогон ещё не выполнен)"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON файл|*.json|Все файлы|*.*",
                FileName = ExportBaseName() + ".json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dlg.FileName, ExportSerialization.Json(data), new UTF8Encoding(false));
                WriteObservationMetadata(dlg.FileName,data.Count,dropped,observedSource);
                _logService.Info($"JSON сохранён: {dlg.FileName} ({data.Count} записей)");
            }
            catch (Exception ex) { _logService.Error($"Ошибка JSON: {ex.Message}"); }
        }

        public void Shutdown()
        {
            Stop(); _sourceVersion++; _sessionVersion++;
            lock(_cacheLock) { _cachedFrame?.Dispose(); _cachedFrame=null; }
            _ = Task.Run(()=> { lock(_processing) _detectorService?.Dispose(); });
        }

        private void FramePump_ErrorOccurred(object sender, string errorMessage)
        {
            _logService.Error(errorMessage);
            IsPlaying = false;
        }
    }
}
