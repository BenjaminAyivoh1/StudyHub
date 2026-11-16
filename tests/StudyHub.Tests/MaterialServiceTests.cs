using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class MaterialServiceTests
{
    private static MaterialRequest Request(
        string title = "Operating Systems scheduling notes",
        string body = "FCFS, SJF and Round Robin explained with small examples.",
        string? courseCode = "dcit 301",
        params string[] tags)
        => new(title, body, courseCode, tags.ToList());

    [Fact]
    public async Task Create_normalizes_course_code_and_tags()
    {
        await using var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);

        var created = await service.CreateAsync(
            user.Id, Request(tags: new[] { " OS ", "Notes", "os", "" }));

        Assert.Equal("DCIT 301", created.CourseCode);
        Assert.Equal(new[] { "notes", "os" }, created.Tags);
        Assert.Equal("Ama", created.AuthorName);
    }

    [Fact]
    public async Task Create_reuses_existing_tags_instead_of_duplicating_them()
    {
        await using var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);

        await service.CreateAsync(user.Id, Request(title: "First note title", tags: new[] { "os" }));
        await service.CreateAsync(user.Id, Request(title: "Second note title", tags: new[] { "os" }));

        Assert.Single(db.Tags);
    }

    [Fact]
    public async Task Search_finds_materials_by_text_tag_and_course()
    {
        await using var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);

        await service.CreateAsync(user.Id, Request(
            title: "Scheduling cheat sheet", courseCode: "DCIT 301", tags: new[] { "os" }));
        await service.CreateAsync(user.Id, Request(
            title: "HTML and CSS basics", body: "Tags, selectors and the box model explained.",
            courseCode: "DCIT 305", tags: new[] { "web" }));

        var byText = await service.SearchAsync(new MaterialQuery(Search: "scheduling"));
        var byTag = await service.SearchAsync(new MaterialQuery(Tag: "WEB"));
        var byCourse = await service.SearchAsync(new MaterialQuery(CourseCode: "dcit 301"));

        Assert.Equal("Scheduling cheat sheet", Assert.Single(byText.Items).Title);
        Assert.Equal("HTML and CSS basics", Assert.Single(byTag.Items).Title);
        Assert.Equal("Scheduling cheat sheet", Assert.Single(byCourse.Items).Title);
    }

    [Fact]
    public async Task Search_paginates_and_clamps_the_page_size()
    {
        await using var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);

        for (var i = 1; i <= 5; i++)
        {
            await service.CreateAsync(user.Id, Request(title: $"Note number {i}"));
        }

        var firstPage = await service.SearchAsync(new MaterialQuery(Page: 1, PageSize: 2));
        var lastPage = await service.SearchAsync(new MaterialQuery(Page: 3, PageSize: 2));
        var huge = await service.SearchAsync(new MaterialQuery(PageSize: 10_000));

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(lastPage.Items);
        Assert.Equal(50, huge.PageSize);
    }

    [Fact]
    public async Task Update_by_the_author_changes_the_material()
    {
        await using var db = TestDb.Create();
        var user = await TestDb.AddUserAsync(db);
        var service = new MaterialService(db);
        var created = await service.CreateAsync(user.Id, Request(tags: new[] { "os" }));

        var updated = await service.UpdateAsync(
            user.Id, created.Id, Request(title: "A better title", tags: new[] { "scheduling" }));

        Assert.Equal("A better title", updated.Title);
        Assert.Equal(new[] { "scheduling" }, updated.Tags);
    }

    [Fact]
    public async Task Update_by_someone_else_is_forbidden()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var created = await service.CreateAsync(author.Id, Request());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(other.Id, created.Id, Request(title: "Hijacked title")));
    }

    [Fact]
    public async Task Delete_by_the_author_removes_it_and_by_someone_else_is_forbidden()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var other = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var created = await service.CreateAsync(author.Id, Request());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(other.Id, created.Id));
        await service.DeleteAsync(author.Id, created.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created.Id));
    }

    [Fact]
    public async Task Get_throws_not_found_for_an_unknown_id()
    {
        await using var db = TestDb.Create();
        var service = new MaterialService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Report_is_saved_and_a_second_open_report_from_the_same_user_conflicts()
    {
        await using var db = TestDb.Create();
        var author = await TestDb.AddUserAsync(db);
        var reporter = await TestDb.AddUserAsync(db, "kofi@example.com", "Kofi");
        var service = new MaterialService(db);
        var created = await service.CreateAsync(author.Id, Request());

        await service.ReportAsync(reporter.Id, created.Id, new ReportRequest("Looks copied from a textbook."));

        Assert.Single(db.Reports);
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReportAsync(reporter.Id, created.Id, new ReportRequest("Reporting again.")));
    }
}
