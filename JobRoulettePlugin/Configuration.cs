using Dalamud.Configuration;
using Dalamud.Plugin;

namespace JobRoulettePlugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public HashSet<uint> EnabledJobIds { get; set; } = [];

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

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

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
