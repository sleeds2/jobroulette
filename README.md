# Job Roulette

`Job Roulette` is a Dalamud plugin for **Final Fantasy XIV** that randomly selects one of your enabled combat jobs and switches to that job's saved gear set.

Have a lot of jobs and don't know what to play? `Job Roulette` will pick one based on a number of settings you can control!

## Features

- Randomly selects from your enabled combat jobs.
- Switches to the selected job via that job's saved gear set.
- Configuration UI with role-grouped job toggles.
- Option to randomly select a glamour plate apply after switching to the selected job.


## In-game usage

- Settings panel allows a selection of jobs. All jobs are enabled by default.
- In-game slash command (`/jobroulette`) will randomly pick from the enabled jobs and switch to it.
- Add a role argument to restrict the roulette to enabled jobs in that role, e.g. `/jobroulette tank` or `/jobroulette support`
  - All Supports (tanks and healers): `support`
  - Tanks: `tank`
  - Healers: `healer`
  - All DPS (melee, physical ranged, caster): `dps`
  - Melee: `melee`
  - Physical Ranged: `ranged`
  - Caster: `caster`, or `magic`
- Supports Adventurer-In-Need bonuses when roulette is specified, e.g. `/jobroulette trials`
  - Leveling Roulette: `leveling, level, ldr`
  - Trials: `trial`, `trials`
  - Main Scenario: `mainscenario`, `main`, `msq`, `ms`
  - Alliance Raids: `alliance`, `allianceraid`, `allianceraids`, `araid`, `ally`
  - Normal Raids: `normal`, `normalraid`, `normalraids`, `nraid`
  - Mentor: `mentor`
  - Expert: `expert`, `ex`, `exdr`
  - High-Level Dungeons: `highlevel`, `high`, `hl`
  - Level Cap Dungeons: `levelcap`, `levelcapdungeons`, `cap`
  - Guildhests: `guildhest`, `guildhests`
- Toggle random glamplate switching via `/jobroulette glam` command or the settings window.

## Requirements and notes

- At least one job must be enabled.
- Enabled jobs should have a saved gear set.
- If multiple gear sets exist for the same job, the first matching set is used.
- Non-combat jobs and limited jobs are not included in roulette selection.
- Classes are currently not supported.

## Future features
- Random glamplate switching
