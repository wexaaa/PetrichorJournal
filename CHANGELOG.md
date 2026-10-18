# Changelog

## 0.1.3

- Replaced Unity `JsonUtility`, which silently discarded all journal collections, with a tested data-contract JSON serializer.
- Added round-trip coverage for stages, stage inventories, item events, deaths, player summaries, and loaded plugins.
- Kept schema-1 archive entries readable; missing legacy collections are normalized to empty lists.

## 0.1.2

- Replaced the unreliable stage callback with client-side scene tracking and a polling fallback.
- Replaced private `RunReport.playerInfos` access with the public player-report API.
- Resolved the local player through `LocalUserManager`, matching the installed game assembly.
- Added actual stage-build, final-build, equipment, and loaded-plugin details to the journal window.
- Expanded search to survivors, stages, items, plugin names, plugin GUIDs, notes, and outcomes.
- Normalized old or incomplete archive records so the journal remains usable after upgrades.

## 0.1.1

- Fixed a runtime access error caused by using the private `NetworkUser.localPlayers` field.
- Prevented recorder callback failures from interrupting enemy, chest, or inventory game logic.
- Reduced work performed for inventory changes that do not belong to the local player.

## 0.1.0

- Added a private local archive with one JSON report per run.
- Added crash-safe active-run autosaves and interrupted-run recovery.
- Added stage, inventory, item-change, death, player-stat, and loaded-mod records.
- Added an F8 archive window with search, outcome filtering, notes, and summary copying.
- Redacted multiplayer names by default and kept all data offline.
