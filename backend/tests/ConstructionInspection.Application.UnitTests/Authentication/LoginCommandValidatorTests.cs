using ConstructionInspection.Application.Authentication.Commands.Login;

namespace ConstructionInspection.Application.UnitTests.Authentication;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Theory]
    [InlineData("", "password")]
    [InlineData("not-an-email", "password")]
    [InlineData("user@example.test", "")]
    public async Task ValidateAsync_WithInvalidInput_ReturnsErrors(string email, string password)
    {
        var result = await _validator.ValidateAsync(new LoginCommand(email, password));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithValidInput_Succeeds()
    {
        var result = await _validator.ValidateAsync(
            new LoginCommand("user@example.test", "password"));

        Assert.True(result.IsValid);
    }
}
