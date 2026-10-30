namespace UMToptimizer.Models;

public enum PlatformKind { Windows, Android }
public enum ProfileActionKind { PowerPlan, AndroidSetting, WindowsSetting }

public sealed record ProfileAction(ProfileActionKind Kind, string Key, string Value, string Title, string Detail);
public sealed record OptimizationProfile(string Id, string Name, string Subtitle, string Description, PlatformKind Platform, IReadOnlyList<ProfileAction> Actions)
{
    public IReadOnlyList<string> Commands { get; init; } = [];
    public string RiskNotice { get; init; } = "";
    public string? WindowsScriptMode { get; init; }
}
public sealed record AndroidDevice(string Serial, string State, string Details);
public sealed record AndroidSettingSnapshot(string Namespace, string Key, string? PreviousValue);
public sealed class UndoRecord
{
    public string Platform { get; set; } = "";
    public string Profile { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? PreviousPowerPlan { get; set; }
    public string? WindowsScriptMode { get; set; }
    public string? AndroidDeviceSerial { get; set; }
    public bool CanUndo { get; set; } = true;
    public string? Notes { get; set; }
    public List<AndroidSettingSnapshot> AndroidSettings { get; set; } = [];
}
