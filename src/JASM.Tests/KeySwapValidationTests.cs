using GIMI_ModManager.Core.Helpers;
using Xunit;

namespace JASM.Tests;

public class KeySwapValidationTests
{
    [Theory]
    [InlineData("Key enable_thicc", "enable_thicc")] // Issue #75 case: Key followed by space
    [InlineData("key enable_thicc", "enable_thicc")]
    [InlineData("Key_enable_thicc", "enable_thicc")] // Key followed by underscore
    [InlineData("key-enable_thicc", "enable_thicc")] // Key followed by dash
    [InlineData("KeySwapCharacter", "Character")]
    [InlineData("keyswap_outfit", "outfit")]
    [InlineData("KeyCommandListNext", "Next")]
    [InlineData("keycommandlist_next", "next")]
    [InlineData("KeyCommndListNext", "Next")]
    [InlineData("Click", "Click")] // Normal section without prefix
    [InlineData("Key", "Key")] // Exactly ""Key"", do not strip into empty string
    [InlineData("KeySwap", "KeySwap")] // Exactly ""KeySwap"", do not strip into empty string
    [InlineData("KeyCommandList", "KeyCommandList")] // Exactly ""KeyCommandList"", do not strip
    [InlineData("KeyCommndList", "KeyCommndList")] // Exactly ""KeyCommndList"", do not strip
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void RemoveKeyPrefixes_ShouldCorrectlyStripPrefixAndLeadingSeparators(string input, string expected)
    {
        var actual = IniConfigHelpers.RemoveKeyPrefixes(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RemoveKeyPrefixes_WhenStrippingLeavesOnlyWhitespace_ShouldFallbackToTrimmedInput()
    {
        var actual = IniConfigHelpers.RemoveKeyPrefixes("Key   ");
        Assert.Equal("Key", actual);
    }

    [Fact]
    public void ParseKeySwap_WithKeyFollowedBySpace_ShouldParseSuccessfully()
    {
        var lines = new List<string>
        {
            "key = 0",
            "type = cycle"
        };
        var result = IniConfigHelpers.ParseKeySwap(lines, "[Key enable_thicc]");
        Assert.NotNull(result);
        Assert.Equal("[Key enable_thicc]", result.SectionKey);
        Assert.Contains("0", result.ForwardKeys);
        Assert.Equal("cycle", result.Type);

        var stripped = IniConfigHelpers.RemoveKeyPrefixes(result.SectionKey.TrimStart('[').TrimEnd(']'));
        Assert.Equal("enable_thicc", stripped);
    }

    [Theory]
    [InlineData("[KeyCommandListNext]", "Next")]
    [InlineData("[KeyCommndListNext]", "Next")]
    [InlineData("[KeySwapOutfit]", "Outfit")]
    [InlineData("[Key_Body]", "Body")]
    [InlineData("[Key-Hair]", "Hair")]
    public void ParseKeySwap_WithVariousSectionKeyFormats_ShouldParseAndStripPrefix(string sectionHeader, string expectedStripped)
    {
        var lines = new List<string>
        {
            "key = [VK_OEM_3]",
            "back = [VK_SHIFT, VK_OEM_3]",
            "type = cycle"
        };
        var result = IniConfigHelpers.ParseKeySwap(lines, sectionHeader);
        Assert.NotNull(result);
        Assert.Equal(sectionHeader, result.SectionKey);
        Assert.Contains("[VK_OEM_3]", result.ForwardKeys);
        Assert.Contains("[VK_SHIFT, VK_OEM_3]", result.BackwardKeys);
        Assert.Equal("cycle", result.Type);

        var stripped = IniConfigHelpers.RemoveKeyPrefixes(result.SectionKey.TrimStart('[').TrimEnd(']'));
        Assert.Equal(expectedStripped, stripped);
    }

    [Theory]
    [InlineData("Key enable_thicc", "enable_thicc", "Key ")]
    [InlineData("Key_enable_thicc", "enable_thicc", "Key_")]
    [InlineData("Key-enable_thicc", "enable_thicc", "Key-")]
    [InlineData("KeyCommandListNext", "Next", "KeyCommandList")]
    [InlineData("Key1", "1", "Key")]
    public void SectionPrefixExtraction_ShouldPreserveOriginalSeparators(string sectionName, string currentDisplay, string expectedPrefix)
    {
        var prefix = IniConfigHelpers.GetSectionPrefix(sectionName, currentDisplay);
        Assert.Equal(expectedPrefix, prefix);
        var reconstructed = $"[{prefix}{currentDisplay}]";
        Assert.Equal($"[{sectionName}]", reconstructed);
    }

    [Fact]
    public void SplitMultiKeys_WithCommaSeparatedKeys_ShouldSplitCorrectly()
    {
        var input = "VK_OEM_3, VK_1, VK_2";
        var result = KeyMappingHelpers.SplitMultiKeys(input);
        Assert.Equal(["VK_OEM_3", "VK_1", "VK_2"], result);
    }

    [Fact]
    public void SplitMultiKeys_WithCombinationKeysAndGamepad_ShouldPreserveInternalSpaces()
    {
        var input = "VK_SHIFT VK_OEM_3, XB_A, num 1";
        var result = KeyMappingHelpers.SplitMultiKeys(input);
        Assert.Equal(["VK_SHIFT VK_OEM_3", "XB_A", "num 1"], result);
    }

    [Fact]
    public void SplitMultiKeys_WithChineseCommas_ShouldSplitCorrectly()
    {
        var input = "VK_A，VK_B，XB_RIGHT_SHOULDER";
        var result = KeyMappingHelpers.SplitMultiKeys(input);
        Assert.Equal(["VK_A", "VK_B", "XB_RIGHT_SHOULDER"], result);
    }

    [Fact]
    public void SplitMultiKeys_WithEmptyOrWhitespace_ShouldReturnEmptyList()
    {
        Assert.Empty(KeyMappingHelpers.SplitMultiKeys(null));
        Assert.Empty(KeyMappingHelpers.SplitMultiKeys(""));
        Assert.Empty(KeyMappingHelpers.SplitMultiKeys("   "));
    }

    [Fact]
    public void NormalizeKeys_WithMixedLinesAndCommas_ShouldNormalizeAll()
    {
        var rawList = new List<string>
        {
            "VK_OEM_3, VK_1",
            "XB_A",
            "VK_SHIFT VK_2, num 5"
        };
        var result = KeyMappingHelpers.NormalizeKeys(rawList);
        Assert.Equal(["VK_OEM_3", "VK_1", "XB_A", "VK_SHIFT VK_2", "num 5"], result);
    }

    [Fact]
    public void FormatKeyList_ShouldJoinKeysWithCommaSpace()
    {
        var keys = new[] { "VK_OEM_3", "XB_A", "VK_1" };
        var result = KeyMappingHelpers.FormatKeyList(keys);
        Assert.Equal("VK_OEM_3, XB_A, VK_1", result);
    }

    [Fact]
    public void FormatKeyList_WithEmptyOrNull_ShouldReturnEmptyString()
    {
        Assert.Equal(string.Empty, KeyMappingHelpers.FormatKeyList(null));
        Assert.Equal(string.Empty, KeyMappingHelpers.FormatKeyList([]));
    }

    [Fact]
    public void AreKeyListsEqual_ShouldCompareEquivalencyCaseInsensitive()
    {
        var list1 = new[] { "vk_oem_3", "XB_A", "  VK_1  " };
        var list2 = new[] { "VK_OEM_3", "xb_a", "VK_1" };
        Assert.True(KeyMappingHelpers.AreKeyListsEqual(list1, list2));

        var list3 = new[] { "VK_OEM_3", "XB_A" };
        Assert.False(KeyMappingHelpers.AreKeyListsEqual(list1, list3));
    }

    [Fact]
    public void ConvertToFriendlyText_ShouldLocalizeBothLanguages()
    {
        // Chinese localization
        Assert.Equal("`", KeyMappingHelpers.ConvertToFriendlyText("VK_OEM_3", isChinese: true));
        Assert.Equal("手柄 A", KeyMappingHelpers.ConvertToFriendlyText("XB_A", isChinese: true));
        Assert.Equal("Shift + `", KeyMappingHelpers.ConvertToFriendlyText("VK_SHIFT VK_OEM_3", isChinese: true));
        Assert.Equal("小键盘 1", KeyMappingHelpers.ConvertToFriendlyText("num 1", isChinese: true));
        Assert.Equal("鼠标左键", KeyMappingHelpers.ConvertToFriendlyText("VK_LBUTTON", isChinese: true));

        // English localization
        Assert.Equal("`", KeyMappingHelpers.ConvertToFriendlyText("VK_OEM_3", isChinese: false));
        Assert.Equal("Gamepad A", KeyMappingHelpers.ConvertToFriendlyText("XB_A", isChinese: false));
        Assert.Equal("Shift + `", KeyMappingHelpers.ConvertToFriendlyText("VK_SHIFT VK_OEM_3", isChinese: false));
        Assert.Equal("Numpad 1", KeyMappingHelpers.ConvertToFriendlyText("num 1", isChinese: false));
        Assert.Equal("Left Mouse", KeyMappingHelpers.ConvertToFriendlyText("VK_LBUTTON", isChinese: false));
    }

    [Fact]
    public void ParseKeySwap_WithMultiLineKeys_ShouldParseAllForwardAndBackwardKeys()
    {
        var lines = new List<string>
        {
            "key = VK_OEM_3",
            "key = XB_A",
            "back = VK_SHIFT VK_OEM_3",
            "back = XB_B",
            "type = cycle"
        };

        var result = IniConfigHelpers.ParseKeySwap(lines, "[KeySwapOutfit]");
        Assert.NotNull(result);
        Assert.Equal(2, result.ForwardKeys.Count);
        Assert.Contains("VK_OEM_3", result.ForwardKeys);
        Assert.Contains("XB_A", result.ForwardKeys);

        Assert.Equal(2, result.BackwardKeys.Count);
        Assert.Contains("VK_SHIFT VK_OEM_3", result.BackwardKeys);
        Assert.Contains("XB_B", result.BackwardKeys);
    }

    [Theory]
    [InlineData(192, "VK_OEM_3")] // `
    [InlineData((int)'A', "VK_A")]
    [InlineData((int)'0', "VK_0")]
    [InlineData(112, "VK_F1")]
    [InlineData(32, "VK_SPACE")]
    [InlineData(13, "VK_RETURN")]
    [InlineData(27, "VK_ESCAPE")]
    [InlineData(38, "VK_UP")]
    [InlineData(0x60, "VK_NUMPAD0")]
    public void ConvertVirtualKeyToGimiKey_ShouldReturnCorrectGimiKey(int vk, string expected)
    {
        var actual = KeyMappingHelpers.ConvertVirtualKeyToGimiKey(vk);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildComboKey_ShouldAssembleCorrectModifiersAndPrimaryKey()
    {
        Assert.Equal("VK_CONTROL VK_SHIFT VK_OEM_3", KeyMappingHelpers.BuildComboKey(true, true, false, "VK_OEM_3"));
        Assert.Equal("VK_MENU VK_A", KeyMappingHelpers.BuildComboKey(false, false, true, "VK_A"));
        Assert.Equal("XB_A", KeyMappingHelpers.BuildComboKey(false, false, false, "XB_A"));
        Assert.Equal("VK_CONTROL", KeyMappingHelpers.BuildComboKey(true, false, false, null));
    }

    [Fact]
    public void DeconstructComboKey_ShouldParseModifiersAndPrimaryKeyCorrectly()
    {
        var (ctrl, shift, alt, primary) = KeyMappingHelpers.DeconstructComboKey("VK_CONTROL VK_SHIFT VK_OEM_3");
        Assert.True(ctrl);
        Assert.True(shift);
        Assert.False(alt);
        Assert.Equal("VK_OEM_3", primary);

        var (c2, s2, a2, p2) = KeyMappingHelpers.DeconstructComboKey("XB_A");
        Assert.False(c2);
        Assert.False(s2);
        Assert.False(a2);
        Assert.Equal("XB_A", p2);
    }

    [Fact]
    public void AreKeyListsEqual_ShouldCorrectlyCompareSequences()
    {
        Assert.True(KeyMappingHelpers.AreKeyListsEqual(new[] { "VK_A", "VK_B" }, new[] { "VK_A", "VK_B" }));
        Assert.True(KeyMappingHelpers.AreKeyListsEqual(new[] { "vk_a ", "VK_B" }, new[] { "VK_A", "vk_b" }));
        Assert.False(KeyMappingHelpers.AreKeyListsEqual(new[] { "VK_A" }, new[] { "VK_A", "VK_B" }));
        Assert.False(KeyMappingHelpers.AreKeyListsEqual(new[] { "VK_A", "VK_B" }, new[] { "VK_B", "VK_A" }));
        Assert.True(KeyMappingHelpers.AreKeyListsEqual((IEnumerable<string>?)null, (IEnumerable<string>?)null));
    }

    [Fact]
    public void SplitAndFormatKeyList_RoundTrip_ShouldBeConsistent()
    {
        var raw = "VK_OEM_3, XB_A, no_vk_shift VK_UP";
        var split = KeyMappingHelpers.SplitMultiKeys(raw);
        Assert.Equal(3, split.Count);
        Assert.Equal("VK_OEM_3", split[0]);
        Assert.Equal("XB_A", split[1]);
        Assert.Equal("no_vk_shift VK_UP", split[2]);

        var formatted = KeyMappingHelpers.FormatKeyList(split);
        Assert.Equal("VK_OEM_3, XB_A, no_vk_shift VK_UP", formatted);
    }

    [Theory]
    [InlineData("cycle", 15, true)]
    [InlineData("CYCLE", 2, true)]
    [InlineData("cycle", 1, false)]
    [InlineData("cycle", 0, false)]
    [InlineData("toggle", 2, false)]
    [InlineData("toggle", 0, false)]
    [InlineData(null, 5, false)]
    [InlineData("", 2, false)]
    public void IsBackKeySupported_ShouldMatchDomainMatrix(string? type, int variationsCount, bool expected)
    {
        var actual = IniConfigHelpers.IsBackKeySupported(type, variationsCount);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("cycle", 15, true, "15 项")]
    [InlineData("cycle", 15, false, "15 items")]
    [InlineData("cycle", 2, true, "2 项")]
    [InlineData("cycle", 1, true, "")]
    [InlineData("cycle", 0, true, "")]
    [InlineData("toggle", 2, true, "开关")]
    [InlineData("toggle", 2, false, "Toggle")]
    [InlineData(null, 0, true, "")]
    [InlineData("custom_mode", 0, true, "custom_mode")]
    public void GetModeDisplayText_ShouldFormatProperly(string? type, int count, bool isZh, string expected)
    {
        var actual = IniConfigHelpers.GetModeDisplayText(type, count, isZh);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, true, "切换")]
    [InlineData(true, false, "Switch")]
    [InlineData(false, true, "按键")]
    [InlineData(false, false, "Key")]
    public void GetActionLabel_ShouldReturnCorrectLabel(bool isBackSupported, bool isZh, string expected)
    {
        var actual = IniConfigHelpers.GetActionLabel(isBackSupported, isZh);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, "反向")]
    [InlineData(false, "Back")]
    public void GetBackActionLabel_ShouldReturnCorrectLabel(bool isZh, string expected)
    {
        var actual = IniConfigHelpers.GetBackActionLabel(isZh);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConvertToFriendlyText_WithComboNumpad_ShouldNotSplitIntoNumAndDigit()
    {
        // Regression: "VK_CONTROL VK_MENU num 8" should not be split into "Ctrl + Alt + num + 8".
        var zh = KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL VK_MENU num 8", isChinese: true);
        Assert.Equal("Ctrl + Alt + 小键盘 8", zh);

        var en = KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL VK_MENU num 8", isChinese: false);
        Assert.Equal("Ctrl + Alt + Numpad 8", en);

        var zhSingle = KeyMappingHelpers.ConvertToFriendlyText("num 8", isChinese: true);
        Assert.Equal("小键盘 8", zhSingle);

        var zhShiftNum = KeyMappingHelpers.ConvertToFriendlyText("VK_SHIFT num 0", isChinese: true);
        Assert.Equal("Shift + 小键盘 0", zhShiftNum);
    }

    [Fact]
    public void DeconstructComboKey_WithComboNumpad_ShouldNormalizeToVkNumpad()
    {
        var (ctrl, shift, alt, primary) = KeyMappingHelpers.DeconstructComboKey("VK_CONTROL VK_MENU num 8");
        Assert.True(ctrl);
        Assert.False(shift);
        Assert.True(alt);
        Assert.Equal("VK_NUMPAD8", primary);

        // Preserve standalone keys without modifiers
        var (sCtrl, sShift, sAlt, sPrimary) = KeyMappingHelpers.DeconstructComboKey("num 8");
        Assert.False(sCtrl);
        Assert.False(sShift);
        Assert.False(sAlt);
        Assert.Equal("num 8", sPrimary);

        // Normalize numpad tokens to standard VK_NUMPAD8 format
        Assert.Equal("VK_CONTROL VK_NUMPAD8", KeyMappingHelpers.BuildComboKey(true, false, false, "num 8"));
        Assert.Equal("VK_CONTROL VK_NUMPAD8", KeyMappingHelpers.BuildComboKey(true, false, false, "numpad8"));
        Assert.Equal("VK_NUMPAD8", KeyMappingHelpers.BuildComboKey(false, false, false, "num 8"));
    }

    [Theory]
    [InlineData("hold", 0, true, "长按")]
    [InlineData("hold", 0, false, "Hold")]
    [InlineData("press", 0, true, "按压")]
    [InlineData("press", 0, false, "Press")]
    public void GetModeDisplayText_WithHoldAndPress_ShouldLocalizeProperly(string type, int count, bool isZh, string expected)
    {
        var actual = IniConfigHelpers.GetModeDisplayText(type, count, isZh);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConvertToFriendlyText_WithNoModifiersComma_ShouldReturnComma()
    {
        // Regression: "no_modifiers ," must display as a comma rather than an empty string
        var zh = KeyMappingHelpers.ConvertToFriendlyText("no_modifiers ,", isChinese: true);
        Assert.Equal("逗号", zh);

        var en = KeyMappingHelpers.ConvertToFriendlyText("no_modifiers ,", isChinese: false);
        Assert.Equal(",", en);

        var zhOem = KeyMappingHelpers.ConvertToFriendlyText("no_modifiers VK_OEM_COMMA", isChinese: true);
        Assert.Equal("逗号", zhOem);

        var zhCombo = KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL ,", isChinese: true);
        Assert.Equal("Ctrl + 逗号", zhCombo);
    }

    [Fact]
    public void DeconstructComboKey_WithNoModifiersComma_ShouldExtractCommaAsPrimary()
    {
        var (ctrl, shift, alt, primary) = KeyMappingHelpers.DeconstructComboKey("no_modifiers ,");
        Assert.False(ctrl);
        Assert.False(shift);
        Assert.False(alt);
        Assert.Equal(",", primary);
    }

    [Fact]
    public void ConvertToFriendlyText_WithGamepadCombo_ShouldFormatProperly()
    {
        var actual = KeyMappingHelpers.ConvertToFriendlyText("XB_LEFT_TRIGGER XB_DPAD_RIGHT", isChinese: true);
        Assert.Equal("手柄 LT + →", actual);
    }

    [Fact]
    public void ConvertToFriendlyText_WithVkNumpad_ShouldLocalizeBothLanguages()
    {
        var zh = KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL VK_MENU VK_NUMPAD8", isChinese: true);
        Assert.Equal("Ctrl + Alt + 小键盘 8", zh);

        var en = KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL VK_MENU VK_NUMPAD8", isChinese: false);
        Assert.Equal("Ctrl + Alt + Numpad 8", en);

        var zhNumpad = KeyMappingHelpers.ConvertToFriendlyText("numpad8", isChinese: true);
        Assert.Equal("小键盘 8", zhNumpad);

        var enNumpad = KeyMappingHelpers.ConvertToFriendlyText("numpad8", isChinese: false);
        Assert.Equal("Numpad 8", enNumpad);
    }

    [Theory]
    [InlineData(true, true, "切换")]
    [InlineData(true, false, "Switch")]
    [InlineData(false, true, "按键")]
    [InlineData(false, false, "Key")]
    public void GetActionLabel_ShouldLocalizeCorrectly(bool isBackKeySupported, bool isChinese, string expected)
    {
        var actual = IniConfigHelpers.GetActionLabel(isBackKeySupported, isChinese);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("cycle", 4, true, "循环切换模式 (共 4 项)")]
    [InlineData("cycle", 4, false, "Cycle mode (4 items)")]
    [InlineData("cycle", 1, true, "")]
    [InlineData("cycle", 1, false, "")]
    [InlineData("toggle", 1, true, "开关模式 (切换开启/关闭)")]
    [InlineData("toggle", 1, false, "Toggle mode (turn on/off)")]
    [InlineData("hold", 1, true, "长按模式 (按住生效)")]
    [InlineData("hold", 1, false, "Hold mode (active while held)")]
    [InlineData("press", 1, true, "按压模式 (按下触发)")]
    [InlineData("press", 1, false, "Press mode (trigger on press)")]
    [InlineData("", 1, true, "")]
    [InlineData(null, 1, false, "")]
    public void GetModeToolTipText_ShouldLocalizeAndExplainBehaviorCorrectly(
        string? type, int variationsCount, bool isChinese, string expected)
    {
        var actual = IniConfigHelpers.GetModeToolTipText(type, variationsCount, isChinese);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData((int)'8', "8")]
    [InlineData((int)'0', "0")]
    [InlineData(0x68, "NUMPAD8")]
    [InlineData(0x60, "NUMPAD0")]
    [InlineData(107, "ADD")]
    [InlineData(106, "MULTIPLY")]
    [InlineData(17, "VK_CONTROL")]
    [InlineData(16, "VK_SHIFT")]
    [InlineData(18, "VK_MENU")]
    [InlineData(32, "VK_SPACE")]
    [InlineData(0x41, "VK_A")]
    [InlineData(112, "VK_F1")]
    public void ConvertVirtualKeyToGimiInputKey_ShouldReturn3DMigotoCompatibleRawKey(int vk, string expected)
    {
        var actual = KeyMappingHelpers.ConvertVirtualKeyToGimiInputKey(vk);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConvertToFriendlyText_ShouldHandleDirectNumbersAndNumpadsCorrectly()
    {
        Assert.Equal("8", KeyMappingHelpers.ConvertToFriendlyText("8", isChinese: true));
        Assert.Equal("8", KeyMappingHelpers.ConvertToFriendlyText("8", isChinese: false));
        Assert.Equal("小键盘 8", KeyMappingHelpers.ConvertToFriendlyText("NUMPAD8", isChinese: true));
        Assert.Equal("Numpad 8", KeyMappingHelpers.ConvertToFriendlyText("NUMPAD8", isChinese: false));
        Assert.Equal("Ctrl + 8", KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL 8", isChinese: true));
        Assert.Equal("Ctrl + 8", KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL 8", isChinese: false));
        Assert.Equal("Ctrl + 小键盘 8", KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL NUMPAD8", isChinese: true));
        Assert.Equal("Ctrl + Numpad 8", KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL NUMPAD8", isChinese: false));
        Assert.Equal("Ctrl + Shift + 8", KeyMappingHelpers.ConvertToFriendlyText("VK_CONTROL VK_SHIFT 8", isChinese: true));
    }

    [Fact]
    public void MergeRecordedKeyWithOriginalConstraints_ShouldPreserveAndInvalidateCorrectly()
    {
        // 1. Pure key with no_modifiers preserves constraint when changed to another pure key
        var result1 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("no_modifiers left", "VK_RIGHT");
        Assert.Equal("no_modifiers VK_RIGHT", result1);

        // 2. Pure key with no_modifiers strips constraint when changed to combination key
        var result2 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("no_modifiers left", "VK_MENU VK_RIGHT");
        Assert.Equal("VK_MENU VK_RIGHT", result2);

        // 3. no_alt and no_ctrl: changing to pure key preserves both
        var result3 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("VK_NUMPAD7 no_alt no_ctrl", "VK_NUMPAD8");
        Assert.Equal("VK_NUMPAD8 no_alt no_ctrl", result3);

        // 4. no_alt and no_ctrl: changing to Alt+8 strips no_alt and preserves no_ctrl
        var result4 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("VK_NUMPAD7 no_alt no_ctrl", "VK_MENU VK_NUMPAD8");
        Assert.Equal("VK_MENU VK_NUMPAD8 no_ctrl", result4);

        // 5. no_alt and no_ctrl: changing to Ctrl+Alt+8 strips both no_alt and no_ctrl
        var result5 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("VK_NUMPAD7 no_alt no_ctrl", "VK_CONTROL VK_MENU VK_NUMPAD8");
        Assert.Equal("VK_CONTROL VK_MENU VK_NUMPAD8", result5);

        // 6. Original without constraints: records cleanly without adding arbitrary constraints
        var result6 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("VK_F1", "VK_F2");
        Assert.Equal("VK_F2", result6);

        // 7. Empty new key returns empty
        var result7 = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints("VK_NUMPAD7 no_alt", "");
        Assert.Equal("", result7);
    }

    [Fact]
    public void KeyBindingExpression_ToggleAndSetNegativeModifiers_ShouldBehaveAccurately()
    {
        var expr = KeyBindingExpression.Parse("left");
        Assert.Equal(NegativeModifierMask.None, expr.NegativeModifiers);

        var withNoMod = expr.ToggleNegativeModifier(NegativeModifierMask.NoModifiers);
        Assert.True(withNoMod.NegativeModifiers.HasFlag(NegativeModifierMask.NoModifiers));
        Assert.Equal("no_modifiers left", withNoMod.ToIniString());

        var withoutNoMod = withNoMod.ToggleNegativeModifier(NegativeModifierMask.NoModifiers);
        Assert.False(withoutNoMod.NegativeModifiers.HasFlag(NegativeModifierMask.NoModifiers));
        Assert.Equal("left", withoutNoMod.ToIniString());

        var withSpecific = expr.SetNegativeModifier(NegativeModifierMask.NoAlt, true)
                               .SetNegativeModifier(NegativeModifierMask.NoCtrl, true);
        Assert.True(withSpecific.NegativeModifiers.HasFlag(NegativeModifierMask.NoAlt));
        Assert.True(withSpecific.NegativeModifiers.HasFlag(NegativeModifierMask.NoCtrl));
        Assert.Equal("left no_alt no_ctrl", withSpecific.ToIniString());

        var altCombo = KeyBindingExpression.Parse("alt left");
        var sanitized = altCombo.SetNegativeModifier(NegativeModifierMask.NoAlt, true);
        Assert.False(sanitized.NegativeModifiers.HasFlag(NegativeModifierMask.NoAlt));
    }

    [Theory]
    // Physical numpad keys (isExtendedKey == false) disambiguated from navigation keys
    [InlineData(35, 0x4F, false, "NUMPAD1")] // VK_END redirected from Numpad 1
    [InlineData(40, 0x50, false, "NUMPAD2")] // VK_DOWN redirected from Numpad 2
    [InlineData(34, 0x51, false, "NUMPAD3")] // VK_NEXT redirected from Numpad 3
    [InlineData(37, 0x4B, false, "NUMPAD4")] // VK_LEFT redirected from Numpad 4
    [InlineData(12, 0x4C, false, "NUMPAD5")] // VK_CLEAR redirected from Numpad 5
    [InlineData(39, 0x4D, false, "NUMPAD6")] // VK_RIGHT redirected from Numpad 6
    [InlineData(36, 0x47, false, "NUMPAD7")] // VK_HOME redirected from Numpad 7
    [InlineData(38, 0x48, false, "NUMPAD8")] // VK_UP redirected from Numpad 8
    [InlineData(33, 0x49, false, "NUMPAD9")] // VK_PRIOR redirected from Numpad 9
    [InlineData(45, 0x52, false, "NUMPAD0")] // VK_INSERT redirected from Numpad 0
    [InlineData(46, 0x53, false, "DECIMAL")] // VK_DELETE redirected from Numpad .
    // Navigation cluster keys (isExtendedKey == true) are NOT treated as numpad
    [InlineData(35, 0x4F, true, "VK_END")]
    [InlineData(46, 0x53, true, "VK_DELETE")]
    [InlineData(36, 0x47, true, "VK_HOME")]
    [InlineData(38, 0x48, true, "VK_UP")]
    public void ConvertVirtualKeyToGimiInputKey_WithScanCodeAndExtendedKey_DisambiguatesPhysicalNumpad(
        int virtualKey, uint scanCode, bool isExtendedKey, string expectedToken)
    {
        var result = KeyMappingHelpers.ConvertVirtualKeyToGimiInputKey(virtualKey, scanCode, isExtendedKey);
        Assert.Equal(expectedToken, result);
    }
}