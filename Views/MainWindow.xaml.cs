using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ObjectDetectTrackApp.ViewModels;

namespace ObjectDetectTrackApp
{
    public partial class MainWindow : Window
    {
        // ---- Зум/пан превью ----
        private const double ZoomMin = 0.2;
        private const double ZoomMax = 10.0;
        private const double ZoomStep = 1.15;

        private bool _dragging;
        private Point _dragStart;
        private double _startH, _startV;

        public MainWindow()
        {
            InitializeComponent();
            var easterEggCommand = new RoutedUICommand();
            InputBindings.Add(new KeyBinding(easterEggCommand, Key.Y, ModifierKeys.Control | ModifierKeys.Alt));
            CommandBindings.Add(new CommandBinding(easterEggCommand, (_, e) =>
            {
                new EasterEggWindow(this).ShowDialog();
                e.Handled = true;
            }));
            this.DataContext = new MainViewModel();
            Closed += (_, _) => (DataContext as MainViewModel)?.Shutdown();

            if (DataContext is MainViewModel vm)
            {
                // «Шипы» детекций VM отдаёт снапшотом — обновляем кодом (Playhead/FrameCount приходят биндингом)
                vm.TimelineChanged += () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    Tl.Hits = vm.GetTimelineHitsSnapshot();
                }));
            }
        }

        // WindowChrome handles caption dragging, double-click and resize hit testing.
        // Keep commands native: KILL closes normally and executes the existing Closed cleanup.
        private void WindowMinimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

        private void WindowMaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void WindowClose_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

        private HwndSource? _chromeSource;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _chromeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _chromeSource?.AddHook(WindowBoundsHook);
        }

        protected override void OnClosed(EventArgs e)
        {
            _chromeSource?.RemoveHook(WindowBoundsHook);
            _chromeSource = null;
            base.OnClosed(e);
        }

        // Native monitor/work rectangles use device pixels, including mixed-DPI monitors.
        // Restrict maximisation to the current monitor's work area, not over the taskbar.
        private IntPtr WindowBoundsHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmGetMinMaxInfo = 0x0024;
            if (msg != WmGetMinMaxInfo || lParam == IntPtr.Zero) return IntPtr.Zero;
            IntPtr monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var bounds = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
                bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                bounds.MaxSize.X = info.Work.Right - info.Work.Left;
                bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                Marshal.StructureToPtr(bounds, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMinMaxInfo
        {
            public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor, Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

        /// <summary>Клик по мини-таймлайну: позиция мыши -> индекс кадра -> переход.</summary>
        private void Timeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel vm || Tl.FrameCount < 2) return;

            double x = e.GetPosition(Tl).X;
            int idx = (int)Math.Round(x / Math.Max(1.0, Tl.ActualWidth) * (Tl.FrameCount - 1));
            idx = Math.Max(0, Math.Min(Tl.FrameCount - 1, idx));
            vm.JumpToTimelineFrame(idx);
            e.Handled = true;
        }

        private ScaleTransform Zoom => (ScaleTransform)PreviewImage.LayoutTransform;

        /// <summary>Чекбокс «🎯 следить» у объекта в списке DETECTIONS.</summary>
        private void Det_Track_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ObjectDetectTrackApp.Models.Detection d &&
                DataContext is MainViewModel vm)
                vm.SetSelectionFor(d, cb.IsChecked == true);
        }

        /// <summary>Колесо мыши: приблизить/отдалить. Центр вида стараемся не «гулять».</summary>
        private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double old = Zoom.ScaleX;
            double v = Math.Clamp(old * (e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep), ZoomMin, ZoomMax);
            if (Math.Abs(v - old) < 1e-9) { e.Handled = true; return; }

            // относительная точка под курсором — к ней и масштабируем
            Point under = e.GetPosition(PreviewScroll);
            double cx = PreviewScroll.HorizontalOffset + under.X;
            double cy = PreviewScroll.VerticalOffset + under.Y;

            Zoom.ScaleX = Zoom.ScaleY = v;
            UpdatePreviewScrollbars();
            PreviewScroll.UpdateLayout();

            double k = v / old;
            PreviewScroll.ScrollToHorizontalOffset(cx * k - under.X);
            PreviewScroll.ScrollToVerticalOffset(cy * k - under.Y);

            UpdateZoomLabel(v);
            e.Handled = true;
        }

        private void UpdatePreviewScrollbars()
        {
            // Explicit policy avoids Auto -> smaller viewport -> changed image size -> Auto oscillation.
            bool zoomed = Zoom.ScaleX > 1.0001;
            var mode = zoomed ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
            PreviewScroll.HorizontalScrollBarVisibility = mode;
            PreviewScroll.VerticalScrollBarVisibility = mode;
            if (!zoomed)
            {
                PreviewScroll.ScrollToHorizontalOffset(0);
                PreviewScroll.ScrollToVerticalOffset(0);
            }
        }

        private void UpdateZoomLabel(double v)
        {
            if (Math.Abs(v - 1.0) < 1e-9)
            {
                ZoomLabel.Visibility = Visibility.Collapsed;
                return;
            }
            ZoomLabel.Text = $"{v * 100:F0}%";
            ZoomLabel.Visibility = Visibility.Visible;
        }

        // ---- Режим выбора: клик (без сдвига мыши) = выбрать/снять объект ----
        private bool _selDown;
        private Point _selDownPos;

        private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _selDown = false;
            _dragging = false;
            if (DataContext is MainViewModel vm && vm.SelectMode)
            {
                // Selection and panning are mutually exclusive. Measure gesture in viewport DIPs.
                _selDown = true;
                _selDownPos = e.GetPosition(PreviewScroll);
                PreviewImage.CaptureMouse();
                e.Handled = true;
                return;
            }
            if (Zoom.ScaleX <= 1.0001) return;
            _dragging = true;
            _dragStart = e.GetPosition(PreviewScroll);
            _startH = PreviewScroll.HorizontalOffset;
            _startV = PreviewScroll.VerticalOffset;
            PreviewImage.CaptureMouse();
            PreviewScroll.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void Preview_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(PreviewScroll);
            PreviewScroll.ScrollToHorizontalOffset(_startH - (p.X - _dragStart.X));
            PreviewScroll.ScrollToVerticalOffset(_startV - (p.Y - _dragStart.Y));
            e.Handled = true;
        }

        private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            bool selecting = _selDown, dragging = _dragging;
            Point down = _selDownPos;
            // Always release capture, including the successful-selection path.
            _selDown = false;
            _dragging = false;
            if (PreviewImage.IsMouseCaptured) PreviewImage.ReleaseMouseCapture();
            PreviewScroll.Cursor = null;
            if (!selecting && !dragging) return;
            e.Handled = true;
            if (!selecting) return;
            Point up = e.GetPosition(PreviewScroll);
            if (Math.Abs(up.X - down.X) + Math.Abs(up.Y - down.Y) > 4) return;
            if (DataContext is not MainViewModel vm ||
                PreviewImage.Source is not System.Windows.Media.Imaging.BitmapSource bs) return;
            // GetPosition(Image) already undoes LayoutTransform and scrolling. Do not divide by zoom again.
            Point imagePoint = e.GetPosition(PreviewImage);
            if (ObjectDetectTrackApp.Services.PreviewCoordinates.TryMapUniform(
                imagePoint.X, imagePoint.Y, PreviewImage.ActualWidth, PreviewImage.ActualHeight,
                bs.PixelWidth, bs.PixelHeight, out double x, out double y))
                vm.SelectAt(x, y, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }

        private void Preview_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _selDown = false;
            _dragging = false;
            PreviewScroll.Cursor = null;
        }

        /// <summary>Двойной клик — вписать в окно (сброс зума).</summary>
        private void Preview_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            Zoom.ScaleX = Zoom.ScaleY = 1.0;
            UpdatePreviewScrollbars();
            UpdateZoomLabel(1.0);
            PreviewScroll.UpdateLayout();
            PreviewScroll.ScrollToHorizontalOffset(0);
            PreviewScroll.ScrollToVerticalOffset(0);
            e.Handled = true;
        }

        // ---- Логи: правый клик = копировать (этот обработчик был в репо, но не был
        //      привязан в XAML; теперь привязан на LogListBox) ----
        private void LogListBox_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var listBox = sender as System.Windows.Controls.ListBox;
            var viewModel = this.DataContext as MainViewModel;

            if (viewModel?.LogViewModel?.Logs == null || viewModel.LogViewModel.Logs.Count == 0)
            {
                MessageBox.Show("Логи пусты!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Если клик по элементу - копируем этот элемент
            if (listBox?.SelectedItem != null)
            {
                var selectedLog = listBox.SelectedItem.ToString();
                CopyToClipboard(selectedLog);
                e.Handled = true;
                return;
            }

            // Если клик в пустое место - копируем все логи
            var allLogs = string.Join(System.Environment.NewLine,
                viewModel.LogViewModel.Logs.Select(log => log.ToString()));

            CopyToClipboard(allLogs);
            e.Handled = true;
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                MessageBox.Show("✓ Скопировано в буфер обмена!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
