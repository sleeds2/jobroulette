# Job Roulette

`Job Roulette` is a Dalamud plugin for **Final Fantasy XIV** that randomly selects one of your enabled combat jobs and switches to that job's saved gear set.

Have a lot of jobs and don't know what to play? `Job Roulette` takes a basic random job picker, such as https://wheelofnames.com/ztw-r4y randomizer, and puts the functionality directly into the game.

## Features

- Randomly selects from your enabled combat jobs.
- Switches to the selected job via that job's saved gear set.
- Configuration UI with role-grouped job toggles.

## In-game usage

- Settings panel allows a selection of jobs. All jobs are enabled by default.
- In-game slash command (`/jobroulette`) will randomly pick from the enabled jobs and switch to it.
- Add a role argument to restrict the roulette to enabled jobs in that role: `/jobroulette support`, `/jobroulette tank`, `/jobroulette healer`, `/jobroulette dps`, `/jobroulette melee`, `/jobroulette ranged`, `/jobroulette caster`, or `/jobroulette magic`
- Supports Adventurer-In-Need bonuses when roulette is specified: `/jobroulette leveling`, `/jobroulette expert`, etc.

## Requirements and notes

- At least one job must be enabled.
- Enabled jobs should have a saved gear set.
- If multiple gear sets exist for the same job, the first matching set is used.
- Non-combat jobs and limited jobs are not included in roulette selection.
- Classes are currently not supported.

## Future features
- Random glamplate switching
