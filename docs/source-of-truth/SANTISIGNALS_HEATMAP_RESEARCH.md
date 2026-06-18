# Santisignals + Heatmap Research Digest

Date: 2026-06-17
Lane: evidence
Status: research-only; no trading authorization

## Method

- Firecrawl key was read from the parent Windows environment variable
  `FIRECRAWL_API_KEY`; the key was not printed or written to repo files.
- Santisignals `profile/posts` returned 404, so the complete post inventory was
  taken from Substack archive/RSS surfaces and every archive URL was then
  scraped with Firecrawl.
- Full scraped content is stored outside the repo under the OpenCode temp dir.
  This repo file keeps only summaries and source metadata to avoid copying full
  copyrighted post bodies.

Artifacts:

- `C:\Users\gianz\AppData\Local\Temp\opencode\santisignals_archive_2026-06-17T00-06-54-744Z.json`
- `C:\Users\gianz\AppData\Local\Temp\opencode\santisignals_firecrawl_scrapes_2026-06-17T00-06-54-744Z.json`
- `C:\Users\gianz\AppData\Local\Temp\opencode\sports_analytics_firecrawl_sources_2026-06-17T00-07-48-609Z.json`

Firecrawl results:

- Santisignals archive posts: 72/72 Firecrawl scrape calls succeeded.
- Access split: 12 public posts, 60 paid/gated posts. Paid/gated posts were
  still fetched, but only public preview/metadata is usable without a
  subscription.
- Transferable-source sweep: 20/20 Firecrawl scrape calls succeeded.

## Executive Takeaway

- Santisignals' useful transfer is not a magic pick list. It is a feature design
  pattern: price each nation/player through multiple lenses and look where
  public sentiment, model rating, squad value, fixture burden, and environment
  disagree.
- For World Cup modeling, raw host city and raw distance are weak. The better
  units are committed heat exposure, roof/AC/open-air state, kickoff slot,
  WBGT/humidity, altitude, rest days, and time-zone direction.
- The Medium heatmap article transfers as monitor design discipline: discrete
  palettes, percentile bins, visible low/high-density thresholds, readable
  annotations, and no misleading continuous gradients.
- xT/VAEP/OBV/pitch-control sources transfer as football-state features:
  action-value surfaces, pressure-adjusted shot quality, controlled space,
  momentum, and event-sequence value.
- None of this is an edge claim. Every feature needs OOS calibration against
  sharp/live odds or clean held-out match outcomes before it can influence any
  Polymarket candidate ranking.

## Santisignals Complete Inventory

Source: `https://santisignals.substack.com/api/v1/archive?sort=new&offset=...&limit=12` plus Firecrawl scrape per URL.

| Date | Audience | Firecrawl chars | Post |
|---|---:|---:|---|
| 2026-06-15 | everyone | 15175 | [The World Cup Intelligence Report: France's Afternoon](https://santisignals.substack.com/p/the-fpl-intelligence-report-frances) |
| 2026-06-12 | only_paid | 3244 | [The World Cup Intelligence Report: The Second Draw](https://santisignals.substack.com/p/the-world-cup-intelligence-report-c5d) |
| 2026-06-09 | only_paid | 12200 | [The World Cup Intelligence Report: Heat](https://santisignals.substack.com/p/the-world-cup-intelligence-report-a63) |
| 2026-06-08 | everyone | 9854 | [The World Cup Intelligence Report: The Big Short](https://santisignals.substack.com/p/the-world-cup-intelligence-report-2da) |
| 2026-06-07 | only_paid | 11226 | [The World Cup Intelligence Report: Belief](https://santisignals.substack.com/p/the-world-cup-intelligence-report-3d0) |
| 2026-06-05 | everyone | 11686 | [The World Cup Intelligence Report: The Favorites Got the Easy Draw](https://santisignals.substack.com/p/the-world-cup-intelligence-report) |
| 2026-06-03 | only_paid | 7735 | [The World Cup Fantasy Intelligence Report: Preview](https://santisignals.substack.com/p/the-world-cup-fantasy-intelligence) |
| 2026-05-22 | only_paid | 5026 | [The FPL Intelligence Report: GW38](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw38) |
| 2026-05-14 | only_paid | 6422 | [The FPL Intelligence Report: GW37](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw37) |
| 2026-05-07 | only_paid | 5991 | [The FPL Intelligence Report: GW36](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw36) |
| 2026-04-30 | only_paid | 5236 | [The FPL Intelligence Report: WC35 Team](https://santisignals.substack.com/p/the-fpl-intelligence-report-wc35) |
| 2026-04-28 | only_paid | 7704 | [The FPL Intelligence Report: GW35](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw35) |
| 2026-04-23 | only_paid | 6130 | [The FPL Intelligence Report: BGW34](https://santisignals.substack.com/p/the-fpl-intelligence-report-bgw34) |
| 2026-04-17 | only_paid | 5862 | [The FPL Intelligence Report: DGW33](https://santisignals.substack.com/p/the-fpl-intelligence-report-dgw33) |
| 2026-04-14 | only_paid | 8899 | [The FPL Intelligence Report: Warfare](https://santisignals.substack.com/p/the-fpl-intelligence-report) |
| 2026-04-06 | only_paid | 7374 | [The FPL Intelligence Report: GW32](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw32) |
| 2026-03-20 | everyone | 15107 | [The FPL Intelligence Report: BGW31](https://santisignals.substack.com/p/the-fpl-intelligence-report-bgw31) |
| 2026-03-17 | only_paid | 4683 | [The FPL Intelligence Report: The Calm Before the Storm](https://santisignals.substack.com/p/the-fpl-intelligence-report-the-calm) |
| 2026-03-13 | only_paid | 4331 | [The FPL Intelligence Report: GW30](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw30) |
| 2026-03-06 | only_paid | 9037 | [The FPL Intelligence Report: Parabellum](https://santisignals.substack.com/p/the-fpl-intelligence-report-parabellum) |
| 2026-03-02 | only_paid | 5215 | [The FPL Intelligence Report: GW29](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw29) |
| 2026-02-27 | only_paid | 6471 | [The FPL Intelligence Report: GW28](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw28) |
| 2026-02-24 | only_paid | 6404 | [The FPL Intelligence Report: Fallout](https://santisignals.substack.com/p/the-fpl-intelligence-report-fallout) |
| 2026-02-20 | only_paid | 6433 | [The FPL Intelligence Report: GW27](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw27) |
| 2026-02-09 | only_paid | 6624 | [The FPL Intelligence Report: DGW26](https://santisignals.substack.com/p/the-fpl-intelligence-report-dgw26) |
| 2026-02-06 | only_paid | 9157 | [The FPL Intelligence Report: GW25](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw25) |
| 2026-02-03 | only_paid | 10351 | [The FPL Intelligence Report: Crushing it!](https://santisignals.substack.com/p/the-fpl-intelligence-report-united) |
| 2026-01-30 | only_paid | 19112 | [The FPL Intelligence Report: GW24](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw24) |
| 2026-01-27 | only_paid | 15622 | [The FPL Intelligence Report: Words of wisdom](https://santisignals.substack.com/p/the-fpl-intelligence-report-words) |
| 2026-01-23 | only_paid | 17070 | [The FPL Intelligence Report: GW23](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw23) |
| 2026-01-19 | only_paid | 14443 | [The FPL Intelligence Report: Green Arrows and Question Marks](https://santisignals.substack.com/p/the-fpl-intelligence-report-green) |
| 2026-01-16 | only_paid | 7898 | [The FPL Intelligence Report: GW22](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw22) |
| 2026-01-12 | only_paid | 13421 | [The FPL Intelligence Report: Give me all you got!](https://santisignals.substack.com/p/the-fpl-intelligence-report-give) |
| 2026-01-05 | everyone | 15945 | [The FPL Intelligence Report GW 20/21: Panic Stations!](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw-2021) |
| 2026-01-02 | only_paid | 12702 | [The FPL Intelligence Report: GW 19/20](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw1920) |
| 2025-12-29 | only_paid | 10501 | [The FPL Intelligence Report: Hangover](https://santisignals.substack.com/p/the-fpl-intelligence-report-hangover) |
| 2025-12-26 | only_paid | 7504 | [The FPL Intelligence Report: GW18](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw18) |
| 2025-12-24 | only_paid | 7628 | [The (Yet Another Holiday Bonus) FPL Intelligence Report: Moneyhaul](https://santisignals.substack.com/p/the-yet-another-holiday-bonus-fpl) |
| 2025-12-23 | only_paid | 7958 | [The (Holiday Bonus) FPL Intelligence Report: Five for Fighting](https://santisignals.substack.com/p/the-holiday-bonus-fpl-intelligence) |
| 2025-12-22 | only_paid | 9095 | [The FPL Intelligence Report: Fast and Furious](https://santisignals.substack.com/p/the-fpl-intelligence-report-fast) |
| 2025-12-19 | only_paid | 12499 | [The FPL Intelligence Report: GW17](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw17) |
| 2025-12-16 | only_paid | 4555 | [The FPL Intelligence (Mini) Report: Free Hit Bonus!](https://santisignals.substack.com/p/the-fpl-intelligence-mini-report) |
| 2025-12-14 | only_paid | 7349 | [The FPL Intelligence Report: GW 16/17](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw-1617) |
| 2025-12-12 | only_paid | 6722 | [The FPL Intelligence Report: GW16](https://santisignals.substack.com/p/the-fpl-intelligence-report-gw16) |
| 2025-12-08 | only_paid | 4834 | [The FPL Intelligence Report: Moneyball](https://santisignals.substack.com/p/the-fpl-intelligence-report-moneyball) |
| 2025-12-05 | only_paid | 4276 | [World Cup Intelligence Brief: Argentina](https://santisignals.substack.com/p/world-cup-intelligence-brief-argentina) |
| 2025-12-05 | only_paid | 4609 | [The FPL Intelligence Brief: GW15](https://santisignals.substack.com/p/the-fpl-intelligence-brief-gw15) |
| 2025-12-01 | only_paid | 4394 | [The FPL Intelligence Brief: Midweek GW14 BONUS](https://santisignals.substack.com/p/the-fpl-intelligence-brief-midweek) |
| 2025-11-27 | only_paid | 5823 | [My FPL GW13 Team](https://santisignals.substack.com/p/my-fpl-gw13-team) |
| 2025-11-25 | only_paid | 2991 | [The FPL Intelligence Brief](https://santisignals.substack.com/p/the-fpl-intelligence-brief-d19) |
| 2025-11-23 | only_paid | 5421 | [Eye on the Market](https://santisignals.substack.com/p/eye-on-the-market) |
| 2025-11-20 | only_paid | 3773 | [My FPL GW12 Team](https://santisignals.substack.com/p/my-fpl-gw12-team) |
| 2025-11-19 | only_paid | 2017 | [Too Much Transfer](https://santisignals.substack.com/p/too-much-transfer) |
| 2025-11-14 | only_paid | 4909 | [The SantiSignals Cabinet of Curiosities: International Break Edition](https://santisignals.substack.com/p/the-santisignals-cabinet-of-curiosities) |
| 2025-11-11 | only_paid | 7877 | [The FPL Intelligence Brief](https://santisignals.substack.com/p/the-fpl-intelligence-brief-6cc) |
| 2025-11-07 | only_paid | 3483 | [My FPL GW11 Team](https://santisignals.substack.com/p/my-fpl-gw11-team) |
| 2025-11-06 | only_paid | 4268 | [The Midweek FPL Intelligence Brief](https://santisignals.substack.com/p/the-midweek-fpl-intelligence-brief-08e) |
| 2025-11-03 | only_paid | 4987 | [The FPL GW10 Intelligence Brief](https://santisignals.substack.com/p/the-fpl-gw10-intelligence-brief) |
| 2025-11-02 | only_paid | 2873 | [The Newcastle United FPL GW10 Emergency Post](https://santisignals.substack.com/p/the-newcastle-united-fpl-gw10-emergency) |
| 2025-11-02 | only_paid | 10262 | [The FPL Mid-GW10 Report](https://santisignals.substack.com/p/the-fpl-mid-gw10-report) |
| 2025-10-31 | only_paid | 3823 | [My GW10 Team](https://santisignals.substack.com/p/my-gw10-team) |
| 2025-10-29 | only_paid | 4898 | [The Midweek FPL Intelligence Brief](https://santisignals.substack.com/p/the-midweek-fpl-intelligence-brief) |
| 2025-10-26 | only_paid | 5507 | [The FPL Intelligence Brief](https://santisignals.substack.com/p/the-fpl-intelligence-brief) |
| 2025-10-23 | only_paid | 4485 | [My FPL GW9 Team](https://santisignals.substack.com/p/my-fpl-gw9-team) |
| 2025-10-22 | only_paid | 2134 | [How to Architect the Optimal FPL Wildcard Squad (and Beat 11 Million Managers)](https://santisignals.substack.com/p/how-to-architect-the-optimal-fpl-d46) |
| 2025-08-09 | everyone | 17090 | [How to Architect the Optimal FPL Squad (and Beat 11 Million Managers)](https://santisignals.substack.com/p/how-to-architect-the-optimal-fpl) |
| 2025-08-03 | everyone | 11305 | [Seeing the Forest for the Trees](https://santisignals.substack.com/p/seeing-the-forest-for-the-trees) |
| 2025-07-13 | everyone | 12687 | [The Premier League Silly Season](https://santisignals.substack.com/p/the-premier-league-silly-season) |
| 2025-07-01 | everyone | 6782 | [Heat Check: Herro Outplayed Bam But Only One Can Anchor a Contender](https://santisignals.substack.com/p/heat-check-herro-outplayed-bam-but) |
| 2025-06-30 | everyone | 6025 | [The Curious Case of Jabari Smith Jr.](https://santisignals.substack.com/p/the-curious-case-of-jabari-smith) |
| 2025-06-29 | everyone | 6644 | [Messi v Ronaldo 2.0?](https://santisignals.substack.com/p/messi-v-ronaldo-20) |
| 2025-06-29 | everyone | 6640 | [Manchester United’s Comedy of Unforced Errors](https://santisignals.substack.com/p/manchester-uniteds-comedy-of-unforced) |

## Santisignals Ideas To Transfer

| Idea | Type | Project relevance | Failure mode | Validation gate |
|---|---|---|---|---|
| Committed heat, not host-city heat | Formula candidate | Add venue/kickoff/roof/AC/WBGT exposure to World Cup group simulation and match priors. | Weather data may be forecast-noisy; teams adapt/tactically slow down. | Backtest prior World Cups/international summer matches; compare calibration with/without heat terms. |
| Travel + time-zone burden | Hypothesis | Add route burden to group-stage fatigue and late-match intensity priors. | Distance alone overfits; charter travel and acclimation differ by team. | OOS tournament validation; ablate distance, time zones, rest days separately. |
| Sentiment minus model rating | Hypothesis | Use public sentiment/attention as a crowd-pricing feature for Polymarket demand, not as truth. | Social media sentiment is noisy/manipulated and may just proxy odds. | Compare sentiment divergence to PM-vs-sharp divergence receive-time only. |
| Multi-lens team card | Design pattern | World Cup Edge Lab should show ranking, Elo/PELE-like strength, squad value, market odds, burden, sentiment, and model fair. | Operator may treat disagreement as edge without execution evidence. | Mark as `HOLD` until sharp-anchor and fill-cost gates pass. |
| Player opportunity stack | Design pattern | Player props need xMin, shots, xG/shot, touches in box, set pieces, opponent allowed profile, clean-sheet/team-total context. | Prop-fade artifact: player props often lack sharp anchor and lineup certainty. | No player-prop trade ranking until lineup/minutes + sharp/market anchor exists. |
| Regression vs outcome | Risk control | Do not pay for goals/returns unsupported by xG/xA/SOT/touches/role. | Underlying data may lag role changes or opponent context. | Player-level reliability by role and minutes bins. |
| Pre-registered ledgers | Process pattern | Publish locked baselines before kickoff; later score model deltas and CLV. | Narrative retrospection. | Store snapshot hash/time before result or market movement. |

## Medium Heatmap Guide Transfer

Source: [A Heatmap guide for game level analysis](https://medium.com/@dariarodionovano/a-heatmap-guide-for-game-level-analysis-68cb6a7bcb2b), Daria Rodionova, 2022-12-27.

Useful transfer:

- Use discrete color maps, not smooth gradients that hide bins.
- Use percentile or boundary-normalized bins so low-density paths and extreme
  hotspots are both visible.
- Remove near-zero noise when the question is path/action concentration.
- Overlay heatmaps on the true spatial surface; in football that means a pitch,
  zone grid, event locations, pass/carry arrows, shot/pressure points, and
  game-state annotations.
- Every artistic choice must encode a data meaning. If it does not change an
  operator decision, it is decoration.

World Cup Edge Lab monitor consequence:

- Add state heatmaps as dense operator surfaces: `xT by zone`, `pressure by zone`,
  `corner-source zones`, `shot-source zones`, `set-piece targets`, and `heat/travel
  burden by fixture`.
- Use fixed bins and visible labels so comparisons across teams/fixtures are not
  chart-scale artifacts.

## Similar/Transferable Sources Fetched With Firecrawl

| Source | URL | Transfer |
|---|---|---|
| SantiSignals WC terminal | https://santisignals-wc2026.netlify.app | Multi-lens nation card: FIFA rank, PELE/Silver Bulletin, sentiment, Opta score, squad value, burden, altitude. |
| Expected Threat, Karun Singh | https://karun.in/blog/expected-threat.html | Event-level value surface; action value = end-zone xT minus start-zone xT. |
| socceraction action valuation | https://socceraction.readthedocs.io/en/latest/documentation/valuing_actions/index.html | Framework for xT, VAEP, Atomic-VAEP and SPADL state representation. |
| socceraction xT docs | https://socceraction.readthedocs.io/en/latest/documentation/valuing_actions/xT.html | Implementable xT reference and model API. |
| mplsoccer heatmap | https://mplsoccer.readthedocs.io/en/latest/gallery/pitch_plots/plot_heatmap.html | Practical pitch heatmaps, smoothing, bins, labels, and pass/pressure overlays. |
| StatsBomb open data | https://github.com/statsbomb/open-data | Open event/lineup/360 data for validation prototypes. |
| Metrica sample tracking | https://github.com/metrica-sports/sample-data | Synchronized event+tracking data for pitch-control and live-state prototypes. |
| Soccermatics pitch control | https://soccermatics.readthedocs.io/en/latest/lesson6/PitchControl.html | Voronoi/pitch-control mental model; space control, passing options. |
| LaurieOnTracking | https://github.com/Friends-of-Tracking-Data-FoTD/LaurieOnTracking | Practical tracking, pitch control, EPV implementation references. |
| VAEP paper | https://arxiv.org/abs/1802.07127 | Action value by scoring/conceding probability changes. |
| SoccerMap | https://arxiv.org/abs/2010.10202 | Deep pass-value surface with visual interpretability. |
| Bayesian in-game win probability | https://arxiv.org/abs/1906.05029 | Live soccer W/D/L model structure from score/minute/state. |
| StatsBomb OBV | https://statsbomb.com/articles/soccer/introducing-on-ball-value-obv/ | Possession value split into for/against components. |
| Defensive pressure and shots | https://statsbomb.com/articles/soccer/closing-down-how-defensive-pressure-impacts-shots/ | Pressure-adjusted xG/shots and defender proximity context. |
| Opta xG explainer | https://theanalyst.com/na/2021/07/what-are-expected-goals-xg/ | Shot-quality feature taxonomy for scorer/SOT props. |
| 2014 WC heat stress | https://pubmed.ncbi.nlm.nih.gov/25690408/ | Empirical evidence connecting heat stress and football performance. |
| Soccer recovery strategies | https://pubmed.ncbi.nlm.nih.gov/23315753/ | Recovery/fatigue mechanisms for congested schedules. |
| Tournament travel/jet lag | https://pubmed.ncbi.nlm.nih.gov/33831844/ | Travel/sleep/jet-lag feature candidates; transferable with caution. |
| Attack-zone prediction | https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0265372 | Early-possession graph/ML features for final-third entry. |

## Use Now / Test / Park / Reject

### Use Now

- Extend the Combo Lab monitor with model-source cards and blockers: odds/sharp,
  ranking, squad value, sentiment, heat/travel, lineups/minutes, scoreline grid,
  and market-family support.
- Pre-register model snapshots before matches. Store hash, timestamp, input
  versions, and the fair probabilities produced.
- Add readable heatmaps only where a decision changes: fixture burden map,
  xT/pressure map, and player opportunity map.
- Use the `wc26-burden-v1` fixture-burden gate as a feature readiness surface:
  missing kickoff, venue, WBGT, roof/open-air state, rest, travel, or altitude
  blocks confidence instead of being silently imputed.

### Test

- Heat/travel burden feature in World Cup group simulations.
- Sentiment-vs-model divergence as a predictor of PM-vs-sharp overpricing.
- xT/VAEP/OBV-derived player opportunity priors for shots/SOT/goals props.
- Live W/D/L score-state model as a baseline before any in-running feature.

### Park

- Full pitch control and SoccerMap until tracking data or reliable proxies exist.
- Player-level public sentiment until we can measure whether it predicts market
  mispricing rather than just popularity.

### Reject

- Any direct trading conclusion from Santisignals posts.
- Any player-prop edge without lineup/minutes certainty and sharp/market anchor.
- Any heat/travel coefficient tuned on the 2026 schedule without historical OOS.

## Required Validation Gates Before Belief

1. Exact identity join: fixture, team, player, market type, line, period, token.
2. Receive-time only: feature existed before market quote timestamp.
3. Baseline kill-check: beat `score + minute + red cards + pre-match odds` before
   adding spatial or burden features.
4. OOS split by season/tournament/competition, not random rows.
5. Calibration by market family: Brier/log loss/reliability curves.
6. Feature ablation: show incremental OOS lift for heat, travel, xT, sentiment,
   and player opportunity stacks separately.
7. Sharp comparison: fair-value candidates must be compared to Pinnacle/OddsAPI
   or another external anchor when available.
8. Execution separation: even a good model row remains analysis-only until the
   NativePM evidence and risk gates approve it.
