from .bases import CW4TestBase


class TestAccess(CW4TestBase):
    def test_something_is_reachable_with_nothing(self) -> None:
        # No starting weapon is granted, so the opening rests entirely on caches
        # that can be taken with the rift lab and one tower. If this breaks, the
        # generator has nowhere to place a first item and generation fails.
        reachable = [
            loc.name for loc in self.multiworld.get_locations(self.player)
            if loc.address is not None and self.can_reach_location(loc.name)
        ]
        self.assertTrue(reachable, "nothing is reachable with an empty inventory")
        # Whatever the starters are, the free thing is always a cache.
        self.assertTrue(any(" - Cache " in n for n in reachable))

    def test_starters_are_drawn_from_the_eligible_set(self) -> None:
        from ..items import STARTER_ELIGIBLE
        for n in self.world.starter_missions:
            self.assertIn(n, STARTER_ELIGIBLE)

    def test_starter_eligibility_matches_the_logic(self) -> None:
        # A mission may only start unlocked if its cache is genuinely free -
        # waives the mission's requirements AND has none of its own. Archon
        # waives the weapon but needs a Terp and Pylon, so it must not qualify.
        from ..items import STARTER_ELIGIBLE
        from ..rules import OBJECTIVE_OWN, WAIVES_INSTANCE, WAIVES_MISSION_REQUIREMENTS
        eligible = {
            m for (m, kind) in WAIVES_MISSION_REQUIREMENTS
            if kind == "Collect" and not OBJECTIVE_OWN.get((m, "Collect"))
        }
        # A per-INSTANCE waiver qualifies too: it is enough that ONE cache on the
        # mission is free, which is exactly Farsite's case.
        eligible |= {
            m for (m, kind, _i) in WAIVES_INSTANCE
            if kind == "Collect" and not OBJECTIVE_OWN.get((m, "Collect"))
        }
        self.assertEqual(set(STARTER_ELIGIBLE), eligible)

    def test_farsites_two_caches_have_different_rules(self) -> None:
        # The reason WAIVES_INSTANCE exists. The worksheet: "first item can get
        # with just tower, 2nd item needs weapon to get over creep." If these ever
        # come out equal, the split has been lost and Farsite either lies about
        # its second cache or cannot open a seed.
        from ..rules import location_requirements
        first = location_requirements("Farsite - Cache 1", 1)
        second = location_requirements("Farsite - Cache 2", 1)
        self.assertEqual([], first, "Farsite's first cache must need nothing")
        self.assertIn(["Cannon", "Mortar"], second, "the second cache needs a weapon")

    def test_farsite_can_open_a_seed(self) -> None:
        from ..items import STARTER_ELIGIBLE
        self.assertIn(1, STARTER_ELIGIBLE)

    def test_no_weapon_is_granted(self) -> None:
        precollected = [i.name for i in self.multiworld.precollected_items[self.player]]
        self.assertNotIn("Cannon", precollected)
        self.assertNotIn("Mortar", precollected)
        pool = [i.name for i in self.multiworld.itempool]
        for weapon in ("Cannon", "Mortar", "Sprayer"):
            self.assertIn(weapon, pool)

    def test_starter_missions_need_no_unlock(self) -> None:
        from ..items import MISSION_TITLES
        pool = [i.name for i in self.multiworld.itempool]
        starters = set(self.world.starter_missions)
        for n in range(1, 21):
            name = f"Mission Unlock: {MISSION_TITLES[n]}"
            if n in starters:
                self.assertNotIn(name, pool, f"{name} should not be in the pool")
            else:
                self.assertIn(name, pool, f"{name} should be in the pool")

    def test_every_unlock_has_an_id_even_when_it_starts_unlocked(self) -> None:
        # Item ids must not shift with the starter set, or a player changing the
        # option would break the client's name-to-id mapping.
        from ..items import ITEM_NAME_TO_ID, MISSION_TITLES
        for n in range(1, 21):
            self.assertIn(f"Mission Unlock: {MISSION_TITLES[n]}", ITEM_NAME_TO_ID)

    def test_missions_need_their_unlock(self) -> None:
        # Pick a mission this seed did NOT start with - starters are random now,
        # so naming one would fail whenever it happened to be a starter.
        from ..items import MISSION_TITLES, STARTER_ELIGIBLE
        n = next(m for m in STARTER_ELIGIBLE if m not in self.world.starter_missions)
        title = MISSION_TITLES[n]
        self.assertAccessDependency(
            [f"{title} - Cache 1", f"{title} - Mission Complete"],
            [[f"Mission Unlock: {title}"]],
            only_check_listed=True,
        )

    def test_free_caches_need_no_weapon(self) -> None:
        # "You can get the item immediately with rift lab and single tower."
        self.collect_all_but(["Cannon", "Mortar"])
        for name in (
            "Home - Cache 1",
            "Not My Mars - Cache 1",
            "War and Peace - Cache 1",
            "The Experiment - Cache 1",
        ):
            with self.subTest(location=name):
                self.assertTrue(self.can_reach_location(name))

    def test_caches_the_worksheet_calls_hard_do_need_a_weapon(self) -> None:
        # "No easy way to get the item at the start of this one. it starts under
        # creep" (More and More); "need to fight back creep to do it" (Tower of
        # Darkness).
        self.collect_all_but(["Cannon", "Mortar"])
        self.assertFalse(self.can_reach_location("More and More - Cache 1"))
        self.assertFalse(self.can_reach_location("Tower of Darkness - Cache 1"))

    def test_completing_a_mission_still_needs_a_weapon(self) -> None:
        # A free cache waives the weapon for its own check only.
        self.collect_all_but(["Cannon", "Mortar"])
        self.assertTrue(self.can_reach_location("Home - Cache 1"))
        self.assertFalse(self.can_reach_location("Home - Mission Complete"))

    def test_archon_caches_need_terp_and_pylon_but_no_weapon(self) -> None:
        # "If you have a pylon and a terp you can get the 2nd item (no weapons
        # needed)."
        self.collect_all_but(["Cannon", "Mortar"])
        self.assertTrue(self.can_reach_location("Archon - Cache 1"))

    def test_archon_caches_need_terp_and_pylon(self) -> None:
        self.assertAccessDependency(
            ["Archon - Cache 1"],
            [["Terp", "Pylon"]],   # one combination, both needed
            only_check_listed=True,
        )

    def test_early_totems_run_on_loose_liftic(self) -> None:
        # Missions 2, 3 and 4 power totems from liftic caches on the ground.
        self.collect_all_but(["Greenar Refinery", "Factory"])
        self.assertTrue(self.can_reach_location("Home - Totem 1"))
        self.assertTrue(self.can_reach_location("Not My Mars - Totem 1"))
        self.assertTrue(self.can_reach_location("Ruins Repurposed - Totem 1"))
        self.assertFalse(self.can_reach_location("We Know Nothing - Totem 1"))
        self.assertFalse(self.can_reach_location("Serious - Totem 1"))

    def test_nullify_objectives_need_nullifier(self) -> None:
        self.assertAccessDependency(
            ["Home - Nullify 1", "Ever After - Nullify 1"],
            [["Nullifier"]],
            only_check_listed=True,
        )

    def test_buried_caches_need_the_terp(self) -> None:
        self.assertAccessDependency(
            ["The Compound - Cache 1", "Sequence - Cache 1", "Wallis - Cache 1"],
            [["Terp"]],
            only_check_listed=True,
        )

    def test_compound_needs_a_sniper(self) -> None:
        self.collect_all_but("Sniper")
        self.assertFalse(self.can_reach_location("The Compound - Cache 1"))
        self.assertFalse(self.can_reach_location("The Compound - Mission Complete"))

    def test_founders_needs_terp_beacon_and_platform(self) -> None:
        self.assertAccessDependency(
            ["Founders - Cache 1"],
            [["Terp", "Chronat", "Platform"]],   # one combination, all needed
            only_check_listed=True,
        )

    def test_ever_after_is_an_ordinary_mission(self) -> None:
        # It is no longer the finale, so it has a Mission Complete check and
        # Founders does not.
        names = [loc.name for loc in self.multiworld.get_locations(self.player)]
        self.assertIn("Ever After - Mission Complete", names)
        self.assertNotIn("Founders - Mission Complete", names)
        self.assertIn("Founders - Victory", names)

    def test_prerequisites_are_expanded(self) -> None:
        # A Platform needs the greenar chain to build, so a rule asking for one
        # asks for the chain too. Since 2026-09-03 that chain is a single item:
        # the Factory unlocks the refinery as well, so the expansion is one
        # entry rather than two.
        from ..rules import objective_requirements
        groups = objective_requirements(19, 4)  # Founders - Collect
        self.assertIn(["Factory"], groups)
        # The retired name must never reappear in a rule - it is no longer
        # generated, so requiring it would be unsatisfiable.
        self.assertNotIn(["Greenar Refinery"], groups)

    def test_the_retired_refinery_gates_nothing_but_keeps_its_id(self) -> None:
        # Retiring an item is only safe if its NAME survives: ids are positional
        # and the client's table has to match across every yaml, so a removed
        # name would renumber everything after it and break existing seeds.
        from ..items import ITEM_NAME_TO_ID, RETIRED_ITEMS, UNIT_ITEMS
        from ..rules import logic_item_names, requirement_groups
        self.assertIn("Greenar Refinery", RETIRED_ITEMS)
        self.assertIn("Greenar Refinery", ITEM_NAME_TO_ID)
        self.assertIn("Greenar Refinery", UNIT_ITEMS)
        # Its id sits where it always did, immediately after Factory's.
        self.assertEqual(ITEM_NAME_TO_ID["Factory"] + 1,
                         ITEM_NAME_TO_ID["Greenar Refinery"])
        # And nothing requires it any more.
        self.assertNotIn("Greenar Refinery", logic_item_names())
        for casual in (False, True):
            groups = requirement_groups(casual)
            for table in (groups["mission_requirements"],
                          groups["location_requirements"]):
                for entry in table.values():
                    for group in entry:
                        self.assertNotIn("Greenar Refinery", group)

    def test_a_retired_item_is_never_placed(self) -> None:
        # The positive half: the pool must not contain it, or a player would
        # receive an item that unlocks a building they already have.
        from ..items import RETIRED_ITEMS
        names = [i.name for i in self.multiworld.itempool]
        for retired in RETIRED_ITEMS:
            self.assertNotIn(retired, names)

    def test_alternatives_do_not_inherit_prerequisites(self) -> None:
        # "Porter or Platform" must not demand the platform's greenar chain.
        from ..rules import objective_requirements
        groups = objective_requirements(11, 0)  # Shattered - Nullify
        self.assertIn(["Porter", "Platform"], groups)
        self.assertNotIn(["Greenar Refinery"], groups)

    def test_mission_complete_inherits_its_objectives(self) -> None:
        self.collect_all_but("Chronat")
        self.assertFalse(self.can_reach_location("Tower of Darkness - Nullify 1"))
        self.assertFalse(self.can_reach_location("Tower of Darkness - Mission Complete"))

    def test_slot_data_matches_rules(self) -> None:
        data = self.world.fill_slot_data()
        self.assertEqual(data["mission_requirements"]["story1"], [["Cannon", "Mortar"]])
        self.assertEqual(data["location_requirements"]["Home - Nullify 1"],
                         [["Nullifier"], ["Cannon", "Mortar"]])
        # A waived cache carries no requirement at all.
        self.assertNotIn("Home - Cache 1", data["location_requirements"])

    def test_location_count(self) -> None:
        # One check per INSTANCE: 20 caches + 63 totems + 120 nullify targets,
        # plus 11 reclaim and 3 custom objectives, plus 19 rift jumps (the
        # finale's completion is the Victory event, not a location).
        real = [loc for loc in self.multiworld.get_locations(self.player) if loc.address is not None]
        self.assertEqual(len(real), 236)

    def test_every_instance_of_a_type_shares_its_rule(self) -> None:
        # Logic cannot tell one totem from another, so all of a mission's totems
        # carry the same requirement.
        data = self.world.fill_slot_data()
        reqs = data["location_requirements"]
        for i in range(1, 5):
            self.assertEqual(reqs["We Know Nothing - Totem 1"],
                             reqs[f"We Know Nothing - Totem {i}"])

    def test_optional_objectives_are_locations_too(self) -> None:
        # Not My Mars does not REQUIRE nullifying, but its two nullifiable
        # enemies are still checks - clearing a map fully is rewarded.
        names = [loc.name for loc in self.multiworld.get_locations(self.player)]
        self.assertIn("Not My Mars - Nullify 1", names)
        self.assertIn("Not My Mars - Nullify 2", names)
        self.assertNotIn("Not My Mars - Nullify 3", names)

    def test_optional_nullify_still_needs_the_nullifier(self) -> None:
        self.collect_all_but("Nullifier")
        self.assertFalse(self.can_reach_location("Not My Mars - Nullify 1"))
        self.assertTrue(self.can_reach_location("Not My Mars - Cache 1"))

    def test_instance_counts_match_the_survey(self) -> None:
        names = [loc.name for loc in self.multiworld.get_locations(self.player)]
        # War and Peace has 8 totems, the most of any mission.
        self.assertIn("War and Peace - Totem 8", names)
        self.assertNotIn("War and Peace - Totem 9", names)
        # Founders has 17 nullify targets.
        self.assertIn("Founders - Nullify 17", names)
        self.assertNotIn("Founders - Nullify 18", names)

    def test_no_mission_uses_the_hold_objective(self) -> None:
        names = [loc.name for loc in self.multiworld.get_locations(self.player)]
        self.assertFalse([n for n in names if n.endswith(" - Hold")])

    def test_archon_needs_nullifier_and_shield(self) -> None:
        # Both from hedged worksheet notes, both treated as required: an enemy
        # that shuts off energy production, and constant creeper rain.
        for missing in ("Nullifier", "Shield"):
            with self.subTest(missing=missing):
                self.assertAccessDependency(
                    ["Archon - Totem 1"],
                    [["Nullifier", "Shield"]],
                    only_check_listed=True,
                )

    def test_snipers_gate_only_where_stated(self) -> None:
        # Snipers are nice-to-have almost everywhere, and a hard requirement on
        # exactly two missions, each for a stated reason:
        #
        #   16 The Compound - saw blades "can only be killed with snipers. You
        #      need snipers to get past them. no way to do any objectives
        #      without."
        #   18 Wallis - "You need snipers to actually do this level as a hard
        #      requirement. (regardless of energy/weapons)" (2026-09-03).
        #
        # Wallis was added after the map review; before that this test asserted
        # "only 16" and it is the reason that change could not slip in quietly.
        from ..rules import mission_requirements
        MISSION_WIDE = {16, 18}
        for n in range(1, 21):
            groups = mission_requirements(n)
            has_sniper = any("Sniper" in g for g in groups)
            self.assertEqual(has_sniper, n in MISSION_WIDE,
                             f"story{n} sniper requirement")

    def test_missile_launcher_is_never_required_on_standard(self) -> None:
        # Every mention in the worksheet is a negative: "possible without
        # missles", "no need for missles". It gates nothing on standard logic -
        # under casual it becomes one half of the anti-air requirement.
        from ..rules import requirement_groups
        groups = requirement_groups(casual=False)
        for table in (groups["mission_requirements"], groups["location_requirements"]):
            for entry in table.values():
                for group in entry:
                    self.assertNotIn("Missile Launcher", group)

    def test_miner_appears_only_where_verified(self) -> None:
        # Economy is NOT outside logic any more, and the exceptions are all
        # traceable to a specific play or map review.
        #
        # The anchor still holds: Tower of Darkness was played on 2026-08-31
        # with exactly its logic requirements and no Miner - "Yes very doable
        # with no miners on Tower of Darkness. not a requirement." A later map
        # review suggested miners were needed there after all, and the designer
        # ruled that the controlled play wins (2026-09-03), so m15 stays clear.
        #
        # Where Miner IS in logic, and why:
        #
        #   3  Not My Mars       SOFT - islands, towers cannot carry the energy
        #   4  Ruins Repurposed  SOFT - same energy problem, one notch easier
        #   8  Serious           one of three OR alternatives past its wall
        #   17 Sequence          instances 3+; the RESO is the only way to push
        #   18 Wallis            all nullifies; ~20 gen without it
        #   20 Ever After        HARD, mission-wide - "you need Miners, morters
        #                        and cannons. hard requirement."
        MISSIONS = {3, 4, 8, 17, 18, 20}
        from ..rules import requirement_groups
        from ..locations import LOCATIONS_PER_MISSION
        allowed_locs = set()
        for m in MISSIONS:
            allowed_locs |= set(LOCATIONS_PER_MISSION[m])
        allowed_specs = {f"story{m}" for m in MISSIONS}
        for casual in (False, True):
            groups = requirement_groups(casual)
            for spec, entry in groups["mission_requirements"].items():
                for group in entry:
                    if "Miner" in group:
                        self.assertIn(spec, allowed_specs)
            for name, entry in groups["location_requirements"].items():
                for group in entry:
                    if "Miner" in group:
                        self.assertIn(name, allowed_locs)

    def test_energy_missions_need_a_miner_in_logic(self) -> None:
        # The positive half: asserting Miner is absent elsewhere proves nothing
        # if the rule failed to land at all.
        from ..rules import mission_requirements, location_requirements
        for mission, cache in ((3, "Not My Mars - Cache 1"),
                               (4, "Ruins Repurposed - Cache 1")):
            for casual in (False, True):
                flat = [n for g in mission_requirements(mission, casual) for n in g]
                self.assertIn("Miner", flat)
                # A waived cache stays free, or the mission can no longer open a
                # seed.
                self.assertEqual([], location_requirements(cache, mission, casual))

    def test_miner_is_physical_only_where_it_was_played(self) -> None:
        # The soft layer's whole point: on Not My Mars and Ruins Repurposed
        # logic asks for a Miner and PHYSICS DOES NOT, so those checks paint
        # yellow rather than red. If a Miner requirement leaks into the physical
        # layer there, the tracker starts calling a reachable check unreachable.
        #
        # Ever After, Sequence and Wallis are different: the designer stated
        # those as hard requirements, so a Miner there is physical on purpose.
        PHYSICAL_OK = {17, 18, 20}
        SOFT_ONLY = {3, 4}
        from ..rules import requirement_groups
        from ..locations import LOCATIONS_PER_MISSION
        soft_locs = set()
        for m in SOFT_ONLY:
            soft_locs |= set(LOCATIONS_PER_MISSION[m])
        ok_locs = set()
        for m in PHYSICAL_OK:
            ok_locs |= set(LOCATIONS_PER_MISSION[m])
        # A Miner as one option in an OR-group is an ALTERNATIVE, not a
        # requirement - Serious offers (Cannon or Terp or Miner), and holding a
        # cannon satisfies it. Only a group that is Miner ALONE means the player
        # cannot proceed without one, so that is what this asserts on.
        def required_alone(entry):
            return any(g == ["Miner"] for g in entry)

        for casual in (False, True):
            groups = requirement_groups(casual, physical=True)
            for spec, entry in groups["mission_requirements"].items():
                if required_alone(entry):
                    self.assertNotIn(spec, {f"story{m}" for m in SOFT_ONLY},
                                     f"{spec}: soft Miner leaked into physics")
            for name, entry in groups["location_requirements"].items():
                if required_alone(entry):
                    self.assertNotIn(name, soft_locs,
                                     f"{name}: soft Miner leaked into physics")
                    self.assertIn(name, ok_locs,
                                  f"{name}: unexpected physical Miner")

    def test_reclaim_always_needs_a_nullifier(self) -> None:
        # Reclaim is "clear the map", and nothing clears while a source is still
        # producing. Encoded per-mission it covered only We Were Never Alone,
        # leaving nine of eleven Reclaim checks asking for a weapon alone -
        # found when Hints' Reclaim was the last check logic thought a lone
        # Mortar could take (designer, 2026-09-03).
        #
        # Required in BOTH layers: this is physics, not a logic preference, so a
        # Reclaim check with no Nullifier is red rather than yellow.
        from ..rules import location_requirements
        from ..locations import LOCATIONS_PER_MISSION
        seen = 0
        for m in range(1, 21):
            for name in LOCATIONS_PER_MISSION[m]:
                if " - Reclaim" not in name:
                    continue
                seen += 1
                for casual in (False, True):
                    for physical in (False, True):
                        reqs = location_requirements(name, m, casual, physical)
                        self.assertTrue(
                            any(g == ["Nullifier"] for g in reqs),
                            f"{name} (casual={casual}, physical={physical}) "
                            f"does not require a Nullifier: {reqs}")
        # A loop that found no Reclaim checks would pass while asserting nothing.
        self.assertEqual(11, seen, "expected 11 Reclaim checks")

    def test_reclaim_does_not_hard_require_anti_air(self) -> None:
        # The designer's call, 2026-09-03: Nullifier type-wide, but anti-air
        # stays in the casual tier rather than becoming a hard requirement on
        # spore missions. Pinned so it cannot drift in quietly.
        from ..rules import location_requirements, DEFENSIVE
        from ..locations import LOCATIONS_PER_MISSION
        for m in range(1, 21):
            for name in LOCATIONS_PER_MISSION[m]:
                if " - Reclaim" not in name:
                    continue
                std = location_requirements(name, m, False)
                for group in std:
                    self.assertNotEqual(sorted(group), sorted(DEFENSIVE),
                                        f"{name} hard-requires anti-air")

    def test_sniper_on_tower_of_darkness_is_casual_only(self) -> None:
        # The other half of the same verdict. First pass: snipers are "a little
        # more important" on story15. Then, explicitly: "snipers are not needed,
        # but nice to haves. you can beat the level without them."
        #
        # So this is NOT a hedge to be promoted later. Compare The Compound, where
        # the note is absolute ("no way to do any objectives without") and the
        # sniper IS in logic. story15 must therefore ask for a sniper under casual
        # and NOT under standard - which is what this asserts in both directions.
        from ..rules import mission_requirements
        self.assertNotIn(["Sniper", "Missile Launcher"], mission_requirements(15, casual=False))
        self.assertIn(["Sniper", "Missile Launcher"], mission_requirements(15, casual=True))

    def test_defensive_units_are_progression_because_casual_uses_them(self) -> None:
        # Classification is per item NAME, not per seed, so anything that gates
        # under EITHER tier must be progression - otherwise a casual seed could
        # place it behind itself.
        from ..items import classification
        from BaseClasses import ItemClassification
        for name in ("Sniper", "Missile Launcher"):
            self.assertTrue(classification(name) & ItemClassification.progression)
