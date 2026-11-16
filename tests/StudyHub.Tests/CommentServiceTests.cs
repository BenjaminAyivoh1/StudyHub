using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class CommentServiceTests
{
    private static MaterialRequest Note(string title, string? body = null)
        => new(title, body ?? "Some useful notes for revision before the exam.", "CSC 301", new List<string>());

    [Fact]
    public async Task Adding_a_comment_saves_it_with_the_author_name()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var note = await new MaterialService(db).CreateAsync(author.Id, Note("Scheduling notes"));
        var comments = new CommentService(db);

        var added = await comments.AddAsync(reader.Id, note.Id, new CommentRequest("  Is Round Robin preemptive?  "));

        Assert.Equal("Is Round Robin preemptive?", added.Body);
        Assert.Equal("Kofi", added.AuthorName);
        Assert.Equal(reader.Id, added.AuthorId);

        var listed = await comments.ListAsync(note.Id);
        Assert.Equal(added.Id, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task Comments_are_listed_oldest_first()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var note = await new MaterialService(db).CreateAsync(author.Id, Note("Scheduling notes"));
        var comments = new CommentService(db);

        var second = await comments.AddAsync(author.Id, note.Id, new CommentRequest("Written second"));
        var first = await comments.AddAsync(author.Id, note.Id, new CommentRequest("Written first"));

        // Move one comment back in time so the order doesn't depend on the clock.
        var stored = db.Comments.Single(c => c.Id == first.Id);
        stored.CreatedAtUtc = stored.CreatedAtUtc.AddHours(-1);
        await db.SaveChangesAsync();

        var listed = await comments.ListAsync(note.Id);

        Assert.Equal(2, listed.Count);
        Assert.Equal(first.Id, listed[0].Id);
        Assert.Equal(second.Id, listed[1].Id);
    }

    [Fact]
    public async Task Only_the_comment_author_can_delete_it()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var note = await new MaterialService(db).CreateAsync(author.Id, Note("Scheduling notes"));
        var comments = new CommentService(db);
        var added = await comments.AddAsync(reader.Id, note.Id, new CommentRequest("A question about this"));

        await Assert.ThrowsAsync<ForbiddenException>(() => comments.DeleteAsync(author.Id, note.Id, added.Id));
        await comments.DeleteAsync(reader.Id, note.Id, added.Id);

        Assert.Empty(await comments.ListAsync(note.Id));
    }

    [Fact]
    public async Task Commenting_on_a_note_that_does_not_exist_is_not_found()
    {
        await using var db = TestDb.Create();
        var reader = await TestDb.AddUserAsync(db);
        var comments = new CommentService(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            comments.AddAsync(reader.Id, Guid.NewGuid(), new CommentRequest("Hello there")));
        await Assert.ThrowsAsync<NotFoundException>(() => comments.ListAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            comments.DeleteAsync(reader.Id, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task The_notes_list_shows_a_preview_and_the_comment_count()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var materials = new MaterialService(db);
        var longBody = new string('a', 500);
        var longNote = await materials.CreateAsync(author.Id, Note("A long note", longBody));
        await materials.CreateAsync(author.Id, Note("A short note", "Short and sweet notes."));
        var comments = new CommentService(db);
        await comments.AddAsync(author.Id, longNote.Id, new CommentRequest("First comment"));
        await comments.AddAsync(author.Id, longNote.Id, new CommentRequest("Second comment"));

        var list = await materials.SearchAsync(new MaterialQuery());
        var listedLong = list.Items.Single(n => n.Title == "A long note");
        var listedShort = list.Items.Single(n => n.Title == "A short note");

        Assert.Equal(200, listedLong.Preview.Length);
        Assert.Equal(2, listedLong.CommentCount);
        Assert.Equal("Short and sweet notes.", listedShort.Preview);
        Assert.Equal(0, listedShort.CommentCount);
    }
}
