using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PenguTEX.Core;
using PenguTEX.Imaging;

namespace PenguTEX.Desktop;

public sealed class PreviewWindow : Window
{
    private readonly Image _before = new() { Stretch = Stretch.Uniform }, _after = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _channel = new() { ItemsSource = new[] { "RGB", "Red", "Green", "Blue", "Alpha / data" }, SelectedIndex = 0, Width = 160 };
    private readonly NumericUpDown _mip = new() { Minimum = 0, Increment = 1, Width = 100, Value = 0 };
    private PixelImage? _left, _right; private WriteableBitmap? _a, _b;
    private readonly Asset _asset; private readonly PlanItem _item; private CancellationTokenSource? _cancel;
    public string Status => _status.Text ?? "";
    public PreviewWindow(Asset asset, PlanItem item)
    {
        _asset = asset; _item = item; Title = "Texture inspection — " + item.Path; Width = 1100; Height = 760;
        _mip.Maximum = Math.Max(0, (item.Input?.MipCount ?? 1) - 1);
        if (item.Input is { } info) _mip.Value = info.DropFor(4096);
        var load = new Button { Content = "Load mip" }; load.Click += async (_, _) => await LoadAsync();
        var toolbar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12,
            Children = { new TextBlock { Text = "Source mip", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, _mip, load, _channel } };
        var images = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        images.Children.Add(new DockPanel { Children = { Label("Source", _before), _before } });
        var right = new DockPanel { Children = { Label(item.Decision == Decision.Rebuild ? "Proposed: CPU rebuild" : $"Proposed: mip {item.DropMips}", _after), _after } }; Grid.SetColumn(right, 1); images.Children.Add(right);
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        root.Children.Add(toolbar); Grid.SetRow(images, 1); root.Children.Add(images); Grid.SetRow(_status, 2); root.Children.Add(_status); Content = root;
        _channel.SelectionChanged += (_, _) => Display(); Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => { _cancel?.Cancel(); _a?.Dispose(); _b?.Dispose(); };
    }
    private static TextBlock Label(string text, Image _) { var label = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(label, Dock.Top); return label; }
    public async Task LoadAsync()
    {
        _cancel?.Cancel(); var cancel = _cancel = new CancellationTokenSource(); int mip = (int)(_mip.Value ?? 0);
        try
        {
            _status.Text = "Decoding selected mip levels…";
            var pair = await Task.Run(() =>
            {
                if (_item.Input is { RecordLayout: not null } records)
                {
                    int first = Math.Min(mip, _item.DropMips);
                    if ((long)Math.Max(1, records.Width >> first) * Math.Max(1, records.Height >> first) > 16 * 1024 * 1024)
                        throw new NotSupportedException("Choose smaller mip levels for record-recovery previews (16 million pixels maximum).");
                    using var original = _asset.Open(); using var canonical = new MemoryStream();
                    Dds.Reduce(original, canonical, first, _asset.Length, cancel.Token, records);
                    canonical.Position = 0; var a = TexturePreview.Decode(canonical, mip - first, ct: cancel.Token);
                    canonical.Position = 0; var b = TexturePreview.Decode(canonical, _item.DropMips - first, ct: cancel.Token);
                    return (a, b);
                }
                using var source = _asset.Open(); var left = TexturePreview.Decode(source, mip, _asset.Length, cancel.Token, _item.Input?.DeclaredMipCount is not null);
                using var target = _asset.Open(); PixelImage right;
                if (_item.Decision == Decision.Rebuild)
                {
                    using var rebuilt = new MemoryStream(); new CpuMipProcessor().Process(target, rebuilt, _item, _asset.Length, cancel.Token);
                    rebuilt.Position = 0; right = TexturePreview.Decode(rebuilt, ct: cancel.Token);
                }
                else right = TexturePreview.Decode(target, _item.DropMips, _asset.Length, cancel.Token, _item.Input?.DeclaredMipCount is not null);
                return (left, right);
            }, cancel.Token);
            if (cancel.IsCancellationRequested) return; (_left, _right) = pair; Display();
            _status.Text = $"Source {_left.Width}×{_left.Height} • Proposed {_right.Width}×{_right.Height} • {_item.Input?.Format} • {_item.Reason}\nRGB is shown independently of alpha. Alpha may contain material data. Proposed pixels use the selected processing operation.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!cancel.IsCancellationRequested) _status.Text = e.GetBaseException().Message; }
    }
    private void Display()
    {
        if (_left is null || _right is null) return; string channel = _channel.SelectedItem as string ?? "RGB";
        _a?.Dispose(); _b?.Dispose(); _a = Bitmap(TexturePreview.Channel(_left, channel)); _b = Bitmap(TexturePreview.Channel(_right, channel));
        _before.Source = _a; _after.Source = _b;
    }
    private static WriteableBitmap Bitmap(PixelImage pixels)
    {
        var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (int y = 0; y < pixels.Height; y++) Marshal.Copy(pixels.Rgba, y * pixels.Width * 4, buffer.Address + y * buffer.RowBytes, pixels.Width * 4);
        return bitmap;
    }
}
