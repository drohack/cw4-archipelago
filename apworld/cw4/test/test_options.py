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
            any(n in ("Energy Storage Upgrade", "Base Generation Upgrade") for n in names),
            "energy upgrades should appear as filler at the default weights",
        )

    def test_energy_upgrades_are_useful_not_filler(self) -> None:
        # They have a real, measured effect in game, so they should not be
        # classified as junk.
        for item in self.multiworld.itempool:
            if item.name in ("Energy Storage Upgrade", "Base Generation Upgrade"):
                self.assertTrue(item.useful or item.advancement)
                return

    def test_amounts_travel_in_slot_data_not_item_names(self) -> None:
        # Item ids must be identical across yamls, so the names carry no numbers.
        data = self.world.fill_slot_data()
        self.assertEqual(data["energy_storage_step"], 50)
        self.assertEqual(data["energy_storage_decay"], 80)
        self.assertEqual(data["base_generation_start"], 5)
        self.assertEqual(data["base_generation_ramp"], 2)
        for item in self.multiworld.itempool:
            self.assertNotIn("+50", item.name)


class TestNoErns(bases.CW4TestBase):
    options = {"progressive_erns": 0}

    def test_erns_can_be_switched_off(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        self.assertNotIn("Progressive ERN", names)
        locations = [l for l in self.multiworld.get_locations(self.player) if l.address is not None]
        self.assertEqual(len(names), len(locations))


class TestOnlyStorageFiller(bases.CW4TestBase):
    options = {
        "filler_energy_storage_weight": 100,
        "filler_base_generation_weight": 0,
        "filler_build_limit_weight": 0,
    }

    def test_weights_are_respected(self) -> None:
        names = [i.name for i in self.multiworld.itempool]
        self.assertIn("Energy Storage Upgrade", names)
        self.assertNotIn("Base Generation Upgrade", names)
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


class TestTraps(bases.CW4TestBase):
    def test_traps_are_about_half_the_leftovers(self) -> None:
        from ..items import TRAP_ITEMS
        pool = [i.name for i in self.multiworld.itempool]
        traps = [n for n in pool if n in TRAP_ITEMS]
        self.assertTrue(traps, "traps should appear at the default 50 percent")
        # Real items are ~46; the rest splits half traps, half useful.
        leftovers = len(pool) - 46
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
        from ..items import TRAP_ITEMS, ENERGY_STORAGE_ITEM
        pool = [i.name for i in self.multiworld.itempool]
        self.assertTrue([n for n in pool if n in TRAP_ITEMS])
        self.assertNotIn(ENERGY_STORAGE_ITEM, pool)
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
