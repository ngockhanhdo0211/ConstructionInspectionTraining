using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjects;

namespace ConstructionInspection.Application.UnitTests.ConstructionObjects;

public sealed class ConstructionObjectQueryValidatorTests
{
    private readonly GetConstructionObjectsQueryValidator _validator = new();

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validate_WithInvalidPagination_Fails(int pageNumber, int pageSize)
    {
        var result = _validator.Validate(new GetConstructionObjectsQuery(
            PageNumber: pageNumber,
            PageSize: pageSize));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("parentId")]
    [InlineData("name;drop table")]
    [InlineData("")]
    public void Validate_WithSortOutsideWhitelist_Fails(string sortBy)
    {
        var result = _validator.Validate(new GetConstructionObjectsQuery(SortBy: sortBy));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithSpecificParentWithoutId_Fails()
    {
        var result = _validator.Validate(new GetConstructionObjectsQuery(
            ParentFilterMode: ParentFilterMode.Specific,
            ParentId: null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WithSupportedPaginationAndSort_Succeeds()
    {
        var result = _validator.Validate(new GetConstructionObjectsQuery(
            PageNumber: 2,
            PageSize: 100,
            SortBy: "updatedAt",
            SortDirection: "desc"));

        Assert.True(result.IsValid);
    }
}
