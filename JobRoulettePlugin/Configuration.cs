using Dalamud.Configuration;

namespace JobRoulettePlugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public HashSet<uint> EnabledJobIds { get; set; } = [];

    /// <summary>
    /// When true, a random Glamour Plate (1–20) will be applied alongside the selected gear set.
    /// </summary>
    public bool RandomGlamourPlate { get; set; } = false;

    public bool IsEnabled(uint jobId) => this.EnabledJobIds.Contains(jobId);

    public void SetEnabled(uint jobId, bool enabled)
    {
        if (enabled)
        {
            this.EnabledJobIds.Add(jobId);
        }
        else
        {
            this.EnabledJobIds.Remove(jobId);
        }

        this.Save();
    }

    public void EnableAll(IEnumerable<uint> jobIds)
    {
        this.EnabledJobIds = jobIds.ToHashSet();
        this.Save();
    }

    public void DisableAll()
    {
        this.EnabledJobIds.Clear();
        this.Save();
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
