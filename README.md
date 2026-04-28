# Job Roulette Dalamud Plugin

`JobRoulettePlugin` is a Dalamud plugin that randomly chooses one of your enabled combat jobs and switches to that job's saved gear set.

## Project layout

- `JobRoulettePlugin/JobRoulettePlugin.csproj` - plugin project file.
- `JobRoulettePlugin/Plugin.cs` - plugin entry point, slash command registration, random selection, gear-set switching.
- `JobRoulettePlugin/Configuration.cs` - persisted plugin settings.
- `JobRoulettePlugin/Windows/ConfigWindow.cs` - settings UI.

## Build

1. Install a recent .NET SDK that supports `net8.0-windows`.
2. Restore and build:

   ```bash
   dotnet restore JobRoulettePlugin/JobRoulettePlugin.csproj
   dotnet build JobRoulettePlugin/JobRoulettePlugin.csproj -c Release
   ```

3. Package/load with your preferred Dalamud development workflow.

## In-game usage

- Slash command: `/jobroulette`
- Behavior:
  1. Reads enabled jobs from plugin configuration.
  2. Picks one enabled job at random.
  3. Finds the first saved gear set whose class job matches that selection.
  4. Dispatches `/gearset change X` for that gear set index.

## Configuration UI behavior

Open plugin settings from Dalamud plugin configuration.

- Jobs are grouped by role:
  - Tank
  - Healer
  - Melee DPS
  - Ranged Physical DPS
  - Magical Ranged DPS
- Per-job checkboxes are saved immediately when toggled.
- Convenience buttons:
  - `Enable All`
  - `Disable All`

## Feedback and failure modes

The plugin prints chat feedback for key outcomes:

- Success includes selected job name and gear set number.
- Errors are shown when:
  - no jobs are enabled,
  - no matching gear set exists for the selected job,
  - gear-set command dispatch fails.

## Assumptions / requirements

- You must have at least one enabled job.
- Enabled jobs should have a saved gear set to allow swapping.
- If multiple gear sets exist for the same job, the first matching set is used.
