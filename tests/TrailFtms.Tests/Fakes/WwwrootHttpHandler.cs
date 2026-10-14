using System.Net;

namespace TrailFtms.Tests.Fakes;

/// <summary>Serves the app's real wwwroot files, as GitHub Pages would.</summary>
public sealed class WwwrootHttpHandler : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("http://localhost/");

    public static string WwwrootPath { get; } = FindWwwroot();

    public static HttpClient CreateClient() => new(new WwwrootHttpHandler()) { BaseAddress = BaseAddress };

    /// <summary>A shipped program as published, so tests follow its content when it is improved.</summary>
    public static TrailFtms.Services.Programs.RideProgram ShippedProgram(string id) =>
        TrailFtms.Services.Programs.ProgramParser.Parse(File.ReadAllText(Path.Combine(WwwrootPath, "data", "programs", $"{id}.json"))).Program!;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Path.Combine(WwwrootPath, request.RequestUri!.AbsolutePath.TrimStart('/'));
        var response = File.Exists(path)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(path)) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        return Task.FromResult(response);
    }

    private static string FindWwwroot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TrailFtms.slnx")))
                return Path.Combine(dir.FullName, "src", "TrailFtms", "wwwroot");
        }
        throw new DirectoryNotFoundException("Repository root (TrailFtms.slnx) not found.");
    }
}
