using System.Windows.Forms;

namespace DesktopPet.Windows.Tests;

public sealed class ChatEntryPointTests
{
    [Fact]
    public void TrayMenuRaisesSeparateEventsForEachChatDestination()
    {
        using var tray = new TrayController();
        var bubble = 0;
        var full = 0;
        tray.BubbleChatRequested += (_, _) => bubble++;
        tray.FullChatRequested += (_, _) => full++;

        var bubbleItem = tray.ContextMenuStrip.Items
            .OfType<ToolStripMenuItem>()
            .Single(item => item.Text == "轻量气泡聊天");
        var fullItem = tray.ContextMenuStrip.Items
            .OfType<ToolStripMenuItem>()
            .Single(item => item.Text == "完整聊天窗口");

        bubbleItem.PerformClick();
        fullItem.PerformClick();

        Assert.Equal(1, bubble);
        Assert.Equal(1, full);
    }
}
