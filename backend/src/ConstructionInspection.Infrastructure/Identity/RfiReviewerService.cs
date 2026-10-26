using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Rfis.Interfaces;
using ConstructionInspection.Application.Rfis.Models;
using ConstructionInspection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Identity;

public sealed class RfiReviewerService(ApplicationDbContext context) : IRfiReviewerService
{
    public async Task<RfiReviewerEligibility?> GetForAssignmentAsync(Guid id, CancellationToken ct)
    {
        var user = await context.Users
            .FromSqlInterpolated($"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking().Select(x => new { x.Id, x.FullName, x.IsActive }).SingleOrDefaultAsync(ct);
        if (user is null) return null;
        var reviewer = await (from ur in context.UserRoles.AsNoTracking()
                              join role in context.Roles.AsNoTracking() on ur.RoleId equals role.Id
                              where ur.UserId == id && role.NormalizedName == ApplicationRoles.Reviewer
                              select ur.UserId).AnyAsync(ct);
        return new(user.Id, user.FullName, user.IsActive, reviewer);
    }

    public async Task<PagedResult<RfiResponderOptionDto>> GetOptionsAsync(int pageNumber, int pageSize,
        string? search, CancellationToken ct)
    {
        var query = from user in context.Users.AsNoTracking()
                    join ur in context.UserRoles.AsNoTracking() on user.Id equals ur.UserId
                    join role in context.Roles.AsNoTracking() on ur.RoleId equals role.Id
                    where user.IsActive && role.NormalizedName == ApplicationRoles.Reviewer
                    select user;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.FullName.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new RfiResponderOptionDto(x.Id, x.FullName)).ToArrayAsync(ct);
        return new(items, pageNumber, pageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize));
    }
}
