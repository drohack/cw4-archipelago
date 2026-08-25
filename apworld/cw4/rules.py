"""Access rules for Creeper World 4.

SKELETON. The authoritative logic lives in docs/randomizer-design.md,
section "Logic corrections from the designer" - implement it here:
- has_offense = Cannon OR Mortar (Sprayer only where blueite is obtainable)
- Nullifier required only by nullify-objective locations
- Refinery prerequisite for Rocket Pad / Platform / Chronat
- Snipers/Missiles are difficulty-tier extras, never hard requirements
- Open questions before logic freeze: porter-required missions,
  per-mission blueite, factory resource storage.
"""
from worlds.generic.Rules import set_rule

from .items import MISSION_TITLES
from .locations import LOCATIONS_PER_MISSION


def set_all_rules(world) -> None:
    player = world.player

    # Placeholder combat gate: every mission past story1 needs an offensive
    # weapon. TODO: replace with the full per-mission/per-objective rules.
    for n in range(2, 21):
        for name in LOCATIONS_PER_MISSION[n]:
            set_rule(
                world.get_location(name),
                lambda state: state.has_any(("Cannon", "Mortar"), player),
            )

    world.multiworld.completion_condition[player] = lambda state: state.has(
        "Victory", player
    )
