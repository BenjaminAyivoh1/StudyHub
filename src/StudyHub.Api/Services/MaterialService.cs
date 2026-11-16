using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Services;

public interface IMaterialService
{
    Task<PagedResult<MaterialSummary>> SearchAsync(MaterialQuery query, CancellationToken ct = default);
    Task<MaterialResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<MaterialResponse> CreateAsync(Guid userId, MaterialRequest request, CancellationToken ct = default);
    Task<MaterialResponse> UpdateAsync(Guid userId, Guid id, MaterialRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task ReportAsync(Guid userId, Guid id, ReportRequest request, CancellationToken ct = default);
}

public class MaterialService(AppDbContext db) : IMaterialService
{
    private const int MaxPageSize = 50;

    public async Task<PagedResult<MaterialSummary>> SearchAsync(MaterialQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, MaxPageSize);

        IQueryable<Material> query = db.Materials.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Simple case-insensitive match for now. Week 3 upgrades this to PostgreSQL full-text search.
            var term = q.Search.Trim().ToLowerInvariant();
            query = query.Where(m => m.Title.ToLower().Contains(term) || m.Body.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(q.Tag))
        {
            var tag = NormalizeTag(q.Tag);
            query = query.Where(m => m.Tags.Any(t => t.Name == tag));
        }

        var courseCode = NormalizeCourseCode(q.CourseCode);
        if (courseCode is not null)
        {
            query = query.Where(m => m.CourseCode == courseCode);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MaterialSummary(
                m.Id,
                m.Title,
                m.CourseCode,
                m.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
                m.Author.DisplayName,
                m.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<MaterialSummary>(items, page, pageSize, totalCount);
    }

    public async Task<MaterialResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var material = await db.Materials
            .AsNoTracking()
            .Include(m => m.Tags)
            .Include(m => m.Author)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Material not found.");

        return ToResponse(material);
    }

    public async Task<MaterialResponse> CreateAsync(Guid userId, MaterialRequest request, CancellationToken ct = default)
    {
        var material = new Material
        {
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            CourseCode = NormalizeCourseCode(request.CourseCode),
            AuthorId = userId,
            Tags = await ResolveTagsAsync(request.Tags, ct)
        };

        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);

        return await GetAsync(material.Id, ct);
    }

    public async Task<MaterialResponse> UpdateAsync(Guid userId, Guid id, MaterialRequest request, CancellationToken ct = default)
    {
        var material = await db.Materials
            .Include(m => m.Tags)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Material not found.");

        if (material.AuthorId != userId)
        {
            throw new ForbiddenException("You can only edit your own materials.");
        }

        material.Title = request.Title.Trim();
        material.Body = request.Body.Trim();
        material.CourseCode = NormalizeCourseCode(request.CourseCode);
        material.UpdatedAtUtc = DateTime.UtcNow;

        var tags = await ResolveTagsAsync(request.Tags, ct);
        material.Tags.Clear();
        material.Tags.AddRange(tags);

        await db.SaveChangesAsync(ct);

        return await GetAsync(material.Id, ct);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Material not found.");

        if (material.AuthorId != userId)
        {
            throw new ForbiddenException("You can only delete your own materials.");
        }

        db.Materials.Remove(material);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReportAsync(Guid userId, Guid id, ReportRequest request, CancellationToken ct = default)
    {
        if (!await db.Materials.AnyAsync(m => m.Id == id, ct))
        {
            throw new NotFoundException("Material not found.");
        }

        var alreadyReported = await db.Reports.AnyAsync(
            r => r.MaterialId == id && r.ReporterId == userId && r.Status == ReportStatus.Open, ct);
        if (alreadyReported)
        {
            throw new ConflictException("You have already reported this material.");
        }

        db.Reports.Add(new Report
        {
            MaterialId = id,
            ReporterId = userId,
            Reason = request.Reason.Trim()
        });

        await db.SaveChangesAsync(ct);
    }

    // ----- helpers -----

    /// <summary>Finds existing tags and creates the ones that don't exist yet.</summary>
    private async Task<List<Tag>> ResolveTagsAsync(IEnumerable<string>? names, CancellationToken ct)
    {
        var normalized = (names ?? Enumerable.Empty<string>())
            .Select(NormalizeTag)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
        {
            return new List<Tag>();
        }

        var existing = await db.Tags.Where(t => normalized.Contains(t.Name)).ToListAsync(ct);

        var missing = normalized
            .Except(existing.Select(t => t.Name))
            .Select(name => new Tag { Name = name })
            .ToList();

        db.Tags.AddRange(missing);
        existing.AddRange(missing);

        return existing;
    }

    private static string NormalizeTag(string? tag)
    {
        var value = (tag ?? string.Empty).Trim().ToLowerInvariant();
        return value.Length > 50 ? value[..50] : value;
    }

    private static string? NormalizeCourseCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
    }

    private static MaterialResponse ToResponse(Material m) => new(
        m.Id,
        m.Title,
        m.Body,
        m.CourseCode,
        m.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
        m.AuthorId,
        m.Author.DisplayName,
        m.CreatedAtUtc,
        m.UpdatedAtUtc);
}

