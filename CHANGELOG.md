# Changelog

All notable changes to Hadithi are documented here.

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
