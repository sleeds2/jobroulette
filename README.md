# Job Roulette Dalamud Plugin

`JobRoulettePlugin` is a Dalamud plugin for **Final Fantasy XIV** that randomly selects one of your enabled combat jobs and switches to that job's saved gear set.

## Features

- Randomly selects from your enabled combat jobs.
- Switches to the selected job via that job's saved gear set.
- In-game slash command (`/jobroulette`).
- Configuration UI with role-grouped job toggles.
- One-click `Enable All` / `Disable All` controls.

## In-game usage

- Slash command: `/jobroulette`
- Behavior:
  1. Reads enabled jobs from plugin configuration.
  2. Picks one enabled job at random.
  3. Finds the first saved gear set whose class job matches that selection.
  4. Dispatches `/gearset change X` for that gear set index.

## Configuration

Open plugin settings from Dalamud's plugin configuration UI.

Jobs are grouped by role:

- Tank
- Healer
- Melee DPS
- Ranged Physical DPS
- Magical Ranged DPS

Per-job checkboxes are saved immediately when toggled.

## Feedback and failure modes

The plugin prints chat feedback for key outcomes:

- Success includes selected job name and gear set number.
- Errors are shown when:
  - no jobs are enabled,
  - no matching gear set exists for the selected job,
  - gear-set command dispatch fails.

## Requirements and notes

- At least one job must be enabled.
- Enabled jobs should have a saved gear set.
- If multiple gear sets exist for the same job, the first matching set is used.
- Non-combat jobs are not included in roulette selection.

