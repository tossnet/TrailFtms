using Bunit;
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

    private static (BunitContext Ctx, ProgramStore Store) CreateStore(string? stored = null)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Setup<string?>("localStorage.getItem", StorageKey).SetResult(stored);
        ctx.JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        return (ctx, new ProgramStore(WwwrootHttpHandler.CreateClient(), ctx.JSInterop.JSRuntime));
    }

    [TestMethod]
    public async Task Loads_shipped_programs()
    {
        var (ctx, store) = CreateStore();
        using var _ = ctx;

        await store.LoadAsync();

        var program = store.Find("blue-pools-nz");
        Assert.IsNotNull(program);
        Assert.AreEqual("KiEN4HxM4ic", program.YoutubeId);
        Assert.IsFalse(store.IsImported("blue-pools-nz"));
    }

    [TestMethod]
    public async Task Import_adds_program_and_saves_it()
    {
        var (ctx, store) = CreateStore();
        using var _ = ctx;

        var result = await store.ImportAsync(ProgramJson("my-run"));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(store.IsImported("my-run"));
        var saved = ctx.JSInterop.VerifyInvoke("localStorage.setItem");
        Assert.AreEqual(StorageKey, saved.Arguments[0]);
        StringAssert.Contains((string)saved.Arguments[1]!, "my-run");
    }

    [TestMethod]
    public async Task Invalid_import_changes_nothing()
    {
        var (ctx, store) = CreateStore();
        using var _ = ctx;
        await store.LoadAsync();
        var before = store.Programs.Count;

        var result = await store.ImportAsync("{ }");

        Assert.IsFalse(result.IsValid);
        Assert.HasCount(before, store.Programs);
        ctx.JSInterop.VerifyNotInvoke("localStorage.setItem");
    }

    [TestMethod]
    public async Task Imported_program_replaces_shipped_one_with_same_id_until_removed()
    {
        var (ctx, store) = CreateStore();
        using var _ = ctx;

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
        var (ctx, store) = CreateStore(stored);
        using var _ = ctx;

        await store.LoadAsync();

        Assert.IsTrue(store.IsImported("saved-run"));
    }

    [TestMethod]
    public async Task Corrupted_storage_is_ignored()
    {
        var (ctx, store) = CreateStore("{not an array");
        using var _ = ctx;

        await store.LoadAsync();

        Assert.IsNotNull(store.Find("blue-pools-nz"));
    }
}
