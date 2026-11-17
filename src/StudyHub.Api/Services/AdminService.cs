using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Services;

/// <summary>What a moderator can do: review reports, and remove notes or comments.</summary>
public interface IAdminService
{
    Task<IReadOnlyList<AdminReportResponse>> ListOpenReportsAsync(CancellationToken ct = default);
    Task DismissReportAsync(Guid reportId, CancellationToken ct = default);
    Task RemoveMaterialAsync(Guid materialId, CancellationToken ct = default);
    Task RemoveCommentAsync(Guid commentId, CancellationToken ct = default);
    Task<SiteStatsResponse> GetStatsAsync(DateTime now, CancellationToken ct = default);
}

public class AdminService(AppDbContext db) : IAdminService
{
    private const int MaxReports = 200;
    private const int PreviewLength = 300;

    /// <summary>Reports nobody has dealt with yet, newest first.</summary>
    public async Task<IReadOnlyList<AdminReportResponse>> ListOpenReportsAsync(CancellationToken ct = default)
    {
        return await db.Reports
            .AsNoTracking()
            .Where(r => r.Status == ReportStatus.Open)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(MaxReports)
            .Select(r => new AdminReportResponse(
                r.Id,
                r.Reason,
                r.CreatedAtUtc,
                db.Users.Where(u => u.Id == r.ReporterId).Select(u => u.DisplayName).FirstOrDefault() ?? "",
                r.MaterialId,
                r.Material.Title,
                r.Material.Author.DisplayName,
                r.Material.Body.Length > PreviewLength ? r.Material.Body.Substring(0, PreviewLength) : r.Material.Body))
            .ToListAsync(ct);
    }

    /// <summary>The note is fine: close the report and keep the note.</summary>
    public async Task DismissReportAsync(Guid reportId, CancellationToken ct = default)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Report not found.");

        report.Status = ReportStatus.Dismissed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes a note for everyone, along with its reports, comments and votes.</summary>
    public async Task RemoveMaterialAsync(Guid materialId, CancellationToken ct = default)
    {
        var material = await db.Materials
            .Include(m => m.Reports)
            .Include(m => m.Comments)
            .Include(m => m.HelpfulVotes)
            .FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new NotFoundException("Material not found.");

        db.Materials.Remove(material);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Totals for the whole site. Deliberately no individual students' results.</summary>
    public async Task<SiteStatsResponse> GetStatsAsync(DateTime now, CancellationToken ct = default)
    {
        var weekAgo = now.AddDays(-7);

        return new SiteStatsResponse(
            await db.Users.CountAsync(u => u.Email != SampleData.TeamEmail, ct),
            await db.Materials.CountAsync(ct),
            await db.Comments.CountAsync(ct),
            await db.Quizzes.CountAsync(ct),
            await db.GameSessions.CountAsync(ct),
            await db.GameResults.CountAsync(ct),
            await db.GameSessions.CountAsync(s => s.PlayedAtUtc >= weekAgo, ct),
            await db.GameResults.CountAsync(r => r.Session.PlayedAtUtc >= weekAgo, ct),
            await db.Reports.CountAsync(r => r.Status == ReportStatus.Open, ct));
    }

    public async Task RemoveCommentAsync(Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId, ct)
            ?? throw new NotFoundException("Comment not found.");

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
    }
}
