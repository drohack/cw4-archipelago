# Per-mission requirements worksheet (manual playthrough)

Fill this in as you play. This is the one thing that cannot be derived from the
game data - see "Why this is manual" at the bottom.

## Setup: CW4 Dev Tools

Surveying 20 missions at real speed is not the job. `CW4DevTools` is a separate
plugin (not part of the randomizer) that removes the grind without changing what
a mission requires. Config:
`<game>/BepInEx/config/com.droha.cw4devtools.cfg`, all keys hot-toggleable:

| Key | Cheat |
|---|---|
| **F5** | Instant build - buildings finish on placement, free |
| **F6** | All buildings - ignore the campaign unlock schedule |
| **F7** | Infinite resources - energy, ammo and wares topped up |
| **F8** | Indestructible - your units cannot be destroyed |
| **F9** | Freeze creeper - nothing flows, so you can study a map safely |
| **F10** | Game speed - cycles off/2/4/8/16 (the in-game buttons stop at 4x) |
| **F11** | Reveal fog (one-shot) |
| **End** | Complete all objectives (one-shot) - leave a mission once you know |

Currently on: instant build, infinite resources, indestructible. `AllBuildings`
is OFF so you see each mission's normal schedule - press F6 when you want
everything.

A strip at the bottom centre lists EVERY option with its key, green when on and
grey when off, so the state is readable at a glance and it is never a guess
whether a mission was surveyed vanilla. All grey means vanilla.

Not shown as hotkeys but available: a file-command channel at
`<game>/BepInEx/cw4dev-commands.txt` (`boot:storyN`, `ada:close`,
`sim:run [speed]`, `spawn:<RealUnitName> [n]`, `shot:<path>`, `dump`,
`set:<cheat>=on|off`) for driving the tools without a keyboard.

Everything is scoped to your units. Enemies, creeper, terrain and objectives are
untouched, so **what a mission requires is unchanged** - which is what makes the
notes below trustworthy. Note "place anywhere" is deliberately absent: whether
somewhere is reachable without air or a porter is exactly what you are
measuring.

To play with no mod at all, move `BepInEx/plugins/CW4DevTools` out of the
plugins folder (renaming it `.disabled` does not work - BepInEx scans
subfolders).

**How to use:** play each mission and answer the questions in its block. Terse is
fine - `cannon only`, `needed terp for the ridge`, `porter mandatory`. The
"Notes" line is for anything surprising. Leave a field blank rather than
guessing; blank is information, a wrong guess is not.

The goal is the MINIMUM to finish, not what is comfortable. Where something only
makes it easier, put it under "Helped but not needed" - that feeds the
casual/normal difficulty tiers rather than core logic.

---

## Legend

- **Min units to complete** - smallest set you could have won with. Assume rift
  lab and tower are always available (they are, in the randomizer too).
- **Required for an OBJECTIVE only** - a unit needed for a specific objective
  but not to finish the mission. Nullifier is the known case.
- **Helped but not needed** - difficulty-tier material.
- **Objective notes** - anything about which objectives are actually required,
  or objectives that turned out to be optional.
- **Blockers** - anything that made the mission unwinnable or nearly so without
  a specific unit (terrain needing terp, isolated ground needing air/porter).

Known-good defaults, so you only note deviations:
- Offense is cannon or mortar. If cannon alone sufficed, just write `cannon`.
- Nullify objectives need the nullifier.
- Totems objectives need greenar (confirm the refinery was actually required).

---

## 0. Tutorial/09 Leo, 266
- Objectives require "Tower" (no rift lab needed, as one is provided), 4 cannons provided
- Only has a single objective: survive/beat the level

## 1. Farsite

- Objectives present (required): Custom
- Min units to complete: a weapon that only needs energy
- Required for an OBJECTIVE only:
- Helped but not needed:
- Objective notes: first item can get with just tower, 2nd item needs weapon to get over creep. 2nd item spawns in 2 totems
- Blockers:
- Notes: auto gives you rift lab (expect all other levels to require a rift lab to be placed at the start). only a liftic cache is available that is needed to power both totems, and the pyramid (that dispenses anticreep). you need to get rid of creeper to get to the liftic cache and totems, and pyramid. You might be able to get the 2nd item with a porter and no weapon (I'm not sure, only if that counts i don't think it does)? The 2 items are not in the objectives, but the totems are for rift jump/completion.

## 2. Home

- Objectives present (required): Nullify, Totems, Collect
- Min units to complete: weapon & nullifier
- Required for an OBJECTIVE only: an energy weapon, and towers
- Helped but not needed:
- Objective notes: Need nullifier to nullyfy the single enemy/objective
- Blockers:
- Notes:You can get the only item if you put the rift lab initially close and build a tower quick enough (no weapon needed, get before creep spreads too quick, need to test without god mode). Only need tower and weapon to get the liftic cache, and power the 2 totems (might take a minute if not nullifying, but possible)

## 3. Not My Mars

- Objectives present (required): Totems, Collect
- Objectives optional: nullify 2 enemies
- Min units to complete:
- Required for an OBJECTIVE only: tower, weapons that only need energy.
- Helped but not needed: There is RESO on the map (land to put Miner on), so you can get Bluite for sprayer.
- Objective notes: has 3 caches of liftic to power totems.
- Blockers:
- Notes: You can get the single item if you spawn in the rift lab near it and place a single tower (need to test outside of god mode, but i'm pretty sure it's possible). In the base game it does unlock pylons in this level. You can cheese it by moving the rift lab back and forth and "cheat" the connection while it's flying... but that's hard mode move. You can do this with platforms instead of pylons. and you might be able to do it with porters instead of either as well. confirmed you can move the liftic to the totems via porter.

## 4. Ruins Repurposed

- Objectives present (required): Totems, Collect
- Objectives (optional) need nullifier to nullify the 4 enemies.
- Min units to complete: 
- Required for an OBJECTIVE only:
- Helped but not needed: Factory to get bluite from RESO ground using Miners, this helps power the shield pyramids and anticreep pyramid on the map.
- Objective notes: There's a liftic cache to power the totems.
- Blockers:
- Notes: Again you can get the item by spawning in the rift lab close to it and building a single tower.

## 5. We Know Nothing

- Objectives present (required): Totems, Collect
- Objectives (optional): nullify 2 enemies
- Min units to complete:
- Required for an OBJECTIVE only: Refinery and Factory, to store liftic to give to totems. Can only get 1 totem easily, the other 3 are harder to get without good weapons. Nullifier to get optional objective.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: Again spawn rift lab and tower to get single item.there's RESO ground for miners, and a bluite crystal directly, but you still need a facotry to store it to be able to use it.

## 6. We Were Never Alone

- Objectives present (required): Reclaim
- Objectives (optional) nullify 9 enemies
- Min units to complete:
- Required for an OBJECTIVE only: I think nullifier is probably nessesary to reclaim as there's just too much to keep it under raps, but possible in hard mode not to need it.
- Helped but not needed: Missile, there's 6 spore spawners on this level. technically able to beat it without and just get close and nullify, but you'll need a lot of other weapons to defend yourself. They give off very little creep and only start shooting after the 4:30 mark of the level so if your quick and smart i think it's possible without missles, just harder.
- Objective notes:
- Blockers:
- Notes: There's RESO for Miners here to get bluite, there's a single shield pyramid in this level that needs bluite. not a lot of RESO so only sprayers i think is impossible. There are redite crystals for missles. Need factory for use of both bluite and redite. Without nullifier it's going to be really hard to reclaim without a lot of firepower, so probably not worth it.

## 7. Hints

- Objectives present (required): Totems, 1 Collect
- Objectives (optional): nullify 4 enemies, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: There's Greenar crystal, that requires refinery and factory to fill the totems.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: Again can get item if spawn in rift next to it and a tower. There's RESO for miners, probably enough to support only beating with sprayers. there's a single redit crystal for missles, 2 spore spawners, probably doable without missles. there are crimson crystals which make normal weapons harder to beat creep, but anticreep/sprayers better. There's a single AC source on the map that just needs energy, it's very slow so only really good for defence, not offence. but can get some AC, and you can move it around by sucking with Sprayers, and distributing to elsewhere (if you don't have miners, but still need a factory). Def not enough from just the AC source to support sprayers as a weapon. Spores are not targeting on this map so easier to defend without missles, and they start at 4min.

## 8. Serious

- Objectives present (required): Totems
- Objectives (optional): nullify 1 enemy, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: Need factory and refinery to get greenar and activate totems.
- Helped but not needed: TERP to build up defensive walls (i believe terps are introduced in this level)
- Objective notes:
- Blockers:
- Notes: Nothing you can easily do at the start. There's RESO for miners, there's a AC pyramid, but you need miners to get bluite, and a factory to move it around. Not needed, but nice to have. there's a single redite crystal, the only enemy is a single spore that starts at 4min, and is targeted to buildings, it's actually pretty harmless so i think you could easily beat it without missles. the main enemy is the creeper spawner around the map that needs firepower to get to and get rid of.

## 9. More and More

- Objectives present (required): 4 Totems, Collect 1
- Objectives (optional): nully 6 enemies, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: refinery, factory for totems. nullifier for enemies.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: No easy way to get the item at the start of this one. it starts under creep. there's quite a bit of space at the start, a greenar crystal (redinery, factory) to power totems. 2 free ERNs, 4 more are burried and would need a terp to dig up. a single redite crystal to fight the 2 spores, the big enemy are the 3 blob nests that require snipers to supress. They only target the creep spreader, not your towers. technically you don't need snipers. I'd say medium difficulty without snipers. Probably don't need to worry about the spore spawner that much in this one as well. There's a little RESO (miner, fatory to get bluite), but not enough to go only Sprayer. Though it wouldn't be too tough to just get the item, it's just some creep, as long as you build enough to get some weapon you could fight it back enough to get it early.

## 10. War and Peace

- Objectives present (required): Totems, Collect
- objective (optional): nullify 5 enemies, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: refinery, factory for totems. nullifier for nullifying.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: You can get the item immediately with rift lab and single tower. There's RESO for miners, and sprayers if you have factory. there's a single ERN available for free at the start. 6 more if you have a TERP. This is the first level with spores spawning EGGS. annoying, and can be dealth with with snipers, but you can also just let them blow up and deal with the creep they leave behind. There's a single greenar crystal (refinery, factory) to power the totems. You could probably just nullyfy enemies with towers and weapons. There are spores, and blob spawners. it's nice to have sniper for the blobs, but not needed, just put down buildings for them to blow up you don't care about. and the spoers are not targeted, so not much of a threat (don't need missles). their eggs are more of a threat, but medium difficulty to deal with them without snipers. This is the mid point of levels and the map difficulty starts to ramp here. 

## 11. Shattered

- Objectives present (required): Nullify 4, Collect 1
- Objectives (optional): active 3 totems
- Min units to complete:
- Required for an OBJECTIVE only: You can get 2 of the 3 totems with a refinery (greenar crystal), and factory. To get to the Enemy (nullify), and the 3rd totem you either need porter, or platform to cross space.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: Easy to collect item with rift lab and tower at start. There is a little RESO for bluite, but not enough to win with only sprayers. and a single redite crystal (facotry) to use in missles. There's 1 blob spawner, and 2 spore spwners. some eggs. would be nice to have sniper, but i think easily doable without snipers or missles. 6 ERN are burried (need TERP), not needed, but nice. Nice to have pylon as things are far away on this map. Spores are half targeted. which is good and bad. since there's so much space the creep that hits it spreads very far. so can be hard to deal with if it gets close. Medium difficulty without missles.

## 12. Archon

- Objectives present (required): 3 Totems, Collect 2
- Objectives (optional): nullfy 3, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: Both items are burried, need TERP to get.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: there's a special enemy that stops energy production (you start with 5 sattelies that give you a little). So I think super hard to do anything without a nullifier. You can get 1 of the items easily if you have a TERP. the 2nd is much harder. This level constantly rains creep on you. there's a greenar crystal (refinery, factory) to power the 3 totems, but they're out in the rain. If you have a pylon and a terp you can get the 2nd item (no weapons needed). No RESO in your safe area so can't use sprayers only. This level gets doable/not super hard mode if you get SHIELDS as it creates a safe space from the rain. They do require redite (lots of crystals) and a facotry to use. The 2 spore launches are not much of a threat, so don't need missle or sniper. Though sniper does shoot rain out of the sky before they spread creep so they are nice to have to make it a little easier.

## 13. The Experiment

- Objectives present (required): 2 Totems, Collect 1
- Objectives (optional): nullify 8
- Min units to complete:
- Required for an OBJECTIVE only:
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: You can get the item instantly, with just rift lab and tower. this map starts you with a runway, so you could just get bomber/ac bomber. There's probably enough RESO to get bluite. There's a special ERN Foarg (redite crystals, facotry) to help you out. Greenar crystal (refinery, factory) for totems. This is a tougher map, as there's a lot of starting creep, and you need to make a defence before going out. spore launchers, blob spawners. thin paths. Sniper and Missle (factory) nice to have to make easier. "might be able to do with only moarter, but cannon will make this easier. again maybe sprayer only? but that's hard mode, i don't think it's possible to defend easily with sprayer only to build up. This is one of the first levels where you really need to turtle for a minute to build up, another small spike in map difficulty.

## 14. Somewhere in Spacetime

- Objectives present (required): 4 Totems, Collect 1
- Objective (optional): nullify 8, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: Greenar crystal (refinery, factory) for totems. 
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: You can get the item immediately with rift lab and a tower. This is the first level with skimmers (stun your towers). can be dealt with, but nice to have snipers here. No RESO, but bluite crystals here. And breeder terrain for anti creep (though deep in the level). The bluite crystals are also deep in the level so can't rely on them at the beginning. blob and spores again. not the worst in this level. the skimmers are a little mroe annoying. 6 burried ERNs (TERP). Reddite crystals are easy enough to get to. Getting to the bluite crystals you can get mid game, so need at least one enegery weapon to get there.

## 15. Tower of Darkness

- Objectives present (required): Nullify 9, 4 Totems, Collect 1
- Objectives (optional): reclaim
- Min units to complete: cannon, nullifier, chronat, refinery, factory - PLAYED 2026-08-31 with exactly this set and nothing else (no miner, pylon, platform, terp, sniper, missile launcher, and no energy/storage items): "Yes very doable with no miners"
- Required for an OBJECTIVE only: Need beacon to get to the center where all enemies are. nullifier to nullify enemies. Refinery, factory for liftic for totems.
- Helped but not needed: snipers - "not needed, but nice to haves. you can beat the level without them" (so: casual tier, NOT logic, and not a hedge to promote later). pylon/platform per the notes below. miner: NOT needed, tower energy carries the opening.
- Objective notes: darkness must be lifted with beacons of light - ANSWERED, that is the CHRONAT (the mission's own intro says "We need beacons of light to lift the darkness").
- Blockers:
- Notes: First level with darkness. No easy way to get item. need to fight back creep to do it. You can beat this level without pylon/platform, but they do make it easier since it's a big map, and there's a bit of space. RESO at start for mining, but you might need it for energy at the start since there's not a lot of land for towers. once you get some of the other corners it gets a little easier. There's redite (factory) for missles here. blobs, spores, eggs, skimmers are all about eaquily bad here. so snipers and missles make it easier. Techincally might not be needed. but hard mode if not.

## 16. The Compound

- Objectives present (required): 6 Totems, Collect 1
- Min units to complete:
- Required for an OBJECTIVE only: Need TERP to get burried item, and it's deep in the level so no easy way to get it. Greenar crystal (refinery, factory) for totems.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: This level has a unique enemy, the spawning saw blades, they shoot beams to delete buildings, can only be killed with snipers. You need snipers to get past them. no way to do any objectives without. You star twith 2 free ERNs, 6 burried (TERP). A single bluite, and redite crystals (factory), and a bit of RESO, might be enough to handle sprayers, but my guess is there's too much creep for this level to be sprayer only (or at least hard mode). Lots of creep, some spore, blob, skimmers so Missles, and Snipers are nice to have.

## 17. Sequence

- Objectives present (required): Collect 2
- objectives (optional): nullify 14, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: 5 of the enemies are in darkness (beacon). The 2nd item (behind the enemies) is burried (TERP).
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: No easy item to get. you could get to 1 with some patience and weapons (there's 2 creeper spawners in the way, don't need nullifier to get past them, but makes it easier). This level gives you 3 BERTHAs, but they take a while to spool up/give energy. There's quite a bit of RESO, but you need a PYLON or PLATFORM to get there (You can use a porter to get over there as well, and port back bluite/energy from miner, but that's medium difficulty). There's also 3 Reddite crystals (FACTORY) to power missles. There's a few skimmers, spores, blobs, but nothing that you can't handle (sniper, missles are nice to have). This is the first level with Platform, the only way to get to the BERTHA is to use PLATFORM, not needed, but nice to have (pylon does not work). You don't need the platform/pylon to get to the enemies, platform makes it much easier. This needs quite a bit of firepower to get through the amount of creep produced on the level. But with platforms and a bit of firepower you can skip nullyfing and just go straight for the 2nd item (TERP), and having missles and snipers make that much easier, without i think it's hard mode. There are 6 burried ERNs on the map (TERP).

## 18. Wallis

- Objectives present (required): Collect 1
- Objectives (optional): nullify 9, 2 totems, reclaim
- Min units to complete:
- Required for an OBJECTIVE only: item is burried (TERP) and behind lots of enemies, not easily gotten. There's 1 greenar crystal (REFINERY, FACTORY) to power the totems.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: There's 3 special Wallis weapons on this map (REDDITE crystals, FACTORY) to help you. There's RESO and 3 bluite crystals, will help to power Sprayer, there's 2 purple crystals where AC helps a little, not needed. There's 2 spores, and 2 skimmers, and 1 blob. Snipers and missles (factory) help a lot. There's quite a bit of redite crystals (1 easy to get, 4 more medium to get) to power wallis, and missles. TERP can help with defence, but not needed. This again is a turtle first to establish, then push and advance.

## 19. Founders

- Objectives present (required): Collect 1, Custom
- Objectives (optional): nullify 17, 5 totems
- Min units to complete:
- Required for an OBJECTIVE only: The item is in darkness (BEACON), and burried (TERP), and behind enemy lines. You will need Platforms to get from the safe starter island to get to the enemies (pylon will not work, and porter might, but would be very hard mode). You need to nullify the 4 obelisk reactors, and 1 neutron reactor to finish the custom "End the Beginning" objective, it does take a few seconds after nullifying the neutron reactor for it to do it.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: There's 4 special enemys called obelisks, that stop your buildings from getting close to the last objective, the Neutron Reactor. To disable the Obelisks there's an Obelisk Reactor you need to nullify elsewhere on the map. There's 9 burried ERNs (TERP), 2 Reddite crystals, 3 bluite crystals, RESO for miners. 2 of the totems are on the starting/safe island and there's a Greenar crystal (REFINERY, FACTORY) that can power them easily. The other totems are on the enemy islands. I believe this is the first level with Air Sacs, really nice to have snipers to deal with their drops, and kind of nice to have missles to actually kill the sacs. There's 2 spores, 1 skimmer, and 1 blob. Again nice to have sniper and missles to help deal with those. Of the nullify enemies, 4 are the Obelisk Reactors, and 1 is the Neutron Reactor?

## 20. Ever After

- Objectives present (required): Nullify 2, 3 Totems, Reclaim, Custom (life)
- Min units to complete:
- Required for an OBJECTIVE only: REFINERY, FACTORY for totems. Nullifier for enemies.
- Helped but not needed:
- Objective notes:
- Blockers:
- Notes: There's only a blob and spore launcher. The main threat is just so much creep and ground that spawns it. There are 3 redite crystals for missles. 8 burried ERNs. and some RESO and starting energy, but not a lot of space at the start. Nothing easy to do at the beginning and all objectives are required to survive. It's a hard map, but not good for a finale. I would say currently lock Founders (19) as the finale, and have this as an additional level. You can porter to 5 of the ERNs and the 3 reddit crystals to get their resources. Then take a minute to get to the greenite and other 3 erns. I don't actually see the spores shoot any spores, just eggs. so no need for missles. snipers help with the eggs, and blobs. Probably don't have this connected to the final level 19, have it connected to level 18 as a new branch and it can be to the right of Wallis instead of underneath it.

---

What would be nice is if you already collected the item, powered the totem that they be collected/powered on your next re-visit. They will be if you start your save, but if you re-start the mission to do better it might be nice for them to be fulfilled. not sure if that's easy to do or messes up any triggers in the mission.

Other filler items: bonus storage (up by 50 per), bonus base/rift lab generation (add starting value each time? should be like 1 at a time, or mabye 0.5, and could ramp up slowly. the extra few energy at the start helps build out towers to get more energy, then you start needing like a few more to do better.).

Reminder that BERTHA, AIR SHIP, and SWEEPER are powerful, but take a long time to build, not things to only win with.

Burried ERNs are not needed, but make it a little easier if you get them. You can use them to power weapons to make them better, or put them in the ERN Port to generally make things better.

---

## What is already derived - do NOT count these by hand

Measured from the game (2026-08-27). The objective panel's "0/N" denominator is
the live count of the relevant units, so these ARE the objective targets:

| # | Title | caches (Collect) | totems | nullifiable | rift lab pre-placed |
|---|---|---|---|---|---|
| 1 | Farsite | 2 | 0 | 0 | **YES** |
| 2 | Home | 1 | 2 | 1 | no |
| 3 | Not My Mars | 1 | 4 | 2 | no |
| 4 | Ruins Repurposed | 1 | 4 | 4 | no |
| 5 | We Know Nothing | 1 | 4 | 2 | no |
| 6 | We Were Never Alone | 0 | 0 | 9 | no |
| 7 | Hints | 1 | 3 | 4 | no |
| 8 | Serious | 0 | 2 | 1 | no |
| 9 | More and More | 1 | 4 | 6 | no |
| 10 | War and Peace | 1 | 8 | 5 | no |
| 11 | Shattered | 1 | 3 | 4 | no |
| 12 | Archon | 2 | 3 | 3 | no |
| 13 | The Experiment | 1 | 2 | 8 | no |
| 14 | Somewhere in Spacetime | 1 | 4 | 8 | no |
| 15 | Tower of Darkness | 1 | 4 | 9 | no |
| 16 | The Compound | 1 | 6 | 12 | no |
| 17 | Sequence | 2 | 0 | 14 | no |
| 18 | Wallis | 1 | 2 | 9 | no |
| 19 | Founders | 1 | 5 | 17 | no |
| 20 | Ever After | 0 | 3 | 2 | no |
| | **total** | **20** | **63** | **120** | 1 mission |

Cross-checked: every REQUIRED counting objective has a non-zero target, so
nothing here is hidden behind mid-mission spawning.

**RE-VERIFIED 2026-08-30, independently.** These counts now drive 236 location
ids, which cannot move once a seed exists, so they were re-measured by booting
all 20 missions afresh and reading `gs.totems` / `gs.nullifiableUnits` /
`gs.maxMustCollect` again (CW4DevTools `obj:dump`). Every mission matched the
table exactly.

They also match a THIRD source: the manual notes above. Every nullify count
written by hand while playing - 9 on We Were Never Alone, 4 on Shattered, 14 on
Sequence, 17 on Founders, and the rest - agrees with both machine surveys, as do
the totem and cache counts. Three independent sources agreeing is about as good
as this gets without shipping a seed.

**The one residual risk, stated plainly.** These are START-of-mission counts. If
a mission spawns more nullifiable units later, the extra ones simply have no
check - wasted content, harmless. The dangerous direction would be a check that
can never be sent, which would happen if a counted target could be removed
without the objective counter advancing (a nullifiable unit destroyed by creeper
rather than nullified, say). That has not been observed, but it has not been
disproved either, and it is the thing to watch for in the first real playthrough:
a mission that cannot reach its full count leaves dead locations behind. The required-objective list
read straight from `MissionObjectiveData` also matches the earlier independent
survey exactly.

Custom objective names, for reference: story1 `--OFFLINE--`,
story19 `End the Beginning`, story20 `Life...` (truncated in the log).

## Cross-mission questions

ANSWERED 2026-08-30 from the notes above; the logic in `apworld/cw4/rules.py` is
built from them. See randomizer-design.md, "Per-mission logic, derived from the
playthrough", for the resulting tables.

1. Greenar for totems - ANSWERED. Missions 2, 3 and 4 run their totems off loose
   liftic caches, no refinery. Everywhere else names refinery + factory.
2. Porter - ANSWERED. Never the only way; appears once as an alternative to
   Platform on Shattered.
3. Terp - ANSWERED. Required for buried caches on 12, 16, 17, 18, 19.
4. Sprayer / bluite - ANSWERED. Never sufficient alone on any map.
4b. Sniper, Missile Launcher, Shield - ANSWERED 2026-08-31. Sniper gates The
   Compound and nothing else; Missile Launcher is required nowhere (all fourteen
   mentions are negatives); Shield is required on Archon, treated the same way as
   the Nullifier hedge on that same mission. The many nice-to-have notes are
   difficulty-tier material, not logic.
5. Reactor / SuperTower / DeliveryPad - ANSWERED as a NAMING question, still
   unmodelled as buildings. These are BUTTON object names, not units: PYLON's
   button is SuperTowerButton, MINER's is ReactorButton, PORTER's is
   DeliveryPadButton. So they are not three unmodelled extra buildings at all -
   they are three buttons the mod already drives through pylonAvailable,
   minerAvailable and porterAvailable.
6. Power zones - ANSWERED. There are none in the campaign, and that is now proven
   rather than assumed: two independent readers agree at zero, and writing three
   cells with SetPowerZone makes both report three. The "bright blue reactor
   ground" was almost certainly RESO, misread through the ReactorButton name.
7. Miner / economy - ANSWERED. Tower of Darkness was played with exactly its logic
   requirements and no Miner, Pylon, Platform, Terp or energy items: "Yes very
   doable with no miners on Tower of Darkness. not a requirement. the snipers are
   a little more important." Clarified: "snipers are not needed, but nice to
   haves. you can beat the level without them." Economy stays out of logic;
   snipers stay in the casual tier and are explicitly not a hedge.
8. Air units - ANSWERED. Never the only way to reach anything.
9. Rift lab pre-placed - still only story1.

Original list below.

1. **Greenar Refinery for totems** - is the refinery actually required to feed
   totems, or do greenar drones deliver without it? Missions 2, 3 and 4 have
   Totems objectives but no greenar-mother on the map, so is greenar coming from
   loose crystals there? (The survey only counted greenar-mothers, not crystals.)
2. **Porter** - which missions genuinely cannot be finished without it?
3. **Terp** - which missions need terrain shaping to win, not just to make it
   easier?
4. **Sprayer / bluite** - on missions with bluite, is a sprayer ever the only
   viable answer? Bluite deposits exist on 5, 14, 16, 18, 19.
5. **Reactor, SuperTower, DeliveryPad** - these three are in the build menu but
   the mod does not model them at all. Do they unlock through campaign
   progression like other units? If so, resetting progression per slot may mean
   a player never gets them. Are any needed to win a mission?
6. **Power zones** (bright blue reactor ground) - do any Farsite missions have
   it? An automated scan read zero on all 20 and is unverified.
   ANSWERED: no, and the scan is now verified by a positive control.
7. **Miner / economy** - which missions actually demand mining, versus just
   running on tower energy?
   ANSWERED: none of them. Tower of Darkness, the only candidate, plays fine
   without a Miner.
8. **Air units** - any mission where air is the only way to reach something?
9. **Rift lab pre-placed** - only story1 measured as pre-placed, checked ~24s
   after load. You mentioned "some levels" - if you spot another, note it here,
   because it decides the starter set if Rift Lab becomes an item.

## Why this is manual

Two things are derivable from the game and are already recorded in
[randomizer-design.md](../randomizer-design.md): the vanilla unlock schedule
(which units the campaign grants by mission N) and the per-mission resource and
objective data. Neither answers what a mission *requires*.

Availability is only an upper bound - a mission cannot need a unit vanilla never
grants by then, but it can leave everything it grants unused. An attempt to
derive per-mission availability at runtime also failed: CW4 appears to unlock
units by global campaign progression rather than per mission, so every mission
booted on a fresh profile reports the same starting set.

So the minimum-to-win set has to come from playing. Everything else is in place
around it.
