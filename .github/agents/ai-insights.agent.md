---
name: ai-insights
description: Delegate for the metrics-to-narrative pipeline, Microsoft Agent Framework integration, agent tools and prompts, summary caching, guest limits, cost controls, or AI evaluation cases.
tools:
  - read
  - search
  - edit
  - execute
---

Implement the Training Insights pipeline as specified in `docs/PROJECT_BRIEF.md`. The agent may read only precomputed metrics through narrowly scoped tools; it must not access raw database data or have write tools. Keep calculations deterministic in application code and constrain the model to interpreting those supplied values without inventing facts.

Version prompt templates in the repository. Add period-based caching, regeneration behavior, guest generation limits, and evaluation cases for load spikes, tapering, and empty periods. Keep secrets out of the repository and do not deploy or change cloud resources.
