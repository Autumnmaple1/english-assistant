using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace ScreenEnglish;

public sealed class CaptureWindow : Window
{
    private readonly TaskCompletionSource<Native.RECT?> completion = new();
    private readonly Grid root = new();
    private readonly Canvas canvas = new();
    private readonly Rectangle selection = new() { Stroke = UI.Brush(255, 255, 255), StrokeThickness = 1, Visibility = Visibility.Collapsed };
    private readonly Image clearImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly RectangleGeometry clearClip = new();
    private Border? guide;
    private Point? start;
    private Native.RECT monitor;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer escapeTimer;
    public CaptureWindow() {
        Title = "Select a region"; Content = root; Native.Borderless(this);
        escapeTimer = DispatcherQueue.CreateTimer(); escapeTimer.Interval = TimeSpan.FromMilliseconds(40);
        escapeTimer.Tick += (_, _) => { if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) Finish(null); };
        root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) Finish(null); };
        root.PointerPressed += Down; root.PointerMoved += Move; root.PointerReleased += Up;
        root.PointerCanceled += (_, _) => Finish(null);
        Closed += (_, _) => { escapeTimer.Stop(); completion.TrySetResult(null); };
    }
    public async Task<Native.RECT?> Select(byte[] image, Native.RECT bounds, string action) {
        monitor = bounds;
        var source = await UI.Image(image);
        root.Children.Add(new Image { Source = source, Stretch = Stretch.Fill });
        root.Children.Add(new Border { Background = UI.Brush(0, 0, 0, 140) });
        clearImage.Source = source; clearImage.Clip = clearClip; root.Children.Add(clearImage);
        root.Children.Add(canvas); canvas.Children.Add(selection);
        guide = new Border { Background = UI.Brush(28, 28, 30), CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = $"Drag to {action}   ·   Esc to cancel", Foreground = new SolidColorBrush(Colors.White), FontSize = 12 }, IsHitTestVisible = false };
        root.Children.Add(guide);
        Native.Place(this, bounds);
        escapeTimer.Start();
        return await completion.Task;
    }
    private void Down(object sender, PointerRoutedEventArgs e) {
        if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
        start = e.GetCurrentPoint(root).Position; root.CapturePointer(e.Pointer);
        Update(start.Value); e.Handled = true;
    }
    private void Move(object sender, PointerRoutedEventArgs e) { if (start is not null) Update(e.GetCurrentPoint(root).Position); }
    private void Update(Point end) {
        if (start is not Point from) return;
        double x = Math.Clamp(end.X, 0, root.ActualWidth), y = Math.Clamp(end.Y, 0, root.ActualHeight);
        double left = Math.Min(from.X, x), top = Math.Min(from.Y, y), width = Math.Abs(from.X - x), height = Math.Abs(from.Y - y);
        foreach (var rectangle in new[] { selection }) { Canvas.SetLeft(rectangle, left); Canvas.SetTop(rectangle, top); rectangle.Width = width; rectangle.Height = height; rectangle.Visibility = Visibility.Visible; }
        clearClip.Rect = new Rect(left, top, width, height); clearImage.Visibility = Visibility.Visible; if (guide is not null) guide.Visibility = Visibility.Collapsed;
    }
    private void Up(object sender, PointerRoutedEventArgs e) {
        if (start is null) return; Update(e.GetCurrentPoint(root).Position); root.ReleasePointerCaptures();
        CompleteSelection();
    }
    internal void PreviewSelection(Point from, Point to) { start = from; Update(to); }
    internal void CompleteSelection() {
        if (selection.Width < 5 || selection.Height < 5) {
            start = null; selection.Visibility = clearImage.Visibility = Visibility.Collapsed; if (guide is not null) guide.Visibility = Visibility.Visible; return;
        }
        double sx = monitor.Width / root.ActualWidth, sy = monitor.Height / root.ActualHeight;
        int left = (int)Math.Floor(Canvas.GetLeft(selection) * sx), top = (int)Math.Floor(Canvas.GetTop(selection) * sy);
        int right = Math.Min(monitor.Width, (int)Math.Ceiling((Canvas.GetLeft(selection) + selection.Width) * sx));
        int bottom = Math.Min(monitor.Height, (int)Math.Ceiling((Canvas.GetTop(selection) + selection.Height) * sy));
        Finish(new Native.RECT(left, top, right, bottom));
    }
    internal void CancelSelection() => Finish(null);
    private void Finish(Native.RECT? result) { if (completion.TrySetResult(result)) Close(); }
}



