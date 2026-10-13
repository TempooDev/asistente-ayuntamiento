using System.Text.Json;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.Application.Features.Users;

public interface IHistoryAnalyzerService
{
    Task AnalyzeAndMergeUserHistoryAsync(string auth0UserId, CancellationToken cancellationToken = default);
}
