using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

public sealed class PreferencesStoreTests
{
    [Fact]
    public void Preferences_round_trip_as_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        try
        {
            var store = new PreferencesStore(path);
            var original = new PetPreferences("DISPLAY2", 640, true, false)
            {
                LeftWalkSpeed = 1.5,
                RightWalkSpeed = 3.0
            };
            store.Save(original);
            Assert.Equal(original, store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Corrupt_preferences_use_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{broken");
        try { Assert.Equal(PetPreferences.Default, new PreferencesStore(path).Load()); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Utf8_bom_preferences_are_still_read()
    {
        // Notepad and `Set-Content -Encoding utf8` both write a BOM; it must not wipe settings.
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        var json = """{"monitorId":"DISPLAY2","x":640,"isPaused":true,"isManualSleeping":false}""";
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            Assert.Equal(new PetPreferences("DISPLAY2", 640, true, false), new PreferencesStore(path).Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Saved_preferences_are_written_without_a_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        try
        {
            new PreferencesStore(path).Save(new PetPreferences("PRIMARY", 100, false, false));
            var bytes = File.ReadAllBytes(path);

            Assert.Equal((byte)'{', bytes[0]);
            Assert.Equal(new PetPreferences("PRIMARY", 100, false, false), new PreferencesStore(path).Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Older_preferences_without_speeds_use_the_default_speed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"monitorId\":\"PRIMARY\",\"x\":100,\"isPaused\":false,\"isManualSleeping\":false}");
        try
        {
            var loaded = new PreferencesStore(path).Load();

            Assert.Equal(WalkSpeedOptions.Default, loaded.LeftWalkSpeed);
            Assert.Equal(WalkSpeedOptions.Default, loaded.RightWalkSpeed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Invalid_saved_speeds_use_the_default_speed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"monitorId\":\"PRIMARY\",\"x\":100,\"isPaused\":false,\"isManualSleeping\":false,\"leftWalkSpeed\":9,\"rightWalkSpeed\":0}");
        try
        {
            var loaded = new PreferencesStore(path).Load();

            Assert.Equal(WalkSpeedOptions.Default, loaded.LeftWalkSpeed);
            Assert.Equal(WalkSpeedOptions.Default, loaded.RightWalkSpeed);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Missing_monitor_falls_back_to_primary_area()
    {
        var saved = new PetPreferences("MISSING", 900, false, false);
        var area = ScreenPlacement.Resolve(saved, new[] { new MonitorArea("PRIMARY", 0, 0, 1920, 1040) }, "PRIMARY", 180, 210);

        Assert.Equal("PRIMARY", area.MonitorId);
        Assert.Equal(1740, area.Left);
        Assert.Equal(830, area.Top);
    }

    [Fact]
    public void Saved_position_on_left_monitor_is_preserved()
    {
        var saved = new PetPreferences("LEFT", -1200, false, false);
        var monitors = new[] { new MonitorArea("LEFT", -1600, 0, 1600, 860), new MonitorArea("PRIMARY", 0, 0, 1920, 1040) };
        var placement = ScreenPlacement.Resolve(saved, monitors, "PRIMARY", 180, 210);

        Assert.Equal("LEFT", placement.MonitorId);
        Assert.Equal(-1200, placement.Left);
        Assert.Equal(650, placement.Top);
    }
}
