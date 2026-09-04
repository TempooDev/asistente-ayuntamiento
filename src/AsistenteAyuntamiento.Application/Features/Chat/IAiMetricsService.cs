using System;
using System.Collections.Generic;

namespace AsistenteAyuntamiento.Application.Features.Chat;

public interface IAiMetricsService
{
    void RecordCall(AiCallRecord record);
    AiMetricsSnapshot GetSnapshot();
}
