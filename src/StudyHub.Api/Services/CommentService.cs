using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Contracts;
using StudyHub.Api.Data;
using StudyHub.Api.Domain;

namespace StudyHub.Api.Services;

public interface ICommentService
{
    Task<IReadOnlyList<CommentResponse>> ListAsync(Guid materialId, CancellationToken ct = default);
    Task<CommentResponse> AddAsync(Guid userId, Guid materialId, CommentRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid materialId, Guid commentId, CancellationToken ct = default);
}

public class CommentService(AppDbContext db) : ICommentService
{
    private const int MaxComments = 200;

    /// <summary>The comments under a note, oldest first.</summary>
    public async Task<IReadOnlyList<CommentResponse>> ListAsync(Guid materialId, CancellationToken ct = default)
    {
        await EnsureMaterialExistsAsync(materialId, ct);

        return await db.Comments
            .AsNoTracking()
            .Where(c => c.MaterialId == materialId)
            .OrderBy(c => c.CreatedAtUtc)
            .Take(MaxComments)
            .Select(c => new CommentResponse(c.Id, c.Body, c.AuthorId, c.Author.DisplayName, c.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<CommentResponse> AddAsync(
        Guid userId, Guid materialId, CommentRequest request, CancellationToken ct = default)
    {
        await EnsureMaterialExistsAsync(materialId, ct);

        var comment = new Comment
        {
            MaterialId = materialId,
            AuthorId = userId,
            Body = request.Body.Trim()
        };

        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);

        var authorName = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return new CommentResponse(comment.Id, comment.Body, comment.AuthorId, authorName, comment.CreatedAtUtc);
    }

    /// <summary>Only the person who wrote a comment can delete it.</summary>
    public async Task DeleteAsync(Guid userId, Guid materialId, Guid commentId, CancellationToken ct = default)
    {
        var comment = await db.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.MaterialId == materialId, ct)
            ?? throw new NotFoundException("Comment not found.");

        if (comment.AuthorId != userId)
        {
            throw new ForbiddenException("You can only delete your own comments.");
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureMaterialExistsAsync(Guid materialId, CancellationToken ct)
    {
        if (!await db.Materials.AnyAsync(m => m.Id == materialId, ct))
        {
            throw new NotFoundException("Material not found.");
        }
    }
}
