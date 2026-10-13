## Why

The review of the 30 main specs on 2026-10-13 rewrote them to describe the code as it is. Two kinds of findings came out of it and are collected here so they are not lost:
1. Features the old specs described but that were never built. They were removed from the main specs and are kept here as candidates.
2. Places where the code looks wrong. The serious ones were fixed in the same review: #40, #41, #42 and #43, covering the municipality filter, system prompts, typed stream errors, the both-bad vote, per-tenant preferences, per-pipeline job state, baseline metrics, vector sync state, the manual scrape result and the Go shutdown. The rest are listed in `tasks.md`.

## What Changes

Nothing yet. Each task is a candidate to implement, discard or turn into its own change.

## Capabilities

No spec changes until a task is taken. The current behaviour is described in `openspec/specs`.

## Impact

Mainly ingestion, analytics, Arena, LLM configuration, CI/deploy and the Go scraper. See the tasks.
