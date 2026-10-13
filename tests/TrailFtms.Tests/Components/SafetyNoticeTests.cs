using Blazor.WinOld;
using Blazor.WinOld.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Components;
using TrailFtms.Services.Settings;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Components;

[TestClass]
public sealed class SafetyNoticeTests
{
    private static (AppTestContext Ctx, FakeDialogService Dialogs) Create()
    {
        var ctx = new AppTestContext();
        var dialogs = new FakeDialogService();
        ctx.Services.AddSingleton<IDialogService>(dialogs);
        return (ctx, dialogs);
    }

    [TestMethod]
    public void First_launch_shows_a_message_box_and_remembers_the_answer()
    {
        var (ctx, dialogs) = Create();
        using var _ = ctx;

        ctx.Render<SafetyNotice>();

        var box = dialogs.MessageBoxes.Single();
        Assert.AreEqual("Avant de commencer", box.Title);
        Assert.AreEqual("J'ai compris", box.OkButtonText);
        StringAssert.Contains(box.Message, "clé de sécurité");
        Assert.AreEqual(Icon.Alert, box.Icon);
        Assert.IsTrue(box.TouchMode);
        Assert.IsTrue(ctx.Services.GetRequiredService<SettingsStore>().SafetyAccepted);
        Assert.AreEqual("true", ctx.Storage.Items["trailftms.safety-accepted"]);
    }

    [TestMethod]
    public void Closed_without_accepting_asks_again_next_time()
    {
        var (ctx, dialogs) = Create();
        using var _ = ctx;
        dialogs.Answer = null;

        ctx.Render<SafetyNotice>();

        Assert.HasCount(1, dialogs.MessageBoxes);
        Assert.IsFalse(ctx.Services.GetRequiredService<SettingsStore>().SafetyAccepted);
    }

    [TestMethod]
    public void Nothing_shown_when_already_accepted()
    {
        var (ctx, dialogs) = Create();
        using var _ = ctx;
        ctx.Storage.Items["trailftms.safety-accepted"] = "true";

        ctx.Render<SafetyNotice>();

        Assert.IsEmpty(dialogs.MessageBoxes);
    }
}
