## Why

The chunking study (`chunking-study-ragas`, archived) added the recursive and semantic variants, the offline RAGAS evaluation and `Rag:ProductionPipeline`, which chooses the variant that answers the chat. Some pieces of that change were left out so it could close:

- The Arena, the shadow test and the Arena analytics still pair only `Baseline` and `Hierarchical`, so users cannot compare the study variants with the production variant.
- The question set has 25 questions; the study design asked for at least 30 (proposed 40) to tell smaller differences apart.
- The operational latency report exists (`--latency-report`) but has not been run on real usage for chapter 8, and its Arena side latencies mix battles a user started in the chat with shadow-test battles, because `ArenaBattle` stores nothing that tells them apart.
- The production variant has not been chosen yet from the study results.

## What Changes

- `Rag:ArenaPipelines` lists the enabled variants (default `Baseline,Hierarchical`), with startup validation. Each Arena battle and each shadow test pairs the production variant with a challenger chosen uniformly among the other enabled variants.
- Arena analytics per variant and per pair (production vs challenger), and labels for the study variants in the Arena reveal and the metrics page.
- Extend `eval/dataset` to 30–40 questions, adding long regulations where the chunking strategies differ, and repeat the evaluation.
- `ArenaBattle` gains an `Origin` (`Chat` or `Shadow`, existing rows `Chat`), set where each battle is created, and the latency report writes Arena side latencies per pipeline and origin.
- Run the operational latency report over a period of real use and set `Rag:ProductionPipeline` from the study conclusion.

## Capabilities

### Modified Capabilities
- `query-api-semantic-kernel`: shadow test against a challenger among the enabled variants.
- `question-arena`: battles between the production variant and a challenger from any enabled variant; results per variant and per pair; each battle records its origin.
- `rag-evaluation`: the operational latency report separates chat and shadow Arena battles.

## Impact

- **Code**: `Application/Features/Chat` (`WinningPipelineSelector`, `ShadowTestScheduler`, `ArenaStreamingService`), `Application/Features/Arena` (`ArenaService`, `ArenaAnalyticsService`), options `RagOptions`; `Domain/Features/Arena/ArenaBattle.cs` and an EF migration; `Evaluation/OperationalLatencyReport.cs`; Angular `features/arena` and `features/analytics`.
- **Data**: `eval/dataset/questions.jsonl` and `corpus.txt`; new runs under `eval/runs`.

## Non-goals

- Choosing the production variant automatically from votes or scores.
- New chunking strategies or changes to the evaluation harness.
