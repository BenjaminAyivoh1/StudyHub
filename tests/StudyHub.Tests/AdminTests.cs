using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class AdminTests
{
    private static MaterialRequest Note(string title)
        => new(title, "Some useful notes for revision before the exam.", null, new List<string>());

    [Fact]
    public async Task Open_reports_are_listed_with_the_note_and_the_people_involved()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reporter = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var materials = new MaterialService(db);
        var note = await materials.CreateAsync(author.Id, Note("Scheduling notes"));
        await materials.ReportAsync(reporter.Id, note.Id, new ReportRequest("Looks copied from a textbook."));

        var reports = await new AdminService(db).ListOpenReportsAsync();

        var report = Assert.Single(reports);
        Assert.Equal("Looks copied from a textbook.", report.Reason);
        Assert.Equal("Kofi", report.ReporterName);
        Assert.Equal(note.Id, report.MaterialId);
        Assert.Equal("Scheduling notes", report.MaterialTitle);
        Assert.Equal("Ama", report.MaterialAuthorName);
        Assert.StartsWith("Some useful notes", report.MaterialPreview);
    }

    [Fact]
    public async Task Dismissing_a_report_keeps_the_note_and_clears_the_report_from_the_list()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reporter = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var materials = new MaterialService(db);
        var note = await materials.CreateAsync(author.Id, Note("Scheduling notes"));
        await materials.ReportAsync(reporter.Id, note.Id, new ReportRequest("Not study material."));
        var admin = new AdminService(db);
        var reportId = (await admin.ListOpenReportsAsync())[0].Id;

        await admin.DismissReportAsync(reportId);

        Assert.Empty(await admin.ListOpenReportsAsync());
        Assert.Equal(ReportStatus.Dismissed, (await db.Reports.SingleAsync()).Status);
        Assert.Equal("Scheduling notes", (await materials.GetAsync(note.Id)).Title);
    }

    [Fact]
    public async Task Removing_a_note_also_removes_its_reports_comments_and_votes()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var materials = new MaterialService(db);
        var note = await materials.CreateAsync(author.Id, Note("A note that breaks the rules"));
        var kept = await materials.CreateAsync(author.Id, Note("A good note"));
        await materials.ReportAsync(reader.Id, note.Id, new ReportRequest("Offensive content."));
        await materials.MarkHelpfulAsync(reader.Id, note.Id);
        await new CommentService(db).AddAsync(reader.Id, note.Id, new CommentRequest("This should not be here."));

        await new AdminService(db).RemoveMaterialAsync(note.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => materials.GetAsync(note.Id));
        Assert.Equal(0, await db.Reports.CountAsync());
        Assert.Equal(0, await db.Comments.CountAsync());
        Assert.Equal(0, await db.HelpfulVotes.CountAsync());
        Assert.Equal("A good note", (await materials.GetAsync(kept.Id)).Title);
    }

    [Fact]
    public async Task An_admin_can_remove_any_comment()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reader = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var note = await new MaterialService(db).CreateAsync(author.Id, Note("Scheduling notes"));
        var comments = new CommentService(db);
        var comment = await comments.AddAsync(reader.Id, note.Id, new CommentRequest("Something rude."));

        await new AdminService(db).RemoveCommentAsync(comment.Id);

        Assert.Empty(await comments.ListAsync(note.Id));
    }

    [Fact]
    public async Task Acting_on_something_that_does_not_exist_is_not_found()
    {
        await using var db = TestDb.Create();
        var admin = new AdminService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => admin.DismissReportAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.RemoveMaterialAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => admin.RemoveCommentAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("benjamin@example.com", true)]
    [InlineData("  BENJAMIN@Example.com ", true)]
    [InlineData("second.admin@example.com", true)]
    [InlineData("someone.else@example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_listed_emails_are_admins(string? email, bool expected)
    {
        var options = new AdminOptions { Emails = "benjamin@example.com, Second.Admin@example.com" };

        Assert.Equal(expected, options.IsAdmin(email));
    }

    [Fact]
    public void Nobody_is_an_admin_when_no_emails_are_set()
    {
        Assert.False(new AdminOptions().IsAdmin("benjamin@example.com"));
    }
}
