"""Regions for Creeper World 4.

Open-missions mode (authoritative user decision): every mission is reachable
from Menu once its Mission Unlock item is held; the campaign's linear chain
is display-only. This seed's starter missions (world.starter_missions) begin
unlocked; the rest need their Mission Unlock item.

ONE REGION PER MISSION THE SEED CONTAINS, which is world.mission_roster rather
than 1..20 - with the SPAN Experiments on, the 20 are drawn from 46.

REGION NAMES ARE NOT SPECIFIERS. "mission21" is an internal key; what the GAME
calls that mission is locations.mission_specifier(21), which is a map guid. They
were both "storyN" while the campaign was the only thing here, and keeping that
spelling for a SPAN mission would have invented a "story21" the game has never
heard of.
"""
from BaseClasses import Region

from .items import ALL_MISSION_TITLES


def create_and_connect_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    world.multiworld.regions.append(menu)
    for n in world.mission_roster:
        region = Region(f"mission{n}", world.player, world.multiworld)
        world.multiworld.regions.append(region)
        if n in world.starter_missions:
            menu.connect(region)
        else:
            unlock = f"Mission Unlock: {ALL_MISSION_TITLES[n]}"
            menu.connect(
                region,
                rule=lambda state, item=unlock: state.has(item, world.player),
            )
