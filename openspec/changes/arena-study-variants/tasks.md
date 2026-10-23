## 1. Arena with challengers

- [ ] 1.1 Add `Rag:ArenaPipelines` (`RagOptions.cs`) with startup validation (unknown names, production variant not ingested), and keep `Rag:ProductionPipeline` as implemented in `WinningPipelineSelector.cs`.
- [ ] 1.2 Pick a challenger among the other enabled pipelines in `ArenaStreamingService.cs`, `ArenaService.cs` and `ShadowTestScheduler.cs`; tests for default pairing, challenger distribution and single-pipeline (no shadow).
- [ ] 1.3 Per-pipeline and per-pair aggregates in `ArenaAnalyticsService.cs` and `PipelineMetrics`, and labels for the study variants in the Arena reveal and the metrics page (`src/AsistenteAyuntamiento.Angular/src/app/features/arena/`, `features/analytics/`).

## 2. Battle origin

- [ ] 2.1 Add `ArenaBattleOrigin` (`Chat`, `Shadow`) and `ArenaBattle.Origin` with an EF migration (existing rows `Chat`); set it in `ArenaStreamingService.cs`/`ArenaService.cs` (chat) and `ShadowTestScheduler.cs` (shadow), with tests.
- [ ] 2.2 Group `arena-sides.csv` by pipeline and origin in `OperationalLatencyReport.cs` (column `origin`), update `OperationalLatencyReportTests.cs` and `eval/README.md`.

## 3. Study follow-up

- [ ] 3.1 Extend `eval/dataset/questions.jsonl` to at least 30 questions (proposed 40, including `sin-respuesta`), adding long regulations to `corpus.txt`; ingest them with the four variants and repeat the evaluation (manual review of the questions by Antonio).
- [ ] 3.2 Run the operational latency report (`--latency-report`) over a period of real use for chapter 8.
- [ ] 3.3 Set `Rag:ProductionPipeline` from the study conclusion and decide which variants stay in `Rag:ArenaPipelines`.

## 4. Specs

- [ ] 4.1 `openspec validate arena-study-variants --strict` before archiving.
