using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using UMToptimizer.Core;
using Xunit;

namespace UMToptimizer.Tests;

public sealed class ProfileTests
{
    public static TheoryData<string> AndroidIds => [.. Profiles.For(Platform.Android).Select(p => p.Id)];

    [Theory, MemberData(nameof(AndroidIds))]
    public void AndroidProfile_LoadsCleanCommands(string id)
    {
        var p = Profiles.Find(Platform.Android, id)!;
        Assert.NotEmpty(p.Commands);
        Assert.DoesNotContain(p.Commands, c => c.StartsWith("adb ", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(p.Commands.Count, p.Commands.Distinct().Count());
    }

    [Fact]
    public void Balanced_DuplicatesRemoved() => Assert.Equal(1447, Profiles.Find(Platform.Android, "balanced")!.Commands.Count);

    [Theory]
    [InlineData("adb shell settings put global x 1", "settings put global x 1")]
    [InlineData("  adb shell setprop a b  ", "setprop a b")]
    [InlineData("adb shell 'settings put global k \"{a:b}\"'", "settings put global k \"{a:b}\"")] // eski hatalı satır
    [InlineData("adb shell setprop a ''", "setprop a ''")] // boş değer tırnakları korunur
    public void Normalize_StripsPrefixAndWrappingQuotes(string line, string expected) => Assert.Equal(expected, Profile.Normalize(line));

    [Theory]
    [InlineData(Platform.Windows, "balanced", "standard")]      // eski CLI adı
    [InlineData(Platform.Windows, "performance", "aggressive")] // eski CLI adı
    [InlineData(Platform.Android, "Extra Boost", "extra-boost")]
    [InlineData(Platform.Android, "extraboost", "extra-boost")]
    [InlineData(Platform.Android, "VULKAN", "vulkan")]
    public void Find_AcceptsAliases(Platform platform, string key, string expectedId) => Assert.Equal(expectedId, Profiles.Find(platform, key)?.Id);

    [Fact]
    public void Find_ReturnsNull_ForUnknown() => Assert.Null(Profiles.Find(Platform.Android, "yok"));

    [Fact]
    public void MostAndroidCommands_AreUndoable()
    {
        var all = Profiles.For(Platform.Android).SelectMany(p => p.Commands).ToList();
        var ratio = all.Count(c => Adb.Target(c) is not null) / (double)all.Count;
        Assert.True(ratio > 0.95, $"Geri alınabilir oran: {ratio:P1}");
    }
}

public sealed class AdbParsingTests
{
    [Fact]
    public void DevicesLong_SpaceSeparated_IsParsed()
    {
        // "adb devices -l" alanları SEKME değil boşlukla ayırır (eski sürüm bunu kaçırıyordu)
        var list = AdbDevice.ParseAll("""
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            R58M12ABCDE            device usb:1-1 product:a52qnsxx model:SM_A525F device:a52q transport_id:1
            emulator-5554          unauthorized transport_id:2
            """);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].Ready);
        Assert.Equal("SM_A525F", list[0].Model);
        Assert.Equal("unauthorized", list[1].State);
        Assert.False(list[1].Ready);
    }

    [Fact]
    public void DevicesShort_TabSeparated_IsParsed()
    {
        var d = AdbDevice.Parse("ABC123\tdevice");
        Assert.Equal(("ABC123", "device", ""), (d!.Serial, d.State, d.Model));
    }

    [Theory]
    [InlineData("settings put global foo 1", "settings|global|foo")]
    [InlineData("settings put secure a.b_c 'x y'", "settings|secure|a.b_c")]
    [InlineData("setprop debug.hwui.renderer skiavk", "prop||debug.hwui.renderer")]
    [InlineData("device_config put runtime_native usap_pool_enabled true", "config|runtime_native|usap_pool_enabled")]
    [InlineData("cmd device_config put activity_manager max_cached_processes 32", "config|activity_manager|max_cached_processes")]
    public void Target_RecognizesUndoableCommands(string cmd, string id) => Assert.Equal(id, Adb.Target(cmd)?.Id);

    [Theory]
    [InlineData("pm compile -a -f -m everything")]
    [InlineData("cmd power set-mode 1")]
    [InlineData("dumpsys deviceidle force-idle")]
    public void Target_ReturnsNull_ForNonUndoable(string cmd) => Assert.Null(Adb.Target(cmd));

    [Fact]
    public void ParseCurrent_ReadsAllSections()
    {
        var map = Adb.ParseCurrent("""
            @@global
            window_animation_scale=1.0
            battery_saver_constants=a=1,b=2
            empty_value=
            @@system
            screen_brightness=128
            @@prop
            [debug.hwui.renderer]: [skiagl]
            [ro.weird]: [ends]]]
            @@config
            runtime_native/usap_pool_enabled=false
            """.Replace("\r\n", "\n"));
        Assert.Equal("1.0", map["settings|global|window_animation_scale"]);
        Assert.Equal("a=1,b=2", map["settings|global|battery_saver_constants"]); // değerdeki '=' korunur
        Assert.Equal("", map["settings|global|empty_value"]);                     // boş değer ≠ yok
        Assert.Equal("128", map["settings|system|screen_brightness"]);
        Assert.Equal("skiagl", map["prop||debug.hwui.renderer"]);
        Assert.Equal("ends]]", map["prop||ro.weird"]);                            // yalnızca son ']' atılır
        Assert.Equal("false", map["config|runtime_native|usap_pool_enabled"]);
    }
}

public sealed class RestoreCommandTests
{
    [Theory]
    [InlineData("settings", "global", "foo", null, "settings delete global foo")]
    [InlineData("settings", "global", "foo", "1", "settings put global foo '1'")]
    [InlineData("settings", "global", "foo", "", "settings put global foo ''")]
    [InlineData("settings", "system", "foo", "it's", "settings put system foo 'it'\\''s'")]
    [InlineData("config", "ns", "k", null, "device_config delete ns k")]
    [InlineData("prop", "", "debug.x", null, "setprop debug.x ''")]
    public void RestoreCommand_BuildsQuotedCommand(string kind, string ns, string key, string? value, string expected)
        => Assert.Equal(expected, Adb.RestoreCommand(new(kind, ns, key, value)));

    [Theory]
    [InlineData("settings", "global", "x; reboot")]       // komut enjeksiyonu
    [InlineData("settings", "global", "$(rm -rf /sdcard)")]
    [InlineData("settings", "glo`id`bal", "x")]
    [InlineData("prop", "", "a|b")]
    [InlineData("bilinmeyen", "", "a")]
    public void RestoreCommand_RejectsUnsafeInput(string kind, string ns, string key)
        => Assert.Null(Adb.RestoreCommand(new(kind, ns, key, "1")));

    [Fact]
    public void RestoreCommand_ValueInjection_IsQuoted()
    {
        var cmd = Adb.RestoreCommand(new("settings", "global", "k", "'; reboot; echo '"))!;
        Assert.Equal("settings put global k ''\\''; reboot; echo '\\'''", cmd);
    }
}

public sealed class WindowsScriptTests
{
    private static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [Fact]
    public void ElevatedCommand_PinsHashAndEscapesQuotes()
    {
        var cmd = WindowsTweaks.ElevatedCommand(@"C:\Users\o'neil\a.ps1", "x", "Standard", @"C:\ProgramData\UMToptimizer\logs\l.log", new(true));
        Assert.Contains(@"'C:\Users\o''neil\a.ps1'", cmd); // PowerShell tek tırnak kaçışı
        Assert.Contains("2D711642B726B04401627CA9FBAC32F5C8530FB1903CC4DB02258717921A4881", cmd); // SHA-256("x")
        Assert.Contains("-RestorePoint", cmd);
        Assert.DoesNotContain("-AllowBitLocker", cmd);
    }

    /// <summary>
    /// Gerçek uçtan uca kontrol: doğru betik → hash eşleşir, betik çalışır (yönetici olmadığı için 1 ile çıkar);
    /// kurcalanmış betik → 3 ile çıkar ve HİÇ çalışmaz. Yönetici olarak çalıştırılırsa sistem değişmesin diye atlanır.
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void ElevatedCommand_RejectsTamperedScript(bool tamper, int expectedExit)
    {
        if (IsElevated) return;
        var dir = Directory.CreateTempSubdirectory("umt-test-").FullName;
        try
        {
            var text = WindowsTweaks.Script;
            var path = Path.Combine(dir, "s.ps1");
            File.WriteAllText(path, tamper ? text + "\n# saldırgan eki" : text, new System.Text.UTF8Encoding(true));
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var a in (string[])["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                         WindowsTweaks.ElevatedCommand(path, text, "Standard", Path.Combine(dir, "l.log"), new(false))])
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(expectedExit, p.ExitCode);
            if (!tamper) Assert.Contains("Yönetici yetkisi gerekli", output);
        }
        finally { Directory.Delete(dir, true); }
    }
}
