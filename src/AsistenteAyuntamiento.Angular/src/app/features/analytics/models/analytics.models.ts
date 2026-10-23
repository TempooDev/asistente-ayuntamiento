/** Metrics period: UTC dates, `to` exclusive. */
export interface DateRange {
  from: string;
  to: string;
}

export interface AiModelConsumption {
  modelId: string;
  type: string;
  totalCalls: number;
  succeededCalls: number;
  averageDurationMs: number;
  totalTokens: number;
  costEur: number;
}

export interface AiCostByStep {
  step: string;
  tokens: number;
  costEur: number;
}

export interface AiDailyUsage {
  date: string;
  answers: number;
  failed: number;
  latencyP95Ms: number | null;
}

export interface AiPipelineUsage {
  /** "baseline", "hierarchical", "arena" or "shadow". */
  pipeline: string;
  answers: number;
  successRate: number;
  latencyP50Ms: number | null;
  latencyP95Ms: number | null;
  ttftP50Ms: number | null;
  averageTokensPerAnswer: number;
  costPer1000AnswersEur: number;
}

/** Chat usage of the caller's organization. `successRate` is already a percentage (0–100). */
export interface AiMetricsSummary {
  generatedAtUtc: string;
  from: string;
  to: string;
  totalCalls: number;
  succeededCalls: number;
  failedCalls: number;
  successRate: number;
  averageDurationMs: number;
  latencyP50Ms: number | null;
  latencyP95Ms: number | null;
  ttftP50Ms: number | null;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalTokens: number;
  averageTokensPerCall: number;
  estimatedCostEur: number;
  unpricedModels: string[];
  costByStep: AiCostByStep[];
  daily: AiDailyUsage[];
  pipelines: AiPipelineUsage[];
  consumptions: AiModelConsumption[];
}

export interface IngestionPipelineUsage {
  /** "Baseline" or "Hierarchical". */
  pipeline: string;
  documents: number;
  processingP50Ms: number | null;
  processingP95Ms: number | null;
  averageChunks: number | null;
  embeddedTokens: number;
  enrichmentCalls: number;
  enrichmentTokens: number;
  failedDocuments: number;
}

/** Platform-wide ingestion (platform administrators only). */
export interface IngestionUsage {
  generatedAtUtc: string;
  from: string;
  to: string;
  documents: number;
  enrichmentCalls: number;
  enrichmentTokens: number;
  embeddedTokens: number;
  averageProcessingDurationMs: number;
  pipelines: IngestionPipelineUsage[];
}

export interface EvaluationRunPipelineSummary {
  pipeline: string;
  faithfulness: number | null;
  contextRecall: number | null;
}

export interface EvaluationRunSummary {
  runId: string;
  startedAt: string;
  finishedAt: string | null;
  chatModel: string;
  embeddingModel: string;
  judgeModel: string | null;
  questionCount: number;
  importedAt: string;
  results: EvaluationRunPipelineSummary[];
}

/** Scores are 0–1; null means the metric could not be computed (never 0). */
export interface EvaluationPipelineResult {
  pipeline: string;
  contextPrecision: number | null;
  contextPrecisionStd: number | null;
  contextRecall: number | null;
  contextRecallStd: number | null;
  faithfulness: number | null;
  faithfulnessStd: number | null;
  answerRelevancy: number | null;
  answerRelevancyStd: number | null;
  hallucinationRate: number | null;
  hitAt1: number | null;
  hitAt3: number | null;
  hitAt5: number | null;
  mrr: number | null;
  noAnswerDetectedRate: number | null;
  failed: number;
  missing: number;
  latencyP50Ms: number | null;
  latencyP95Ms: number | null;
  ttftP50Ms: number | null;
  ttftP95Ms: number | null;
  meanTokens: number | null;
  indexingP50Ms: number | null;
  indexingP95Ms: number | null;
  chunksPerDocument: number | null;
  points: number | null;
}

export interface EvaluationRunDetail {
  runId: string;
  startedAt: string;
  finishedAt: string | null;
  gitCommit: string | null;
  seed: number | null;
  chatModel: string;
  embeddingModel: string;
  judgeModel: string | null;
  queryInstruction: string | null;
  questionCount: number;
  datasetHash: string | null;
  corpusHash: string | null;
  ragasVersion: string | null;
  importedAt: string;
  results: EvaluationPipelineResult[];
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
