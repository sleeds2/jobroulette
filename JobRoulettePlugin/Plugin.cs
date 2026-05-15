using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace JobRoulettePlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobroulette";
    private const string SettingsArgument = "settings";
    
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("JobRoulette");
    private readonly ConfigWindow configWindow;
    private readonly Random rng = new();

    private readonly Configuration configuration;
    private readonly Dictionary<uint, ClassJob> jobsById;

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        this.jobsById = this.LoadSupportedJobs();
        if (this.configuration.EnabledJobIds.Count == 0)
        {
            // Start with all jobs enabled by default for first-time users.
            this.configuration.EnableAll(this.jobsById.Keys);
        }

        this.configWindow = new ConfigWindow(this.configuration, this.jobsById);
        this.windowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Usage: /jobroulette - Randomly pick an enabled job and equip its gear set.\n"
                        + "Usage: /jobroulette settings - Toggle the Job Roulette settings window."
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        this.windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string arguments)
    {
        var normalizedArguments = arguments.Trim().ToLowerInvariant();
        if (normalizedArguments == SettingsArgument)
        {
            this.configWindow.Toggle();
            return;
        }

        var enabled = this.configuration.EnabledJobIds
            .Where(this.jobsById.ContainsKey)
            .ToList();

        if (enabled.Count == 0)
        {
            this.PrintError("No jobs are enabled. Open plugin settings and enable at least one job.");
            return;
        }

        var selectedJobId = enabled[this.rng.Next(enabled.Count)];
        if (!this.jobsById.TryGetValue(selectedJobId, out var selectedJob))
        {
            this.PrintError($"Unable to resolve class job data for job id {selectedJobId}.");
            return;
        }

        if (!TryFindGearsetIndexForJob(selectedJobId, out var gearsetIndex))
        {
            this.PrintError($"No matching gear set found for {selectedJob.Name.ExtractText()}.");
            return;
        }

        try
        {
            if (TryEquipGearsetDirect(gearsetIndex))
            {
                this.PrintInfo($"Selected {selectedJob.Name.ExtractText()} (gear set {gearsetIndex + 1}).");
                return;
            }

            this.PrintError($"Failed to equip gear set directly (index {gearsetIndex}).");
        }
        catch (Exception ex)
        {
            this.PrintError($"Failed to equip gear set directly (index {gearsetIndex}): {ex.Message}");
        }
    }

    private static unsafe bool TryEquipGearsetDirect(int gearsetIndex)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return false;
        }

        module->EquipGearset(gearsetIndex);
        return true;
    }

    private Dictionary<uint, ClassJob> LoadSupportedJobs()
    {
        var supportedIds = JobCatalog.All.Select(j => j.JobId).ToHashSet();
        var rows = DataManager.GetExcelSheet<ClassJob>()!;
        var result = new Dictionary<uint, ClassJob>();

        foreach (var row in rows)
        {
            if (supportedIds.Contains(row.RowId))
            {
                result[row.RowId] = row;
            }
        }

        return result;
    }

    private void DrawUi() => this.windowSystem.Draw();

    private void OpenMainUi() => this.configWindow.IsOpen = true;

    private void OpenConfigUi() => this.configWindow.IsOpen = true;

    private void PrintError(string message)
        => ChatGui.PrintError($"[JobRoulette] {message}");

    private void PrintInfo(string message)
        => ChatGui.Print(new SeStringBuilder().AddText($"[JobRoulette] {message}").Build());

    private static unsafe bool TryFindGearsetIndexForJob(uint classJobId, out int gearsetIndex)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            gearsetIndex = -1;
            return false;
        }

        for (var i = 0; i < module->NumGearsets; i++)
        {
            var gearset = module->GetGearset(i);
            if (gearset == null)
            {
                continue;
            }

            if (!gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
            {
                continue;
            }

            if (gearset->ClassJob == classJobId)
            {
                gearsetIndex = i;
                return true;
            }
        }

        gearsetIndex = -1;
        return false;
    }
}

public static class JobCatalog
{
    public static readonly JobDefinition[] All =
    [
        new(19, "Paladin", JobRole.Tank),
        new(21, "Warrior", JobRole.Tank),
        new(32, "Dark Knight", JobRole.Tank),
        new(37, "Gunbreaker", JobRole.Tank),

        new(24, "White Mage", JobRole.Healer),
        new(28, "Scholar", JobRole.Healer),
        new(33, "Astrologian", JobRole.Healer),
        new(40, "Sage", JobRole.Healer),

        new(20, "Monk", JobRole.Melee),
        new(22, "Dragoon", JobRole.Melee),
        new(30, "Ninja", JobRole.Melee),
        new(34, "Samurai", JobRole.Melee),
        new(39, "Reaper", JobRole.Melee),
        new(41, "Viper", JobRole.Melee),

        new(23, "Bard", JobRole.Ranged),
        new(31, "Machinist", JobRole.Ranged),
        new(38, "Dancer", JobRole.Ranged),

        new(25, "Black Mage", JobRole.Caster),
        new(27, "Summoner", JobRole.Caster),
        new(35, "Red Mage", JobRole.Caster),
        new(42, "Pictomancer", JobRole.Caster),
    ];
}

public readonly record struct JobDefinition(uint JobId, string Name, JobRole Role);

public enum JobRole
{
    Tank,
    Healer,
    Melee,
    Ranged,
    Caster,
}
