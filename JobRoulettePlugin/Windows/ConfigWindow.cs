using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace JobRoulettePlugin;

public sealed class ConfigWindow : Window
{
    private readonly Configuration configuration;
    private readonly Dictionary<uint, ClassJob> jobsById;

    public ConfigWindow(Configuration configuration, Dictionary<uint, ClassJob> jobsById)
        : base("Job Roulette Settings")
    {
        this.configuration = configuration;
        this.jobsById = jobsById;

        this.Size = new System.Numerics.Vector2(450, 450);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Enable the jobs that can be selected by /jobroulette.");

        if (ImGui.Button("Enable All"))
        {
            this.configuration.EnableAll(JobCatalog.All
                .Select(x => x.JobId)
                .Where(IsJobUnlocked));
        }

        ImGui.SameLine();
        if (ImGui.Button("Disable All"))
        {
            this.configuration.DisableAll();
        }

        ImGui.Separator();

        DrawRoleSection(JobRole.Tank, "Tank");
        DrawRoleSection(JobRole.Healer, "Healer");
        DrawRoleSection(JobRole.Melee, "Melee DPS");
        DrawRoleSection(JobRole.Ranged, "Ranged Physical DPS");
        DrawRoleSection(JobRole.Caster, "Magical Ranged DPS");
    }

    private void DrawRoleSection(JobRole role, string header)
    {
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        foreach (var job in JobCatalog.All.Where(x => x.Role == role))
        {
            var resolvedName = this.jobsById.TryGetValue(job.JobId, out var row)
                ? row.Name.ExtractText()
                : job.Name;

            var displayName = resolvedName == resolvedName.ToLowerInvariant()
                ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(resolvedName)
                : resolvedName;

            var enabled = this.configuration.IsEnabled(job.JobId);
            var unlocked = IsJobUnlocked(job.JobId);
            if (!unlocked)
            {
                enabled = false;
                this.configuration.SetEnabled(job.JobId, false);
            }

            if (!unlocked)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Checkbox($"{displayName}##{job.JobId}", ref enabled) && unlocked)
            {
                this.configuration.SetEnabled(job.JobId, enabled);
            }

            if (!unlocked)
            {
                ImGui.EndDisabled();
            }
        }
    }

    private static unsafe bool IsJobUnlocked(uint jobId)
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            return false;
        }

        return playerState->ClassJobLevels[jobId] > 0;
    }
}
