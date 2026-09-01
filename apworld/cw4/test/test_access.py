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
        from ..rules import objective_requirements
        groups = objective_requirements(19, 4)  # Founders - Collect
        self.assertIn(["Greenar Refinery"], groups)
        self.assertIn(["Factory"], groups)

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

    def test_snipers_gate_only_the_compound(self) -> None:
        # Your notes say snipers are nice-to-have everywhere except The
        # Compound, whose saw blades "can only be killed with snipers".
        from ..rules import mission_requirements
        for n in range(1, 21):
            groups = mission_requirements(n)
            has_sniper = any("Sniper" in g for g in groups)
            self.assertEqual(has_sniper, n == 16, f"story{n} sniper requirement")

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

    def test_miner_gates_nothing(self) -> None:
        # Economy is deliberately outside logic. Tower of Darkness was the one
        # mission where the worksheet suspected mining might be needed for energy
        # ("not a lot of land for towers"), so it was played with EXACTLY its logic
        # requirements granted and no Miner, no Pylon, no Platform, no Terp and no
        # energy items at all. Designer's verdict, 2026-08-31:
        #
        #   "Yes very doable with no miners on Tower of Darkness. not a
        #    requirement. the snipers are a little more important."
        #
        # So Miner stays a filler item that gates nothing. If a future change puts
        # it into logic, this test is where the counter-evidence has to be argued.
        from ..rules import requirement_groups
        for casual in (False, True):
            groups = requirement_groups(casual)
            for table in (groups["mission_requirements"], groups["location_requirements"]):
                for entry in table.values():
                    for group in entry:
                        self.assertNotIn("Miner", group)

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
