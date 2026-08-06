using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace JobRoulettePlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobroulette";
    private const string CommandAlias = "/jr";
    private const string SettingsArgument = "settings";
    private const string CurrentJobArgument = "current";

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
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IUnlockState UnlockState { get; private set; } = null!;

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
            // Store the job slot in configuration; its unlocked class is resolved at runtime.
            this.configuration.EnableAll(JobCatalog.All
                .Select(job => job.JobId)
                .Where(jobId => TryResolveUnlockedClassJobId(this.jobsById, jobId, out _)));
        }

        this.configWindow = new ConfigWindow(this.configuration, this.jobsById);
        this.windowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Randomly pick an enabled job and equip its gear set.\n"
                        + "/jobroulette support|tank|healer|dps|melee|ranged|caster (or magic) - Randomly pick an enabled job from that role.\n"
                        + "/jobroulette leveling|expert|highlevel|levelcap|trials|msq|allianceraid|normalraid|guildhests|mentor - Request a role-in-need roulette job for that duty roulette.\n"
                        + "/jobroulette glam|glamour - Toggle random Glamour Plate selection on/off.\n"
                        + "/jobroulette current|currentjob - Toggle whether Job Roulette may select your current job.\n"
                        + "/jobroulette settings - Toggle the Job Roulette settings window."
        });

        CommandManager.AddHandler(CommandAlias, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Alias for /jobroulette."
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

        PluginLog.Information("plugin_initialized supportedJobs={SupportedJobs}, enabledJobs={EnabledJobs}", this.jobsById.Count, this.configuration.EnabledJobIds.Count);
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
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
            case CommandRequestKind.ToggleGlamour:
                this.configuration.RandomGlamourPlate = !this.configuration.RandomGlamourPlate;
                this.configuration.Save();
                var state = this.configuration.RandomGlamourPlate ? "enabled" : "disabled";
                PluginLog.Information("glamour_plate_toggled enabled={Enabled}", this.configuration.RandomGlamourPlate);
                this.PrintInfo($"Random Glamour Plate is now {state}.");
                return;
            case CommandRequestKind.ToggleCurrentJob:
                this.configuration.AllowCurrentJob = !this.configuration.AllowCurrentJob;
                this.configuration.Save();
                var currentJobState = this.configuration.AllowCurrentJob ? "allowed" : "disallowed";
                PluginLog.Information("current_job_selection_toggled allowCurrentJob={AllowCurrentJob}", this.configuration.AllowCurrentJob);
                this.PrintInfo($"Rolling your current job is now {currentJobState}.");
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

        if (normalizedArguments.Equals("glam", StringComparison.OrdinalIgnoreCase)
            || normalizedArguments.Equals("glamour", StringComparison.OrdinalIgnoreCase))
        {
            return CommandRequest.ToggleGlamour();
        }

        if (normalizedArguments.Equals(CurrentJobArgument, StringComparison.OrdinalIgnoreCase)
            || normalizedArguments.Equals("currentjob", StringComparison.OrdinalIgnoreCase))
        {
            return CommandRequest.ToggleCurrentJob();
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
                ? "No classes or jobs are enabled. Open plugin settings and enable at least one option."
                : $"No {requestedRoleLabel} classes or jobs are enabled. Open plugin settings and enable at least one {requestedRoleLabel} option.");
            return;
        }

        var eligibleJobs = this.GetEligibleEnabledJobs(roleFilter);
        if (eligibleJobs.Count == 0)
        {
            PluginLog.Warning("roulette_failed_no_eligible_jobs enabledKnownJobs={EnabledKnownJobs}, roleFilter={RoleFilter}", enabledKnownJobs.Count, roleFilter);
            this.PrintError(requestedRoleLabel is null
                ? "No enabled classes or jobs are currently eligible. Options must be unlocked and have an existing gear set."
                : $"No enabled {requestedRoleLabel} classes or jobs are currently eligible. Options must be unlocked and have an existing gear set.");
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
            this.PrintError($"No enabled, unlocked, and configured {roleLabel} classes or jobs are eligible for {roulette.DisplayName}. Enable at least one {roleLabel} option and make sure it has an existing gear set.");
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

        // Pick a random Glamour Plate (1–20) when enabled; 0 means use the linked plate, if any.
        var hasLinkedGlamourPlate = HasLinkedGlamourPlate(gearsetIndex);
        byte glamourPlateId = 0;
        if (this.configuration.RandomGlamourPlate
            && (!hasLinkedGlamourPlate || this.configuration.RandomGlamourPlateWhenLinked))
        {
            glamourPlateId = (byte)this.rng.Next(1, 21);
        }

        var jobName = selectedJob.Name.ExtractText();
        try
        {
            if (TryEquipGearsetDirect(gearsetIndex, glamourPlateId))
            {
                var plateLabel = glamourPlateId > 0 ? $", glamour plate {glamourPlateId}" : string.Empty;
                PluginLog.Information("roulette_completed context={Context}, jobId={JobId}, jobName={JobName}, gearsetIndex={GearsetIndex}, glamourPlateId={GlamourPlateId}", context, selectedJobId, jobName, gearsetIndex, glamourPlateId);
                this.PrintInfo(context is null
                    ? $"Selected {jobName} (gear set {gearsetIndex + 1}{plateLabel})."
                    : $"{context}; selected {jobName} (gear set {gearsetIndex + 1}{plateLabel}).");
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
        var currentJobId = this.GetCurrentJobId();
        foreach (var jobId in this.configuration.EnabledJobIds)
        {
            if (!this.IsKnownJobInRole(jobId, roleFilter))
            {
                continue;
            }

            if (!TryResolveEligibleClassJobId(this.jobsById, jobId, out var resolvedClassJobId, out var gearsetIndex))
            {
                continue;
            }

            if (!this.configuration.AllowCurrentJob && currentJobId == resolvedClassJobId)
            {
                continue;
            }

            candidates.Add(new EligibleJobCandidate(resolvedClassJobId, gearsetIndex));
        }

        return candidates;
    }

    private uint? GetCurrentJobId()
    {
        if (!PlayerState.IsLoaded)
        {
            return null;
        }

        return PlayerState.ClassJob.RowId;
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
        ToggleGlamour,
        ToggleCurrentJob,
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

        public static CommandRequest ToggleGlamour() => new(CommandRequestKind.ToggleGlamour);

        public static CommandRequest ToggleCurrentJob() => new(CommandRequestKind.ToggleCurrentJob);

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

    private static unsafe bool TryEquipGearsetDirect(int gearsetIndex, byte glamourPlateId = 0)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
        {
            return false;
        }

        module->EquipGearset(gearsetIndex, glamourPlateId);
        return true;
    }

    private Dictionary<uint, ClassJob> LoadSupportedJobs()
    {
        var supportedIds = JobCatalog.All
            .SelectMany(j => j.ClassId is { } classId ? new[] { j.JobId, classId } : new[] { j.JobId })
            .ToHashSet();
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
    {
        var prefixedMessage = $"[JobRoulette] {message}";
        switch (this.configuration.NotificationMode)
        {
            case NotificationMode.Chat:
                ChatGui.PrintError(prefixedMessage);
                break;
            case NotificationMode.Toast:
                ToastGui.ShowError(prefixedMessage);
                break;
            case NotificationMode.ChatAndToast:
                ChatGui.PrintError(prefixedMessage);
                ToastGui.ShowError(prefixedMessage);
                break;
            default:
                ChatGui.PrintError(prefixedMessage);
                break;
        }
    }

    private void PrintInfo(string message)
    {
        var prefixedMessage = $"[JobRoulette] {message}";
        switch (this.configuration.NotificationMode)
        {
            case NotificationMode.Chat:
                ChatGui.Print(new SeStringBuilder().AddText(prefixedMessage).Build());
                break;
            case NotificationMode.Toast:
                ToastGui.ShowNormal(prefixedMessage);
                break;
            case NotificationMode.ChatAndToast:
                ChatGui.Print(new SeStringBuilder().AddText(prefixedMessage).Build());
                ToastGui.ShowNormal(prefixedMessage);
                break;
            default:
                ChatGui.Print(new SeStringBuilder().AddText(prefixedMessage).Build());
                break;
        }
    }

    private static unsafe bool HasLinkedGlamourPlate(int gearsetIndex)
    {
        var module = RaptureGearsetModule.Instance();
        return module != null && module->HasLinkedGlamourPlate(gearsetIndex);
    }

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
        => IsJobUnlocked(jobsById, classJobId, UnlockState)
            && IsRequiredUnlockQuestComplete(classJobId, jobsById[classJobId]);

    internal static bool IsJobUnlocked(IReadOnlyDictionary<uint, ClassJob> jobsById, uint classJobId, IUnlockState unlockState)
        => jobsById.TryGetValue(classJobId, out var job)
            && PlayerState.GetClassJobLevel(job) > 0
            && unlockState.IsClassJobUnlocked(job);

    private static unsafe bool IsRequiredUnlockQuestComplete(uint classJobId, ClassJob job)
    {
        var definition = JobCatalog.All.FirstOrDefault(candidate => candidate.JobId == classJobId);
        if (definition.ClassId is null)
        {
            return true;
        }

        var unlockQuestId = job.UnlockQuest.RowId;
        var questManager = QuestManager.Instance();
        return unlockQuestId != 0
            && questManager != null
            && questManager->IsQuestComplete(unlockQuestId);
    }

    internal static bool TryResolveUnlockedClassJobId(
        IReadOnlyDictionary<uint, ClassJob> jobsById,
        uint jobId,
        out uint classJobId)
    {
        if (IsJobUnlocked(jobsById, jobId))
        {
            classJobId = jobId;
            return true;
        }

        var definition = JobCatalog.All.FirstOrDefault(job => job.JobId == jobId);
        if (definition.ClassId is { } baseClassId && IsJobUnlocked(jobsById, baseClassId))
        {
            classJobId = baseClassId;
            return true;
        }

        classJobId = 0;
        return false;
    }

    internal static bool TryResolveDisplayClassJobId(
        IReadOnlyDictionary<uint, ClassJob> jobsById,
        uint jobId,
        out uint classJobId)
    {
        if (IsJobUnlocked(jobsById, jobId))
        {
            classJobId = jobId;
            return true;
        }

        var definition = JobCatalog.All.FirstOrDefault(job => job.JobId == jobId);
        if (definition.ClassId is { } baseClassId)
        {
            classJobId = baseClassId;
            return IsJobUnlocked(jobsById, baseClassId);
        }

        classJobId = jobId;
        return false;
    }

    private static bool TryResolveEligibleClassJobId(
        IReadOnlyDictionary<uint, ClassJob> jobsById,
        uint jobId,
        out uint classJobId,
        out int gearsetIndex)
    {
        if (IsJobUnlocked(jobsById, jobId) && TryFindGearsetIndexForJob(jobId, out gearsetIndex))
        {
            classJobId = jobId;
            return true;
        }

        var definition = JobCatalog.All.FirstOrDefault(job => job.JobId == jobId);
        if (definition.ClassId is { } baseClassId
            && IsJobUnlocked(jobsById, baseClassId)
            && TryFindGearsetIndexForJob(baseClassId, out gearsetIndex))
        {
            classJobId = baseClassId;
            return true;
        }

        classJobId = 0;
        gearsetIndex = -1;
        return false;
    }
}

public readonly record struct EligibleJobCandidate(uint JobId, int GearsetIndex);

public static class JobCatalog
{
    public static readonly JobDefinition[] All =
    [
        new(19, "Paladin", JobRole.Tank, 1),
        new(21, "Warrior", JobRole.Tank, 3),
        new(32, "Dark Knight", JobRole.Tank),
        new(37, "Gunbreaker", JobRole.Tank),

        new(24, "White Mage", JobRole.Healer, 6),
        new(28, "Scholar", JobRole.Healer),
        new(33, "Astrologian", JobRole.Healer),
        new(40, "Sage", JobRole.Healer),

        new(20, "Monk", JobRole.Melee, 2),
        new(22, "Dragoon", JobRole.Melee, 4),
        new(30, "Ninja", JobRole.Melee, 29),
        new(34, "Samurai", JobRole.Melee),
        new(39, "Reaper", JobRole.Melee),
        new(41, "Viper", JobRole.Melee),

        new(23, "Bard", JobRole.Ranged, 5),
        new(31, "Machinist", JobRole.Ranged),
        new(38, "Dancer", JobRole.Ranged),

        new(25, "Black Mage", JobRole.Caster, 7),
        new(27, "Summoner", JobRole.Caster, 26),
        new(35, "Red Mage", JobRole.Caster),
        new(42, "Pictomancer", JobRole.Caster),
    ];
}

public readonly record struct JobDefinition(uint JobId, string Name, JobRole Role, uint? ClassId = null);

public enum JobRole
{
    Tank,
    Healer,
    Melee,
    Ranged,
    Caster,
}
