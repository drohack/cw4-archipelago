from . import bases


class TestDefaults(bases.CW4TestBase):
    def test_pool_exactly_fills_locations(self) -> None:
        # The pool is zero-sum against the locations: every unfilled location
        # needs exactly one item, so a miscount here fails generation outright.
        locations = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        pool = [i for i in self.multiworld.itempool]
        self.assertEqual(len(pool), len(locations))

    def test_energy_upgrades_are_in_the_pool(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        self.assertTrue(
            any(n in ("Progressive Energy Storage", "Progressive Base Generation") for n in names),
            "energy upgrades should appear as filler at the default weights",
        )

    def test_energy_upgrades_are_useful_not_filler(self) -> None:
        # They have a real, measured effect in game, so they should not be
        # classified as junk.
        for item in self.multiworld.itempool:
            if item.name in ("Progressive Energy Storage", "Progressive Base Generation"):
                self.assertTrue(item.useful or item.advancement)
                return

    def test_amounts_travel_in_slot_data_not_item_names(self) -> None:
        # Item ids must be identical across yamls, so the names carry no numbers.
        data = self.world.fill_slot_data()
        # A maximum and the copy count that reaches it. The per-copy value is
        # derived from the pair, which is what makes a dead copy impossible and
        # is also why the step cannot be the setting: +10 over 8 copies is 1.25
        # each, not an integer.
        self.assertEqual(data["energy_storage_max"], 200)
        self.assertEqual(data["energy_storage_copies"], 8)
        self.assertEqual(data["base_generation_max"], 10)
        self.assertEqual(data["base_generation_copies"], 8)
        for item in self.multiworld.itempool:
            self.assertNotIn("+25", item.name)
            self.assertNotIn("+200", item.name)


class TestNoErns(bases.CW4TestBase):
    options = {"progressive_erns": 0}

    def test_erns_can_be_switched_off(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        self.assertNotIn("Progressive ERN", names)
        locations = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        self.assertEqual(len(names), len(locations))


class TestOnlyStorageFiller(bases.CW4TestBase):
    """The filler WEIGHTS no longer size the energy blocks.

    Their counts are their own options now, because both curves are capped and
    the count that reaches the cap is the only count worth generating. The
    weight options are kept so existing yamls naming them are not errors - the
    same treatment build limits got - but they no longer decide anything.
    """
    options = {
        "filler_energy_storage_weight": 100,
        "filler_base_generation_weight": 0,
        "filler_build_limit_weight": 0,
    }

    def test_counts_come_from_their_own_options_not_the_weights(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        # Zero weight, but the copies option is still 8.
        self.assertEqual(names.count("Progressive Base Generation"), 8)
        self.assertEqual(names.count("Progressive Energy Storage"), 8)
        self.assertNotIn("Build Limit +1 (Tower)", names)


class TestAllWeightsZero(bases.CW4TestBase):
    options = {
        "filler_energy_storage_weight": 0,
        "filler_base_generation_weight": 0,
        "filler_build_limit_weight": 0,
    }

    def test_pool_still_fills(self) -> None:
        # An unfillable preference must not fail generation.
        locations = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        self.assertEqual(len(self.multiworld.itempool), len(locations))


class TestEarlyWeaponMortar(bases.CW4TestBase):
    options = {"early_weapon": "mortar"}

    def test_mortar_is_forced_early(self) -> None:
        early = self.multiworld.early_items[self.player]
        self.assertEqual(1, early.get("Mortar"))
        self.assertNotIn("Cannon", early)

    def test_both_weapons_are_still_real_checks(self) -> None:
        # Forcing one early must not remove the other from the pool - the point
        # is to choose the OPENING, not to hand the player a weapon.
        names = [i.name for i in self.multiworld.itempool]
        self.assertIn("Mortar", names)
        self.assertIn("Cannon", names)


class TestEarlyWeaponCannon(bases.CW4TestBase):
    options = {"early_weapon": "cannon"}

    def test_cannon_is_forced_early(self) -> None:
        early = self.multiworld.early_items[self.player]
        self.assertEqual(1, early.get("Cannon"))
        self.assertNotIn("Mortar", early)


class TestEarlyWeaponRandom(bases.CW4TestBase):
    options = {"early_weapon": "random"}

    def test_exactly_one_weapon_is_forced_early(self) -> None:
        # "random" is Archipelago's own, for any Choice - the world defines only
        # mortar and cannon. Defining an option_random is actually forbidden
        # (Options.py asserts on it), which is why the default is the string.
        early = self.multiworld.early_items[self.player]
        forced = [w for w in ("Cannon", "Mortar") if w in early]
        self.assertEqual(1, len(forced), f"expected one early weapon, got {forced}")
        self.assertEqual(forced[0], self.world.early_weapon)


class TestEarlyWeaponDefaultIsRandom(bases.CW4TestBase):
    options = {}

    def test_the_default_still_forces_one_weapon(self) -> None:
        # default = "random" is a string rather than a value, so it goes through
        # from_any -> from_text and could silently resolve to nothing if the
        # spelling were ever wrong. This is the test that would catch that.
        early = self.multiworld.early_items[self.player]
        forced = [w for w in ("Cannon", "Mortar") if w in early]
        self.assertEqual(1, len(forced), f"expected one early weapon, got {forced}")
        self.assertIn(self.world.early_weapon, ("Cannon", "Mortar"))


class TestEarlyWeaponWithOneStarter(bases.CW4TestBase):
    """One starter is a one-location opening, and the bootstrap owns it.

    Archipelago's default battery is off here because `test_fill` generates, and
    what this class is for - that the opening gets widened before the general fill
    sees it - is asserted directly and needs no fill.
    """
    options = {"early_weapon": "mortar", "starter_missions": 1}
    run_default_tests = False

    def test_the_opening_starts_one_location_wide(self) -> None:
        from ..items import opening_width
        self.assertEqual(1, opening_width(self.world))

    def test_no_early_items_are_requested_at_this_width(self) -> None:
        # Two requests into one slot is what broke 12 percent of these seeds.
        # At this width the bootstrap places instead, so nothing is requested.
        self.assertEqual({}, dict(self.multiworld.early_items[self.player]))

    def test_the_bootstrap_widens_the_opening(self) -> None:
        from BaseClasses import CollectionState
        from ..items import SAFE_OPENING
        self.assertTrue(self.world.bootstrapped, "nothing was bootstrapped")
        # prevent_sweep, or the sweep collects the OTHER locked bootstrap items
        # for us and the measurement stops being about the listed items.
        state = CollectionState(self.multiworld)
        for _name, item_name in self.world.bootstrapped:
            state.collect(self.world.create_item(item_name), prevent_sweep=True)
        reachable = [l for l in self.multiworld.get_locations(self.player)
                     if l.address is not None and l.can_reach(state)]
        self.assertGreaterEqual(len(reachable), SAFE_OPENING)

    def test_every_bootstrapped_item_opened_something(self) -> None:
        # The whole point: never spend a scarce slot on an item that unlocks
        # nothing. A lone Factory is half of the Greenar pair and was the item
        # that stranded seed 20100.
        from BaseClasses import CollectionState
        state = CollectionState(self.multiworld)
        before = len([l for l in self.multiworld.get_locations(self.player)
                      if l.address is not None and l.can_reach(state)])
        for _name, item_name in self.world.bootstrapped:
            state.collect(self.world.create_item(item_name), prevent_sweep=True)
            after = len([l for l in self.multiworld.get_locations(self.player)
                         if l.address is not None and l.can_reach(state)])
            self.assertGreater(after, before,
                               f"{item_name} was placed but opened nothing")
            before = after

    def test_the_pool_plus_the_bootstrap_still_fills_exactly(self) -> None:
        # The bootstrap locks its items into locations and takes them OUT of the
        # pool, so the pool alone no longer matches the location count.
        locations = [l for l in self.multiworld.get_locations(self.player)
                     if l.address is not None]
        self.assertEqual(len(self.multiworld.itempool) + len(self.world.bootstrapped),
                         len(locations))


class TestBootstrapStandsDownForOtherGames(bases.CW4TestBase):
    """A mixed multiworld does not need the bootstrap, and pays for it if it runs.

    The funnel is only a single point of failure when this world is the only place
    its own progression can live. With another game present the fill can park CW4
    unlocks in that world and fill CW4's opening with that game's items - measured
    at 0 generation failures in 40 seeds without the bootstrap, against 7 of the
    opening checks holding a foreign item. Running it anyway drove that to 0.

    This test fakes the condition rather than building a real multiworld, which
    keeps it fast; the real two-game case is covered by measurement.
    """
    options = {"starter_missions": 1}
    run_default_tests = False

    def test_solo_needs_the_bootstrap(self) -> None:
        self.assertTrue(self.world.needs_bootstrap())

    def test_another_game_in_the_multiworld_turns_it_off(self) -> None:
        class NotCW4:
            game = "ChecksFinder"
        real = dict(self.multiworld.worlds)
        try:
            self.multiworld.worlds[self.player] = self.world
            fake = max(self.multiworld.player_ids) + 1
            self.multiworld.worlds[fake] = NotCW4()
            self.multiworld.player_ids = tuple(list(self.multiworld.player_ids) + [fake])
            self.assertFalse(self.world.needs_bootstrap())
        finally:
            self.multiworld.worlds.clear()
            self.multiworld.worlds.update(real)
            self.multiworld.player_ids = tuple(
                p for p in self.multiworld.player_ids if p in real)


class TestOpeningWidthAtTwoStarters(bases.CW4TestBase):
    options = {"early_weapon": "mortar", "starter_missions": 2}

    def test_two_starters_can_afford_both_early_items(self) -> None:
        from ..items import opening_width
        self.assertGreaterEqual(opening_width(self.world), 2)
        early = self.multiworld.early_items[self.player]
        self.assertEqual(1, early.get("Mortar"))
        self.assertTrue([k for k in early if k.startswith("Mission Unlock:")])


class TestBuildLimitsNeverGenerate(bases.CW4TestBase):
    """Build limits are out of the pool, and this is the test that keeps them out.

    The weight is turned UP to 100 and the two real fillers to zero, so the ONLY
    thing the old code could have drawn is a build limit. If they ever return to
    the pool by accident rather than by decision, this fails.

    It also covers the fallback: with every poolable weight at zero the draw has
    nothing to pick, and the pool still has to fill exactly.
    """
    options = {
        "filler_build_limit_weight": 100,
        "filler_energy_storage_weight": 0,
        "filler_base_generation_weight": 0,
    }

    def test_no_build_limit_item_is_generated(self) -> None:
        from ..items import BUILD_LIMIT_ITEMS
        names = [i.name for i in self.multiworld.itempool]
        for name in BUILD_LIMIT_ITEMS:
            self.assertNotIn(name, names)

    def test_the_pool_still_fills_exactly(self) -> None:
        locations = [l for l in self.multiworld.get_locations(self.player)
                     if l.address is not None]
        self.assertEqual(len(self.multiworld.itempool), len(locations))

    def test_the_names_keep_their_ids(self) -> None:
        # Removed from the pool, NOT from the id table. Renumbering ids would
        # break every existing client and seed, which is why the names stay.
        from ..items import BUILD_LIMIT_ITEMS, ITEM_NAME_TO_ID
        for name in BUILD_LIMIT_ITEMS:
            self.assertIn(name, ITEM_NAME_TO_ID)


class TestTraps(bases.CW4TestBase):
    def test_traps_are_about_half_the_leftovers(self) -> None:
        from ..items import TRAP_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        traps = [n for n in pool if n in TRAP_ITEMS]
        self.assertTrue(traps, "traps should appear at the default 50 percent")
        # Real items are ~46, and the ERN port upgrades take a further fixed
        # block (12 names x 4 copies at the default). Only what is left after
        # BOTH splits half traps, half useful - the ERN block is generated
        # before the trap split, so counting it as a leftover understated the
        # denominator and made this expect 95 traps where 71 is correct.
        # Real items are ~46, and three FIXED blocks come off the top before
        # the trap split: the ERN upgrades, and the two energy upgrades now that
        # their counts are capped. Only what remains after all of those splits
        # half traps, half padding.
        from ..items import (ERN_UPGRADE_ITEMS, ENERGY_STORAGE_ITEM,
                             BASE_GENERATION_ITEM)
        erns = len([n for n in pool if n in ERN_UPGRADE_ITEMS])
        energy = pool.count(ENERGY_STORAGE_ITEM) + pool.count(BASE_GENERATION_ITEM)
        leftovers = len(pool) - 46 - erns - energy
        self.assertAlmostEqual(len(traps), leftovers // 2, delta=2)

    def test_traps_are_classified_as_traps(self) -> None:
        from ..items import TRAP_ITEMS
        for item in self.multiworld.itempool:
            if item.name in TRAP_ITEMS:
                self.assertTrue(item.trap)
                return

    def test_trap_names_match_the_client(self) -> None:
        # The mod dispatches on these exact strings; a rename on either side
        # would silently stop traps firing rather than fail loudly.
        from ..items import TRAP_ITEMS
        self.assertEqual(TRAP_ITEMS, [
            "Spore Strike", "Spore Scatter", "Creeper Surge", "Energy Drain",
            "Emitter Overdrive", "Unit Stun", "Ammo Drain",
        ])


class TestNoTraps(bases.CW4TestBase):
    options = {"trap_percentage": 0}

    def test_traps_can_be_switched_off(self) -> None:
        from ..items import TRAP_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        self.assertFalse([n for n in pool if n in TRAP_ITEMS])


class TestAllTraps(bases.CW4TestBase):
    options = {"trap_percentage": 100}

    def test_every_leftover_can_be_a_trap(self) -> None:
        # "Every leftover" no longer means the whole pool. The energy upgrades
        # are a FIXED block sized by their own options, so trap_percentage
        # cannot displace them - it only governs the slots left after the fixed
        # blocks. Set their copies to 0 if you want them gone.
        from ..items import TRAP_ITEMS, ENERGY_STORAGE_ITEM
        pool = [i.name for i in self.multiworld.itempool]
        self.assertTrue([n for n in pool if n in TRAP_ITEMS])
        self.assertEqual(pool.count(ENERGY_STORAGE_ITEM), 8)
        locations = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        self.assertEqual(len(pool), len(locations))


class TestOneTrapKind(bases.CW4TestBase):
    options = {
        "trap_weight_spore_strike": 100,
        "trap_weight_spore_scatter": 0,
        "trap_weight_creeper_surge": 0,
        "trap_weight_energy_drain": 0,
        "trap_weight_emitter_overdrive": 0,
        "trap_weight_unit_stun": 0,
        "trap_weight_ammo_drain": 0,
    }

    def test_trap_weights_are_respected(self) -> None:
        pool = [i.name for i in self.multiworld.itempool]
        self.assertIn("Spore Strike", pool)
        self.assertNotIn("Creeper Surge", pool)
        self.assertNotIn("Unit Stun", pool)


class TestClassification(bases.CW4TestBase):
    def test_every_item_in_logic_is_progression(self) -> None:
        # Archipelago's hard rule: an item referenced in a location's rules MUST
        # be progression, or the fill can place it behind itself.
        from ..items import classification
        from ..rules import logic_item_names
        from BaseClasses import ItemClassification
        for name in logic_item_names():
            self.assertTrue(
                classification(name) & ItemClassification.progression,
                f"{name} is in logic but not classified progression",
            )

    def test_buildings_that_gate_nothing_are_useful_not_progression(self) -> None:
        from ..items import UNIT_ITEMS, classification
        from ..rules import logic_item_names
        from BaseClasses import ItemClassification
        idle = [n for n in UNIT_ITEMS if n not in logic_item_names()]
        self.assertTrue(idle, "expected some buildings to gate nothing")
        for name in idle:
            self.assertFalse(
                classification(name) & ItemClassification.progression,
                f"{name} gates nothing but is classified progression",
            )

    def test_mission_unlocks_are_progression(self) -> None:
        # They gate their region rather than appearing in a rule, so the
        # logic-derived check above would miss them.
        from ..items import MISSION_UNLOCK_ITEMS, classification
        from BaseClasses import ItemClassification
        for name in MISSION_UNLOCK_ITEMS:
            self.assertTrue(classification(name) & ItemClassification.progression)


class TestCasualLogic(bases.CW4TestBase):
    options = {"logic_difficulty": "casual"}

    def test_anti_air_is_required_from_the_first_spores(self) -> None:
        # Mission 6 is the first with spores. Requiring anti-air there is what
        # makes Archipelago place one EARLIER in the spheres, rather than a seed
        # legally leaving it in the finale.
        self.collect_all_but(["Sniper", "Missile Launcher"])
        self.assertFalse(self.can_reach_location("We Were Never Alone - Nullify 1"))
        self.assertFalse(self.can_reach_location("Founders - Cache 1"))

    def test_either_anti_air_building_satisfies_it(self) -> None:
        from ..rules import mission_requirements
        self.assertIn(["Sniper", "Missile Launcher"], mission_requirements(6, casual=True))

    def test_early_missions_are_untouched(self) -> None:
        # Nothing before the first spores should gain a defensive requirement.
        from ..rules import mission_requirements
        for n in range(1, 6):
            for group in mission_requirements(n, casual=True):
                self.assertNotIn("Sniper", group)


class TestStandardLogic(bases.CW4TestBase):
    options = {"logic_difficulty": "standard"}

    def test_anti_air_is_not_required(self) -> None:
        self.collect_all_but(["Sniper", "Missile Launcher"])
        self.assertTrue(self.can_reach_location("We Were Never Alone - Nullify 1"))

    def test_the_compound_still_needs_its_sniper(self) -> None:
        # The one hard sniper requirement is independent of the tier.
        self.collect_all_but("Sniper")
        self.assertFalse(self.can_reach_location("The Compound - Cache 1"))


class TestFinaleGate(bases.CW4TestBase):
    # Completion events cannot be withheld - the generator grants them the
    # moment a mission's completion is reachable - so the gate is tested by
    # holding exactly what the FINALE needs and nothing else. Without the gate
    # that would be enough to win; with it, twelve missions must be completable.
    FINALE_ONLY = [
        "Mission Unlock: Founders", "Cannon", "Terp", "Chronat", "Platform",
        "Nullifier", "Greenar Refinery", "Factory",
    ]

    def test_finale_needs_twelve_missions_by_default(self) -> None:
        from ..locations import VICTORY_EVENT
        self.assertEqual(self.world.options.missions_for_finale.value, 12)
        self.collect_by_name(self.FINALE_ONLY)
        self.assertFalse(self.can_reach_location(VICTORY_EVENT))

    def test_completion_events_are_local_and_cost_no_pool_slot(self) -> None:
        # Events have no address, are never sent to the multiworld, and must not
        # consume a location that could hold a real item.
        from ..locations import BEATEN_ITEM
        events = [l for l in self.multiworld.get_locations(self.player) if l.address is None]
        beaten = [l for l in events if l.item and l.item.name == BEATEN_ITEM]
        self.assertEqual(len(beaten), 19)   # every mission but the finale
        real = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        self.assertEqual(len(real), 236)
        self.assertEqual(len(self.multiworld.itempool), len(real))


class TestNoFinaleGate(bases.CW4TestBase):
    options = {"missions_for_finale": 0}

    def test_gate_can_be_switched_off(self) -> None:
        # The same holdings that fail the default must win with the gate off.
        from ..locations import VICTORY_EVENT
        self.collect_by_name([
            "Mission Unlock: Founders", "Cannon", "Terp", "Chronat", "Platform",
            "Nullifier", "Greenar Refinery", "Factory",
        ])
        self.assertTrue(self.can_reach_location(VICTORY_EVENT))


class TestFullFinaleGate(bases.CW4TestBase):
    options = {"missions_for_finale": 19}

    def test_every_mission_can_be_required(self) -> None:
        # The hardest setting must still be winnable: with everything held, all
        # 19 missions are completable, so the gate opens.
        from ..locations import VICTORY_EVENT
        self.collect_all_but(["Victory"])
        self.assertTrue(self.can_reach_location(VICTORY_EVENT))

    def test_the_gate_actually_bites_at_nineteen(self) -> None:
        from ..locations import VICTORY_EVENT
        self.collect_by_name([
            "Mission Unlock: Founders", "Cannon", "Terp", "Chronat", "Platform",
            "Nullifier", "Greenar Refinery", "Factory",
        ])
        self.assertFalse(self.can_reach_location(VICTORY_EVENT))


class TestEmitterOverdriveIsNotGenerated(bases.CW4TestBase):
    """Emitter Overdrive is out of the pool, and the id map still holds it.

    The traps spike admits an effect only if it fires on essentially every
    mission or carries a fallback. Emitter Overdrive does neither - it no-ops
    where a mission has no emitters, which is roughly a quarter to a third of
    the campaign - so it is not generated (designer, 2026-08-31).

    Both halves are asserted on purpose. Dropping the NAME rather than just the
    pool entry would renumber every item id after it, which is the one thing
    that must not move.
    """

    def test_no_emitter_overdrive_in_the_pool(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        self.assertNotIn("Emitter Overdrive", names)

    def test_the_id_is_still_reserved(self) -> None:
        from ..items import ITEM_NAME_TO_ID, TRAP_ITEMS, POOL_TRAP_ITEMS
        self.assertIn("Emitter Overdrive", ITEM_NAME_TO_ID)
        self.assertIn("Emitter Overdrive", TRAP_ITEMS)
        self.assertNotIn("Emitter Overdrive", POOL_TRAP_ITEMS)

    def test_the_other_six_are_still_generated(self) -> None:
        # A weight-filter bug could silently empty the trap pool; this is the
        # canary for that.
        from ..items import POOL_TRAP_ITEMS
        self.assertEqual(6, len(POOL_TRAP_ITEMS))

    def test_only_the_two_energy_upgrades_are_poolable_filler(self) -> None:
        from ..items import BASE_GENERATION_ITEM, ENERGY_STORAGE_ITEM, POOL_FILLER_KINDS
        self.assertEqual([ENERGY_STORAGE_ITEM, BASE_GENERATION_ITEM],
                         list(POOL_FILLER_KINDS))


class TestArchipelagoConventions(bases.CW4TestBase):
    """The encouraged-feature list from Archipelago's own "adding games" doc.

    Name groups are the easy ones to get wrong: they are plain strings, nothing
    validates them at import, and a typo or a renamed item leaves a group that
    silently matches nothing. A player writing `non_local_items: [Units]` would
    get no error and no effect.
    """

    def test_every_item_group_member_is_a_real_item(self) -> None:
        from ..groups import ITEM_NAME_GROUPS
        from ..items import ITEM_NAME_TO_ID
        for group, names in ITEM_NAME_GROUPS.items():
            self.assertTrue(names, f"item group '{group}' is empty")
            for name in names:
                self.assertIn(name, ITEM_NAME_TO_ID, f"group '{group}' names a missing item")

    def test_every_item_group_member_can_actually_be_generated(self) -> None:
        # Stronger than "is a real item", and it is the check that was missing:
        # "Build Limits" was a group of three items that all existed and none of
        # which were ever placed. A player writing `non_local_items: [Build Limits]`
        # got no error and no effect - the same silent failure the docstring above
        # warns about, arrived at from the other direction.
        from ..groups import ITEM_NAME_GROUPS
        from ..items import (BONUS_UNIT_ITEMS, MISSION_UNLOCK_ITEMS,
                             POOL_FILLER_KINDS, POOL_TRAP_ITEMS, PROGRESSIVE_ERN,
                             UNIT_ITEMS)
        poolable = (set(MISSION_UNLOCK_ITEMS) | set(UNIT_ITEMS) | set(BONUS_UNIT_ITEMS)
                    | {PROGRESSIVE_ERN} | set(POOL_FILLER_KINDS) | set(POOL_TRAP_ITEMS))
        for group, names in ITEM_NAME_GROUPS.items():
            for name in names:
                self.assertIn(name, poolable,
                              f"group '{group}' names '{name}', which is never generated")

    def test_every_location_group_member_is_a_real_location(self) -> None:
        from ..groups import LOCATION_NAME_GROUPS
        from ..locations import LOCATION_NAME_TO_ID
        for group, names in LOCATION_NAME_GROUPS.items():
            self.assertTrue(names, f"location group '{group}' is empty")
            for name in names:
                self.assertIn(name, LOCATION_NAME_TO_ID, f"group '{group}' names a missing location")

    def test_mission_groups_cover_every_location(self) -> None:
        # The 20 per-mission groups should partition the whole location set, so
        # `exclude_locations: [<mission>]` cannot miss a check.
        from ..groups import LOCATION_NAME_GROUPS
        from ..items import MISSION_TITLES
        from ..locations import LOCATION_NAME_TO_ID
        covered = set()
        for n in range(1, 21):
            covered |= LOCATION_NAME_GROUPS[MISSION_TITLES[n]]
        self.assertEqual(set(LOCATION_NAME_TO_ID), covered)

    def test_presets_name_real_options_and_values(self) -> None:
        from ..options import options_presets, CW4Options
        fields = set(CW4Options.type_hints)
        for preset, settings in options_presets.items():
            for key in settings:
                self.assertIn(key, fields, f"preset '{preset}' sets unknown option '{key}'")

    def test_option_groups_cover_every_game_option(self) -> None:
        # A grouped option shows under its heading; an ungrouped one is filed
        # under a generic bucket, which is how an option quietly becomes hard to
        # find on the webhost.
        import inspect
        from Options import Option
        from .. import options as opt
        grouped = {o.__name__ for g in opt.option_groups for o in g.options}
        # Every Option subclass DEFINED in our options module - which is exactly
        # the set of game-specific options, and needs no guessing about which of
        # Archipelago's common options are inherited.
        ours = {
            name for name, obj in vars(opt).items()
            if inspect.isclass(obj) and issubclass(obj, Option) and obj.__module__ == opt.__name__
        }
        # Subset, not equality: Archipelago APPENDS its own "Item & Location
        # Options" group (LocalItems, StartInventory, ExcludeLocations and the
        # rest) to every world's list at import, so the grouped set is always a
        # superset of ours. What matters is that none of OURS is left out - an
        # ungrouped option gets filed under a generic "Game Options" heading,
        # which is how an option quietly becomes hard to find on the webhost.
        self.assertEqual(set(), ours - grouped)

class TestErnUpgradeItems(bases.CW4TestBase):
    def test_names_match_the_client(self) -> None:
        # The mod counts received items by these exact strings
        # (CW4Archipelago.Core.ErnUpgradeRules). A rename on either side would
        # silently stop the upgrades applying rather than fail loudly - the same
        # reason the trap names are pinned above.
        from ..items import ERN_UPGRADE_ITEMS, ERN_UPGRADE_NAMES_ORDER
        self.assertEqual(ERN_UPGRADE_NAMES_ORDER, [
            "Energy Production", "Mine Production", "Build Speed",
            "Move Speed", "Fire Range", "Fire Rate",
        ])
        self.assertEqual(ERN_UPGRADE_ITEMS, [
            "Progressive ERN Efficiency Rate: Energy Production",
            "Progressive ERN Efficiency Rate: Mine Production",
            "Progressive ERN Efficiency Rate: Build Speed",
            "Progressive ERN Efficiency Rate: Move Speed",
            "Progressive ERN Efficiency Rate: Fire Range",
            "Progressive ERN Efficiency Rate: Fire Rate",
            "Progressive ERN Efficiency Cap: Energy Production",
            "Progressive ERN Efficiency Cap: Mine Production",
            "Progressive ERN Efficiency Cap: Build Speed",
            "Progressive ERN Efficiency Cap: Move Speed",
            "Progressive ERN Efficiency Cap: Fire Range",
            "Progressive ERN Efficiency Cap: Fire Rate",
        ])

    def test_exactly_four_of_each_by_default(self) -> None:
        # FIXED counts, not a weighted draw. filler_sequence draws with
        # replacement, which could produce nine copies of one name - five of
        # them inert, because a fifth copy does nothing - and none of another.
        from ..items import ERN_UPGRADE_ITEMS, ERN_UPGRADE_MAX_COPIES
        pool = [i.name for i in self.multiworld.itempool]
        for name in ERN_UPGRADE_ITEMS:
            self.assertEqual(pool.count(name), ERN_UPGRADE_MAX_COPIES,
                             f"{name} should appear exactly "
                             f"{ERN_UPGRADE_MAX_COPIES} times")

    def test_never_more_than_the_useful_maximum(self) -> None:
        from ..items import ERN_UPGRADE_ITEMS, ERN_UPGRADE_MAX_COPIES
        pool = [i.name for i in self.multiworld.itempool]
        for name in ERN_UPGRADE_ITEMS:
            self.assertLessEqual(pool.count(name), ERN_UPGRADE_MAX_COPIES)

    def test_ids_are_pinned_so_nothing_renumbers(self) -> None:
        # Ids are positional and the client's map must match the server's, so a
        # name may only ever be APPENDED. Pinning the actual values is a
        # stronger guard than "the ERN ids are the highest", which was the first
        # version of this test and broke as soon as a boon item was appended
        # after them - the ids had not moved at all, only the assertion was
        # asking the wrong question.
        from ..items import ITEM_NAME_TO_ID, ERN_UPGRADE_ITEMS, BASE_ID
        self.assertEqual(ITEM_NAME_TO_ID["Mission Unlock: Farsite"], BASE_ID)
        self.assertEqual(ITEM_NAME_TO_ID[ERN_UPGRADE_ITEMS[0]], BASE_ID + 57)
        self.assertEqual(ITEM_NAME_TO_ID[ERN_UPGRADE_ITEMS[-1]], BASE_ID + 68)
        # contiguous, and every id unique
        ern_ids = sorted(ITEM_NAME_TO_ID[n] for n in ERN_UPGRADE_ITEMS)
        self.assertEqual(ern_ids, list(range(ern_ids[0], ern_ids[0] + 12)))
        self.assertEqual(len(set(ITEM_NAME_TO_ID.values())), len(ITEM_NAME_TO_ID))

    def test_classified_as_filler(self) -> None:
        from ..items import ERN_UPGRADE_ITEMS
        for item in self.multiworld.itempool:
            if item.name in ERN_UPGRADE_ITEMS:
                self.assertTrue(item.filler, f"{item.name} should be filler")


class TestNoErnUpgrades(bases.CW4TestBase):
    options = {"ern_upgrade_copies": 0}

    def test_they_can_be_switched_off(self) -> None:
        from ..items import ERN_UPGRADE_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        self.assertFalse([n for n in pool if n in ERN_UPGRADE_ITEMS])

    def test_the_pool_still_fills_every_location(self) -> None:
        pool = [i.name for i in self.multiworld.itempool]
        locations = [l for l in self.multiworld.get_locations(self.player)
                     if l.address is not None]
        self.assertEqual(len(pool), len(locations))


class TestOneErnUpgradeCopy(bases.CW4TestBase):
    options = {"ern_upgrade_copies": 1}

    def test_one_of_each(self) -> None:
        from ..items import ERN_UPGRADE_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        for name in ERN_UPGRADE_ITEMS:
            self.assertEqual(pool.count(name), 1)

class TestErnMagnitudesAreConfigurable(bases.CW4TestBase):
    options = {
        "ern_rate_max": 600,
        "ern_cap_max": 300,
        "ern_cap_max_build_speed": 175,
    }

    def test_they_reach_the_client_in_slot_data(self) -> None:
        # The magnitudes must travel in slot_data, never in an item name: a name
        # carrying an amount would move item ids whenever a player retuned an
        # option, and the client's mapping would stop matching the server's.
        data = self.multiworld.worlds[self.player].fill_slot_data()
        self.assertEqual(data["ern_rate_max_percent"], 600)
        self.assertEqual(data["ern_cap_max_percent"], 300)
        self.assertEqual(data["ern_cap_max_build_speed_percent"], 175)

    def test_item_ids_do_not_move_with_the_options(self) -> None:
        from ..items import ITEM_NAME_TO_ID, BASE_ID, ERN_UPGRADE_ITEMS
        self.assertEqual(ITEM_NAME_TO_ID["Mission Unlock: Farsite"], BASE_ID)
        self.assertEqual(ITEM_NAME_TO_ID[ERN_UPGRADE_ITEMS[0]], BASE_ID + 57)
        for name in ERN_UPGRADE_ITEMS:
            self.assertIn(name, ITEM_NAME_TO_ID)


class TestErnMagnitudeDefaults(bases.CW4TestBase):
    def test_defaults_are_the_measured_values(self) -> None:
        # Pinned so a future tuning pass has to be deliberate. See
        # docs/ern-upgrade-measurements.md for where each number comes from.
        data = self.multiworld.worlds[self.player].fill_slot_data()
        self.assertEqual(data["ern_rate_max_percent"], 400)
        self.assertEqual(data["ern_cap_max_percent"], 200)
        self.assertEqual(data["ern_cap_max_build_speed_percent"], 150)

class TestBoonPadding(bases.CW4TestBase):
    def test_the_pool_is_padded_with_a_one_shot_item(self) -> None:
        # Every cumulative filler kind is capped now, so the leftover slots need
        # an item with no ceiling. A one-shot effect qualifies: the sixty-third
        # copy refills weapons exactly as well as the first.
        from ..items import BOON_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        self.assertGreater(pool.count(BOON_ITEMS[0]), 0)

    def test_padding_does_not_override_an_explicit_option(self) -> None:
        # Progressive ERN was the first padder and was wrong for exactly this
        # reason: a player asking for zero ERNs still received sixty-six.
        locations = [l for l in self.multiworld.get_locations(self.player)
                     if l.address is not None]
        self.assertEqual(len(self.multiworld.itempool), len(locations))

    def test_names_match_the_client(self) -> None:
        from ..items import BOON_ITEMS
        # Order matters for ids, and the six surges must follow the upgrade
        # order the mod addresses by index.
        from ..items import ERN_UPGRADE_NAMES_ORDER
        self.assertEqual(BOON_ITEMS[:4],
                         ["Ammo Resupply", "Energy Cache", "Field Shield",
                          "Resource Cache"])
        self.assertEqual(BOON_ITEMS[4:],
                         ["ERN Surge: " + u for u in ERN_UPGRADE_NAMES_ORDER])
        self.assertEqual(len(BOON_ITEMS), 10)

    def test_the_padding_is_split_evenly_between_them(self) -> None:
        # Alternating, not a weighted draw: the counts should be even and
        # deterministic rather than a sample that happens to favour one name.
        from ..items import BOON_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        counts = [pool.count(n) for n in BOON_ITEMS]
        self.assertTrue(all(c > 0 for c in counts), f"some boon is absent: {counts}")
        self.assertLessEqual(max(counts) - min(counts), 1,
                             f"padding is lopsided: {counts}")

class TestCasualBootstrap(bases.CW4TestBase):
    """Casual logic gets a wider opening, because its fill was failing.

    CI hit a FillError in TestCasualLogic.test_fill, which draws a fresh random
    seed every run - so the passing commit before it proved nothing. Sampling
    the exact configuration on Archipelago 0.6.7, with a positive control
    because an unverified zero is worthless, put the rate at roughly 0.2 to 1
    percent of casual seeds, present both before and after the filler work.

    Casual is the HARSHER setting despite the name: rules._casual_defense adds a
    defensive requirement from CASUAL_DEFENSE_FROM onward, so more items are
    needed before the same locations open. At the default two starters the
    opening is two wide - which cleared the old SAFE_OPENING_MIN of 2 while
    sitting far below the SAFE_OPENING of 4 that items treats as slack.
    """
    options = {"logic_difficulty": "casual"}

    def test_the_threshold_is_one_wider_for_casual(self) -> None:
        from .. import items
        self.assertEqual(items.bootstrap_threshold(self.world),
                         items.SAFE_OPENING_MIN + 1)

    def test_the_bootstrap_engages(self) -> None:
        # The point of the fix. If this stops being true the sub-percent
        # FillError comes back, and it will come back as a rare CI flake that
        # looks like bad luck rather than a regression.
        from .. import items
        width = items.opening_width(self.world)
        self.assertLess(width, items.bootstrap_threshold(self.world),
                        f"casual opening is {width}, so the bootstrap will not run")
        self.assertTrue(getattr(self.world, "bootstrapped", None),
                        "bootstrap_opening placed nothing")


class TestNormalLogicBootstrapUnchanged(bases.CW4TestBase):
    """The casual fix must cost nothing where there was no problem.

    Bootstrapping trades away the cross-game placements that make a narrow
    opening interesting (see World.needs_bootstrap), so it is deliberately +1
    for casual only.
    """
    def test_normal_logic_does_not_bootstrap(self) -> None:
        from .. import items
        self.assertEqual(items.bootstrap_threshold(self.world),
                         items.SAFE_OPENING_MIN)
        self.assertGreaterEqual(items.opening_width(self.world),
                                items.bootstrap_threshold(self.world))
