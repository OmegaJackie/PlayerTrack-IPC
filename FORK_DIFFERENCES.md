# Fork Differences

This document summarizes how `OmegaJackie/PlayerTrack-IPC` diverges from the
upstream [`kalilistic/PlayerTrack`](https://github.com/kalilistic/PlayerTrack)
codebase.

The fork branched off at commit `0e8c6d9` ("Implement IPC Gate"). Everything
described below is additive on top of upstream's feature set — no upstream
functionality has been removed. Where the fork touches existing systems, it
extends them rather than replacing them.

## Summary

| Area | Upstream | This Fork |
| --- | --- | --- |
| IPC API surface | None | `IPlayerTrackAPI` exposed via Dalamud IPC |
| Categorization | Manual only | Manual + Auto-Categorizer (keyword/encounter rules) |
| Player bios | Not tracked | Captured from adventurer plates, persisted, scrapable |
| Chat integration | — | Parses `Search Info` chat output for bio keywords |
| Plugin integrations | Lodestone | Lodestone + Visibility (threaded) + XIV Instant Messenger |
| Player list sorting | By category | By category, name, first/last seen, encounter time, zone time |
| Purge actions | Delete players / categories | Adds *Unassign Categories* bulk action |
| Player summary UI | Original layout | Refactored info grid with XIM and Player Search buttons |

## IPC API

The headline feature of the fork. PlayerTrack now exposes a Dalamud IPC
surface so other plugins can read and mutate its data live.

- `PlayerTrack.Plugin/API/IPlayerTrackAPI.cs` — public interface contract.
- `PlayerTrack.Plugin/API/PlayerTrackAPI.cs` — implementation.
- `PlayerTrack.Plugin/API/PlayerTrackProvider.cs` — registers the IPC gate.

Current methods:

- `GetPlayerCurrentNameWorld(name, worldId)` — resolves a historical
  name/world pair to the player's current name/world.
- `GetPlayerNotes(name, worldId)` — returns the freeform notes for a player.
- `GetAllPlayerNameWorldHistories()` — enumerates every tracked player with
  all known previous name/world combinations.
- `AssignCategory(name, worldId, categoryId)` — assigns a category and
  immediately updates both the database and the in-memory cache so the UI
  reflects the change without a plugin restart.

## Auto-Categorizer

A rule engine that automatically assigns players to categories based on
text matching. Two rule types share the same evaluator:

- **Plate rules** — match against the adventurer plate bio captured when a
  player's CharaCard is opened (see Bio Scraper below). Implemented by
  `Handler/PlateWatcher.cs` using `Models/CategoryRule.cs`.
- **Encounter rules** — match against in-zone duration. When
  `EncounterService` raises `EncounterEnded`, `Handler/EncounterWatcher.cs`
  applies the matching category. Implemented via `Models/EncounterRule.cs`.

The rules UI lives in `UserInterface/Windows/Config/Components/CategorizerComponent.cs`
under tabs for Plate, Encounter, and Auto Scrape, accessible from the
Config window.

## Player Bios

Adventurer plate bios are now a first-class entity stored in their own table.

- `Models/PlayerBio.cs`, `Infrastructure/DTOs/PlayerBioDTO.cs`,
  `Infrastructure/Mappings/PlayerBioMappingProfile.cs`.
- `Infrastructure/Repositories/PlayerBioRepository.cs`.
- DB migration `Infrastructure/Migrations/M007_PlayerBios.cs`.
- `Domain/Services/PlayerBioService.cs`.

### Bio Scraper

`Handler/BioScraper.cs` optionally auto-opens CharaCard windows for players
who enter the zone and persists their bios. `PlateWatcher` raises an
`OnPlateProcessed` event that the scraper subscribes to. Robust against
CharaCard close without data (`1b7695b`) and properly seeds the queue on
zone entry (`f91c738`).

### Chat parsing

`PlateWatcher` also registers a `ChatMessage` handler that parses
`Search Info from <player>` chat output, extracts the bio, and runs it
through the categorizer (`12cc8b8`). The SimpleTweaks bio prefix, if
present, is stripped (`5d8119f`).

## Visibility Integration

The fork hardens the Visibility IPC integration that exists upstream:

- `Domain/Services/VisibilityService.cs` now dispatches IPC calls through
  the game's framework thread to avoid crashes (`aac0b75`).
- The dispatch is skipped entirely for untracked players whose name is
  `None`, which removes a frame-time stutter on busy zones (`bc0f2a6`).

## XIV Instant Messenger Integration

`API/XIVInstantMessengerProvider.cs` calls XIVIM's `Messenger.OpenMessenger`
IPC with the correct `Name@World` format and gates availability on the
installed plugins list rather than probing IPC at runtime.

The player summary now has:

- A **chat button** (CommentDots icon) that opens an XIM window for the
  selected player.
- A **Player Search button** (Search icon) that opens the in-game Social
  window via `AgentFriendlist` and copies the player's name to the
  clipboard, replacing the older `/search` workflow.

## Player List — Advanced Sort/Filter Panel

`UserInterface/Windows/Main/Components/PlayerAdvancedFilterComponent.cs`
adds a sort dropdown above the player list with options:

- ByCategory (default, matches upstream behaviour)
- ByName
- ByLastSeen / ByFirstSeen
- BySeenCount
- ByTotalEncounterTime
- ByZoneEncounterTime (with a zone selector populated from the encounter
  database, cached with a 60 s TTL)

Implemented across `PlayerSortType`, an extended `PlayerComparer`,
aggregate queries on `PlayerEncounterRepository` and `EncounterRepository`,
and session-scoped state on `PlayerCacheService`. The category separator
in the list is suppressed for non-category sort modes.

## Data Purge — Unassign Categories

`DataActionType.UnassignCategories` (value 3) is a new bulk action in the
Config → Data tab. The user picks any combination of categories from a
scrollable checkbox list (dynamic categories are marked) and the action
deletes the `player_categories` rows for those categories while leaving
the category records themselves intact.
`PlayerCategoryService.UnassignCategoryFromAllPlayers(int)` performs the
delete and clears the in-memory cache.

## Player Summary Refactor

`UserInterface/Windows/Main/Components/PlayerSummaryComponent.cs` was
restructured to render player info as a grid, with a corresponding
expansion of `PlayerView` / `PlayerViewMapper` (`a8d3094`). Also accommodates
the new XIM and Player Search buttons described above.

## Reliability / Cleanup

- `Plugin.cs`: `RunPostStartup` is wrapped in try/catch so a partial
  initialization failure is logged rather than crashing silently (`12cc8b8`).
- `MenuItemClickedArgsExtension`: the context-menu player check no longer
  requires a non-zero `contentId`/`objectId`, allowing right-click on
  players outside the local zone (`f91c738`, `12cc8b8`).
- `PlayerBioRepository`: uses `SetCreateTimestamp` instead of the
  upstream-style `SetInsertTimestamps` to match the bio insert path
  (`c4aab31`).
- All inline references to the unrelated "DailyRoutines" plugin were
  removed from comments and method names (`e0a17f0`).
- Dynamic social-list categories are now excluded from the categorizer
  rule pickers since rules cannot meaningfully target them (`80ea7bf`).

## Files Added by the Fork

```
PlayerTrack.Plugin/API/IPlayerTrackAPI.cs
PlayerTrack.Plugin/API/PlayerTrackAPI.cs
PlayerTrack.Plugin/API/PlayerTrackProvider.cs
PlayerTrack.Plugin/API/XIVInstantMessengerProvider.cs
PlayerTrack.Plugin/Handler/PlateWatcher.cs
PlayerTrack.Plugin/Handler/BioScraper.cs
PlayerTrack.Plugin/Handler/EncounterWatcher.cs
PlayerTrack.Plugin/Domain/Services/PlayerBioService.cs
PlayerTrack.Plugin/Models/CategoryRule.cs
PlayerTrack.Plugin/Models/EncounterRule.cs
PlayerTrack.Plugin/Models/PlayerBio.cs
PlayerTrack.Plugin/Models/Enums/PlayerSortType.cs
PlayerTrack.Plugin/Infrastructure/DTOs/PlayerBioDTO.cs
PlayerTrack.Plugin/Infrastructure/Mappings/PlayerBioMappingProfile.cs
PlayerTrack.Plugin/Infrastructure/Migrations/M007_PlayerBios.cs
PlayerTrack.Plugin/Infrastructure/Repositories/PlayerBioRepository.cs
PlayerTrack.Plugin/UserInterface/Windows/Config/Components/CategorizerComponent.cs
PlayerTrack.Plugin/UserInterface/Windows/Main/Components/PlayerAdvancedFilterComponent.cs
PlayerTrack.Plugin/UserInterface/Windows/ViewModels/PlayerBioView.cs
```
