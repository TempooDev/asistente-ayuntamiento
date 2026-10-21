## MODIFIED Requirements

### Requirement: Streaming Routed to the Arena Winner
Streamed answers SHALL come from the production variant: the pipeline named in `Rag:ProductionPipeline` when it is set to an enabled pipeline, or otherwise the enabled pipeline with more Arena wins across all tenants (`WinningPipelineSelector`, cached for 5 minutes; ties and no votes choose `Baseline`). Enabled pipelines are those listed in `Rag:ArenaPipelines` (default `Baseline,Hierarchical`) plus the production variant. For 40% of streamed answers, a challenger chosen uniformly among the other enabled pipelines SHALL also run in the background (shadow test) and the pair SHALL be stored as a pending `ArenaBattle`. An unknown or not enabled value in `Rag:ProductionPipeline` SHALL be logged as a warning at startup and ignored.

#### Scenario: Production variant configured
- **WHEN** `Rag:ProductionPipeline = Semantic`
- **THEN** streamed answers SHALL come from the semantic flat-chunk pipeline whatever the Arena votes are

#### Scenario: Hierarchical pipeline is winning
- **WHEN** `Rag:ProductionPipeline` is not set and `Hierarchical` has more Arena wins than any other enabled pipeline
- **THEN** streamed answers SHALL come from `HierarchicalRagPipeline`: query expansion with the ingestion model, hybrid retrieval (Qdrant `ChildFragments` plus PostgreSQL full text, fused with RRF, top 5) and a plain-language answer from the platform chat model (`Ai:Chat:*`), based only on the latest question

#### Scenario: Hierarchical pipeline finds nothing
- **WHEN** the hierarchical retrieval returns no fragment
- **THEN** the answer SHALL be a fixed message saying that no relevant regulation was found

#### Scenario: Shadow run
- **WHEN** a streamed answer is sampled for a shadow test and `Rag:ArenaPipelines = Baseline,Hierarchical,Recursive,Semantic` with `Rag:ProductionPipeline = Hierarchical`
- **THEN** one of `Baseline`, `Recursive` or `Semantic` SHALL answer the same turn on the background work queue, and the battle SHALL be saved with `Winner = Pending`

#### Scenario: Only one enabled pipeline
- **WHEN** the production variant is the only enabled pipeline
- **THEN** no shadow test SHALL run
