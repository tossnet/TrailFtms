using Bunit;
using TrailFtms.Components;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Components;

[TestClass]
public sealed class SafetyNoticeTests
{
    [TestMethod]
    public void Shown_on_first_launch_and_closed_once_accepted()
    {
        using var ctx = new AppTestContext();

        var cut = ctx.Render<SafetyNotice>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "clé de sécurité"));
        cut.FindAll("button").Single(b => b.TextContent.Contains("J'ai compris")).Click();

        cut.WaitForAssertion(() => Assert.IsEmpty(cut.FindAll(".overlay")));
        ctx.JSInterop.VerifyInvoke("localStorage.setItem");
    }

    [TestMethod]
    public void Hidden_when_already_accepted()
    {
        using var ctx = new AppTestContext();
        ctx.JSInterop.Setup<string?>("localStorage.getItem", "trailftms.safety-accepted").SetResult("true");

        var cut = ctx.Render<SafetyNotice>();

        Assert.IsEmpty(cut.FindAll(".overlay"));
    }
}
