using TrailFtms.Services.Programs;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Services.Programs;

[TestClass]
public sealed class ProgramStoreTests
{
    private const string StorageKey = "trailftms.programs";

    private static string ProgramJson(string id, string title = "Imported") => $$"""
        { "id": "{{id}}", "title": "{{title}}", "youtubeId": "KiEN4HxM4ic",
          "segments": [ { "t": 0, "speed": 3, "incline": 1 } ] }
        """;

    private static (FakeBrowserStorage Storage, ProgramStore Store) CreateStore(string? stored = null)
    {
        var storage = new FakeBrowserStorage();
        if (stored is not null)
            storage.Items[StorageKey] = stored;
        return (storage, new ProgramStore(WwwrootHttpHandler.CreateClient(), storage));
    }

    [TestMethod]
    public async Task Loads_shipped_programs()
    {
        var (storage, store) = CreateStore();

        await store.LoadAsync();

        var program = store.Find("blue-pools-nz");
        Assert.IsNotNull(program);
        Assert.AreEqual("KiEN4HxM4ic", program.YoutubeId);
        Assert.IsFalse(store.IsImported("blue-pools-nz"));
    }

    [TestMethod]
    public async Task Import_adds_program_and_saves_it()
    {
        var (storage, store) = CreateStore();

        var result = await store.ImportAsync(ProgramJson("my-run"));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(store.IsImported("my-run"));
        StringAssert.Contains(storage.Items[StorageKey], "my-run");
    }

    [TestMethod]
    public async Task Invalid_import_changes_nothing()
    {
        var (storage, store) = CreateStore();
        await store.LoadAsync();
        var before = store.Programs.Count;

        var result = await store.ImportAsync("{ }");

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(before, store.Programs);
        Assert.IsFalse(storage.Items.ContainsKey(StorageKey));
    }

    [TestMethod]
    public async Task Imported_program_replaces_shipped_one_with_same_id_until_removed()
    {
        var (storage, store) = CreateStore();

        await store.ImportAsync(ProgramJson("blue-pools-nz", "My Blue Pools"));
        Assert.AreEqual("My Blue Pools", store.Find("blue-pools-nz")!.Title);
        Assert.HasCount(1, store.Programs.Where(p => p.Id == "blue-pools-nz"));

        await store.RemoveAsync("blue-pools-nz");
        Assert.AreEqual("Blue Pools, Nouvelle-Zélande", store.Find("blue-pools-nz")!.Title);
    }

    [TestMethod]
    public async Task Restores_imported_programs_from_storage()
    {
        var stored = System.Text.Json.JsonSerializer.Serialize(new[] { ProgramJson("saved-run"), "garbage" });
        var (_, store) = CreateStore(stored);

        await store.LoadAsync();

        Assert.IsTrue(store.IsImported("saved-run"));
    }

    [TestMethod]
    public async Task Corrupted_storage_is_ignored()
    {
        var (_, store) = CreateStore("{not an array");

        await store.LoadAsync();

        Assert.IsNotNull(store.Find("blue-pools-nz"));
    }
}
