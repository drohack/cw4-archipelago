"""Creeper World 4 Archipelago world.

Repository: the cw4-archipelago repo also carries the BepInEx game mod
(src/CW4Archipelago) that acts as the in-game client.
Design source of truth: docs/randomizer-design.md.
"""
from collections.abc import Mapping
from typing import Any

from worlds.AutoWorld import WebWorld, World
from BaseClasses import Tutorial

from . import items, locations, regions, rules
from .options import CW4Options


class CW4WebWorld(WebWorld):
    game = "Creeper World 4"
    theme = "ice"
    setup_en = Tutorial(
        "Multiworld Setup Guide",
        "A guide to installing the Creeper World 4 mod and connecting to a multiworld.",
        "English",
        "setup_en.md",
        "setup/en",
        ["droha"],
    )
    tutorials = [setup_en]


class CW4World(World):
    """
    Creeper World 4 is a real-time strategy tower defense game where you fight
    the Creeper, a fluid enemy that floods the terrain. Randomizes the Farsite
    Expedition campaign: mission unlocks, unit unlocks, and ERNs.
    """

    game = "Creeper World 4"
    web = CW4WebWorld()

    options_dataclass = CW4Options
    options: CW4Options

    location_name_to_id = locations.LOCATION_NAME_TO_ID
    item_name_to_id = items.ITEM_NAME_TO_ID

    def create_regions(self) -> None:
        regions.create_and_connect_regions(self)
        locations.create_all_locations(self)

    def set_rules(self) -> None:
        rules.set_all_rules(self)

    def create_items(self) -> None:
        items.create_all_items(self)

    def create_item(self, name: str) -> items.CW4Item:
        return items.create_item(self, name)

    def get_filler_item_name(self) -> str:
        return items.get_filler_item_name(self)

    def fill_slot_data(self) -> Mapping[str, Any]:
        data = dict(rules.requirement_groups())
        data["starter_missions"] = ["story1"]
        data["ern_per_item"] = 1
        return data
