using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using UMToptimizer.Core;
using Xunit;

namespace UMToptimizer.Tests;

internal static class TestSetup
{
    /// <summary>Testler kullanıcının gerçek %LOCALAPPDATA%\UMToptimizer klasörüne asla dokunmaz.</summary>
    [ModuleInitializer]
    internal static void Init() => AppPaths.Root = Directory.CreateTempSubdirectory("umt-tests-").FullName;
}

public sealed class ProfileTests
{
    public static TheoryData<string> AndroidIds => [.. Profiles.For(Platform.Android).Select(p => p.Id)];

    [Theory, MemberData(nameof(AndroidIds))]
    public void AndroidProfile_LoadsCleanCommands(string id)
    {
        var p = Profiles.Find(Platform.Android, id)!;
        Assert.NotEmpty(p.Commands);
        Assert.DoesNotContain(p.Commands, c => c.StartsWith("adb ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(p.Commands, c => c.Contains('\n') || c.Contains('\r')); // tek satır: betik bölünmez
        Assert.Equal(p.Commands.Count, p.Commands.Distinct().Count());
    }

    [Fact]
    public void Balanced_DuplicatesRemoved() => Assert.Equal(1447, Profiles.Find(Platform.Android, "balanced")!.Commands.Count);

    [Theory]
    [InlineData("adb shell settings put global x 1", "settings put global x 1")]
    [InlineData("  adb shell setprop a b  ", "setprop a b")]
    [InlineData("adb shell 'settings put global k \"{a:b}\"'", "settings put global k \"{a:b}\"")] // tamamı tırnaklı satır
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
        var ratio = all.Count(c => Android.Target(c) is not null) / (double)all.Count;
        Assert.True(ratio > 0.95, $"Geri alınabilir oran: {ratio:P1}");
    }

    [Fact]
    public void WindowsProfiles_MapToScriptModes() =>
        Assert.All(Profiles.For(Platform.Windows), p => Assert.Contains(p.Source, WindowsTweaks.Modes));
}

public sealed class AdbParsingTests
{
    [Fact]
    public void DevicesLong_SpaceSeparated_IsParsed()
    {
        var list = AdbDevice.ParseAll("""
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            R58M12ABCDE            device usb:1-1 product:a52qnsxx model:SM_A525F device:a52q transport_id:1
            emulator-5554          unauthorized transport_id:2
            192.168.1.20:5555      device product:x model:Pixel_8 transport_id:3
            """);
        Assert.Equal(3, list.Count);
        Assert.True(list[0].Ready);
        Assert.Equal("SM_A525F", list[0].Model);
        Assert.Equal("SM A525F", list[0].Name);
        Assert.Equal("unauthorized", list[1].State);
        Assert.False(list[1].Ready);
        Assert.Equal("192.168.1.20:5555", list[2].Serial);
    }

    [Fact]
    public void DevicesShort_TabSeparated_IsParsed()
    {
        var d = AdbDevice.Parse("ABC123\tdevice");
        Assert.Equal(("ABC123", "device", ""), (d!.Serial, d.State, d.Model));
    }

    [Theory]
    [InlineData("R58M12ABCDE", true)]
    [InlineData("192.168.1.20:5555", true)]
    [InlineData("adb-ABC._adb-tls-connect._tcp", true)]
    [InlineData("-s", false)]            // adb'ye seçenek olarak geçerdi
    [InlineData("a b", false)]
    [InlineData("a;reboot", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeSerial_AllowsOnlyPlainTokens(string? serial, bool expected) => Assert.Equal(expected, Adb.IsSafeSerial(serial));

    [Fact]
    public void Parse_RejectsUnsafeSerial() => Assert.Null(AdbDevice.Parse("--help device"));

    [Theory]
    [InlineData("settings put global foo 1", "settings|global|foo")]
    [InlineData("settings put secure a.b_c 'x y'", "settings|secure|a.b_c")]
    [InlineData("setprop debug.hwui.renderer skiavk", "prop||debug.hwui.renderer")]
    [InlineData("device_config put runtime_native usap_pool_enabled true", "config|runtime_native|usap_pool_enabled")]
    [InlineData("cmd device_config put activity_manager max_cached_processes 32", "config|activity_manager|max_cached_processes")]
    public void Target_RecognizesUndoableCommands(string cmd, string id) => Assert.Equal(id, Android.Target(cmd)?.Id);

    [Theory]
    [InlineData("pm compile -a -f -m everything")]
    [InlineData("cmd power set-mode 1")]
    [InlineData("dumpsys deviceidle force-idle")]
    public void Target_ReturnsNull_ForNonUndoable(string cmd) => Assert.Null(Android.Target(cmd));

    [Fact]
    public void ParseCurrent_ReadsAllSections()
    {
        var map = Android.ParseCurrent("""
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
        => Assert.Equal(expected, Android.RestoreCommand(new(kind, ns, key, value)));

    [Theory]
    [InlineData("settings", "global", "x; reboot", "1")]         // komut enjeksiyonu
    [InlineData("settings", "global", "$(rm -rf /sdcard)", "1")]
    [InlineData("settings", "glo`id`bal", "x", "1")]
    [InlineData("prop", "", "a|b", "1")]
    [InlineData("bilinmeyen", "", "a", "1")]
    [InlineData("settings", "", "x", "1")]                       // ad alanı eksik
    [InlineData("prop", "global", "x", "1")]                     // prop'ta ad alanı olmaz
    [InlineData("settings", "global", "x", "1\nreboot")]         // çok satırlı değer betiği böler
    public void RestoreCommand_RejectsUnsafeInput(string kind, string ns, string key, string value)
        => Assert.Null(Android.RestoreCommand(new(kind, ns, key, value)));

    [Fact]
    public void RestoreCommand_NullFieldsFromTamperedJson_DoNotThrow() =>
        Assert.Null(Android.RestoreCommand(new(null!, null!, null!, null)));

    [Fact]
    public void RestoreCommand_ValueInjection_IsQuoted()
    {
        var cmd = Android.RestoreCommand(new("settings", "global", "k", "'; reboot; echo '"))!;
        Assert.Equal("settings put global k ''\\''; reboot; echo '\\'''", cmd);
    }
}

public sealed class StorageTests
{
    [Fact]
    public void Json_Read_ReturnsDefault_ForMissingOrCorruptFile()
    {
        var file = Path.Combine(AppPaths.Root, "bozuk.json");
        Assert.Null(Json.Read<AndroidSnapshot>(file));
        File.WriteAllText(file, "{ bu json değil");
        Assert.Null(Json.Read<AndroidSnapshot>(file));
    }

    [Fact]
    public void Json_Write_IsAtomic_AndRoundTrips()
    {
        var file = Path.Combine(AppPaths.Root, "yaz.json");
        Json.Write(file, new AndroidSnapshot { Serial = "ABC", Values = { ["settings|global|x"] = new("settings", "global", "x", "1") } });
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Equal("1", Json.Read<AndroidSnapshot>(file)!.Values["settings|global|x"].Value);
    }

    /// <summary>Elle bozulmuş yedek dosyaları arayüzü düşürmemeli: kullanılamayanlar sessizce atlanır.</summary>
    [Fact]
    public void AllSnapshots_SkipsBrokenFiles_AndNullFields()
    {
        File.WriteAllText(Path.Combine(AppPaths.Snapshots, "bozuk.json"), "çöp");
        File.WriteAllText(Path.Combine(AppPaths.Snapshots, "nullu.json"), """{"Serial":"NULLU1","Profiles":null,"Values":{"a":null,"b":{"Kind":"settings","Ns":"global","Key":"k","Value":"1"},"c":{"Kind":null,"Ns":null,"Key":null}}}""");
        File.WriteAllText(Path.Combine(AppPaths.Snapshots, "serisiz.json"), """{"Serial":"; reboot","Values":{}}""");

        var all = Android.AllSnapshots();
        var snap = Assert.Single(all, s => s.Serial == "NULLU1");
        Assert.Empty(snap.Profiles);
        Assert.Equal("settings|global|k", Assert.Single(snap.Values).Key);
        Assert.NotNull(snap.Summary);
        Assert.DoesNotContain(all, s => s.Serial.Contains("reboot"));
    }
}

public sealed class SnapshotSafetyTests
{
    private static Dictionary<string, string> Read(string output) => Android.ParseCurrent(output.Replace("\r\n", "\n"));

    /// <summary>Eksik okuma, var olan ayarları "önceden yoktu" diye kaydettirir; geri yükleme onları silerdi.</summary>
    [Fact]
    public void IsCompleteRead_RequiresEverySettingsSectionAndProps()
    {
        const string full = "@@global\na=1\n@@system\nb=2\n@@secure\nc=3\n@@prop\n[ro.x]: [y]\n@@config\n@@done";
        Assert.True(Android.IsCompleteRead(Read(full)));                                   // device_config boş olabilir (eski Android)
        Assert.False(Android.IsCompleteRead(Read(full.Replace("c=3\n", ""))));             // secure boş döndü
        Assert.False(Android.IsCompleteRead(Read(full.Replace("c=3", "Exception occurred while executing 'list'")))); // hata satırı
        Assert.False(Android.IsCompleteRead(Read(full.Replace("[ro.x]: [y]\n", ""))));     // getprop boş
        Assert.False(Android.IsCompleteRead(Read("@@done")));
    }

    [Fact]
    public void SnapshotFile_IsInjective_ButKeepsLegacyNameForPlainSerials()
    {
        Assert.Equal("R58M12ABC.json", Path.GetFileName(Android.SnapshotFile("R58M12ABC"))); // eski sürümle aynı
        var a = Android.SnapshotFile("a:b");
        var b = Android.SnapshotFile("a_b");
        Assert.NotEqual(a, b);                                                              // eskiden ikisi de "a_b.json" idi
        Assert.StartsWith(AppPaths.Snapshots, a);
        Assert.DoesNotContain(':', Path.GetFileName(a));
    }

    [Fact]
    public void LoadSnapshot_IgnoresFileBelongingToAnotherDevice()
    {
        File.WriteAllText(Android.SnapshotFile("DEVICEA1"), """{"Serial":"DEVICEB2","Profiles":["Balanced"],"Values":{}}""");
        Assert.Null(Android.LoadSnapshot("DEVICEA1"));
    }

    [Fact]
    public void LoadSnapshot_FindsLegacyFileName_WhenSerialMatches()
    {
        File.WriteAllText(Path.Combine(AppPaths.Snapshots, "emulator_5554.json"), """{"Serial":"emulator-5554","Profiles":["Vulkan"],"Values":{}}""");
        Assert.Equal("emulator-5554", Android.LoadSnapshot("emulator-5554")?.Serial);
        Assert.Null(Android.LoadSnapshot("emulator_5554x"));
    }
}

public sealed class UserStepsTests
{
    /// <summary>Geçici dosya temizliği: eskiler silinir, yeniler ve junction hedefleri korunur.</summary>
    [Fact]
    public void CleanTemp_DeletesOldFiles_KeepsRecentOnes_AndNeverFollowsJunctions()
    {
        var root = Directory.CreateTempSubdirectory("umt-clean-").FullName;
        try
        {
            var temp = Directory.CreateDirectory(Path.Combine(root, "Temp")).FullName;
            var victim = Directory.CreateDirectory(Path.Combine(root, "kurban")).FullName;
            var old = DateTime.UtcNow.AddDays(-3);
            string Make(string path, bool aged)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "x");
                if (aged) File.SetLastWriteTimeUtc(path, old);
                return path;
            }
            var precious = Make(Path.Combine(victim, "onemli.txt"), aged: true);
            var stale = Make(Path.Combine(temp, "eski.tmp"), aged: true);
            var staleDeep = Make(Path.Combine(temp, "alt", "eski2.tmp"), aged: true);
            var fresh = Make(Path.Combine(temp, "yeni.tmp"), aged: false);
            var readOnly = Make(Path.Combine(temp, "saltokunur.tmp"), aged: true);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var link = Path.Combine(temp, "baglanti");
            using (var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{victim}\"") { CreateNoWindow = true, UseShellExecute = false })!) mk.WaitForExit();
            Assert.True(Directory.Exists(link), "junction kurulamadı (ön koşul)");

            var deleted = UserSteps.CleanTemp(temp, TimeSpan.FromHours(24));

            Assert.Equal(3, deleted);
            Assert.False(File.Exists(stale));
            Assert.False(File.Exists(staleDeep));
            Assert.False(File.Exists(readOnly));
            Assert.True(File.Exists(fresh));     // son 24 saatte değişen dosya korunur
            Assert.True(File.Exists(precious));  // junction hedefi DURUYOR
            Assert.True(Directory.Exists(link)); // bağlantıya dokunulmadı

            // Kökün kendisi bir bağlantıysa hiçbir şey yapılmaz.
            Assert.Equal(0, UserSteps.CleanTemp(link, TimeSpan.Zero));
            Assert.True(File.Exists(precious));
            Directory.Delete(link); // yalnızca bağlantıyı kaldırır
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanTemp_IgnoresMissingFolder() =>
        Assert.Equal(0, UserSteps.CleanTemp(Path.Combine(AppPaths.Root, "yok-boyle-klasor"), TimeSpan.Zero));
    // Sürücü kökü koruması (DirectoryInfo.Parent == null) bilerek test edilmiyor: yanlış çıkarsa test diski silerdi.
}

public sealed class CliTests
{
    /// <summary>Eski hata: "--serial ABC" değeri konum argümanı sayılıyor, komut eşleşmiyor ve yardım basılıyordu.</summary>
    [Fact]
    public void Parse_SerialValue_IsNotPositional()
    {
        var p = Cli.Parse(["android", "apply", "Balanced", "--serial", "R58M12", "--YES"]);
        Assert.Equal(["android", "apply", "balanced"], p.Positional);
        Assert.Equal("R58M12", p.Options["--serial"]); // seri numarasının harf büyüklüğü korunur
        Assert.Contains("--yes", p.Flags);
    }

    [Fact]
    public void Parse_SerialWithoutValue_IsUserError() => Assert.Throws<UserException>(() => Cli.Parse(["android", "restore", "--serial"]));

    [Fact]
    public async Task Apply_WithSerial_ReachesTheCommand_AndAsksForYes()
    {
        var (code, _, err) = await Run("android", "apply", "balanced", "--serial", "R58M12");
        Assert.Equal(1, code);              // 2 = "komut tanınmadı"; 1 = komut bulundu, onay bekliyor
        Assert.Contains("--yes", err);
    }

    [Theory]
    [InlineData("windows", "apply", "standard")]
    [InlineData("windows", "restore")]
    [InlineData("android", "restore")]
    public async Task SystemChangingCommands_RequireYes(params string[] args)
    {
        var (code, _, err) = await Run(args);
        Assert.Equal(1, code);
        Assert.Contains("--yes", err);
    }

    [Fact]
    public async Task Profiles_ListsAll()
    {
        var (code, output, _) = await Run("profiles");
        Assert.Equal(0, code);
        Assert.All(Profiles.All, p => Assert.Contains(p.Id, output));
    }

    [Fact]
    public async Task UnknownCommand_PrintsHelp_AndReturns2() => Assert.Equal(2, (await Run("yok")).Code);

    [Fact]
    public async Task UnsafeSerial_IsRejected_BeforeAdbRuns()
    {
        var (code, _, err) = await Run("android", "apply", "vulkan", "--serial", "-x", "--yes");
        Assert.Equal(1, code);
        Assert.Contains("--serial", err);
    }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        using var o = new StringWriter();
        using var e = new StringWriter();
        return (await Cli.RunAsync(args, o, e), o.ToString(), e.ToString());
    }
}

public sealed class WindowsScriptTests
{
    private static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [Fact]
    public void ElevatedCommand_PinsHash_AndNeverEmbedsRawValues()
    {
        // Yol hem düz hem "akıllı" tırnak içeriyor: PowerShell ikisini de tırnak sayar. Değerler Base64 gömülmeli.
        const string path = "C:\\Users\\o'neil’;calc;‘\\a.ps1";
        var cmd = WindowsTweaks.ElevatedCommand(path, "x", "Standard", @"C:\ProgramData\UMToptimizer\logs\l.log", new(true));
        Assert.DoesNotContain("neil", cmd);
        Assert.DoesNotContain("calc", cmd);
        Assert.DoesNotContain('’', cmd);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path)), cmd);
        Assert.Contains("2D711642B726B04401627CA9FBAC32F5C8530FB1903CC4DB02258717921A4881", cmd); // SHA-256("x")
        Assert.Contains("-RestorePoint", cmd);
        Assert.DoesNotContain("-AllowBitLocker", cmd);
    }

    [Fact]
    public async Task RunAsync_RejectsUnknownMode() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WindowsTweaks.RunAsync("Evil'; calc", new()));

    /// <summary>
    /// Gerçek uçtan uca kontrol: doğru betik → özet eşleşir, betik çalışır (yönetici olmadığı için 1 ile çıkar);
    /// kurcalanmış betik → 3 ile çıkar ve HİÇ çalışmaz. Yönetici olarak çalıştırılırsa sistem değişmesin diye atlanır.
    /// </summary>
    [Theory]
    [InlineData(false, 1, "umt-test-")]
    [InlineData(true, 3, "umt-test-")]
    [InlineData(false, 1, "umt-o'neil’s ‘test’-")] // tırnaklı klasör adı: komut bozulmamalı, enjeksiyon olmamalı
    public void ElevatedCommand_RejectsTamperedScript(bool tamper, int expectedExit, string prefix)
    {
        if (IsElevated) return;
        var dir = Directory.CreateTempSubdirectory(prefix).FullName;
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
