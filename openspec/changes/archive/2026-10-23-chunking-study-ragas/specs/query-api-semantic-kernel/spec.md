## MODIFIED Requirements

### Requirement: Streaming Routed to the Arena Winner
Streamed answers SHALL come from the production variant: the pipeline named in `Rag:ProductionPipeline` (`Baseline`, `Hierarchical`, `Recursive` or `Semantic`) when it is set, or otherwise the pipeline with more Arena wins across all tenants (`WinningPipelineSelector`, cached for 5 minutes; ties and no votes choose `Baseline`). An unknown value in `Rag:ProductionPipeline` SHALL be ignored, falling back to the Arena winner. For 40% of streamed answers, the other of `Baseline` and `Hierarchical` SHALL also run in the background (shadow test) and the pair SHALL be stored as a pending `ArenaBattle`; challengers among the study variants are left to a later change.

#### Scenario: Production variant configured
- **WHEN** `Rag:ProductionPipeline = Semantic`
- **THEN** streamed answers SHALL come from the semantic flat-chunk pipeline whatever the Arena votes are

#### Scenario: Unknown production variant
- **WHEN** `Rag:ProductionPipeline` names no pipeline (for example `Nope`)
- **THEN** streamed answers SHALL come from the Arena winner, as when it is not set

#### Scenario: Hierarchical pipeline is winning
- **WHEN** `Rag:ProductionPipeline` is not set and `Hierarchical` has more Arena wins than `Baseline`
- **THEN** streamed answers SHALL come from `HierarchicalRagPipeline`: query expansion with the ingestion model, hybrid retrieval (Qdrant `ChildFragments` plus PostgreSQL full text, fused with RRF, top 5) and a plain-language answer from the platform chat model (`Ai:Chat:*`), based only on the latest question

#### Scenario: Hierarchical pipeline finds nothing
- **WHEN** the hierarchical retrieval returns no fragment
- **THEN** the answer SHALL be a fixed message saying that no relevant regulation was found

#### Scenario: Shadow run
- **WHEN** a streamed answer is sampled for a shadow test
- **THEN** the other of `Baseline` and `Hierarchical` SHALL answer the same turn on the background work queue, and the battle SHALL be saved with `Winner = Pending`
