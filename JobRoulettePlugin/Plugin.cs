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
    private static readonly IReadOnlyDictionary<string, JobRole> RoleArguments = new Dictionary<string, JobRole>(StringComparer.OrdinalIgnoreCase)
    {
        ["tank"] = JobRole.Tank,
        ["healer"] = JobRole.Healer,
        ["melee"] = JobRole.Melee,
        ["ranged"] = JobRole.Ranged,
        ["caster"] = JobRole.Caster,
        ["magic"] = JobRole.Caster,
    };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;

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
                        + "Usage: /jobroulette tank|healer|melee|ranged|caster|magic - Randomly pick an enabled job from that role. Examples: /jobroulette tank, /jobroulette healer, /jobroulette melee, /jobroulette magic.\n"
                        + "Usage: /jobroulette settings - Toggle the Job Roulette settings window."
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

        PluginLog.Information("plugin_initialized supportedJobs={SupportedJobs}, enabledJobs={EnabledJobs}", this.jobsById.Count, this.configuration.EnabledJobIds.Count);
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        this.windowSystem.RemoveAllWindows();
        PluginLog.Information("plugin_disposed");
    }

    private void OnCommand(string command, string arguments)
    {
        PluginLog.Information("roulette_requested command={Command}, arguments={Arguments}", command, arguments);
        var normalizedArguments = arguments.Trim().ToLowerInvariant();
        if (normalizedArguments == SettingsArgument)
        {
            this.configWindow.Toggle();
            return;
        }

        var roleFilter = RoleArguments.TryGetValue(normalizedArguments, out var requestedRole)
            ? requestedRole
            : (JobRole?)null;
        var requestedRoleLabel = roleFilter is { } role ? GetRoleDisplayName(role) : null;

        var enabledKnownJobs = this.configuration.EnabledJobIds
            .Where(jobId => this.IsKnownJobInRole(jobId, roleFilter))
            .ToList();
        if (enabledKnownJobs.Count == 0)
        {
            PluginLog.Warning("roulette_failed_no_jobs_enabled roleFilter={RoleFilter}", roleFilter);
            this.PrintError(requestedRoleLabel is null
                ? "No jobs are enabled. Open plugin settings and enable at least one job."
                : $"No {requestedRoleLabel} jobs are enabled. Open plugin settings and enable at least one {requestedRoleLabel} job.");
            return;
        }

        var eligibleJobs = this.GetEligibleEnabledJobs(roleFilter);
        if (eligibleJobs.Count == 0)
        {
            PluginLog.Warning("roulette_failed_no_eligible_jobs enabledKnownJobs={EnabledKnownJobs}, roleFilter={RoleFilter}", enabledKnownJobs.Count, roleFilter);
            this.PrintError(requestedRoleLabel is null
                ? "No enabled jobs are currently eligible. Enabled jobs must be unlocked/configured and have an existing gear set."
                : $"No enabled {requestedRoleLabel} jobs are currently eligible. Enabled {requestedRoleLabel} jobs must be unlocked/configured and have an existing gear set.");
            return;
        }

        var selectedCandidate = eligibleJobs[this.rng.Next(eligibleJobs.Count)];
        var selectedJobId = selectedCandidate.JobId;
        var gearsetIndex = selectedCandidate.GearsetIndex;
        if (!this.jobsById.TryGetValue(selectedJobId, out var selectedJob))
        {
            PluginLog.Warning("roulette_failed_missing_job_data jobId={JobId}", selectedJobId);
            this.PrintError($"Unable to resolve class job data for job id {selectedJobId}.");
            return;
        }

        try
        {
            if (TryEquipGearsetDirect(gearsetIndex))
            {
                PluginLog.Information("roulette_completed jobId={JobId}, jobName={JobName}, gearsetIndex={GearsetIndex}", selectedJobId, selectedJob.Name.ExtractText(), gearsetIndex);
                this.PrintInfo($"Selected {selectedJob.Name.ExtractText()} (gear set {gearsetIndex + 1}).");
                return;
            }

            PluginLog.Warning("roulette_failed_equip_unsuccessful jobId={JobId}, gearsetIndex={GearsetIndex}", selectedJobId, gearsetIndex);
            this.PrintError($"Failed to equip gear set directly (index {gearsetIndex}).");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "roulette_failed_exception jobId={JobId}, gearsetIndex={GearsetIndex}", selectedJobId, gearsetIndex);
            this.PrintError($"Failed to equip gear set directly (index {gearsetIndex}): {ex.Message}");
        }
    }

    private List<EligibleJobCandidate> GetEligibleEnabledJobs(JobRole? roleFilter = null)
    {
        var candidates = new List<EligibleJobCandidate>();
        foreach (var jobId in this.configuration.EnabledJobIds)
        {
            if (!this.IsKnownJobInRole(jobId, roleFilter))
            {
                continue;
            }

            if (!IsJobUnlocked(this.jobsById, jobId))
            {
                continue;
            }

            if (TryFindGearsetIndexForJob(jobId, out var gearsetIndex))
            {
                candidates.Add(new EligibleJobCandidate(jobId, gearsetIndex));
            }
        }

        return candidates;
    }

    private bool IsKnownJobInRole(uint jobId, JobRole? roleFilter)
    {
        if (!this.jobsById.ContainsKey(jobId))
        {
            return false;
        }

        if (roleFilter is null)
        {
            return true;
        }

        return JobCatalog.All.FirstOrDefault(job => job.JobId == jobId) is { } definition
            && definition.JobId == jobId
            && definition.Role == roleFilter.Value;
    }

    private static string GetRoleDisplayName(JobRole role)
        => role switch
        {
            JobRole.Tank => "tank",
            JobRole.Healer => "healer",
            JobRole.Melee => "melee",
            JobRole.Ranged => "ranged",
            JobRole.Caster => "caster",
            _ => role.ToString().ToLowerInvariant(),
        };

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

    internal static bool IsJobUnlocked(IReadOnlyDictionary<uint, ClassJob> jobsById, uint classJobId)
        => jobsById.TryGetValue(classJobId, out var job) && PlayerState.GetClassJobLevel(job) > 0;
}

public readonly record struct EligibleJobCandidate(uint JobId, int GearsetIndex);

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
