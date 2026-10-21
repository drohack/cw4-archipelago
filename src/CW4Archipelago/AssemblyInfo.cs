using System.Runtime.CompilerServices;

// The debug and measurement channel lives in its own plugin
// (src/CW4Archipelago.Debug) so that it ships in no release. Nothing in this
// assembly exists FOR it: its counters, readbacks, watch and overrides attach
// from outside, by Harmony and AccessTools (see its ModHooks.cs). What it reads
// through this attribute is player code and player values, nothing else:
//
//   TrackerView.TitleOf / MissionByTitle    how the map resolves a planet
//   BoonEffects.WareId / CacheWareAmount    what a Resource Cache grants
//
// Internal rather than public because these are not the mod's API. The
// dependency is one way only - the mod must never reference the debug
// assembly, and nothing may be added here for its sake.
[assembly: InternalsVisibleTo("CW4Archipelago.Debug")]
