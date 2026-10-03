using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DesktopPet;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    public event EventHandler? TogglePauseRequested;
    public event EventHandler? ToggleSleepRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ChatRequested;

    public TrayController()
    {
        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Desktop Pet",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _icon.ContextMenuStrip.Items.Add("暂停/继续走动", null, (_, _) => TogglePauseRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip.Items.Add("睡觉/唤醒", null, (_, _) => ToggleSleepRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip.Items.Add("和她聊天…", null, (_, _) => ChatRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _icon.ContextMenuStrip.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    public void Update(bool paused, bool sleeping)
    {
        _icon.ContextMenuStrip!.Items[0].Text = paused ? "继续走动" : "暂停走动";
        _icon.ContextMenuStrip.Items[1].Text = sleeping ? "唤醒" : "睡觉";
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(232, 239, 230));
        using var hair = new SolidBrush(Color.FromArgb(34, 28, 34));
        graphics.FillEllipse(hair, 3, 2, 26, 27);
        graphics.FillEllipse(new SolidBrush(Color.FromArgb(250, 222, 205)), 8, 7, 16, 16);
        graphics.FillEllipse(brush, 7, 20, 18, 10);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
