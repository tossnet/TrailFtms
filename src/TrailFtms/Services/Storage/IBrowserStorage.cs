namespace TrailFtms.Services.Storage;

/// <summary>Key / value storage of the browser. Never throws: falls back to memory when blocked.</summary>
public interface IBrowserStorage
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
}
