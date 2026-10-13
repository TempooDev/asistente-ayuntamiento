using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using System.Text.Json;

namespace AsistenteAyuntamiento.Application.Features.Users;

public interface IHistoryAnalyzerService
{
    Task AnalyzeAndMergeUserHistoryAsync(string auth0UserId, CancellationToken cancellationToken = default);
}
