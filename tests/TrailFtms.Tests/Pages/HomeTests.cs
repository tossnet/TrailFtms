using Bunit;
using TrailFtms.Pages;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class HomeTests
{
    [TestMethod]
    public void Renders_app_title()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<Home>();

        Assert.AreEqual("TrailFtms", cut.Find(".hd-1").TextContent);
    }
}
