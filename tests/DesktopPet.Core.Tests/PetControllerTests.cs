using Xunit;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class PetControllerTests
{
    [Fact]
    public void Click_plays_the_sigh_and_returns_to_idle()
    {
        var controller = new PetController();

        controller.Click();

        Assert.Equal(PetState.Sighing, controller.State);
        // The sigh is 122 frames at 30 fps, so it must still be running well past the old 800 ms.
        controller.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(PetState.Sighing, controller.State);
        controller.Advance(TimeSpan.FromSeconds(1.2));
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Sigh_does_not_start_a_walk_and_blocks_one()
    {
        var controller = new PetController();
        controller.Click();

        controller.SetWalking(right: true);

        Assert.Equal(PetState.Sighing, controller.State);
    }

    [Fact]
    public void Walking_resumes_once_the_sigh_finishes()
    {
        var controller = new PetController();
        controller.Click();
        controller.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(PetState.Idle, controller.State);

        controller.SetWalking(right: true);

        Assert.Equal(PetState.WalkingRight, controller.State);
    }

    [Fact]
    public void Five_minutes_without_interaction_enters_auto_sleep()
    {
        var controller = new PetController();

        controller.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(PetState.Sleeping, controller.State);
        Assert.True(controller.IsAutoSleeping);
    }

    [Fact]
    public void Manual_sleep_stays_asleep_until_toggled()
    {
        var controller = new PetController();

        controller.ToggleManualSleep();
        controller.Advance(TimeSpan.FromHours(1));
        Assert.Equal(PetState.Sleeping, controller.State);
        Assert.False(controller.IsAutoSleeping);

        controller.ToggleManualSleep();
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Paused_controller_can_auto_sleep_and_accepts_click()
    {
        var controller = new PetController { IsPaused = true };

        controller.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(PetState.Sleeping, controller.State);

        controller.Click();
        Assert.Equal(PetState.Idle, controller.State);
        controller.Click();
        Assert.Equal(PetState.Sighing, controller.State);
    }

    [Fact]
    public void Drag_keeps_pet_awake_during_long_pointer_hold()
    {
        var controller = new PetController();
        controller.BeginDrag();
        controller.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(PetState.Dragging, controller.State);
        controller.EndDrag();
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Restored_manual_sleep_and_pause_preserve_saved_behavior()
    {
        var controller = new PetController();
        controller.Restore(true, true);

        Assert.True(controller.IsPaused);
        Assert.True(controller.IsManualSleeping);
        Assert.Equal(PetState.Sleeping, controller.State);

        controller.ToggleManualSleep();
        Assert.Equal(PetState.Idle, controller.State);
        Assert.True(controller.IsPaused);
    }

    [Fact]
    public void Sleep_command_wakes_an_auto_sleeping_pet()
    {
        var controller = new PetController();
        controller.Advance(TimeSpan.FromMinutes(5));

        controller.ToggleManualSleep();

        Assert.Equal(PetState.Idle, controller.State);
        Assert.False(controller.IsManualSleeping);
        Assert.False(controller.IsAutoSleeping);
    }

    [Fact]
    public void Movement_is_clamped_to_working_area()
    {
        var area = new PetRect(100, 200, 900, 800);

        Assert.Equal(100, PetGeometry.ClampX(0, area, 180));
        Assert.Equal(820, PetGeometry.ClampX(999, area, 180));
        Assert.Equal(790, PetGeometry.BottomAlignedTop(area, 210));
    }
}
