using Microsoft.JSInterop;

namespace TrailFtms.Services.Storage;

/// <summary>localStorage through wwwroot/js/storage.js.</summary>
public sealed class BrowserStorage(IJSRuntime js) : IBrowserStorage, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public async ValueTask<string?> GetAsync(string key) =>
        await (await ModuleAsync()).InvokeAsync<string?>("getItem", key);

    public async ValueTask SetAsync(string key, string value) =>
        await (await ModuleAsync()).InvokeVoidAsync("setItem", key, value);

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js");

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
            return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
