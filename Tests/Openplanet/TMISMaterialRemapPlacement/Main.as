// Fixed-purpose authoring probe. No renderer, shader, visual or memory writes.
// Adapted from this project's TMISNativeFlagPlacement and Editor++ ItemSpec APIs
// (Editor++ source is Unlicense). This probe is not a map/runtime dependency.
#if DEV
void Main() {
    trace("[TMIS-MATREMAP-PLACE] READY placement-only-user-v3");
    if (!IO::FileExists(IO::FromUserGameFolder("Items/TMIS_MatRemap_20261022_v3/place.enable"))) return;
    try {
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null || editor.Challenge is null || editor.Challenge.MapName != "Unnamed"
            || editor.Challenge.AnchoredObjects.Length != 0) throw("empty disposable map required");
        auto fid = Fids::GetUser("Items/TMIS_MatRemap_20261022_v3/TMIS_MatRemap_Candidate.Item.Gbx");
        auto model = cast<CGameItemModel>(Fids::Preload(fid));
        if (model is null || !string(model.IdName).EndsWith("TMIS_MatRemap_Candidate.Item.Gbx")) throw("wrong model");
        Editor::ItemSpec@[] items;
        items.InsertLast(Editor::MakeItemSpec(model, vec3(1460,8,1360), vec3(0,Math::PI/2,0)));
        bool placed = Editor::PlaceItems(items, true);
        trace("[TMIS-MATREMAP-PLACE] RESULT placed=" + placed + " items=" + editor.Challenge.AnchoredObjects.Length);
    } catch { trace("[TMIS-MATREMAP-PLACE] FAIL " + getExceptionInfo()); }
}
#endif
