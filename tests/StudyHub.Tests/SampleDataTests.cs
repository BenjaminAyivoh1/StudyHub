using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;
using StudyHub.Api.Services;

namespace StudyHub.Tests;

public class SampleDataTests
{
    [Fact]
    public async Task Seeding_adds_the_starter_notes_under_the_team_account()
    {
        await using var db = TestDb.Create();

        var added = await SampleData.SeedAsync(db, new Pbkdf2PasswordHasher());

        Assert.Equal(SampleData.TotalNotes, added);
        Assert.Equal(SampleData.TotalNotes, await db.Materials.CountAsync());
        Assert.True(SampleData.TotalNotes >= 30);

        var team = await db.Users.SingleAsync();
        Assert.Equal(SampleData.TeamName, team.DisplayName);

        // Starter notes come with no votes or comments: only real students add those.
        Assert.Equal(0, await db.HelpfulVotes.CountAsync());
        Assert.Equal(0, await db.Comments.CountAsync());
    }

    [Fact]
    public async Task Seeding_twice_does_not_add_the_notes_again()
    {
        await using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();

        await SampleData.SeedAsync(db, hasher);
        var second = await SampleData.SeedAsync(db, hasher);

        Assert.Equal(0, second);
        Assert.Equal(SampleData.TotalNotes, await db.Materials.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Seeding_reuses_tags_that_already_exist()
    {
        await using var db = TestDb.Create();
        db.Tags.Add(new Tag { Name = "os" });
        await db.SaveChangesAsync();

        await SampleData.SeedAsync(db, new Pbkdf2PasswordHasher());

        Assert.Equal(1, await db.Tags.CountAsync(t => t.Name == "os"));
    }

    [Fact]
    public async Task Starter_notes_are_valid_and_searchable()
    {
        await using var db = TestDb.Create();
        await SampleData.SeedAsync(db, new Pbkdf2PasswordHasher());
        var service = new MaterialService(db);

        var osNotes = await service.SearchAsync(new MaterialQuery(Tag: "os"));
        var everything = await service.SearchAsync(new MaterialQuery(PageSize: 50));

        Assert.Equal(3, osNotes.TotalCount);
        Assert.All(everything.Items, note =>
        {
            Assert.InRange(note.Title.Length, 3, 200);
            Assert.InRange(note.Tags.Count, 1, 5);
            Assert.False(string.IsNullOrWhiteSpace(note.Preview));
            Assert.Null(note.CourseCode); // organised by topic, not by course code
            Assert.Equal(SampleData.TeamName, note.AuthorName);
        });
    }

    [Fact]
    public async Task Seeding_again_clears_course_codes_left_on_starter_notes()
    {
        await using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();
        await SampleData.SeedAsync(db, hasher);
        foreach (var note in db.Materials)
        {
            note.CourseCode = "CSC 301";
        }
        await db.SaveChangesAsync();

        await SampleData.SeedAsync(db, hasher);

        Assert.Equal(0, await db.Materials.CountAsync(m => m.CourseCode != null));
        Assert.Equal(SampleData.TotalNotes, await db.Materials.CountAsync());
    }

    [Fact]
    public async Task A_site_set_up_earlier_receives_the_newer_batch_of_notes()
    {
        await using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();
        await SampleData.SeedAsync(db, hasher);

        // Make it look like a site that was set up before the second batch existed.
        var newer = SampleData.BatchTitles[1];
        db.Materials.RemoveRange(db.Materials.Where(m => newer.Contains(m.Title)));
        await db.SaveChangesAsync();

        var added = await SampleData.SeedAsync(db, hasher);

        Assert.Equal(newer.Count, added);
        Assert.Equal(SampleData.TotalNotes, await db.Materials.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task A_starter_note_removed_by_an_admin_does_not_come_back()
    {
        await using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();
        await SampleData.SeedAsync(db, hasher);

        var removed = await db.Materials.FirstAsync(m => m.Title == SampleData.BatchTitles[1][0]);
        db.Materials.Remove(removed);
        await db.SaveChangesAsync();

        var added = await SampleData.SeedAsync(db, hasher);

        Assert.Equal(0, added);
        Assert.False(await db.Materials.AnyAsync(m => m.Title == removed.Title));
    }

    [Fact]
    public async Task Starter_notes_cover_subjects_beyond_computing()
    {
        await using var db = TestDb.Create();
        await SampleData.SeedAsync(db, new Pbkdf2PasswordHasher());
        var service = new MaterialService(db);

        foreach (var tag in new[] { "biology", "chemistry", "physics", "maths", "economics", "accounting", "education", "history" })
        {
            var found = await service.SearchAsync(new MaterialQuery(Tag: tag));
            Assert.True(found.TotalCount > 0, $"expected a starter note tagged {tag}");
        }

        // Every title is different, so batches can be told apart.
        var titles = SampleData.BatchTitles.SelectMany(batch => batch).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    [Theory]
    [InlineData("postgres://ama:s3cret@db.example.com:6543/studyhub", "db.example.com", 6543, "studyhub", "ama", "s3cret")]
    [InlineData("postgresql://kofi:p%40ss@host.example.com/notes?sslmode=require", "host.example.com", 5432, "notes", "kofi", "p@ss")]
    public void Database_urls_are_converted_to_connection_strings(
        string url, string host, int port, string database, string user, string password)
    {
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString(url));

        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
        Assert.Equal(database, parsed.Database);
        Assert.Equal(user, parsed.Username);
        Assert.Equal(password, parsed.Password);
        Assert.Equal(Npgsql.SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void Ordinary_connection_strings_are_left_alone()
    {
        const string plain = "Host=localhost;Port=5433;Database=studyhub;Username=studyhub;Password=studyhub_dev";

        Assert.Equal(plain, DatabaseUrl.ToConnectionString(plain));
    }
}
