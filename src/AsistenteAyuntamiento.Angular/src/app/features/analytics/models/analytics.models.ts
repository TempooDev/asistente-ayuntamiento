export interface AiModelConsumption {
  modelId: string;
  type: string;
  totalCalls: number;
  succeededCalls: number;
  averageDurationMs: number;
  totalTokens: number;
}

export interface AiMetricsSummary {
  generatedAtUtc: string;
  totalCalls: number;
  succeededCalls: number;
  failedCalls: number;
  successRate: number;
  averageDurationMs: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageTokensPerCall: number;
  consumptions: AiModelConsumption[];
}

export interface PipelineMetrics {
  pipeline: string;
  winCount: number;
  lossCount: number;
  tieCount: number;
  winRate: number;
  averageLatencyMs: number;
  averageTokens: number;
}

export interface ArenaAnalyticsRequest {
  startDate?: string;
  endDate?: string;
}

export interface ArenaAnalyticsResponse {
  totalBattles: number;
  pendingBattles: number;
  completedBattles: number;
  metrics: PipelineMetrics[];
}
