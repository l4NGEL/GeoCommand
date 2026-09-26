namespace GeoCommand.Application.Abstractions;

public sealed class NotFoundException(string message) : Exception(message);
