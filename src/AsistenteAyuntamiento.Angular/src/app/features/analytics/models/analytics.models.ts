export interface AiModelConsumption {
  modelId: string;
  type: string;
  totalCalls: number;
  succeededCalls: number;
  averageDurationMs: number;
  totalTokens: number;
}

/** Chat usage of the caller's organization. `successRate` is already a percentage (0–100). */
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

/** Platform-wide ingestion cost (platform administrators only). */
export interface IngestionUsage {
  generatedAtUtc: string;
  documents: number;
  enrichmentCalls: number;
  enrichmentTokens: number;
  embeddedTokens: number;
  averageProcessingDurationMs: number;
}

export interface PipelineMetrics {
  pipeline: string;
  winCount: number;
  lossCount: number;
  tieCount: number;
  bothBadCount: number;
  /** Wins / (wins + losses), 0–1: ties and "both bad" votes are not counted. */
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
