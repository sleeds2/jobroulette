using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace JobRoulettePlugin;

public sealed class ConfigWindow : Window
{
    private static readonly string[] NotificationModeLabels = ["Chat", "Toast", "Chat and Toast"];

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
        ImGui.TextWrapped("Enable the classes and jobs that can be selected by /jobroulette.");

        if (ImGui.Button("Enable All"))
        {
            this.configuration.EnableAll(JobCatalog.All
                .Select(x => x.JobId)
                .Where(id => Plugin.TryResolveUnlockedClassJobId(this.jobsById, id, out _)));
        }

        ImGui.SameLine();
        if (ImGui.Button("Disable All"))
        {
            this.configuration.DisableAll();
        }

        ImGui.Separator();

        // ── Optional Configs ─────────────────────────────────────────────
        if (ImGui.CollapsingHeader("Optional Configs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var notificationMode = (int)this.configuration.NotificationMode;
            if (ImGui.Combo("Notification mode##notificationMode", ref notificationMode, NotificationModeLabels, NotificationModeLabels.Length))
            {
                this.configuration.NotificationMode = (NotificationMode)notificationMode;
                this.configuration.Save();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Choose where Job Roulette shows success and error messages.");
            }

            var allowCurrentJob = this.configuration.AllowCurrentJob;
            if (ImGui.Checkbox("Allow rolling current job##allowCurrentJob", ref allowCurrentJob))
            {
                this.configuration.AllowCurrentJob = allowCurrentJob;
                this.configuration.Save();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "When disabled, Job Roulette will exclude your currently equipped job\n"
                    + "from random selections.\n"
                    + "Toggle with /jobroulette current.");
            }

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

            ImGui.Indent();
            if (!randomGlamour)
            {
                ImGui.BeginDisabled();
            }

            var randomGlamourWhenLinked = this.configuration.RandomGlamourPlateWhenLinked;
            if (ImGui.Checkbox("Roll even when selected gear set has a linked Glamour Plate##randomGlamourWhenLinked", ref randomGlamourWhenLinked))
            {
                this.configuration.RandomGlamourPlateWhenLinked = randomGlamourWhenLinked;
                this.configuration.Save();
            }

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(
                    "When disabled, gear sets with a linked Glamour Plate will use their linked plate\n"
                    + "instead of rolling a random one. Requires random Glamour Plates to be enabled.");
            }

            if (!randomGlamour)
            {
                ImGui.EndDisabled();
            }

            ImGui.Unindent();
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
            var unlocked = Plugin.TryResolveDisplayClassJobId(this.jobsById, job.JobId, out var resolvedClassJobId);
            var resolvedName = unlocked && this.jobsById.TryGetValue(resolvedClassJobId, out var row)
                ? row.Name.ExtractText()
                : job.Name;

            var displayName = resolvedName == resolvedName.ToLowerInvariant()
                ? CultureInfo.CurrentCulture.TextInfo.ToTitleCase(resolvedName)
                : resolvedName;

            var enabled = this.configuration.IsEnabled(job.JobId);

            if (Plugin.PlayerState.IsLoaded && !unlocked && enabled)
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
}
