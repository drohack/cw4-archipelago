"""Creeper World 4 Archipelago world.

Repository: the cw4-archipelago repo also carries the BepInEx game mod
(src/CW4Archipelago) that acts as the in-game client.
Design source of truth: docs/randomizer-design.md.
"""
from collections.abc import Mapping
from typing import Any

from worlds.AutoWorld import WebWorld, World
from BaseClasses import Tutorial

from . import groups, items, locations, opening, regions, roster, rules
from . import options
from .options import CW4Options


def _world_version() -> str:
    """This apworld's version, from the manifest that already declares it.

    The manifest is the canonical place (tools/check_release.py holds it equal to
    the csproj and Plugin.cs), so it is read rather than duplicated. A packaged
    .apworld carries archipelago.json at the same relative path, so this works
    from a zip as well as from a directory.
    """
    import json
    import os
    path = os.path.join(os.path.dirname(__file__), "archipelago.json")
    try:
        with open(path, encoding="utf-8") as fh:
            return str(json.load(fh).get("world_version", ""))
    except Exception:
        # Never fail generation over a version string. An empty value simply
        # means the mod cannot compare, which is the behaviour of every seed
        # generated before this key existed.
        return ""


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
    # The 20 missions this seed actually contains. With span_missions off this is
    # always 1..20; with it on, 19 of the 20 are drawn from the campaign and the
    # SPAN Experiments together. Every table in the apworld covers all 46 -
    # location and item ids are class-level and cannot vary with a yaml option -
    # so this is the only thing that says which 20 exist for THIS seed.
    mission_roster: list
    early_weapon: str
    bootstrapped: list = []

    def generate_early(self) -> None:
        # Chosen before regions are built, because which missions start unlocked
        # decides which regions need no unlock item.
        self.starter_missions = roster.starter_missions(self)
        # STRICTLY AFTER the starters: the roster is built around them, so that a
        # seed can never start with a mission it does not contain.
        self.mission_roster = roster.mission_roster(self)
        opening.force_early_mission(self)
        opening.force_early_weapon(self)

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
        if opening.opening_width(self) >= opening.bootstrap_threshold(self):
            return False
        return all(self.multiworld.worlds[p].game == self.game
                   for p in self.multiworld.player_ids)

    def pre_fill(self) -> None:
        if self.needs_bootstrap():
            self.bootstrapped = opening.bootstrap_opening(self)
        # Place our own progression, with retries. Archipelago's main fill is
        # not ours to retry, and it gives up on a solvable arrangement about
        # once in 18,000 seeds; oot and pokemon_emerald handle the same problem
        # the same way. See opening.place_own_progression.
        self.own_placements = opening.place_own_progression(self)

    def create_item(self, name: str) -> items.CW4Item:
        return items.create_item(self, name)

    def get_filler_item_name(self) -> str:
        return items.get_filler_item_name(self)

    def fill_slot_data(self) -> Mapping[str, Any]:
        data = dict(rules.requirement_groups(
            rules.is_casual(self), missions_in_seed=self.mission_roster))
        # The PHYSICAL layer as well: what a player genuinely cannot proceed
        # without, as opposed to what logic is willing to assume. It lets the
        # in-game tracker paint red only where a check cannot be reached at all,
        # and yellow where it is reachable but out of logic - a hard-mode run
        # the generator must never count on. The two differ wherever a
        # MISSION_SOFT entry applies (energy on Not My Mars and Ruins
        # Repurposed) or casual logic has added anti-air.
        data["strict_location_requirements"] = (
            rules.requirement_groups(
                physical=True,
                missions_in_seed=self.mission_roster)["location_requirements"])
        # Which objective SLOTS each mission requires in order to be won.
        #
        # The plugin needs this to close a real hole: Farsite was beaten in full
        # and "Farsite - Mission Complete" was sent while "Farsite - Custom"
        # never was, because the game reported IsMissionComplete() true while
        # IsMissionObjectiveComplete(5) was still false in that same frame
        # (observed in a real playthrough, seed 47803770604823003263).
        #
        # Completing a mission means completing the objectives it requires, so
        # the plugin can send those checks on completion rather than depending
        # on a per-objective query that can lag or never flip.
        data["required_objectives"] = {
            locations.mission_specifier(n):
                sorted(locations.ALL_REQUIRED_OBJECTIVES[n])
            for n in self.mission_roster
        }
        data["starter_missions"] = [locations.mission_specifier(n)
                                    for n in self.starter_missions]
        # WHICH MISSION SITS IN WHICH SLOT of the level select.
        #
        # The spiral has 20 planets and the plugin retargets each one, so it
        # needs the roster in the order the slots run. Sent for every seed, SPAN
        # or not: a campaign-only seed's roster is simply story1..story20 in
        # order, which is what the untouched game already shows, so an older
        # plugin ignoring this key still behaves correctly.
        data["mission_roster"] = [locations.mission_specifier(n)
                                  for n in self.mission_roster]
        # Titles alongside - for READERS OTHER THAN THE PLUGIN. The plugin does
        # not use this, and must not: a mission's title is its location-name
        # prefix, the key the tracker resolves a planet by, and the unlock item's
        # name all at once, so it has to be ONE value. That value is the
        # generated table in src/CW4Archipelago.Core/SpanMissionTable.g.cs, which
        # CI regenerates and diffs against this same span_data.py, so the two
        # cannot drift. Wiring the plugin to prefer this key instead would create
        # exactly the divergence the single table exists to prevent.
        #
        # It is sent because an external tracker has no such table and would
        # otherwise see twenty opaque guids.
        data["mission_titles"] = {
            locations.mission_specifier(n): items.ALL_MISSION_TITLES[n]
            for n in self.mission_roster
        }
        data["span_missions"] = bool(self.options.span_missions)
        # WHICH APWORLD BUILT THIS SEED, so the mod can say so when they differ.
        #
        # Three documents promise that the plugin and the apworld are a matched
        # pair, and until this key existed nothing enforced it anywhere but the
        # build: no version travelled in slot data, the mod read none, and
        # required_client_version was never set, so it sat at AutoWorld's
        # (0, 1, 6) default which passes for any client ever built. A player
        # running a v0.1.5 mod against a v0.2.0 seed connected cleanly and
        # desynchronised silently - the exact failure the whole version
        # apparatus exists to prevent.
        #
        # Read from archipelago.json rather than restated here: a second literal
        # is the thing this key is meant to stop.
        data["world_version"] = _world_version()
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
        # And the COPY COUNT, for exactly the reason energy_storage_copies
        # travels: the per-copy step is the maximum divided by the count, so the
        # plugin cannot work out the step without knowing how many the pool
        # holds. Before this was sent it divided by a hardcoded 4, which made
        # lowering ern_upgrade_copies quietly lower the CEILING instead of
        # coarsening the steps - two copies capped efficiency at 150 percent
        # rather than 200, and rate at 250 rather than 400. A seed generated
        # before this key existed sends nothing and the plugin falls back to 4,
        # which is what those seeds were built with.
        data["ern_upgrade_copies"] = self.options.ern_upgrade_copies.value
        data["ern_rate_max_percent"] = self.options.ern_rate_max.value
        data["ern_cap_max_percent"] = self.options.ern_cap_max.value
        data["ern_cap_max_build_speed_percent"] = (
            self.options.ern_cap_max_build_speed.value)
        return data
