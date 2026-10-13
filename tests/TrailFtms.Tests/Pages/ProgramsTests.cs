using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using TrailFtms.Pages;
using TrailFtms.Services.Programs;
using TrailFtms.Tests.Fakes;

namespace TrailFtms.Tests.Pages;

[TestClass]
public sealed class ProgramsTests
{
    private static void Upload(IRenderedComponent<TrailFtms.Pages.Programs> cut, string json, string name = "program.json") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(json, name));

    [TestMethod]
    public void Lists_shipped_programs()
    {
        using var ctx = new AppTestContext();

        var cut = ctx.Render<TrailFtms.Pages.Programs>();

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Blue Pools, Nouvelle-Zélande"));
        StringAssert.Contains(cut.Markup, "10 points, durée 51:52, par tossnet");
        StringAssert.Contains(cut.Markup, "(fourni)");
    }

    [TestMethod]
    public void Ride_button_opens_the_program()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<TrailFtms.Pages.Programs>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Blue Pools"));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Rouler").Click();

        StringAssert.EndsWith(ctx.Services.GetRequiredService<NavigationManager>().Uri, "ride?program=blue-pools-nz");
    }

    [TestMethod]
    public void Valid_file_is_imported_and_listed()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<TrailFtms.Pages.Programs>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Blue Pools"));

        Upload(cut, """
            { "id": "my-run", "title": "Ma sortie", "youtubeId": "KiEN4HxM4ic",
              "segments": [ { "t": 0, "speed": 3, "incline": 1 } ] }
            """);

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Programme « Ma sortie » importé."));
        StringAssert.Contains(cut.Markup, "(importé)");
        Assert.IsTrue(cut.FindAll("button").Any(b => b.TextContent.Trim() == "Supprimer"));
    }

    [TestMethod]
    public void Invalid_file_lists_explicit_errors()
    {
        using var ctx = new AppTestContext();
        var cut = ctx.Render<TrailFtms.Pages.Programs>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Blue Pools"));

        Upload(cut, """
            { "id": "bad", "title": "Bad", "youtubeId": "KiEN4HxM4ic",
              "segments": [ { "t": 0, "speed": 3, "incline": 1 }, { "t": 125, "speed": 50, "incline": 1 } ] }
            """, "bad.json");

        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Le fichier « bad.json » n'est pas un programme valide"));
        StringAssert.Contains(cut.Markup, "vitesse hors limites à 2:05 (0 à 30 km/h)");
        Assert.IsNull(ctx.Services.GetRequiredService<ProgramStore>().Find("bad"));
    }
}
