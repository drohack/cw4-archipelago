from .bases import CW4TestBase


class TestAccess(CW4TestBase):
    def test_tutorial_reachable_with_nothing(self) -> None:
        self.assertTrue(self.can_reach_location("Farsite - Custom"))
        self.assertTrue(self.can_reach_location("Farsite - Mission Complete"))

    def test_missions_need_their_unlock(self) -> None:
        self.assertAccessDependency(
            ["Home - Totems", "Home - Mission Complete"],
            [["Mission Unlock: Home"]],
            only_check_listed=True,
        )

    def test_missions_need_offense(self) -> None:
        self.assertAccessDependency(
            ["Home - Totems", "Wallis - Collect"],
            [["Cannon"], ["Mortar"]],
            only_check_listed=True,
        )

    def test_nullify_objectives_need_nullifier(self) -> None:
        self.assertAccessDependency(
            ["Home - Nullify", "Shattered - Nullify", "Ever After - Nullify"],
            [["Nullifier"]],
            only_check_listed=True,
        )

    def test_non_nullify_objectives_do_not_need_nullifier(self) -> None:
        self.collect_all_but("Nullifier")
        self.assertTrue(self.can_reach_location("Home - Totems"))
        self.assertFalse(self.can_reach_location("Home - Nullify"))

    def test_slot_data_matches_rules(self) -> None:
        data = self.world.fill_slot_data()
        self.assertEqual(data["mission_requirements"]["story1"], [])
        self.assertEqual(data["mission_requirements"]["story2"], [["Cannon", "Mortar"]])
        self.assertEqual(data["location_requirements"]["Home - Nullify"], [["Nullifier"]])
        self.assertNotIn("Home - Totems", data["location_requirements"])
        self.assertEqual(data["starter_missions"], ["story1"])

    def test_location_count(self) -> None:
        # 39 required objectives + 19 mission completes (the finale's
        # completion is the Victory event, not a location)
        real = [loc for loc in self.multiworld.get_locations(self.player) if loc.address is not None]
        self.assertEqual(len(real), 58)
