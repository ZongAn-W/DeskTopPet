using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using DesktopPet.Core;
using WpfPoint = System.Windows.Point;

namespace DesktopPet;

public partial class PetWindow : Window
{
    private const double PetWidth = 180;
    private const double PetHeight = 210;
    private readonly PetController _controller = new();
    private readonly PreferencesStore _preferences = new();
    private readonly DispatcherTimer _timer;
    private TrayController? _tray;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _nextWalk = DateTime.UtcNow.AddSeconds(2);
    private bool _walkingRight = true;
    private double _targetX;
    private WpfPoint _pressPoint;
    private double _pressLeft;
    private double _pressTop;
    private bool _dragging;
    private bool _closing;
    private DateTime _walkUntil = DateTime.MinValue;

    public PetWindow()
    {
        InitializeComponent();
        ContextMenu = CreateContextMenu();
        Loaded += OnLoaded;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closing += OnClosing;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, e) => { ContextMenu.IsOpen = true; e.Handled = true; };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var saved = _preferences.Load();
        _controller.Restore(saved.IsPaused, saved.IsManualSleeping);
        PlaceFromPreferences(saved);
        try
        {
            _tray = new TrayController();
            _tray.TogglePauseRequested += (_, _) => TogglePause();
            _tray.ToggleSleepRequested += (_, _) => ToggleSleep();
            _tray.ExitRequested += (_, _) => Close();
            _tray.Update(_controller.IsPaused, _controller.IsManualSleeping);
        }
        catch { _tray = null; }
        _timer.Start();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        var pause = new MenuItem { Header = "暂停走动" };
        pause.Click += (_, _) => TogglePause();
        var sleep = new MenuItem { Header = "睡觉" };
        sleep.Click += (_, _) => ToggleSleep();
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(pause);
        menu.Items.Add(sleep);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.Opened += (_, _) =>
        {
            pause.Header = _controller.IsPaused ? "继续走动" : "暂停走动";
            sleep.Header = _controller.IsManualSleeping || _controller.IsAutoSleeping ? "唤醒" : "睡觉";
        };
        return menu;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;
        _controller.Advance(elapsed);
        if (!_dragging && _controller.State is not (PetState.Sleeping or PetState.Responding) && !_controller.IsPaused)
        {
            if (_controller.State is PetState.WalkingLeft or PetState.WalkingRight)
            {
                var area = CurrentMonitor().WorkingArea;
                var distance = _targetX - Left;
                Left = PetGeometry.ClampX(Left + Math.Sign(distance) * Math.Min(Math.Abs(distance), 1.1), area, PetWidth);
                if (Math.Abs(Left - _targetX) < 1.2 || now >= _walkUntil)
                {
                    _controller.StopWalking();
                    _nextWalk = now.AddSeconds(Random.Shared.Next(2, 6));
                }
            }
            else if (now >= _nextWalk)
            {
                var area = CurrentMonitor().WorkingArea;
                _targetX = PetGeometry.ClampX(Left + Random.Shared.Next(-150, 151), area, PetWidth);
                _walkingRight = _targetX >= Left;
                _controller.SetWalking(_walkingRight);
                _walkUntil = now.AddSeconds(5);
            }
        }
        Pet.State = _controller.State;
        Pet.FacingRight = _walkingRight;
        Pet.Advance(elapsed.TotalSeconds);
        _tray?.Update(_controller.IsPaused, _controller.IsManualSleeping || _controller.IsAutoSleeping);
    }


    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = PointToScreen(e.GetPosition(this));
        _pressLeft = Left;
        _pressTop = Top;
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var point = PointToScreen(e.GetPosition(this));
        var delta = point - _pressPoint;
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) >= 8)
        {
            _dragging = true;
            _controller.BeginDrag();
        }
        if (_dragging)
        {
            Left = _pressLeft + delta.X;
            Top = _pressTop + delta.Y;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_dragging)
        {
            _controller.EndDrag();
            SnapToCurrentMonitor();
        }
        else _controller.Click();
        _dragging = false;
        e.Handled = true;
    }

    private void TogglePause()
    {
        _controller.IsPaused = !_controller.IsPaused;
        if (_controller.IsPaused) _controller.StopWalking();
        _tray?.Update(_controller.IsPaused, _controller.IsManualSleeping || _controller.IsAutoSleeping);
    }

    private void ToggleSleep()
    {
        _controller.ToggleManualSleep();
        _tray?.Update(_controller.IsPaused, _controller.IsManualSleeping || _controller.IsAutoSleeping);
    }

    private void PlaceFromPreferences(PetPreferences saved)
    {
        var monitors = GetMonitors();
        var primary = Forms.Screen.PrimaryScreen?.DeviceName ?? monitors[0].Id;
        var placement = ScreenPlacement.Resolve(saved, monitors, primary, PetWidth, PetHeight);
        Left = placement.Left;
        Top = placement.Top;
    }

    private void SnapToCurrentMonitor()
    {
        var monitor = CurrentMonitor();
        Left = PetGeometry.ClampX(Left, monitor.WorkingArea, PetWidth);
        Top = PetGeometry.BottomAlignedTop(monitor.WorkingArea, PetHeight);
    }

    private MonitorArea CurrentMonitor()
    {
        var centerPoint = PointToScreen(new WpfPoint(PetWidth / 2, PetHeight / 2));
        var center = new System.Drawing.Point((int)centerPoint.X, (int)centerPoint.Y);
        var screen = Forms.Screen.FromPoint(center) ?? Forms.Screen.PrimaryScreen!;
        return ToMonitorArea(screen);
    }

    private List<MonitorArea> GetMonitors() => Forms.Screen.AllScreens.Select(ToMonitorArea).ToList();

    private MonitorArea ToMonitorArea(Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return new MonitorArea(screen.DeviceName, area.Left, area.Top, area.Width, area.Height);
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new WpfPoint(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new WpfPoint(area.Right, area.Bottom));
        return new MonitorArea(screen.DeviceName, topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(SnapToCurrentMonitor);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        var monitor = CurrentMonitor();
        _preferences.Save(new PetPreferences(monitor.Id, Left, _controller.IsPaused, _controller.IsManualSleeping));
        _tray?.Dispose();
    }
}
