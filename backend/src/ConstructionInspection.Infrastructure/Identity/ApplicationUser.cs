using Microsoft.AspNetCore.Identity;

namespace ConstructionInspection.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public required string FullName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
