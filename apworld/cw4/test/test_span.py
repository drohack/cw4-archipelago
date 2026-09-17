"""The SPAN Experiments toggle: what a mixed seed contains, and what it cannot.

THE INVARIANT THAT MATTERS MOST is that the option changes what a SEED holds and
never what EXISTS. Archipelago computes `location_name_to_id` and
`item_name_to_id` once per class, not once per yaml, so every SPAN location and
item name is present whether or not anybody turns this on. Anything that made
those tables depend on the option would break every existing seed the moment
somebody flipped it - see TestLocationIdsNeverMove, which pins the other half.
"""
from . import bases


class TestSpanOff(bases.CW4TestBase):
    """The default. Nothing about a campaign seed may move because SPAN exists."""

    def test_the_roster_is_the_campaign(self) -> None:
        self.assertEqual(list(range(1, 21)), list(self.world.mission_roster))

    def test_no_span_mission_is_instantiated(self) -> None:
        from ..items import SPAN_MISSION_TITLES
        titles = {SPAN_MISSION_TITLES[n] for n in SPAN_MISSION_TITLES}
        for location in self.multiworld.get_locations(self.player):
            prefix = location.name.split(" - ", 1)[0]
            self.assertNotIn(prefix, titles,
                             f"{location.name} exists in a campaign-only seed")

    def test_no_span_unlock_is_in_the_pool(self) -> None:
        from ..items import SPAN_MISSION_UNLOCK_ITEMS
        names = {item.name for item in self.multiworld.itempool}
        self.assertEqual(set(), names & set(SPAN_MISSION_UNLOCK_ITEMS))

    def test_the_names_still_all_exist(self) -> None:
        # The other half of the invariant: the ids are class-level, so turning
        # the option off must not remove a single name from either table.
        from ..items import ITEM_NAME_TO_ID, SPAN_MISSION_UNLOCK_ITEMS
        from ..locations import LOCATION_NAME_TO_ID, SPAN_MISSION_NUMBERS
        from ..locations import location_names_for_mission
        for name in SPAN_MISSION_UNLOCK_ITEMS:
            self.assertIn(name, ITEM_NAME_TO_ID)
        for mission in SPAN_MISSION_NUMBERS:
            for name in location_names_for_mission(mission):
                self.assertIn(name, LOCATION_NAME_TO_ID)


class TestSpanOn(bases.CW4TestBase):
    options = {"span_missions": 1}

    def test_the_roster_is_twenty_missions(self) -> None:
        roster = list(self.world.mission_roster)
        self.assertEqual(20, len(roster))
        self.assertEqual(len(roster), len(set(roster)))

    def test_founders_is_always_in_the_roster(self) -> None:
        # The goal is Founders whatever else the seed draws. A roster without it
        # has no Victory event and nothing to complete.
        from ..locations import FINAL_MISSION
        self.assertIn(FINAL_MISSION, self.world.mission_roster)

    def test_the_seed_actually_drew_some_span(self) -> None:
        # 26 of the 45 candidates are SPAN, so drawing 19 and getting none is
        # about 1 in 10^6. A test that passed with zero would be testing nothing.
        spanned = [n for n in self.world.mission_roster if n >= 21]
        self.assertTrue(spanned, "no SPAN mission was drawn - the option did nothing")

    def test_at_least_one_starter_is_a_campaign_mission(self) -> None:
        # The whole SPAN roster holds ONE cache, so it cannot reliably supply an
        # opening. A seed whose only starter has a creep-covered cache has
        # nothing reachable, and creep coverage is exactly what the SPAN survey
        # cannot see. Anchoring the first starter in the campaign makes that
        # impossible rather than merely unlikely.
        campaign = [n for n in self.world.starter_missions if n <= 20]
        self.assertTrue(campaign,
                        f"every starter is a SPAN map: {self.world.starter_missions}")

    def test_every_starter_is_in_the_roster(self) -> None:
        # A starter outside the roster is a mission with no region, so its
        # "already unlocked" status points at nothing.
        roster = set(self.world.mission_roster)
        for mission in self.world.starter_missions:
            self.assertIn(mission, roster)

    def test_only_roster_missions_have_locations(self) -> None:
        from ..items import ALL_MISSION_TITLES
        allowed = {ALL_MISSION_TITLES[n] for n in self.world.mission_roster}
        for location in self.multiworld.get_locations(self.player):
            prefix = location.name.split(" - ", 1)[0]
            self.assertIn(prefix, allowed,
                          f"{location.name} is not in this seed's roster")

    def test_only_roster_missions_have_an_unlock(self) -> None:
        from ..items import ALL_MISSION_TITLES
        starters = set(self.world.starter_missions)
        want = {f"Mission Unlock: {ALL_MISSION_TITLES[n]}"
                for n in self.world.mission_roster if n not in starters}
        # THE POOL IS NOT THE WHOLE STORY. bootstrap_opening and
        # place_own_progression both PLACE items and remove them from the pool,
        # so an unlock that has already been put somewhere is absent from
        # itempool and entirely present in the seed. Reading the pool alone
        # reported two missing unlocks on a seed that had neither problem.
        got = {item.name for item in self.multiworld.itempool
               if item.name.startswith("Mission Unlock: ")}
        got |= {loc.item.name
                for loc in self.multiworld.get_locations(self.player)
                if loc.item is not None
                and loc.item.name.startswith("Mission Unlock: ")}
        self.assertEqual(want, got)

    def test_slot_data_carries_the_roster_by_specifier(self) -> None:
        from ..locations import mission_specifier
        data = self.world.fill_slot_data()
        self.assertEqual([mission_specifier(n) for n in self.world.mission_roster],
                         data["mission_roster"])
        self.assertTrue(data["span_missions"])
        # Every specifier in the roster has a title and a requirement entry, or
        # the plugin cannot name the planet or paint its tracker.
        for specifier in data["mission_roster"]:
            self.assertIn(specifier, data["mission_titles"])
            self.assertIn(specifier, data["mission_requirements"])
            self.assertIn(specifier, data["required_objectives"])

    def test_slot_data_holds_only_this_seed(self) -> None:
        # Sending all 46 missions' requirements would be a silent bloat and
        # would let the tracker paint a mission the seed does not contain.
        data = self.world.fill_slot_data()
        self.assertEqual(20, len(data["mission_requirements"]))
        self.assertEqual(20, len(data["required_objectives"]))


class TestSpanSpecifiers(bases.CW4TestBase):
    """Identity, which is where a mismatch would be silent rather than loud."""

    def test_campaign_specifiers_are_unchanged(self) -> None:
        from ..locations import mission_specifier
        for n in range(1, 21):
            self.assertEqual(f"story{n}", mission_specifier(n))

    def test_span_specifiers_are_the_map_guids(self) -> None:
        from ..locations import mission_specifier
        from ..span_data import SPAN_MISSIONS
        for n, (guid, _title) in SPAN_MISSIONS.items():
            self.assertEqual(guid, mission_specifier(n))

    def test_every_specifier_is_unique(self) -> None:
        from ..locations import ALL_MISSION_NUMBERS, mission_specifier
        specifiers = [mission_specifier(n) for n in ALL_MISSION_NUMBERS]
        self.assertEqual(len(specifiers), len(set(specifiers)))

    def test_titles_do_not_collide_across_families(self) -> None:
        # A title is the location-name prefix AND the unlock item name, so a
        # collision would merge two missions' checks without an error anywhere.
        from ..items import ALL_MISSION_TITLES
        self.assertEqual(46, len(ALL_MISSION_TITLES))
        self.assertEqual(46, len(set(ALL_MISSION_TITLES.values())))

    def test_every_span_mission_has_rules(self) -> None:
        from ..locations import ALL_REQUIRED_OBJECTIVES, SPAN_MISSION_NUMBERS
        from ..rules import mission_complete_requirements, mission_requirements
        for n in SPAN_MISSION_NUMBERS:
            self.assertIn(n, ALL_REQUIRED_OBJECTIVES)
            # Completing any mission needs at least a weapon. An empty rule here
            # would mean a mission that logic thinks is free.
            self.assertTrue(mission_requirements(n))
            self.assertTrue(mission_complete_requirements(n))

    def test_span_totems_require_the_factory_where_measured(self) -> None:
        from ..rules import requirements_for_kind
        from ..span_data import SPAN_TOTEMS_NEED_FACTORY
        for n, needs in SPAN_TOTEMS_NEED_FACTORY.items():
            groups = requirements_for_kind(n, "Totems")
            has_factory = any("Factory" in group for group in groups)
            self.assertEqual(needs, has_factory,
                             f"mission {n} totems: factory={has_factory}, measured {needs}")


class TestSpanRosterBreadth(bases.CW4TestBase):
    """A mixed roster must not open narrower than the campaign does.

    THIS IS THE FIX FOR A MEASURED FAILURE, not a precaution. Over 10,000 SPAN
    seeds with the retry cap raised to 25 so the real tail was visible, one seed
    needed SEVEN attempts - it would have failed at the shipped cap of 5, where
    the campaign's worst in 20,000 is 4. Correlating depth against seed shape
    over 4,000 more seeds found exactly one driver: weapon breadth, how many of
    the roster's checks the first weapon opens.

        depth 1  3888 seeds  mean breadth 10.5
        depth 2    98 seeds  mean breadth  8.9
        depth 3    13 seeds  mean breadth  8.0
        depth 4     1 seed   mean breadth  5.0

    Opening width and early-mission candidate count barely moved. Six of the
    4,000 seeds had ZERO breadth - the first weapon opened nothing anywhere.
    """
    options = {"span_missions": 1}

    def test_the_roster_clears_the_floor(self) -> None:
        from ..items import MIN_ROSTER_BREADTH, roster_breadth
        from ..rules import is_casual
        breadth = roster_breadth(self.world.mission_roster, is_casual(self.world))
        self.assertGreaterEqual(breadth, MIN_ROSTER_BREADTH)

    def test_the_floor_is_the_campaigns_own_breadth(self) -> None:
        # Stated as "never narrower than the campaign" rather than as a number,
        # so a logic change that moves the campaign's breadth moves the floor
        # with it instead of leaving an invented constant behind.
        from ..items import MIN_ROSTER_BREADTH, roster_breadth
        self.assertEqual(roster_breadth(range(1, 21)), MIN_ROSTER_BREADTH)

    def test_the_early_mission_is_always_granted(self) -> None:
        # It used to draw only from STARTER_ELIGIBLE inside the roster, and on
        # 1.1 percent of mixed seeds that set was empty, so it silently granted
        # nothing. The bootstrap path owns the narrow openings; outside it there
        # must always be a grant.
        from ..items import bootstrap_threshold, opening_width
        if opening_width(self.world) < bootstrap_threshold(self.world):
            self.skipTest("bootstrap_opening owns this seed's opening")
        early = self.multiworld.local_early_items[self.player]
        self.assertTrue([name for name in early
                         if name.startswith("Mission Unlock: ")],
                        "no mission unlock was pulled early")


class TestEarlyMissionFallback(bases.CW4TestBase):
    """The fallback path in force_early_mission, forced rather than waited for.

    IT SHIPPED BROKEN ONCE. The fallback picks from the whole roster, which can
    hold a SPAN mission, and the name was still being built from the campaign's
    own title table - a KeyError on 2.5 percent of mixed seeds. The suite passed
    anyway, because whether a seed reaches the fallback at all depends on its
    draw, and none of the fixture seeds did. A bulk generation run found it in
    200 seeds.

    So this constructs the condition instead of hoping for it.
    """
    options = {"span_missions": 1}

    def test_a_span_only_roster_still_grants_an_early_mission(self) -> None:
        from ..items import STARTER_ELIGIBLE, force_early_mission
        from ..locations import SPAN_MISSION_NUMBERS

        from ..items import bootstrap_threshold, opening_width

        world = self.world
        early = self.multiworld.local_early_items[self.player]
        real_roster, before = list(world.mission_roster), dict(early)
        real_starters = list(world.starter_missions)
        try:
            # A roster with nothing starter-eligible left in it, which is
            # exactly what the fallback exists for.
            world.mission_roster = list(SPAN_MISSION_NUMBERS[:20])
            self.assertEqual(
                set(), set(world.mission_roster) & set(STARTER_ELIGIBLE),
                "the premise failed: this roster has a starter-eligible mission")

            # THE OTHER HALF OF THE PREMISE, and leaving it to chance made this
            # test FLAKY - 2 failures in 20 runs, measured 2026-09-17, and it
            # had been so since it was written. force_early_mission returns
            # EARLY when opening_width is below bootstrap_threshold, because
            # bootstrap_opening owns those slots. opening_width sums over
            # world.starter_missions, which this test did not substitute - so it
            # was whatever that seed happened to draw, and on the draws that came
            # out narrow the function correctly did nothing and the assertion
            # below blamed the fallback for it.
            #
            # Missions 2 and 3 each carry exactly one cache collectable with a
            # rift lab and a single tower, so this is a width of two, which
            # clears the non-casual threshold. Neither is in the SPAN roster
            # above, so the fallback branch is still the one under test.
            world.starter_missions = [2, 3]
            self.assertGreaterEqual(
                opening_width(world), bootstrap_threshold(world),
                "the premise failed: force_early_mission would return early and "
                "this test would be blaming the fallback for not running")

            # Cleared so that ANY grant is a new one. Generation has already put
            # a mission unlock in here, and filtering against a stale snapshot
            # would hide a real grant that happened to pick the same mission.
            early.clear()

            force_early_mission(world)          # must not raise
            granted = [name for name in early if name.startswith("Mission Unlock: ")]
            self.assertTrue(granted, "the fallback granted nothing")
        finally:
            # RESTORED, because the other tests on this class share the world.
            # Leaving a SPAN-only roster behind broke the inherited reachability
            # test, which passed in isolation and failed in a full run - the
            # worst shape a test failure can take.
            world.mission_roster = real_roster
            world.starter_missions = real_starters
            early.clear()
            early.update(before)


class TestSlotOrder(bases.CW4TestBase):
    """Where each mission sits in the level-select spiral.

    Missions are OPEN, so this decides nothing about what is playable. It
    decides whether the spiral still reads as building toward the finale. A
    plain ascending sort loses that the moment SPAN is on: mission numbers
    21..46 all sort after Founders, so the goal lands mid-spiral with ten maps
    drawn after it.
    """
    options = {"span_missions": 1}

    def test_the_goal_sits_in_slot_nineteen(self) -> None:
        from ..items import GOAL_SLOT
        from ..locations import FINAL_MISSION
        roster = list(self.world.mission_roster)
        self.assertEqual(FINAL_MISSION, roster[GOAL_SLOT - 1])

    def test_the_goal_is_not_in_the_stranded_slot(self) -> None:
        # Slot 20 is the position vanilla parks off the map, and FinalePlacement
        # moves whatever lands there onto the map as a SIDE BRANCH off slot 19.
        # The goal hanging off its own branch would be nonsense.
        from ..locations import FINAL_MISSION
        self.assertNotEqual(FINAL_MISSION, list(self.world.mission_roster)[19])

    def test_everything_else_is_ascending(self) -> None:
        from ..locations import FINAL_MISSION
        rest = [n for n in self.world.mission_roster if n != FINAL_MISSION]
        self.assertEqual(sorted(rest), rest)


class TestSlotOrderWithoutSpan(bases.CW4TestBase):
    def test_a_campaign_seed_is_unchanged(self) -> None:
        # 1..20 in order, exactly as the untouched game shows it - so the
        # retarget is a no-op on a campaign seed and the ordering rule above
        # cannot have moved anything.
        self.assertEqual(list(range(1, 21)), list(self.world.mission_roster))
