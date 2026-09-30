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
    // Travel speed is configured independently for each direction, in DIP per tick.
    //
    // This must be accumulated rather than assigned straight to Window.Left. Assigning a fractional
    // amount every tick looks right in the property but never reaches the screen: the window position
    // is snapped, so a sub-pixel step is silently dropped and the pet crawls at a fixed rate no matter
    // what the setting says. Measured here, 1.1 and 1.65 px/tick both travelled at 21 px/s until the
    // leftover was carried forward.
    //
    // With the remainder carried, the effective rate is about 15 px/s per unit at the default timer
    // cadence. The right-click menu exposes the supported values for each direction.
    private double _leftWalkSpeed = WalkSpeedOptions.Default;
    private double _rightWalkSpeed = WalkSpeedOptions.Default;
    private MenuItem? _leftSpeedMenu;
    private MenuItem? _rightSpeedMenu;
    // Leftover DIP from the previous tick, carried forward so no fraction of a step is lost.
    private double _travelRemainder;
    // Do not start a stroll with less than this much room to the left, so a stroll always visibly
    // travels instead of grinding against the edge of the working area.
    private const double MinStrollRoomPx = 12;
    private readonly PetController _controller = new();
    private readonly PreferencesStore _preferences = new();
    private readonly DispatcherTimer _timer;
    private TrayController? _tray;
    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _nextWalk = DateTime.UtcNow.AddSeconds(2);
    private WpfPoint _pressPoint;
    private double _pressLeft;
    private double _pressTop;
    private bool _dragging;
    private bool _closing;

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
        _leftWalkSpeed = WalkSpeedOptions.Normalize(saved.LeftWalkSpeed);
        _rightWalkSpeed = WalkSpeedOptions.Normalize(saved.RightWalkSpeed);
        UpdateSpeedMenus();
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
        var speed = new MenuItem { Header = "行进速度" };
        _leftSpeedMenu = CreateSpeedMenu("左向速度", goLeft: true);
        _rightSpeedMenu = CreateSpeedMenu("右向速度", goLeft: false);
        speed.Items.Add(_leftSpeedMenu);
        speed.Items.Add(_rightSpeedMenu);
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(pause);
        menu.Items.Add(sleep);
        menu.Items.Add(speed);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        menu.Opened += (_, _) =>
        {
            pause.Header = _controller.IsPaused ? "继续走动" : "暂停走动";
            sleep.Header = _controller.IsManualSleeping || _controller.IsAutoSleeping ? "唤醒" : "睡觉";
            UpdateSpeedMenus();
        };
        return menu;
    }

    private MenuItem CreateSpeedMenu(string header, bool goLeft)
    {
        var menu = new MenuItem { Header = header };
        foreach (var speed in WalkSpeedOptions.Values)
        {
            var item = new MenuItem
            {
                Header = $"{speed:0.0} DIP/次",
                Tag = speed,
                IsCheckable = true
            };
            item.Click += (_, _) => SetWalkSpeed(goLeft, speed);
            menu.Items.Add(item);
        }
        return menu;
    }

    private void SetWalkSpeed(bool goLeft, double speed)
    {
        var normalized = WalkSpeedOptions.Normalize(speed);
        if (goLeft) _leftWalkSpeed = normalized;
        else _rightWalkSpeed = normalized;
        UpdateSpeedMenus();
    }

    private void UpdateSpeedMenus()
    {
        UpdateSpeedMenu(_leftSpeedMenu, _leftWalkSpeed);
        UpdateSpeedMenu(_rightSpeedMenu, _rightWalkSpeed);
    }

    private static void UpdateSpeedMenu(MenuItem? menu, double speed)
    {
        if (menu is null) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsChecked = Equals(item.Tag, speed);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var elapsed = now - _lastTick;
        _lastTick = now;
        _controller.Advance(elapsed);

        if (!_dragging && !_controller.IsPaused)
        {
            var area = CurrentMonitor().WorkingArea;

            // The sprite sequences decide travel, not this loop. Leftward the walk-up and the walk
            // cycle travel while the turns and the walk-down are acted in place; rightward only the
            // walk module travels while the opening turn and final settle are acted in place.
            if (_controller.IsMoving)
            {
                // Accumulate the fractional speed and only move by whole DIP, so the step is never
                // rounded away by the window position snapping.
                var walkSpeed = _controller.IsStrollingRight ? _rightWalkSpeed : _leftWalkSpeed;
                _travelRemainder += walkSpeed;
                var whole = Math.Floor(_travelRemainder);
                if (whole >= 1)
                {
                    _travelRemainder -= whole;
                    var headingLeft = !_controller.IsStrollingRight;
                    var target = headingLeft ? Left - whole : Left + whole;
                    Left = PetGeometry.Step(Left, target, whole, area, PetWidth).X;
                }
            }
            else if (_controller.State == PetState.Idle && now >= _nextWalk)
            {
                var roomLeft = Left - area.Left;
                var roomRight = area.Right - PetWidth - Left;
                var canGoLeft = roomLeft > MinStrollRoomPx;
                var canGoRight = roomRight > MinStrollRoomPx;

                // Pick a direction with room to move, preferring left when both are open.
                var goLeft = canGoLeft || !canGoRight;
                if ((goLeft ? canGoLeft : canGoRight) && _controller.TryStartStroll(goLeft))
                {
                    _nextWalk = now.AddSeconds(Random.Shared.Next(2, 6));
                }
                else
                {
                    _nextWalk = now.AddSeconds(2);
                }
            }
        }

        Pet.State = _controller.State;
        Pet.FacingRight = false;
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
        return ToMonitorArea(screen.DeviceName, area.Left, area.Top, area.Width, area.Height);
    }

    private MonitorArea ToMonitorArea(string deviceName, double left, double top, double width, double height)
    {
        var transform = CurrentDipTransform();
        var dip = transform.ToDipRect(left, top, width, height);
        return new MonitorArea(deviceName, dip.Left, dip.Top, dip.Width, dip.Height);
    }

    /// <summary>
    /// The device-pixel to DIP transform for the monitor the window currently sits on.
    /// </summary>
    /// <remarks>
    /// Known limitation, deliberately left as-is: this single transform is applied to *every*
    /// monitor's rectangle. Strictly, each monitor in a mixed-DPI setup has its own scale factor,
    /// which is why <see cref="DipTransform"/> exists and is unit-tested independently. Fixing the
    /// window integration properly needs real mixed-DPI hardware to validate, because after the
    /// window is moved onto a different-DPI monitor WPF re-interprets <c>Left</c>/<c>Top</c> in that
    /// monitor's DIP space. Treat multi-monitor placement at differing scale factors as unverified.
    /// </remarks>
    private DipTransform CurrentDipTransform()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return DipTransform.Identity;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        return DipTransform.FromDeviceToDip(fromDevice.M11, fromDevice.M22);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(SnapToCurrentMonitor);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        var monitor = CurrentMonitor();
        _preferences.Save(new PetPreferences(monitor.Id, Left, _controller.IsPaused, _controller.IsManualSleeping)
        {
            LeftWalkSpeed = _leftWalkSpeed,
            RightWalkSpeed = _rightWalkSpeed
        });
        _tray?.Dispose();
    }
}
