using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace JobRoulettePlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobroulette";
    private const string SettingsArgument = "settings";

    private static readonly RoleFilter TankFilter = new("tank", JobRole.Tank);
    private static readonly RoleFilter HealerFilter = new("healer", JobRole.Healer);
    private static readonly RoleFilter DpsFilter = new("DPS", JobRole.Melee, JobRole.Ranged, JobRole.Caster);

    private static readonly IReadOnlyDictionary<RouletteType, uint> RouletteRowIds = new Dictionary<RouletteType, uint>
    {
        [RouletteType.Leveling] = 1,
        [RouletteType.HighLevelDungeons] = 2,
        [RouletteType.MainScenario] = 3,
        [RouletteType.Guildhests] = 4,
        [RouletteType.Expert] = 5,
        [RouletteType.Trials] = 6,
        [RouletteType.LevelCapDungeons] = 8,
        [RouletteType.Mentor] = 9,
        [RouletteType.AllianceRaid] = 15,
        [RouletteType.NormalRaid] = 17,
    };

    private static readonly IReadOnlyDictionary<ContentsRouletteRole, RoleFilter> RoleInNeedFilters = new Dictionary<ContentsRouletteRole, RoleFilter>
    {
        [ContentsRouletteRole.Tank] = TankFilter,
        [ContentsRouletteRole.Healer] = HealerFilter,
        [ContentsRouletteRole.Dps] = DpsFilter,
    };

    private static readonly IReadOnlyDictionary<string, RoleFilter> RoleArguments = new Dictionary<string, RoleFilter>(StringComparer.OrdinalIgnoreCase)
    {
        ["tank"] = TankFilter,
        ["healer"] = HealerFilter,
        ["support"] = new("support", JobRole.Tank, JobRole.Healer),
        ["dps"] = DpsFilter,
        ["melee"] = new("melee", JobRole.Melee),
        ["ranged"] = new("ranged", JobRole.Ranged),
        ["caster"] = new("caster", JobRole.Caster),
        ["magic"] = new("caster", JobRole.Caster),
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
            HelpMessage = "Randomly pick an enabled job and equip its gear set.\n"
                        + "/jobroulette support|tank|healer|dps|melee|ranged|caster (or magic) - Randomly pick an enabled job from that role.\n"
                        + "/jobroulette leveling|expert|highlevel|levelcap|trials|msq|allianceraid|normalraid|guildhests|mentor - Request a role-in-need roulette job for that duty roulette.\n"
                        + "/jobroulette settings - Toggle the Job Roulette settings window."
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
        var commandRequest = ParseCommandRequest(arguments);
        switch (commandRequest.Kind)
        {
            case CommandRequestKind.Settings:
                this.configWindow.Toggle();
                return;
            case CommandRequestKind.JobRoulette:
                this.RunJobRoulette(commandRequest.RoleFilter);
                return;
            case CommandRequestKind.RoleInNeedRoulette:
                this.HandleRoleInNeedRoulette(commandRequest.Roulette!);
                return;
            case CommandRequestKind.Invalid:
                PluginLog.Warning("roulette_failed_unknown_argument argument={Argument}", commandRequest.OriginalArgument);
                this.PrintError($"Unknown argument '{commandRequest.OriginalArgument}'. Use /jobroulette, a role filter, a roulette name, or settings.");
                return;
        }
    }

    private static CommandRequest ParseCommandRequest(string arguments)
    {
        var normalizedArguments = arguments.Trim();
        if (string.IsNullOrEmpty(normalizedArguments))
        {
            return CommandRequest.JobRoulette(null);
        }

        if (normalizedArguments.Equals(SettingsArgument, StringComparison.OrdinalIgnoreCase))
        {
            return CommandRequest.Settings();
        }

        if (RoleArguments.TryGetValue(normalizedArguments, out var roleFilter))
        {
            return CommandRequest.JobRoulette(roleFilter);
        }

        if (RouletteCatalog.TryGet(normalizedArguments, out var roulette))
        {
            return CommandRequest.RoleInNeedRoulette(roulette);
        }

        return CommandRequest.Invalid(normalizedArguments);
    }

    private void RunJobRoulette(RoleFilter? roleFilter)
    {
        var requestedRoleLabel = roleFilter?.DisplayName;

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

        this.SelectAndEquipRandomJob(eligibleJobs, null);
    }

    private void HandleRoleInNeedRoulette(RouletteDefinition roulette)
    {
        PluginLog.Information("roulette_requested rouletteType={RouletteType}, rouletteName={RouletteName}", roulette.Type, roulette.DisplayName);

        var roleStatus = this.TryGetRoleInNeed(roulette.Type, out var roleFilter);
        switch (roleStatus)
        {
            case RoleInNeedLookupStatus.UnsupportedRoulette:
                PluginLog.Warning("roulette_failed_unsupported_type rouletteType={RouletteType}, rouletteName={RouletteName}", roulette.Type, roulette.DisplayName);
                this.PrintError($"{roulette.DisplayName} is not supported for adventurer-in-need roulette selection.");
                return;
            case RoleInNeedLookupStatus.UnableToReadData:
                PluginLog.Warning("roulette_failed_role_data_unavailable rouletteType={RouletteType}, rouletteName={RouletteName}", roulette.Type, roulette.DisplayName);
                this.PrintError($"Unable to read adventurer-in-need data for {roulette.DisplayName}. Open Duty Finder once and try again.");
                return;
            case RoleInNeedLookupStatus.NoRoleInNeed:
                PluginLog.Warning("roulette_failed_no_role_in_need rouletteType={RouletteType}, rouletteName={RouletteName}", roulette.Type, roulette.DisplayName);
                this.PrintError($"{roulette.DisplayName} does not currently list an adventurer-in-need role.");
                return;
            case RoleInNeedLookupStatus.Success:
                break;
            default:
                PluginLog.Warning("roulette_failed_role_lookup_unknown_status rouletteType={RouletteType}, rouletteName={RouletteName}, status={Status}", roulette.Type, roulette.DisplayName, roleStatus);
                this.PrintError($"Unable to resolve adventurer-in-need data for {roulette.DisplayName}.");
                return;
        }

        var roleLabel = roleFilter.DisplayName;
        var eligibleJobs = this.GetEligibleEnabledJobs(roleFilter);
        if (eligibleJobs.Count == 0)
        {
            PluginLog.Warning("roulette_failed_no_eligible_jobs rouletteType={RouletteType}, rouletteName={RouletteName}, roleFilter={RoleFilter}", roulette.Type, roulette.DisplayName, roleLabel);
            this.PrintError($"No enabled, unlocked, and configured {roleLabel} jobs are eligible for {roulette.DisplayName}. Enable at least one {roleLabel} job and make sure it has an existing gear set.");
            return;
        }

        this.SelectAndEquipRandomJob(eligibleJobs, $"{roulette.DisplayName} needs {roleLabel}");
    }

    private void SelectAndEquipRandomJob(IReadOnlyList<EligibleJobCandidate> eligibleJobs, string? context)
    {
        var selectedCandidate = eligibleJobs[this.rng.Next(eligibleJobs.Count)];
        var selectedJobId = selectedCandidate.JobId;
        var gearsetIndex = selectedCandidate.GearsetIndex;
        if (!this.jobsById.TryGetValue(selectedJobId, out var selectedJob))
        {
            PluginLog.Warning("roulette_failed_missing_job_data context={Context}, jobId={JobId}", context, selectedJobId);
            this.PrintError($"Unable to resolve class job data for job id {selectedJobId}.");
            return;
        }

        var jobName = selectedJob.Name.ExtractText();
        try
        {
            if (TryEquipGearsetDirect(gearsetIndex))
            {
                PluginLog.Information("roulette_completed context={Context}, jobId={JobId}, jobName={JobName}, gearsetIndex={GearsetIndex}", context, selectedJobId, jobName, gearsetIndex);
                this.PrintInfo(context is null
                    ? $"Selected {jobName} (gear set {gearsetIndex + 1})."
                    : $"{context}; selected {jobName} (gear set {gearsetIndex + 1}).");
                return;
            }

            PluginLog.Warning("roulette_failed_equip_unsuccessful context={Context}, jobId={JobId}, gearsetIndex={GearsetIndex}", context, selectedJobId, gearsetIndex);
            this.PrintError(context is null
                ? $"Failed to equip gear set directly (index {gearsetIndex})."
                : $"{context}, but failed to equip gear set directly (index {gearsetIndex}).");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "roulette_failed_exception context={Context}, jobId={JobId}, gearsetIndex={GearsetIndex}", context, selectedJobId, gearsetIndex);
            this.PrintError(context is null
                ? $"Failed to equip gear set directly (index {gearsetIndex}): {ex.Message}"
                : $"{context}, but failed to equip gear set directly (index {gearsetIndex}): {ex.Message}");
        }
    }

    private unsafe RoleInNeedLookupStatus TryGetRoleInNeed(RouletteType rouletteType, out RoleFilter roleFilter)
    {
        roleFilter = null!;
        if (!RouletteRowIds.TryGetValue(rouletteType, out var rouletteRowId))
        {
            return RoleInNeedLookupStatus.UnsupportedRoulette;
        }

        try
        {
            var contentsFinder = AgentContentsFinder.Instance();
            if (contentsFinder == null)
            {
                return RoleInNeedLookupStatus.UnableToReadData;
            }

            contentsFinder->Refresh();
            var bonuses = contentsFinder->ContentRouletteRoleBonuses;

            var rouletteSheet = DataManager.GetExcelSheet<ContentRoulette>();
            if (rouletteSheet is null || !rouletteSheet.TryGetRow(rouletteRowId, out var rouletteRow))
            {
                return RoleInNeedLookupStatus.UnableToReadData;
            }

            var bonusIndex = (int)rouletteRow.ContentRouletteRoleBonus.RowId;
            if (bonusIndex < 0 || bonusIndex >= bonuses.Length)
            {
                return RoleInNeedLookupStatus.UnableToReadData;
            }

            var rouletteRole = bonuses[bonusIndex];
            if (rouletteRole == ContentsRouletteRole.None)
            {
                return RoleInNeedLookupStatus.NoRoleInNeed;
            }

            if (!RoleInNeedFilters.TryGetValue(rouletteRole, out roleFilter!))
            {
                return RoleInNeedLookupStatus.UnableToReadData;
            }

            return RoleInNeedLookupStatus.Success;
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "roulette_failed_role_data_exception rouletteType={RouletteType}", rouletteType);
            return RoleInNeedLookupStatus.UnableToReadData;
        }
    }

    private List<EligibleJobCandidate> GetEligibleEnabledJobs(RoleFilter? roleFilter = null)
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

    private bool IsKnownJobInRole(uint jobId, RoleFilter? roleFilter)
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
            && roleFilter.Includes(definition.Role);
    }

    private enum RoleInNeedLookupStatus
    {
        Success,
        UnsupportedRoulette,
        UnableToReadData,
        NoRoleInNeed,
    }

    private enum CommandRequestKind
    {
        Settings,
        JobRoulette,
        RoleInNeedRoulette,
        Invalid,
    }

    private sealed class CommandRequest
    {
        private CommandRequest(CommandRequestKind kind, RoleFilter? roleFilter = null, RouletteDefinition? roulette = null, string? originalArgument = null)
        {
            this.Kind = kind;
            this.RoleFilter = roleFilter;
            this.Roulette = roulette;
            this.OriginalArgument = originalArgument;
        }

        public CommandRequestKind Kind { get; }

        public RoleFilter? RoleFilter { get; }

        public RouletteDefinition? Roulette { get; }

        public string? OriginalArgument { get; }

        public static CommandRequest Settings() => new(CommandRequestKind.Settings);

        public static CommandRequest JobRoulette(RoleFilter? roleFilter) => new(CommandRequestKind.JobRoulette, roleFilter);

        public static CommandRequest RoleInNeedRoulette(RouletteDefinition roulette) => new(CommandRequestKind.RoleInNeedRoulette, roulette: roulette);

        public static CommandRequest Invalid(string originalArgument) => new(CommandRequestKind.Invalid, originalArgument: originalArgument);
    }

    private sealed class RoleFilter
    {
        public RoleFilter(string displayName, params JobRole[] roles)
        {
            this.DisplayName = displayName;
            this.Roles = roles.ToHashSet();
        }

        public string DisplayName { get; }

        private IReadOnlySet<JobRole> Roles { get; }

        public bool Includes(JobRole role) => this.Roles.Contains(role);

        public override string ToString() => this.DisplayName;
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
