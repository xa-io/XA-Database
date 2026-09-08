using System.Collections.Generic;
using XADatabase.Core.Collection;
using XADatabase.Models;
using XADatabase.Services;

namespace XADatabase.Collectors;

public static class InventoryCollector
{
    // Summaries and item rows share one observation and one readable-container set.
    // Unavailable bags must not be fabricated as zero-used summaries.
    public static SectionResult<List<InventorySummary>> CollectSection(XaServices services,
        SectionResult<ItemCollectionResult>? observation = null)
    {
        var items = observation ?? ItemCollector.CollectSection(services);
        return new SectionResult<List<InventorySummary>>(items.State, items.Value.Summaries, items.Detail);
    }
}
