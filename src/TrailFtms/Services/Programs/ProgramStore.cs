using System.Net.Http.Json;
using System.Text.Json;
using TrailFtms.Services.Storage;

namespace TrailFtms.Services.Programs;

/// <summary>
/// Programs shipped with the app (wwwroot/data/programs, listed in index.json) plus the ones imported
/// by the user, kept in the browser storage. An imported program replaces a shipped one with the same id.
/// </summary>
public sealed class ProgramStore(HttpClient http, IBrowserStorage storage)
{
    // Not "programs/": a folder with the name of a page breaks that page on GitHub Pages
    // (reloading /programs would redirect to the folder and show its index.json).
    private const string Folder = "data/programs";
    private const string IndexPath = $"{Folder}/index.json";
    private const string StorageKey = "trailftms.programs";

    private readonly List<RideProgram> _embedded = [];
    private readonly List<RideProgram> _imported = [];
    private Task? _loading;

    public IReadOnlyList<RideProgram> Programs =>
        [.. _embedded.Where(e => !_imported.Exists(i => i.Id == e.Id)), .. _imported];

    /// <summary>Loads shipped and imported programs once. Invalid shipped files are skipped.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    public RideProgram? Find(string? id) => Programs.FirstOrDefault(p => p.Id == id);

    public bool IsImported(string id) => _imported.Exists(p => p.Id == id);

    /// <summary>Validates <paramref name="json"/> and, if valid, adds (or replaces) it as an imported program.</summary>
    public async Task<ProgramParseResult> ImportAsync(string json)
    {
        await LoadAsync();
        var result = ProgramParser.Parse(json);
        if (result.Program is { } program)
        {
            _imported.RemoveAll(p => p.Id == program.Id);
            _imported.Add(program);
            await SaveImportedAsync();
        }
        return result;
    }

    public async Task RemoveAsync(string id)
    {
        if (_imported.RemoveAll(p => p.Id == id) > 0)
            await SaveImportedAsync();
    }

    private async Task LoadCoreAsync()
    {
        var files = await http.GetFromJsonAsync(IndexPath, ProgramJsonContext.Default.StringArray) ?? [];
        var texts = await Task.WhenAll(files.Select(f => http.GetStringAsync($"{Folder}/{f}")));
        _embedded.AddRange(texts.Select(t => ProgramParser.Parse(t).Program).OfType<RideProgram>());

        var stored = await storage.GetAsync(StorageKey);
        if (stored is null)
            return;
        try
        {
            var items = JsonSerializer.Deserialize(stored, ProgramJsonContext.Default.StringArray) ?? [];
            _imported.AddRange(items.Select(t => ProgramParser.Parse(t).Program).OfType<RideProgram>());
        }
        catch (JsonException)
        {
            // Corrupted storage: start again with no imported program.
        }
    }

    private ValueTask SaveImportedAsync()
    {
        var items = _imported.Select(ProgramParser.Serialize).ToArray();
        return storage.SetAsync(StorageKey, JsonSerializer.Serialize(items, ProgramJsonContext.Default.StringArray));
    }
}
