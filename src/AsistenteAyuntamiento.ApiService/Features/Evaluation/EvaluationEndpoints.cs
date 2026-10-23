using AsistenteAyuntamiento.ApiService.Security;
using AsistenteAyuntamiento.Application.Features.Evaluation;

namespace AsistenteAyuntamiento.ApiService.Features.Evaluation;

public static class EvaluationEndpoints
{
    /// <summary>Offline evaluation results: platform data (shared corpus and pipelines), platform administrators only.</summary>
    public static void MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evaluation").RequireAuthorization(Policies.PlatformAdmin);

        group.MapGet("/runs", async (IEvaluationResultsService results, CancellationToken cancellationToken) =>
            Results.Ok(await results.ListRunsAsync(cancellationToken)))
        .WithName("ListEvaluationRuns");

        group.MapGet("/runs/{runId}", async (string runId, IEvaluationResultsService results, CancellationToken cancellationToken) =>
            await results.GetRunAsync(runId, cancellationToken) is { } run ? Results.Ok(run) : Results.NotFound())
        .WithName("GetEvaluationRun");
    }
}
