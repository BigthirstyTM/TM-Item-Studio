// Bounded native placement probe for a preloaded embedded FX item.
#if DEV
void Main() {
    trace("[TMIS-FX-PLACE] MAIN");
    const string gatePath = "Items/TMIS_FX_NativeCopy/sparklerclone.enable";
    if (!IO::FileExists(IO::FromUserGameFolder(gatePath))) return;
    try {
        IO::Delete(IO::FromUserGameFolder(gatePath));
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null || editor.Challenge is null) throw("map editor required");
        auto fid = Fids::GetUser("Items/TMIS_FX_NativeCopy/TMIS_SparklerEmbeddedNativeClone.Item.Gbx");
        if (fid is null) throw("user FID not found");
        auto model = cast<CGameItemModel>(Fids::Preload(fid));
        if (model is null) throw("embedded Sparkler preload failed");
        trace("[TMIS-FX-PLACE] PRELOAD id=" + string(model.IdName) + " name=" + model.Name);
        auto spec = Editor::MakeItemSpec(model, vec3(780, 8, 766), vec3(0, 0, 0));
        Editor::ItemSpec@[] items = { spec };
        bool placed = Editor::PlaceItems(items, true);
        trace("[TMIS-FX-PLACE] PLACE result=" + placed + " items=" + editor.Challenge.AnchoredObjects.Length);
        for (uint i = 0; i < editor.Challenge.AnchoredObjects.Length; i++) {
            auto anchored = editor.Challenge.AnchoredObjects[i];
            if (anchored is null || anchored.ItemModel is null) continue;
            trace("[TMIS-FX-PLACE] ITEM index=" + i + " id=" + string(anchored.ItemModel.IdName)
                + " pos=" + anchored.AbsolutePositionInMap.x + "," + anchored.AbsolutePositionInMap.y
                + "," + anchored.AbsolutePositionInMap.z + " scale=" + anchored.Scale);
        }
    } catch { trace("[TMIS-FX-PLACE] FAIL " + getExceptionInfo()); }
}
#endif
