using System.Text.Json;
using System.IO;
using UMToptimizer.Models;

namespace UMToptimizer.Services;

public sealed class UndoStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UMToptimizer", "last-change.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task SaveAsync(UndoRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(record, Options));
    }
    public async Task<UndoRecord?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        try { return JsonSerializer.Deserialize<UndoRecord>(await File.ReadAllTextAsync(_path)); }
        catch { return null; }
    }
    public Task ClearAsync() { if (File.Exists(_path)) File.Delete(_path); return Task.CompletedTask; }
}
