using Blazor.WinOld;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Pages;
using TrailFtms.Services.Programs;
using TrailFtms.Services.Video;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class EditorTests
{
    private sealed record Rig(AppTestContext Ctx, FakeDialogService Dialogs, IRenderedComponent<Editor> Cut) : IDisposable
    {
        public ProgramStore Store => Ctx.Services.GetRequiredService<ProgramStore>();
        public NavigationManager Navigation => Ctx.Services.GetRequiredService<NavigationManager>();

        public void Click(string text) => Cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

        public void Type(string label, string value) =>
            Cut.FindAll("label").Single(l => l.TextContent.Trim() == label).ParentElement!.QuerySelector("input")!.Input(value);

        public void Dispose() => Ctx.Dispose();
    }

    private static readonly int BluePoolsPoints = WwwrootHttpHandler.ShippedProgram("blue-pools-nz").Segments.Count;

    private static Rig Render(string url = "editor")
    {
        var ctx = new AppTestContext();
        var dialogs = new FakeDialogService();
        ctx.Services.AddSingleton<IDialogService>(dialogs);
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        return new Rig(ctx, dialogs, ctx.Render<Editor>());
    }

    private static void AddPointAt(Rig rig, double seconds, string speed, string incline)
    {
        rig.Ctx.Video.CurrentTime = seconds;
        rig.Cut.FindAll(".add-point input")[0].Change(speed);
        rig.Cut.FindAll(".add-point input")[1].Change(incline);
        rig.Click("Ajouter un point ici");
    }

    [TestMethod]
    public void New_program_from_a_video_with_points_at_the_current_time()
    {
        using var rig = Render();

        rig.Type("Lien ou identifiant YouTube", "https://youtu.be/KiEN4HxM4ic");
        rig.Click("Charger");
        rig.Type("Titre", "Blue Pools, Nouvelle-Zélande");
        AddPointAt(rig, 0, "1", "0");
        AddPointAt(rig, 40.7, "3", "3");

        CollectionAssert.AreEqual(new[] { "KiEN4HxM4ic" }, rig.Ctx.Video.Loads);
        Assert.AreEqual("blue-pools-nouvelle-zelande", rig.Cut.FindAll("label").Single(l => l.TextContent.Trim() == "Identifiant").ParentElement!.QuerySelector("input")!.GetAttribute("value"));
        var times = rig.Cut.FindAll(".point .time").Select(b => b.TextContent.Trim()).ToList();
        CollectionAssert.AreEqual(new[] { "0:00", "0:40" }, times);
        Assert.HasCount(1, rig.Cut.FindAll(".timeline"));
    }

    [TestMethod]
    public void Save_reports_validation_errors()
    {
        using var rig = Render();
        rig.Type("Lien ou identifiant YouTube", "KiEN4HxM4ic");
        rig.Click("Charger");
        rig.Type("Titre", "Test");
        AddPointAt(rig, 12, "3", "2"); // nothing at 0:00

        rig.Click("Enregistrer");

        rig.Cut.WaitForAssertion(() => StringAssert.Contains(rig.Cut.Markup, "Le programme n'est pas valide"));
        StringAssert.Contains(rig.Cut.Markup, "le premier segment doit commencer à 0:00 (il commence à 0:12)");
        Assert.IsNull(rig.Store.Find("test"));
    }

    [TestMethod]
    public void Valid_program_is_saved_and_can_be_ridden()
    {
        using var rig = Render();
        rig.Type("Lien ou identifiant YouTube", "KiEN4HxM4ic");
        rig.Click("Charger");
        rig.Type("Titre", "Ma sortie");
        AddPointAt(rig, 0, "3", "1");

        rig.Click("Enregistrer");

        rig.Cut.WaitForAssertion(() => StringAssert.Contains(rig.Cut.Markup, "Programme enregistré dans ce navigateur"));
        var saved = rig.Store.Find("ma-sortie");
        Assert.IsNotNull(saved);
        Assert.AreEqual(new ProgramSegment(0, 3, 1), saved.Segments.Single());
        StringAssert.EndsWith(rig.Navigation.Uri, "editor?program=ma-sortie");

        rig.Click("Démarrer");
        StringAssert.EndsWith(rig.Navigation.Uri, "ride?program=ma-sortie");
    }

    [TestMethod]
    public void Existing_program_is_loaded_and_edited()
    {
        using var rig = Render("editor?program=blue-pools-nz");
        rig.Cut.WaitForAssertion(() => Assert.HasCount(BluePoolsPoints, rig.Cut.FindAll(".point")));

        rig.Cut.FindAll(".point")[1].QuerySelectorAll("input")[0].Change("3.4"); // 0:40 speed
        rig.Cut.FindAll(".point")[0].QuerySelectorAll("button").Last().Click(); // remove the 0:00 point
        rig.Cut.WaitForAssertion(() => Assert.HasCount(BluePoolsPoints - 1, rig.Cut.FindAll(".point")));
        AddPointAt(rig, 0, "1", "0"); // and add it back
        rig.Click("Enregistrer");

        rig.Cut.WaitForAssertion(() => StringAssert.Contains(rig.Cut.Markup, "Programme enregistré"));
        var saved = rig.Store.Find("blue-pools-nz")!;
        Assert.IsTrue(rig.Store.IsImported("blue-pools-nz"));
        Assert.AreEqual(new ProgramSegment(40, 3.4, 3), saved.Segments[1]);
        Assert.HasCount(BluePoolsPoints, saved.Segments);
    }

    [TestMethod]
    public void Video_duration_is_read_from_the_player()
    {
        using var rig = Render();
        rig.Ctx.Video.Duration = 3112.4;
        rig.Type("Lien ou identifiant YouTube", "KiEN4HxM4ic");
        rig.Click("Charger");

        rig.Ctx.Time.Advance(TimeSpan.FromMilliseconds(250)); // one refresh of the position

        rig.Cut.WaitForAssertion(() => StringAssert.Contains(rig.Cut.Markup, "Durée de la vidéo : 51:52"));
    }

    [TestMethod]
    public void Leaving_with_unsaved_changes_asks_first()
    {
        using var rig = Render();
        rig.Type("Titre", "Brouillon");

        rig.Dialogs.Answer = false;
        rig.Click("Retour");
        Assert.HasCount(1, rig.Dialogs.MessageBoxes);
        StringAssert.EndsWith(rig.Navigation.Uri, "editor");

        rig.Dialogs.Answer = true;
        rig.Click("Retour");
        StringAssert.EndsWith(rig.Navigation.Uri, "programs");
    }

    [TestMethod]
    public void Leaving_without_changes_does_not_ask()
    {
        using var rig = Render();

        rig.Click("Retour");

        Assert.IsEmpty(rig.Dialogs.MessageBoxes);
        StringAssert.EndsWith(rig.Navigation.Uri, "programs");
    }

    [TestMethod]
    public void Tapping_a_point_time_seeks_the_video()
    {
        using var rig = Render("editor?program=blue-pools-nz");
        rig.Cut.WaitForAssertion(() => Assert.HasCount(BluePoolsPoints, rig.Cut.FindAll(".point")));

        rig.Click("2:05");

        Assert.AreEqual(125, rig.Ctx.Video.CurrentTime);
    }
}
