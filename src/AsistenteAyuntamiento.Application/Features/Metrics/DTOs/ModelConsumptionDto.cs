namespace AsistenteAyuntamiento.Application.Features.Metrics.DTOs;

/// <summary>Usage of one model for one step (<see cref="Type"/>: "Respuesta", "Expansión de consulta"…) by the current tenant.</summary>
public sealed record ModelConsumptionDto(
    string ModelId,
    string Type,
    int TotalCalls,
    int SucceededCalls,
    double AverageDurationMs,
    long TotalTokens,
    decimal CostEur = 0);
