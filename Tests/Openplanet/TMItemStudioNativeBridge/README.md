# TM Item Studio native pivot bridge

This Developer-mode Openplanet plugin delegates pivot saves to the installed
Editor++ `Editor` plugin. It avoids GBX.NET serialization: Trackmania's own
native item writer creates the destination archive and Editor++ registers it
with the user inventory.

## Install and run

1. Use the Editor++ DEV build and set Openplanet's signature mode to
   **Developer**.
2. Copy `TMItemStudioNativeBridge` to `%USERPROFILE%\OpenplanetNext\Plugins\`
   and enable it through **Openplanet → Plugin Manager**.
3. In TM Item Studio, edit a pivot and download its native pivot request.
   Save it as
   `Documents\Trackmania\Items\TMItemStudioNativeBridge\native-pivot-request.json`.
4. Restart Trackmania, open a new map in the map editor, then select
   **Openplanet → TM Item Studio native pivot bridge** and choose
   **Load downloaded request**.
5. Review the source and destination paths relative to
   `Documents\Trackmania\Items`, choose an existing pivot index and the
   individual **Pivot X**, **Pivot Y**, and **Pivot Z** coordinates, then
   select **Apply pivot and save natively**. Leaving each coordinate at zero
   deliberately moves that pivot to the item's origin.
6. Wait for the native save, leave the Item Editor, and restart Trackmania
   before checking the destination item in the map editor inventory.

The bridge refuses an existing destination and never overwrites the source.
The latest status is also written to Openplanet's log with the
`[TMIS-NATIVE]` prefix.

Do not include an `Items\` prefix in either field. For example, use
`TMItemStudio\Collectable_Base_Pivot.Item.Gbx`, not
`Items\TMItemStudio\Collectable_Base_Pivot.Item.Gbx`.

## Safety boundary

This first native pipeline slice can replace an existing pivot or append the
next pivot. For an item without pivots, use index `0` to create its first
pivot. It does not delete pivots, alter collision or kinematic data, or use
raw memory offsets. Editor++'s documented public exports open the item editor
and save the result through Trackmania's native writer.
