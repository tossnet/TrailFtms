using TrailFtms.Services.Programs;

namespace TrailFtms.Tests.Services.Programs;

[TestClass]
public sealed class ProgramDraftTests
{
    [TestMethod]
    public void Points_are_kept_sorted_one_per_second()
    {
        var draft = new ProgramDraft();

        Assert.AreEqual(0, draft.SetPoint(40.7, 3, 3));
        Assert.AreEqual(0, draft.SetPoint(0, 1, 0));
        Assert.AreEqual(2, draft.SetPoint(60.2, 3.2, 0));
        Assert.AreEqual(2, draft.SetPoint(50, 3, 1));

        CollectionAssert.AreEqual(new double[] { 0, 40, 50, 60 }, draft.Segments.Select(s => s.T).ToArray());
    }

    [TestMethod]
    public void A_point_at_the_same_second_replaces_the_existing_one()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(40.1, 3, 3);

        var index = draft.SetPoint(40.9, 4, 5);

        Assert.AreEqual(0, index);
        Assert.AreEqual(new ProgramSegment(40, 4, 5), draft.Segments.Single());
    }

    [TestMethod]
    public void Adding_the_same_point_twice_keeps_a_single_one()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(0, 1, 0);
        draft.SetPoint(40, 3, 3);

        var index = draft.SetPoint(52, 3, 3);

        Assert.AreEqual(1, index);
        CollectionAssert.AreEqual(new[] { new ProgramSegment(0, 1, 0), new ProgramSegment(40, 3, 3) }, draft.Segments.ToArray());
    }

    [TestMethod]
    public void A_point_inserted_before_an_identical_one_replaces_it()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(0, 1, 0);
        draft.SetPoint(40, 3, 3);

        var index = draft.SetPoint(30, 3, 3);

        Assert.AreEqual(1, index);
        CollectionAssert.AreEqual(new[] { new ProgramSegment(0, 1, 0), new ProgramSegment(30, 3, 3) }, draft.Segments.ToArray());
    }

    [TestMethod]
    public void ToProgram_drops_repeated_points()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(0, 1, 0);
        draft.SetPoint(40, 3, 3);
        draft.Update(1, 1, 0); // edited rows are left alone while editing

        Assert.HasCount(2, draft.Segments);
        Assert.AreEqual(new ProgramSegment(0, 1, 0), draft.ToProgram().Segments.Single());
    }

    [TestMethod]
    public void Values_are_rounded_to_one_decimal()
    {
        var draft = new ProgramDraft();

        draft.SetPoint(0, 3.14159, 2.06);

        Assert.AreEqual(new ProgramSegment(0, 3.1, 2.1), draft.Segments[0]);
    }

    [TestMethod]
    public void Update_and_remove()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(0, 1, 0);
        draft.SetPoint(40, 3, 3);

        draft.Update(1, 3.5, 4);
        draft.Remove(0);

        Assert.AreEqual(new ProgramSegment(40, 3.5, 4), draft.Segments.Single());
    }

    [TestMethod]
    public void ActiveAt_returns_the_segment_in_force()
    {
        var draft = new ProgramDraft();
        draft.SetPoint(10, 1, 0);
        draft.SetPoint(40, 3, 3);

        Assert.IsNull(draft.ActiveAt(5));
        Assert.AreEqual(10, draft.ActiveAt(39.9)!.Value.T);
        Assert.AreEqual(40, draft.ActiveAt(500)!.Value.T);
    }

    [TestMethod]
    public void Round_trip_through_a_program()
    {
        var program = new RideProgram
        {
            Id = "blue-pools-nz",
            Title = "Blue Pools",
            YoutubeId = "KiEN4HxM4ic",
            Author = "tossnet",
            DurationSeconds = 3112,
            Segments = [new(0, 1, 0), new(40, 3, 3)],
        };

        var copy = ProgramDraft.From(program).ToProgram();

        Assert.AreEqual(program with { Segments = [] }, copy with { Segments = [] });
        CollectionAssert.AreEqual(program.Segments.ToArray(), copy.Segments.ToArray());
    }

    [TestMethod]
    public void ToProgram_trims_and_drops_an_empty_author()
    {
        var draft = new ProgramDraft { Id = " my-run ", Title = " Ma sortie ", YoutubeId = " KiEN4HxM4ic ", Author = "  " };

        var program = draft.ToProgram();

        Assert.AreEqual("my-run", program.Id);
        Assert.AreEqual("Ma sortie", program.Title);
        Assert.AreEqual("KiEN4HxM4ic", program.YoutubeId);
        Assert.IsNull(program.Author);
    }

    [TestMethod]
    [DataRow("Tour du Mont Blanc, étape 1", "tour-du-mont-blanc-etape-1")]
    [DataRow("Blue Pools, Nouvelle-Zélande", "blue-pools-nouvelle-zelande")]
    [DataRow("  --Côte d'Azur !!  ", "cote-d-azur")]
    [DataRow("Ça grimpe : 15 %", "ca-grimpe-15")]
    [DataRow("", "")]
    public void Slugify_gives_a_valid_program_id(string title, string expected)
    {
        Assert.AreEqual(expected, ProgramDraft.Slugify(title));
    }
}
