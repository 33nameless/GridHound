using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ObjectDetectTrackApp.Services;

namespace ObjectDetectTrackApp;

internal sealed class EasterEggWindow : Window
{
    private readonly Grid _body = new();
    private readonly PasswordBox _phrase = new() { MaxLength = 256, FontSize = 18, Padding = new Thickness(8) };
    private readonly TextBlock _message = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) };
    private readonly Button _submit;
    private bool _busy, _closed;
    private static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(205,174,100));

    internal EasterEggWindow(Window owner)
    {
        Owner = owner;
        Title = "GridHound";
        Width = 450; Height = 245;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black; Foreground = Brushes.White;
        FontFamily = new FontFamily("Consolas");
        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 38, ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        var root = new DockPanel();
        var header = new DockPanel { Height = 38, Background = Brushes.Black };
        var close = MakeButton("CLOSE"); close.Width = 85;
        close.Click += (_,_) => Close();
        WindowChrome.SetIsHitTestVisibleInChrome(close,true);
        DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text="GRIDHOUND", Margin=new Thickness(12,0,0,0), VerticalAlignment=VerticalAlignment.Center });
        DockPanel.SetDock(header,Dock.Top); root.Children.Add(header); root.Children.Add(_body);
        Content = new Border { BorderBrush=Gold, BorderThickness=new Thickness(1), Child=root };
        var form = new StackPanel { Margin=new Thickness(20) };
        form.Children.Add(new TextBlock { Text="Введите фразу", Margin=new Thickness(0,0,0,10) });
        form.Children.Add(_phrase);
        _submit=MakeButton("ENTER"); _submit.Margin=new Thickness(0,12,0,0);
        _submit.IsDefault=true; _submit.Click += async (_,_) => await VerifyAsync();
        form.Children.Add(_submit); form.Children.Add(_message); _body.Children.Add(form);
        PreviewKeyDown += (_,e) => { if(e.Key==Key.Escape) { e.Handled=true; Close(); } };
        Loaded += (_,_) => _phrase.Focus();
        Closed += (_,_) => { _closed=true; _phrase.Clear(); };
    }

    private static Button MakeButton(string text)
    {
        var b=new Button { Content=text, Background=Brushes.Black, Foreground=Brushes.White,
            BorderBrush=Brushes.Gray, Padding=new Thickness(12,5,12,5), Cursor=Cursors.Hand };
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty,new Thickness(10,5,10,5));
        border.AppendChild(content); template.VisualTree=border;
        var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(45,45,45))));
        template.Triggers.Add(hover); b.Template=template;
        return b;
    }

    private async Task VerifyAsync()
    {
        if(_busy || _closed) return;
        _busy=true; _submit.IsEnabled=false; _phrase.IsEnabled=false; _message.Text="";
        string input=_phrase.Password;
        _phrase.Clear();
        bool valid=false;
        try { valid=await Task.Run(()=>EasterEggVerifier.Verify(input)); }
        catch (Exception) { valid=false; } // Never log the phrase or verification input.
        finally { input=string.Empty; }
        if(!valid) await Task.Delay(800);
        if(_closed) return;
        if(valid) ShowMark();
        else { _message.Text="Не удалось открыть."; _phrase.IsEnabled=true; _submit.IsEnabled=true; _phrase.Focus(); }
        _busy=false;
    }

    private void ShowMark()
    {
        _body.Children.Clear();
        double height=Math.Min(720,Math.Max(300,Owner.ActualHeight-60));
        double width=Math.Min(560,Math.Max(300,Owner.ActualWidth-60));
        Width=width; Height=height;
        Left=Owner.Left+(Owner.ActualWidth-width)/2; Top=Owner.Top+(Owner.ActualHeight-height)/2;
        var grid=new Grid { Margin=new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var picture=new Image { Stretch=Stretch.Uniform,
            Source=new BitmapImage(new Uri("pack://application:,,,/Resources/EasterEgg.png")) };
        grid.Children.Add(picture);
        var name=new TextBlock { Text="yurei", Foreground=Gold, FontSize=28,
            HorizontalAlignment=HorizontalAlignment.Center, Margin=new Thickness(0,14,0,6) };
        Grid.SetRow(name,1); grid.Children.Add(name);
        var inscription=new TextBlock { Text=@"roullet of destiny \\ 143", Foreground=Brushes.White,
            FontSize=16, TextWrapping=TextWrapping.Wrap, TextAlignment=TextAlignment.Center };
        Grid.SetRow(inscription,2); grid.Children.Add(inscription);
        _body.Children.Add(grid);
    }
}
