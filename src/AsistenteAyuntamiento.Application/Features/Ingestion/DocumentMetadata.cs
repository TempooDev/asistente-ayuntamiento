namespace AsistenteAyuntamiento.Application.Features.Ingestion;

public class DocumentMetadata
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("titulo")]
    public string Title { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("departamento")]
    public string Department { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("fecha_publicacion")]
    public string PublicationDate { get; set; } = string.Empty;
}
