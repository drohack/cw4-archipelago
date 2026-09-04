using System.Runtime.CompilerServices;

// The debug and measurement channel lives in its own plugin
// (src/CW4Archipelago.Debug) so that it ships in no release. It reaches a
// handful of internals here - TrackerDiag, TrackerView.TitleOf /
// MissionByTitle, LocationWatcher.TotemPokes / CachePokes - and this is what
// lets it, rather than promoting those to public.
//
// The distinction matters: public would say "part of the mod's API"; this says
// "test scaffolding may look in". The dependency is one way only - the mod must
// never reference the debug assembly.
[assembly: InternalsVisibleTo("CW4Archipelago.Debug")]
