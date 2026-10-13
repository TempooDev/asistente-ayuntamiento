using System.Diagnostics;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class RawFragment
{
    public string Section { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
}
