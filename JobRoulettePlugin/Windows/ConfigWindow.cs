using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
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
                .Where(definition => Plugin.ResolveSelectableClassJobId(this.jobsById, definition) is not null)
                .Select(definition => definition.JobId));
        }

        ImGui.SameLine();
        if (ImGui.Button("Disable All"))
        {
            this.configuration.DisableAll();
        }

        ImGui.Separator();

        // ── Glamour Plate ────────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Glamour Plate", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var randomGlamour = this.configuration.RandomGlamourPlate;
            if (ImGui.Checkbox("Equip a random Glamour Plate##randomGlamour", ref randomGlamour))
            {
                this.configuration.RandomGlamourPlate = randomGlamour;
                this.configuration.Save();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "When enabled, a random Glamour Plate (1\u201320) will be applied\n"
                    + "each time Job Roulette selects a job.\n"
                    + "Toggle with /jobroulette glam.");
            }
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
            var selectableClassJobId = Plugin.ResolveSelectableClassJobId(this.jobsById, job);
            var displayClassJobId = selectableClassJobId ?? job.JobId;
            var resolvedName = this.jobsById.TryGetValue(displayClassJobId, out var row)
                ? row.Name.ExtractText()
                : job.Name;

            var displayName = resolvedName == resolvedName.ToLowerInvariant()
                ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(resolvedName)
                : resolvedName;

            var selectable = selectableClassJobId is not null;
            var enabled = this.configuration.IsEnabled(job.JobId);

            if (Plugin.PlayerState.IsLoaded && !selectable && enabled)
            {
                enabled = false;
                this.configuration.SetEnabled(job.JobId, false);
            }

            if (!selectable)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Checkbox($"{displayName}##{job.JobId}", ref enabled) && selectable)
            {
                this.configuration.SetEnabled(job.JobId, enabled);
            }

            if (!selectable)
            {
                ImGui.EndDisabled();
            }
        }
    }
}
