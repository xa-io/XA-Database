using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XADatabase.Services;

/// <summary>
/// Utility for recursively extracting text from AtkUnitBase addon node trees.
/// Decoupled from FreeCompanyCollector so any collector can use it.
/// </summary>
public static class AddonTextReader
{
    /// <summary>
    /// Recursively collect all text from an AtkResNode, traversing into component children.
    /// </summary>
    public static unsafe void CollectAllTextNodes(AtkResNode* node, string path, List<(string Path, uint NodeId, string Text)> results, int depth = 0)
    {
        if (node == null || depth > 5) return;

        if (node->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();
            if (!string.IsNullOrEmpty(text))
                results.Add((path, node->NodeId, text));
        }
        else if (node->Type == NodeType.Counter)
        {
            var counterNode = (AtkCounterNode*)node;
            var text = counterNode->NodeText.ToString();
            if (!string.IsNullOrEmpty(text))
                results.Add(($"{path}[ctr]", node->NodeId, text));
        }

        // Component nodes have type >= 1000 and contain child node lists
        if ((int)node->Type >= 1000)
        {
            var compNode = (AtkComponentNode*)node;
            if (compNode->Component != null)
            {
                var childCount = compNode->Component->UldManager.NodeListCount;
                for (var i = 0; i < childCount; i++)
                {
                    var child = compNode->Component->UldManager.NodeList[i];
                    if (child != null)
                        CollectAllTextNodes(child, $"{path}→[{i}]", results, depth + 1);
                }
            }
        }
    }

    /// <summary>
    /// Collect all text nodes from an AtkUnitBase addon.
    /// </summary>
    public static unsafe List<(string Path, uint NodeId, string Text)> ReadAllText(AtkUnitBase* addon)
    {
        var results = new List<(string Path, uint NodeId, string Text)>();
        if (addon == null) return results;

        var nodeCount = addon->UldManager.NodeListCount;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node != null)
                CollectAllTextNodes(node, $"[{i}]", results);
        }

        return results;
    }
}
