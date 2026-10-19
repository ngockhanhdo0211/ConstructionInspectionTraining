using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Identity;

public sealed class InspectionReviewerService(ApplicationDbContext context) : IInspectionReviewerService
{
    public async Task<ReviewerEligibility?> GetForAssignmentAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .FromSqlInterpolated($"SELECT * FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}")
            .AsNoTracking()
            .Select(x => new { x.Id, x.FullName, x.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;

        var hasReviewerRole = await (
            from userRole in context.UserRoles.AsNoTracking()
            join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.NormalizedName == ApplicationRoles.Reviewer
            select userRole.UserId).AnyAsync(cancellationToken);

        return new(user.Id, user.FullName, user.IsActive, hasReviewerRole);
    }

    public async Task<PagedResult<ReviewerOptionDto>> GetOptionsAsync(
        ReviewerOptionCriteria criteria, CancellationToken cancellationToken)
    {
        var query =
            from user in context.Users.AsNoTracking()
            join userRole in context.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.IsActive && role.NormalizedName == ApplicationRoles.Reviewer
            select user;

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var search = criteria.Search.Trim();
            query = query.Where(x => x.FullName.Contains(search)
                || (x.UserName != null && x.UserName.Contains(search)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .Skip((criteria.PageNumber - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(x => new ReviewerOptionDto(x.Id, x.FullName))
            .ToArrayAsync(cancellationToken);
        return new(items, criteria.PageNumber, criteria.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)criteria.PageSize));
    }
}
