"""Regions for Creeper World 4.

Open-missions mode (authoritative user decision): every mission is reachable
from Menu once its Mission Unlock item is held; the campaign's linear chain
is display-only. This seed's starter missions (world.starter_missions) begin
unlocked; the rest need their Mission Unlock item.
"""
from BaseClasses import Region

from .items import MISSION_TITLES


def create_and_connect_regions(world) -> None:
    menu = Region("Menu", world.player, world.multiworld)
    world.multiworld.regions.append(menu)
    for n in range(1, 21):
        region = Region(f"story{n}", world.player, world.multiworld)
        world.multiworld.regions.append(region)
        if n in world.starter_missions:
            menu.connect(region)
        else:
            unlock = f"Mission Unlock: {MISSION_TITLES[n]}"
            menu.connect(
                region,
                rule=lambda state, item=unlock: state.has(item, world.player),
            )
