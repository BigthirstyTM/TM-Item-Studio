using GBX.NET.Engines.GameData;

namespace TM_Item_Studio.Models;

public static class ItemExportValidator
{
    public static void EnsureGameReady(CGameItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.EntityModel is null)
            throw new InvalidOperationException("The item has no entity model and cannot be loaded by Trackmania.");
    }
}
