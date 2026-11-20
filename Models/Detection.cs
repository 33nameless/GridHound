namespace ObjectDetectTrackApp.Models
{
    public class Detection : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public int Id { get; set; }
        private string _className;
        public string ClassName { get => _className; set { if (SetProperty(ref _className, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private float _confidence;
        public float Confidence { get => _confidence; set { if (SetProperty(ref _confidence, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private int _x;
        public int X { get => _x; set { if (SetProperty(ref _x, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private int _y;
        public int Y { get => _y; set { if (SetProperty(ref _y, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private int _width;
        public int Width { get => _width; set { if (SetProperty(ref _width, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private int _height;
        public int Height { get => _height; set { if (SetProperty(ref _height, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        private int? _trackId;
        public int? TrackId { get => _trackId; set { if (SetProperty(ref _trackId, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } } // Для SORT
        // Полигон instance-маски (режимы «Контур»/«Маска»): пары x,y в координатах кадра.
        public float[] MaskPolygon { get; set; }
        // Индекс анкорса в выходе seg-модели (для mask-коэффициентов), -1 если не задан
        public int AnchorIdx { get; set; } = -1;
        private int _frameIndex;
        public int FrameIndex { get => _frameIndex; set { if (SetProperty(ref _frameIndex, value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } } // Кадр источника (для перехода из списка результатов)
        // Ручной выбор (клик по превью): рисуем/экспортируем только выбранных, когда выбор непустой
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
        public float[] Appearance { get; set; }
        public Guid? SelectionKey { get; set; }
        private int? _selectionNumber;
        public int? SelectionNumber { get => _selectionNumber; set { if(SetProperty(ref _selectionNumber,value)) { OnPropertyChanged(nameof(DisplayLine)); OnPropertyChanged(nameof(SessionLine)); } } }
        public int FrameWidth {get;set;}
        public int FrameHeight {get;set;}
        public bool IsEdgeContinuation {get;set;}
        public bool IsWeakContinuation {get;set;}
        public double SourceSeconds {get;set;} = double.NaN;
        public DateTime Timestamp { get; set; }

        public void UpdateFrom(Detection d)
        {
            FrameWidth=d.FrameWidth;FrameHeight=d.FrameHeight;IsEdgeContinuation=d.IsEdgeContinuation;
            SourceSeconds=d.SourceSeconds; IsWeakContinuation=d.IsWeakContinuation; Id=d.Id; ClassName=d.ClassName; Confidence=d.Confidence; X=d.X; Y=d.Y;
            Width=d.Width; Height=d.Height; TrackId=d.TrackId; FrameIndex=d.FrameIndex;
            Timestamp=d.Timestamp; MaskPolygon=d.MaskPolygon; Appearance=d.Appearance; IsSelected=d.IsSelected; SelectionKey=d.SelectionKey; SelectionNumber=d.SelectionNumber;
        }

        /// <summary>Готовая строка для списка справа: «person #3 | 0.87» или «bus | 0.90» без трека.</summary>
        public string DisplayLine => SelectionNumber is int number
            ? $"S{number} · {ClassName} | {Confidence:F2} (track {TrackId?.ToString() ?? "—"})"
            : TrackId.HasValue
            ? $"{ClassName} #{TrackId.Value} | {Confidence:F2}"
            : $"{ClassName} | {Confidence:F2}";

        /// <summary>Строка для списка результатов всего видео: [f123] person #2 | 0.87.</summary>
        public string SessionLine => $"[f{FrameIndex}] " + DisplayLine;
    }
}
