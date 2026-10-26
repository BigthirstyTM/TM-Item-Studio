#if DEV
// Disposable DEV-only placement proof for the exported opaque FX carriers and
// their separate typed mesh shells. The shells are deliberately separate: the
// current parser cannot replace a mesh inside CPlugFxSystem's opaque graph.
void Main() {
    trace("[TMIS-FX-COMPOSITE] MAIN");
    const string gatePath = "Items/TMIS_FX_CustomStudioExport_20261025/composite.enable";
    if (!IO::FileExists(IO::FromUserGameFolder(gatePath))) return;
    try {
        IO::Delete(IO::FromUserGameFolder(gatePath));
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null || editor.Challenge is null) throw("map editor required");

        string[] paths = {
            "Items/TMIS_FX_CustomStudioExport_20261025/TMIS_Custom_Fogger_Pyramid.Item.Gbx",
            "Items/TMIS_FX_CustomStudioExport_20261025/TMIS_FX_PyramidShell.Item.Gbx",
            "Items/TMIS_FX_CustomStudioExport_20261025/TMIS_Custom_Sparkler_Diamond.Item.Gbx",
            "Items/TMIS_FX_CustomStudioExport_20261025/TMIS_FX_DiamondShell.Item.Gbx"
        };
        vec3[] positions = {
            vec3(520, 8, 520), vec3(520, 8, 522),
            vec3(560, 8, 520), vec3(560, 8, 522)
        };
        Editor::ItemSpec@[] specs;
        for (uint i = 0; i < paths.Length; i++) {
            auto fid = Fids::GetUser(paths[i]);
            if (fid is null) throw("user FID not found: " + paths[i]);
            auto model = cast<CGameItemModel>(Fids::Preload(fid));
            if (model is null) throw("preload failed: " + paths[i]);
            trace("[TMIS-FX-COMPOSITE] PRELOAD " + paths[i] + " id=" + string(model.IdName));
            specs.InsertLast(Editor::MakeItemSpec(model, positions[i], vec3(0, 0, 0)));
        }
        bool placed = Editor::PlaceItems(specs, true);
        trace("[TMIS-FX-COMPOSITE] PLACE result=" + placed + " anchored=" + editor.Challenge.AnchoredObjects.Length);
        for (uint i = 0; i < editor.Challenge.AnchoredObjects.Length; i++) {
            auto anchored = editor.Challenge.AnchoredObjects[i];
            if (anchored is null || anchored.ItemModel is null) continue;
            trace("[TMIS-FX-COMPOSITE] ITEM index=" + i + " id=" + string(anchored.ItemModel.IdName)
                + " pos=" + anchored.AbsolutePositionInMap.x + "," + anchored.AbsolutePositionInMap.y
                + "," + anchored.AbsolutePositionInMap.z);
        }
    } catch { trace("[TMIS-FX-COMPOSITE] FAIL " + getExceptionInfo()); }
}
#endif
