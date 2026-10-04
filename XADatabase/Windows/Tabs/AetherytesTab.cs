using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using XADatabase.Collectors;
using XADatabase.Models;

namespace XADatabase.Windows;

public partial class MainWindow
{
    private string aetheryteFilter = string.Empty;
    private bool showUncollectedAetherytes;

    private void DrawAetheryteProgress()
    {
        if (!ImGui.CollapsingHeader("Unlocked Aetherytes", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.SetNextItemWidth(Scale(280f));
        ImGui.InputTextWithHint("##AetheryteFilter", "Filter ID, destination or zone", ref aetheryteFilter, 160);
        ImGui.SameLine();
        ImGui.Checkbox("Show uncollected", ref showUncollectedAetherytes);

        var catalog = AetheryteCatalogProvider.GetCatalog(plugin.Services.DataManager);
        var view = AetheryteCatalog.BuildView(cachedAetherytes, catalog.Destinations,
            showUncollectedAetherytes, aetheryteFilter);

        if (view.IsKnown)
        {
            ImGui.TextUnformatted($"{view.CollectedCatalogIds}/{view.CatalogTotal} catalog aetherytes collected; {view.ObservedDestinations} observed destinations including housing.");
            ImGui.TextDisabled($"Last observed {cachedAetherytes.CapturedUtc} UTC. Housing access can change.");
        }
        else
        {
            ImGui.TextUnformatted($"{view.CatalogTotal} catalog aetherytes; collection status unknown.");
            ImGui.TextDisabled("Refresh while logged in and outside a loading screen to collect this character's access.");
        }

        if (!string.IsNullOrEmpty(catalog.Error))
            ImGui.TextDisabled("The full destination catalog is unavailable. Previously observed destinations are still shown.");

        using (var table = ImRaii.Table("AetherytesTable", 5,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, ScaledVector(0f, 220f)))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, Scale(55f));
                ImGui.TableSetupColumn("Destination", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, Scale(100f));
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                foreach (var row in view.Rows)
                {
                    var entry = row.Destination;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.AetheryteId.ToString(CultureInfo.InvariantCulture));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(entry.TerritoryName);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(AetheryteData.FormatLocation(entry));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.Status.ToString());
                }
            }
        }
        ImGui.Spacing();
    }
}
