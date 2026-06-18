---
name: oloraculo-cockpit-parity
description: Build and verify Oloraculo Blazor cockpit and monitor UI with visual parity. Use for Razor/MudBlazor pages, monitor design matching, responsive dashboards, auto-refresh state, market boards, hedge grids, blocker panels, position calculators, and screenshot-based visual QA.
---

# Oloraculo Cockpit Parity

## Purpose

Use this skill for any monitor/cockpit UI where the operator must scan live state
quickly. Visual parity means verified screenshots, not vibes.

## Required Reads

- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`
- `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`
- `Oloraculo.Web/Components/`
- `Oloraculo.Web/wwwroot/app.css`
- Reference design path when supplied, for example `C:\123\monitor-design`

## UI Rules

- Build the actual tool as the first screen.
- Use dense, calm, operator-grade layouts.
- Show stale, missing, blocked, and last-good states explicitly.
- Keep core layout dimensions stable while data refreshes.
- Keep analysis-only/no-order-path language visible on trading surfaces.
- Avoid nested cards, decorative clutter, text overflow, and marketing hero
  composition.

## Procedure

1. Inspect the existing component and CSS conventions.
2. Inventory the reference design: grid, typography, color, density, spacing,
   status colors, tables, and interactive states.
3. Implement in scoped components/CSS using stable responsive dimensions.
4. Run the app and capture screenshots at desktop and mobile widths.
5. Compare against the reference and iterate until parity is exact or better.

## Evidence

- Screenshot paths and viewport sizes.
- Any remaining parity deltas.
- Build/test output when Razor/CSS contracts changed.
- Browser evidence that auto-refresh does not overlap or resize the layout.

## Vetoes

- Visual parity claims without screenshots.
- UI that implies live trading or order placement.
- Text that overflows or overlaps at common viewports.
- Hidden refresh errors.
