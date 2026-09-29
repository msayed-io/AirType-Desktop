using LiveTypeBridge.Core.Input;
using LiveTypeBridge.Core.Settings;
using Xunit;

namespace LiveTypeBridge.Tests;

public class InputPlanTests
{
    [Fact]
    public void Backspaces_Produce_VK_BACK_Events()
    {
        var plan = InputPlanBuilder.Build(3, "");
        Assert.Equal(3, plan.Length);
        Assert.All(plan, p =>
        {
            Assert.Equal(1u, p.type);
            Assert.Equal(NativeInput.VK_BACK, p.U.ki.wVk);
        });
    }

    [Fact]
    public void Latin_Text_Uses_Unicode_Events()
    {
        var plan = InputPlanBuilder.Build(0, "Hi");
        Assert.Equal(2, plan.Length);
        Assert.All(plan, p =>
        {
            Assert.Equal(NativeInput.KEYEVENTF_UNICODE, p.U.ki.dwFlags);
            Assert.Equal(0, p.U.ki.wVk);
        });
        Assert.Equal('H', plan[0].U.ki.wScan);
        Assert.Equal('i', plan[1].U.ki.wScan);
    }

    [Fact]
    public void Arabic_Text_Maps_Character_By_Character()
    {
        const string word = "مرحبا";
        var plan = InputPlanBuilder.Build(0, word);
        Assert.Equal(word.Length, plan.Length);
        for (var i = 0; i < word.Length; i++)
            Assert.Equal(word[i], plan[i].U.ki.wScan);
    }

    [Fact]
    public void Emoji_Surrogate_Pair_Becomes_Two_Events()
    {
        var plan = InputPlanBuilder.Build(0, "🌍");
        Assert.Equal(2, plan.Length);
        Assert.True(char.IsSurrogatePair((char)plan[0].U.ki.wScan, (char)plan[1].U.ki.wScan));
    }

    [Fact]
    public void Newline_Becomes_VK_RETURN_Tab_Becomes_VK_TAB()
    {
        var plan = InputPlanBuilder.Build(0, "\n\t");
        Assert.Equal(2, plan.Length);
        Assert.Equal(NativeInput.VK_RETURN, plan[0].U.ki.wVk);
        Assert.Equal(NativeInput.VK_TAB, plan[1].U.ki.wVk);
    }

    [Fact]
    public void Mixed_Op_Is_Backspace_Then_Insert_Order()
    {
        var plan = InputPlanBuilder.Build(2, "ab");
        Assert.Equal(4, plan.Length);
        Assert.Equal(NativeInput.VK_BACK, plan[0].U.ki.wVk);
        Assert.Equal(NativeInput.VK_BACK, plan[1].U.ki.wVk);
        Assert.Equal('a', plan[2].U.ki.wScan);
        Assert.Equal('b', plan[3].U.ki.wScan);
    }

    [Fact]
    public void Control_Characters_Are_Stripped_From_Plan()
    {
        var plan = InputPlanBuilder.Build(0, "a\u0007b"); // BEL
        Assert.Equal(2, plan.Length);
    }

    [Fact]
    public void Empty_Op_Is_Empty_Plan()
    {
        Assert.Empty(InputPlanBuilder.Build(0, ""));
        Assert.Empty(InputPlanBuilder.Build(0, null));
    }

    [Fact]
    public void Marker_Is_On_Every_Injected_Event()
    {
        var plan = InputPlanBuilder.Build(1, "x");
        Assert.All(plan, p => Assert.Equal(NativeInput.StreamMarker, p.U.ki.dwExtraInfo));
    }
}

public class HotkeyTests
{
    [Fact]
    public void Parses_Standard_Gesture()
    {
        var g = HotkeyGesture.Parse("Ctrl+Alt+Shift+T");
        Assert.NotNull(g);
        var expected = HotkeyGesture.MOD_CONTROL | HotkeyGesture.MOD_ALT | HotkeyGesture.MOD_SHIFT;
        Assert.Equal(expected, g.ModifierFlags);
        Assert.Equal(0x54, g.VirtualKey); // 'T'
        Assert.Equal("Ctrl+Alt+Shift+T", g.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("T")]              // no modifier
    [InlineData("Ctrl")]           // no key
    [InlineData("Ctrl+Alt+X+Y")]   // two keys
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Win+T")]          // Win unsupported on purpose
    public void Rejects_Invalid_Gestures(string? text)
    {
        Assert.Null(HotkeyGesture.Parse(text));
    }

    [Fact]
    public void Conflict_Detection()
    {
        var a = HotkeyGesture.Parse("Ctrl+Alt+T")!;
        var b = HotkeyGesture.Parse("CTRL+ALT+T")!;
        var c = HotkeyGesture.Parse("Ctrl+Alt+Y")!;
        var d = HotkeyGesture.Parse("Alt+Ctrl+T")!;
        Assert.True(a.ConflictsWith(b));
        Assert.True(a.ConflictsWith(d)); // order independent
        Assert.False(a.ConflictsWith(c));
    }

    [Fact]
    public void Digits_Map_To_Virtual_Codes()
    {
        var g = HotkeyGesture.Parse("Ctrl+Alt+5")!;
        Assert.Equal(0x35, g.VirtualKey);
        Assert.Equal("Ctrl+Alt+5", g.ToString());
    }

    [Fact]
    public void Defaults_Do_Not_Clash()
    {
        Assert.False(HotkeyGesture.DefaultToggleStream.ConflictsWith(HotkeyGesture.DefaultShowQr));
        Assert.False(HotkeyGesture.DefaultToggleStream.ConflictsWith(HotkeyGesture.DefaultEmergencyStop));
        Assert.False(HotkeyGesture.DefaultShowQr.ConflictsWith(HotkeyGesture.DefaultEmergencyStop));
    }
}

public class SettingsTests
{
    [Fact]
    public void Duplicate_Hotkeys_Are_Flagged()
    {
        var s = new AppSettings
        {
            HotkeyToggleStream = "Ctrl+Alt+T",
            HotkeyShowQr = "Ctrl+Alt+T",
            HotkeyEmergencyStop = "Ctrl+Alt+X",
        };
        var errors = AppSettings.ValidateHotkeys(s);
        Assert.Single(errors);
    }

    [Fact]
    public void Distinct_Hotkeys_Pass_Validation()
    {
        var s = new AppSettings(); // defaults
        Assert.Empty(AppSettings.ValidateHotkeys(s));
    }
}
