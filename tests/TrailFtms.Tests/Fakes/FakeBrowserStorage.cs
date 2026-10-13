using TrailFtms.Services.Storage;

namespace TrailFtms.Tests.Fakes;

public sealed class FakeBrowserStorage : IBrowserStorage
{
    public Dictionary<string, string> Items { get; } = [];

    public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult(Items.GetValueOrDefault(key));

    public ValueTask SetAsync(string key, string value)
    {
        Items[key] = value;
        return ValueTask.CompletedTask;
    }
}
