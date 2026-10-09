# Playtest sheet: the Lonely Rover basic game (#221)

One person, the easy scenario, 60–90 minutes, thinking aloud. The automated route proves the engine; this finds where a first-time player stalls. Write down what happens, not what should have. Observations are notes for the owner, not rules.

## Before

- [ ] Land `main` with the merged improvements; note the commit: ________
- [ ] Player has not seen the design doc or the route. Say only: "You are a rover on Mars. Get a message home. Talk out loud."
- [ ] Launch the easy world with `HEROIC_TRACE=<path> HEROIC_TRACE_DT=10` (and `GOALS_REPORT=1`); screen recording on.
- [ ] Hints on (default). Do not help. If stuck 5 minutes, ask "what are you trying to do?" and note the answer; give a hint only after 10 minutes and write it down.
- [ ] Note the player's background: games played ________; engineering or physics? ________

## Clock (minutes from the end of the opening)

| Moment | Time | Notes |
|---|---|---|
| First moves the rover | | |
| First opens the rover log (I) | | |
| First opens goals (F2) | | |
| First opens build mode | | |
| First part placed | | |
| First presses J (join) | | |
| First joins two parts | | |
| First changes the speed | | |
| First sleeps | | |
| Notices the bank / generator state | | |
| Wire glows (success) | | |

Success: the wire glows within 45 minutes with no help.

## Watch for

- Time to the first built part, to J, to F2.
- Backhoe presses on the slide before giving up on digging out the bank. Did they think it was required?
- What they say when the bank reads about −63 °C.
- Whether they sleep and notice nothing charged. What do they conclude?
- Do they find the salvaged motor crate? How long?
- Do they work out the gear ratio, or build a windmill that turns too slowly and not know why?
- Do they try heating the rock, and how (in the bin, or by pushing at dusk)?
- Where they read the screen versus the scene; any text they ignore or misread.
- Fast-forward use: which speed, and do they like waiting.
- Any moment they say "what do I do now?" or "why isn't it working?" Write the exact words and the screen state.

## After (ask, don't lead)

1. In your own words, what were you trying to do?
2. What was the most confusing moment?
3. What felt good?
4. Did you ever feel you could not make progress? When?
5. Would you play again? What would make you?

## Record

- Rover log exported (#217): ________
- Trace file: ________   Recording: ________
- Three biggest stalls, in order: 1. ________ 2. ________ 3. ________
- Anything that looked like a bug (not a design choice): ________
- Anything the owner should decide: ________
