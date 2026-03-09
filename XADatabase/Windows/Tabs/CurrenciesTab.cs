using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using XADatabase.Collectors;
using XADatabase.Database;
using XADatabase.Models;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using XADatabase.Services;

namespace XADatabase.Windows;

/// <summary>
/// CurrenciesTab — partial class split from MainWindow.
/// </summary>
public partial class MainWindow
{
    // ───────────────────────────────────────────────
    //  Currencies Tab
    // ───────────────────────────────────────────────
    private void DrawCurrenciesTab()
    {
        using var tab = ImRaii.TabItem("Currencies");
        if (!tab.Success)
            return;

        ImGui.Spacing();

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded && cachedCurrencies.Count == 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Not logged in — select a character above to view data.");
            return;
        }

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Currencies");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (cachedCurrencies.Count == 0)
        {
            ImGui.TextDisabled("No currency data collected yet. Click Refresh.");
            return;
        }

        string currentCategory = string.Empty;

        using (var table = ImRaii.Table("CurrencyTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Cap", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableHeadersRow();

                foreach (var entry in cachedCurrencies)
                {
                    // Category separator row
                    if (entry.Category != currentCategory)
                    {
                        currentCategory = entry.Category;
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.25f, 1.0f)));
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1.0f, 1.0f), currentCategory);
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                    }

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    ImGui.Text($"  {entry.Name}");

                    ImGui.TableNextColumn();
                    if (entry.Amount > 0)
                    {
                        // Gold color for gil, white for others
                        if (entry.Name == "Gil")
                            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"{entry.Amount:N0}");
                        else
                            ImGui.Text($"{entry.Amount:N0}");
                    }
                    else
                    {
                        ImGui.TextDisabled("0");
                    }

                    ImGui.TableNextColumn();
                    if (entry.Cap > 0 && entry.Cap < 999_999_999)
                    {
                        var ratio = (float)entry.Amount / entry.Cap;
                        if (ratio >= 0.95f)
                            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), $"{entry.Cap:N0}");
                        else
                            ImGui.TextDisabled($"{entry.Cap:N0}");
                    }
                    else
                    {
                        ImGui.TextDisabled("-");
                    }
                }
            }
        }

    }

}
