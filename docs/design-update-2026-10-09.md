# The Lonely Rover: where the basic game stands, 2026-10-09

For the owner. One page of state, then the plan. Numbers marked (measured) come from traces and tests on `main` fea8ff8 or the branches named; everything under "How it reads as a game" is judgement, not measurement.

## State

**Built and on `main`:** the rover is the player (185 kg body, backhoe, 0.4 kN push on Mars, never lifts). Ground is diggable and soil flows round bodies with an energy audit. Machines from historical parts run on real physics. The player builds in the rover world, joins machines with a lossless wire, saves the world, and can sleep with machines turning. Goals, tuning, front end, ending, rover log and first-run hints exist.

**Landed 2026-10-08/09:** the real 5 kWh found bank (#209), building in the rover world (#204), wire links saved with the world (#208, #82), wind heading (#193), sleep with physics running (#207), a pushable heated rock (#206), a vault that holds another machine's heat store (#211).

**Waiting on the merger's suite:** hints for the rover game (#98), found-only generators with a salvaged motor in a crate, about 1 m of cover over the bank, optional "free the bank" (branch `basic/found`), and the Heron's fountain redesign.

**In progress (merger session):** smaller banks plus a labelled charging sleep (#215), and wind you can see (#214).

**Owner choices recorded 2026-10-09:** none of #62, #66, #70 for the basic game; #205 parked; the bank is smaller by scenario number AND a sleep charges at the last settled power (labelled approximate); freeing the bank is optional with about 1 m of cover and no boulder on it (boulders stay for other crates); heating the rock in the open-lidded bin is a legitimate route and the dusk push the harder variant; generators are found-only; map wind on by default and shown, heading unchanged.

## The win path

The win is a full bank above 0 °C on the 03:00 relay pass. The route a player can take: get the salvaged motor from the crate; build a windmill and a 256:1 train (the motor charges only above about 1,500 rpm; a windmill turns at about 12); wire the motor to the bank; keep the bank in 0–45 °C with a vault and a heated rock; wait out the night; call.

**The automated route** (`racket/heroic/tests/e2e-route-test.rkt`) builds the windmill and train, links the shaft and wire, and wins at 03:00 on sol 2 with 90.0 W at 1,557 rpm, the bank full 124 s before the pass (measured). It proves the engine: build, wire, charge and win are real and match worked numbers.

**It does not prove a human can win:**
- "Sol 2" is an artefact of its 25 Wh test bank. At the design doc's 5 kWh a 90 W windmill needs about 55.6 h of charging, about 11 h of wall time at the engine's pace.
- Still stand-ins: the vault is placed by the world; the bank is not freed or pushed in; the two links are written into the save because the join gesture needs a real window; the sleep pauses the machines (a live night takes about 2.5 h of wall time).
- Nobody has played it. There is no human playtest data.

## How it reads as a game (judgement)

**Strong:** a clear, physical goal; several routes the physics decides between; failures meant to show themselves in the scene; no dead ends; poor strategies allowed. Closest neighbours: Kerbal Space Program and Besiege for learning from physical failure, The Long Dark and Subnautica for cold-and-power pressure, The Martian for improvising from the crates. It differs by having one goal, no gathering loop, a persistent world and no undo.

**Exposed:**
- Hidden requirements: the 256:1 ratio, the wire, the wall and the mirror target start unset and nothing says why a build is not charging (#212, #213).
- First ten minutes: hints now point at driving, speed, the log and build mode; not yet at the goals panel, joining, what a sleep does, or the cold bank (#216).
- Pace: one night is hours of wall time without the charging sleep; the long-horizon goal has few short-term rewards.
- Open physics games lose most first-time players without guidance; "hints from state, never scripted" fits your intent but may be too light. Only a playtest can say (#221).

## Plan

Milestone **14 First playtest of the basic game**:

| # | Item | Size |
|---|---|---|
| #215 | Bank sizes and the labelled charging sleep (merger, `mvp/bank-sleep`) | M |
| #214 | Wind you can see and siting on by default (merger, `mvp/wind-see`) | M |
| #212 | Say why the bank is not charging | M |
| #213 | A vault built by hand round the buried crate; regolith wall by default on Mars | M |
| #216 | Hints for goals, joining, sleeping and the cold bank | S |
| #217 | Save or export the rover log | S |
| #218 | A route played as a human: gui-check with real gestures, scripted join | M |
| #219 | A route over two or more sols | M |
| #220 | The Stirling route in the crater | M |
| #221 | First playtest, one human, 60–90 min (`docs/playtest-sheet.md`) | — |

**Order:** land `basic/hints` and `basic/found`; then #212, #213, #216, #217 in parallel; then #218; then re-run the route at the new bank sizes; then the playtest. #219 and #220 follow the playtest or run beside it.

**Deferred on purpose:** #62, #66, #70 (owner: revisit after playing), #205, #210.

**Questions only the owner can answer:** none open. Watch for these from the playtest: whether guidance is too light, and whether a 250–500 Wh bank feels too easy.
