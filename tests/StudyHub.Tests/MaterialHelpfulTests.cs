using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class MaterialHelpfulTests
{
    private static MaterialRequest Note(string title)
        => new(title, "Some useful notes for revision before the exam.", "DCIT 301", new List<string>());

    [Fact]
    public async Task Marking_a_note_helpful_raises_the_count_and_remembers_who_did_it()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var note = await service.CreateAsync(author.Id, Note("Scheduling notes"));

        var result = await service.MarkHelpfulAsync(reader.Id, note.Id);

        Assert.Equal(1, result.HelpfulCount);
        Assert.True(result.MarkedHelpfulByMe);

        var seenByReader = await service.GetAsync(note.Id, reader.Id);
        var seenByVisitor = await service.GetAsync(note.Id);
        Assert.True(seenByReader.MarkedHelpfulByMe);
        Assert.False(seenByVisitor.MarkedHelpfulByMe);
        Assert.Equal(1, seenByVisitor.HelpfulCount);
    }

    [Fact]
    public async Task Marking_twice_only_counts_once()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var note = await service.CreateAsync(author.Id, Note("Scheduling notes"));

        await service.MarkHelpfulAsync(reader.Id, note.Id);
        var second = await service.MarkHelpfulAsync(reader.Id, note.Id);

        Assert.Equal(1, second.HelpfulCount);
    }

    [Fact]
    public async Task Authors_cannot_mark_their_own_note_helpful()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);
        var note = await service.CreateAsync(author.Id, Note("Scheduling notes"));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkHelpfulAsync(author.Id, note.Id));
    }

    [Fact]
    public async Task Unmarking_lowers_the_count_and_is_safe_to_repeat()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var note = await service.CreateAsync(author.Id, Note("Scheduling notes"));
        await service.MarkHelpfulAsync(reader.Id, note.Id);

        var first = await service.UnmarkHelpfulAsync(reader.Id, note.Id);
        var second = await service.UnmarkHelpfulAsync(reader.Id, note.Id);

        Assert.Equal(0, first.HelpfulCount);
        Assert.False(first.MarkedHelpfulByMe);
        Assert.Equal(0, second.HelpfulCount);
    }

    [Fact]
    public async Task Marking_a_note_that_does_not_exist_is_not_found()
    {
        await using var db = TestDb.Create();
        var reader = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkHelpfulAsync(reader.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UnmarkHelpfulAsync(reader.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Sorting_by_helpful_puts_the_most_helpful_note_first()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var kofi = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var esi = await TestDb.AddUserAsync(db, "esi@example.com", "Esi");
        var service = new MaterialService(db);

        var older = await service.CreateAsync(author.Id, Note("Older but loved"));
        var newer = await service.CreateAsync(author.Id, Note("Newer with no votes"));
        await service.MarkHelpfulAsync(kofi.Id, older.Id);
        await service.MarkHelpfulAsync(esi.Id, older.Id);

        var byHelpful = await service.SearchAsync(new MaterialQuery(Sort: "helpful"));

        Assert.Equal("Older but loved", byHelpful.Items[0].Title);
        Assert.Equal(2, byHelpful.Items[0].HelpfulCount);
        Assert.Equal(0, byHelpful.Items[1].HelpfulCount);
        Assert.Equal(newer.Id, byHelpful.Items[1].Id);
    }
}
