namespace AsistenteAyuntamiento.Application.Features.Ingestion.DTOs;

public class BlobListStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Queued { get; set; }
    public int Completed { get; set; }
    public int Processing { get; set; }
}
