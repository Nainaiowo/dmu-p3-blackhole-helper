# DMU P3 Blackhole Helper TODO

## Fast Context

- Plugin repo: `Nainaiowo/dmu-p3-blackhole-helper`
- Dalamud feed repo: `Nainaiowo/IMakeSillyThings`
- Dalamud feed URL: `https://raw.githubusercontent.com/Nainaiowo/IMakeSillyThings/refs/heads/main/repo.json`
- Settings command: `/dmup3`
- Helper commands: `/dmup3h`, `/dmup3helper`
- DMU territory ID: `1363`
- Watched statuses:
  - `1604` Accretion
  - `1605` Primordial Crust
  - `3004` First in Line
  - `3005` Second in Line
  - `3006` Third in Line
  - `5452` Black Hole Active
  - `5453` Black Hole Complete
  - `5454` Black Hole Marker
  - `3372` Earth Resistance Down II confirmed in logs
  - `1053`, `2097`, `3372` watched as Earth Resistance Down II variants

## Black Hole Strategy Notes

- `FIL` = First in Line.
- `SIL` = Second in Line.
- `TIL` = Third in Line.
- `CW` means clockwise from Kefka unless another reference point is explicitly named.
- If Accretion is not mentioned, use the player in that line/role who did not have Accretion during the pull.
- `FIL Accretion`, `SIL Accretion`, or `TIL Accretion` means the player in that line group who had Accretion during the pull.
- DPS/support can be inferred from each party member's `ClassJob.RowId`.

## Black Hole Sets

```text
Set 1
Wave 1: FIL DPS
Wave 2: FIL DPS / FIL Support

Set 2
Wave 1: FIL DPS / FIL Support / FIL Accretion
Wave 2: SIL DPS / FIL Support / FIL Accretion
Wave 3: SIL DPS / SIL Support / FIL Accretion

Set 3
Wave 1: SIL DPS / SIL Support / SIL Accretion
Wave 2: TIL DPS / SIL Support / SIL Accretion
Wave 3: TIL DPS / TIL Support / SIL Accretion

Set 4
Wave 1: TIL DPS / TIL Support
Wave 2: TIL Support
```

## Implemented

- [x] Initial plugin project and public release.
- [x] Shared Dalamud feed repo under `IMakeSillyThings`.
- [x] Buff Summary tab in `/dmup3`.
- [x] Watch line debuffs, Accretion, Primordial Crust, and Earth Resistance Down II.
- [x] Track Accretion history per pull so "Accretion" means the player who had it during that pull, even after the debuff is gone.
- [x] Clear Accretion history on DMU duty start, wipe, recommence, or leaving DMU.
- [x] Basic DPS/support role detection from job ID.
- [x] Local player Black Hole assignment recognition from the player's line debuff, job role, and Accretion history.
- [x] Encode all four Black Hole tether sets in the helper window.
- [x] Resolve FIL/SIL/TIL DPS, support, and Accretion assignments consistently for every Black Hole set.
- [x] Hook full set/wave instructions to each individual player so they only see their own job for the current mechanic.
- [x] Keep player-specific instructions independent of unrelated debuffs unless a later rule explicitly depends on another debuff.
- [x] Add `/dmup3` for settings and `/dmup3h` or `/dmup3helper` for the helper window.
- [x] Add optional chat callouts for the local player's current Black Hole job.
- [x] Add helper font scaling and inactive preview.
- [x] Highlight the active Black Hole set/wave in green.

## Next Work

- [x] Detect when this part of P3 is happening so the helper appears at the right time.
- [x] Hide/clear the helper once the mechanic is resolved or no longer active.
- [ ] Validate the Black Hole timeline live in DMU and adjust wave offsets if needed.
- [ ] Consider adding a sound effect to chat callouts.
