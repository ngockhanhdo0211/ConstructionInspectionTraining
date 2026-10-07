using FluentValidation;

namespace ConstructionInspection.Application.Drawings.Commands.UploadDrawing;

public sealed class UploadDrawingCommandValidator : AbstractValidator<UploadDrawingCommand>
{
    public UploadDrawingCommandValidator()
    {
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Content).NotNull().Must(x => x.CanRead).WithMessage("The uploaded file must be readable.");
    }
}
