# Changelog

All notable changes to Hadithi are documented here.

## [0.3.0] - 2026-07-18

### Added

- `HadithiPlayer.PlayFrom(index)`: start the story at a specific beat. Earlier
  beats' On Beat Start / On Beat End events are fast forwarded so any world state
  they establish is consistent, but nothing is shown, played or waited on.
- A play button on every beat row in the Inspector: starts the story from that
  beat. Works in Play Mode, and in Edit Mode it enters Play Mode automatically
  and begins at the chosen beat. Replaces the old button that only appeared
  inside an expanded beat during Play Mode.

## [0.2.0] - 2026-07-18

### Added

- `HadithiPlayer.CurrentBeatRemainingSeconds`: live countdown for the current
  beat's time limit (Wait Seconds duration, or the optional timeout on zone and
  signal beats). Negative when the beat has no time limit. Poll it from a display
  component to show a timer.

### Changed

- Wait Seconds beats now share the same deadline plumbing as other timed beats,
  so their countdown is visible through the new property as well. The documented
  "0 seconds advances immediately" behavior is unchanged.

## [0.1.1] - 2026-07-18

### Fixed

- The custom Inspector could fail to initialize after a domain reload
  (TypeInitializationException: EditorGUIUtility style values were read in static
  field initializers, which Unity forbids in that context). Line metrics are now
  lazy properties, evaluated only while the Inspector draws.

## [0.1.0] - 2026-07-18

### Added

- HadithiPlayer: the story engine. One reorderable Beat list, one beat alive at a
  time, six plain language end conditions (Button Clicked, Controller Press,
  Timeline Finishes, Player Enters Zone, Wait Seconds, Wait For Signal).
- Open language list: add as many languages as needed, each with a name and a short
  code. Every beat gets one timeline slot per language; empty slots fall back to
  the first language.
- Ambience list: parallel looping audio, timelines or objects that play alongside
  the story without being part of it.
- HadithiZone: a physics free trigger volume beats can wait on.
- Friendly Inspector: reorderable beats, only the fields relevant to the chosen
  end condition, language labeled timeline slots, inline validation warnings, and
  a start from this beat testing button.
