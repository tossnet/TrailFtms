using System.Reflection;
using Microsoft.AspNetCore.Components;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Deployment;

[TestClass]
public sealed class StaticFilesTests
{
    // On GitHub Pages, a folder named like a page wins over the app: reloading /programs redirected to
    // /programs/ and showed the folder's index.json. Keep page routes and static folders apart.
    [TestMethod]
    public void No_static_folder_has_the_name_of_a_page()
    {
        var pages = typeof(App).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template.Trim('/').Split('/')[0])
            .Where(segment => segment.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = Directory.GetDirectories(WwwrootHttpHandler.WwwrootPath).Select(d => new DirectoryInfo(d).Name);

        Assert.IsNotEmpty(pages);
        CollectionAssert.AreEquivalent(Array.Empty<string>(), folders.Where(pages.Contains).ToArray());
    }
}
