namespace AsistenteAyuntamiento.ApiService.Features.Notifications;

public partial class RabbitMqNotificationConsumer
{
    private class DocumentNotificationEvent
    {
        public string? DocumentId { get; set; }
        public string? NewStatus { get; set; }
    }
}
