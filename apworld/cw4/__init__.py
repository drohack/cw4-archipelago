"""Creeper World 4 Archipelago world.

Repository: the cw4-archipelago repo also carries the BepInEx game mod
(src/CW4Archipelago) that acts as the in-game client.
Design source of truth: docs/randomizer-design.md.
"""
from collections.abc import Mapping
from typing import Any

from worlds.AutoWorld import WebWorld, World
from BaseClasses import Tutorial

from . import groups, items, locations, regions, rules
from . import options
from .options import CW4Options


class CW4WebWorld(WebWorld):
    game = "Creeper World 4"
    theme = "ice"
    bug_report_page = "https://github.com/drohack/cw4-archipelago/issues"
    setup_en = Tutorial(
        "Multiworld Setup Guide",
        "A guide to installing the Creeper World 4 mod and connecting to a multiworld.",
        "English",
        "setup_en.md",
        "setup/en",
        ["droha"],
    )
    tutorials = [setup_en]

    option_groups = options.option_groups
    options_presets = options.options_presets


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

    # A group name is usable anywhere an item or location name is - in !hint and
    # in yaml lists - so these turn "keep my units local" into one line instead
    # of twenty-four. See groups.py.
    item_name_groups = groups.ITEM_NAME_GROUPS
    location_name_groups = groups.LOCATION_NAME_GROUPS

    starter_missions: list
    early_weapon: str
    bootstrapped: list = []

    def generate_early(self) -> None:
        # Chosen before regions are built, because which missions start unlocked
        # decides which regions need no unlock item.
        self.starter_missions = items.starter_missions(self)
        items.force_early_mission(self)
        items.force_early_weapon(self)

    def create_regions(self) -> None:
        regions.create_and_connect_regions(self)
        locations.create_all_locations(self)

    def set_rules(self) -> None:
        rules.set_all_rules(self)

    def create_items(self) -> None:
        items.create_all_items(self)

    def needs_bootstrap(self) -> bool:
        """Whether this world has to widen its own opening before the fill runs.

        Two conditions, and BOTH have to hold.

        The opening has to be narrower than the fill can safely handle, which only
        happens at `starter_missions: 1` - every starter-eligible mission has
        exactly one cache collectable with no items.

        And this world has to be the only place its own progression can live. Put
        another game in the multiworld and the funnel stops being a funnel: the
        fill can park Creeper World 4's mission unlocks in that game's world and
        fill Creeper World 4's first check with that game's item, so a narrow
        opening is no longer a single point of failure. Measured over 40 seeds of
        CW4 at one starter plus ChecksFinder, with the bootstrap disabled: zero
        generation failures, 7 of the opening checks holding the other game's
        item, and 4 CW4 progression items per seed living in the other world.

        Bootstrapping anyway would cost exactly that. The same 40 seeds WITH it
        had 0 foreign items in the opening - it takes the interesting cross-game
        placements out of the only checks a narrow opening has. So it stands down
        whenever another game is present, which is the common case.

        KNOWN EDGE: a player who forces their own items local (`local_items`) in a
        mixed multiworld recreates the funnel and is not covered here. That is
        rare enough, and visible enough in the yaml, to be worth a re-roll rather
        than a heuristic that guesses at intent.
        """
        if items.opening_width(self) >= items.bootstrap_threshold(self):
            return False
        return all(self.multiworld.worlds[p].game == self.game
                   for p in self.multiworld.player_ids)

    def pre_fill(self) -> None:
        if self.needs_bootstrap():
            self.bootstrapped = items.bootstrap_opening(self)

    def create_item(self, name: str) -> items.CW4Item:
        return items.create_item(self, name)

    def get_filler_item_name(self) -> str:
        return items.get_filler_item_name(self)

    def fill_slot_data(self) -> Mapping[str, Any]:
        data = dict(rules.requirement_groups(rules.is_casual(self)))
        data["starter_missions"] = [f"story{n}" for n in self.starter_missions]
        data["ern_per_item"] = 1
        data["missions_for_finale"] = self.options.missions_for_finale.value
        # Amounts for the energy upgrades. They are here rather than in the item
        # names so that item ids stay identical across yamls.
        data["energy_storage_max"] = self.options.energy_storage_max.value
        data["energy_storage_copies"] = self.options.energy_storage_copies.value
        data["base_generation_max"] = self.options.base_generation_max.value
        data["base_generation_copies"] = self.options.base_generation_copies.value
        # Magnitudes for the ERN port upgrades, here for the same reason: an
        # amount in an item name would move item ids whenever a player retuned
        # an option.
        data["ern_rate_max_percent"] = self.options.ern_rate_max.value
        data["ern_cap_max_percent"] = self.options.ern_cap_max.value
        data["ern_cap_max_build_speed_percent"] = (
            self.options.ern_cap_max_build_speed.value)
        return data
