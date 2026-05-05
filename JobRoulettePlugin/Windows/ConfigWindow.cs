using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

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
            this.configuration.EnableAll(JobCatalog.All.Select(x => x.JobId));
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

            var enabled = this.configuration.IsEnabled(job.JobId);
            if (ImGui.Checkbox($"{resolvedName}##{job.JobId}", ref enabled))
            {
                this.configuration.SetEnabled(job.JobId, enabled);
            }
        }
    }
}
