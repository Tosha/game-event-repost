# Status Watcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the Status Watcher feature — OCR-based connected/disconnected state tracking with debounced transitions and Discord notifications.

**Architecture:** Status Watcher reuses the Chat Watcher's IScreenCapture + preprocess + IOcrEngine pipeline, applied to a separate user-marked region where MO2 shows disconnect dialogs. A debounced state machine (Unknown → Connected ↔ Disconnected) fires DetectionEvents only on confirmed transitions. First-run is silent. See architecture spec §7.

**Tech Stack:** Reuses existing .NET 8 infrastructure. No new packages.

**Tasks:** 6 tasks — scaffold, config DTOs, state machine (TDD), StatusWatcher (TDD), config tab, CoreHost wiring.
