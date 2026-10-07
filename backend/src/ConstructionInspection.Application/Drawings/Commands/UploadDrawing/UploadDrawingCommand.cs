using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Commands.UploadDrawing;

public sealed record UploadDrawingCommand(string OriginalFileName, Stream Content) : IRequest<UploadDrawingResponse>;
