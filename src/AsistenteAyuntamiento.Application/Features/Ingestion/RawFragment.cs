using System.Diagnostics;
using AsistenteAyuntamiento.Domain.Features.Ingestion;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.AI;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using Microsoft.EntityFrameworkCore;

namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class RawFragment
{
    public string Section { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
}
