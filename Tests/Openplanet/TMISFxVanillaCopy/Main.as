// Fixed-purpose native clone probe. The game opens a vanilla carrier in the
// installed Item Editor and uses its own Save Item As writer. One gate is
// consumed per run so a loader failure cannot cascade into a second carrier.
#if DEV
void Main() {
    const string folder = "Items/TMIS_FX_NativeCopy/";
    const string gatePath = folder + "clone.enable";
    const string gate = IO::FromUserGameFolder(gatePath);
    if (!IO::FileExists(gate)) return;

    string mode = "";
    try {
        IO::File f(gate, IO::FileMode::Read);
        mode = f.ReadToEnd().Trim().ToLower();
        f.Close();
        IO::Delete(gate);
    } catch { trace("[TMIS-FX-COPY] FAIL gate " + getExceptionInfo()); return; }

    string source = "";
    string target = "";
    if (mode == "fogger8") {
        source = "GameData/Stadium/Items/ShowFogger8M.Item.Gbx";
        target = "TMIS_FX_NativeCopy/TMIS_Fogger8m_NativeClone.Item.Gbx";
    } else if (mode == "sparkler8") {
        source = "GameData/Stadium/Items/Sparkler8m.Item.Gbx";
        target = "TMIS_FX_NativeCopy/TMIS_Sparkler8m_NativeClone.Item.Gbx";
    } else if (mode == "embedded") {
        source = "Items/ShadocAlex Items/FogVolumetric2/FogVolumetric1_FogEmissionRate_100.Item.Gbx";
        target = "TMIS_FX_NativeCopy/TMIS_FoggerEmbeddedNativeClone.Item.Gbx";
    } else if (mode == "sparkler-embedded") {
        source = "Items/TMIS_FX_EmbeddedSparkler_20261025/TMIS_Sparkler_EmbeddedClone.Item.Gbx";
        target = "TMIS_FX_NativeCopy/TMIS_SparklerEmbeddedNativeClone.Item.Gbx";
    } else if (mode == "rate0" || mode == "rate100" || mode == "rate1000") {
        source = "Items/TMIS_FX_EmbeddedRateProbe_20261025/TMIS_FoggerEmbedded_Rate" + mode.SubStr(4) + ".Item.Gbx";
        target = "TMIS_FX_NativeCopy/TMIS_FoggerRate" + mode.SubStr(4) + "NativeClone.Item.Gbx";
    } else {
        trace("[TMIS-FX-COPY] FAIL unknown mode=" + mode);
        return;
    }

    const string targetPath = IO::FromUserGameFolder("Items/" + target);
    try {
        if (IO::FileExists(targetPath)) throw("destination already exists: " + target);
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null) throw("map editor required");
        auto fid = (mode == "embedded" || mode == "sparkler-embedded"
            || mode == "rate0" || mode == "rate100" || mode == "rate1000")
            ? Fids::GetUser(source) : Fids::GetGame(source);
        if (fid is null) throw("game FID not found: " + source);
        auto model = cast<CGameItemModel>(Fids::Preload(fid));
        if (model is null) throw("native preload failed: " + source);
        trace("[TMIS-FX-COPY] SOURCE id=" + string(model.IdName) + " name=" + model.Name);
        Editor::OpenItemEditor(editor, model);

        uint64 deadline = Time::Now + 15000;
        CGameEditorItem@ itemEditor;
        while (Time::Now < deadline) {
            @itemEditor = cast<CGameEditorItem>(GetApp().Editor);
            if (itemEditor !is null && itemEditor.ItemModel !is null) break;
            yield();
        }
        if (itemEditor is null || itemEditor.ItemModel is null)
            throw("item editor timeout");
        trace("[TMIS-FX-COPY] OPEN id=" + string(itemEditor.ItemModel.IdName)
            + " name=" + itemEditor.ItemModel.Name);
        Editor::SaveItemAsEditorAsync(target);
        deadline = Time::Now + 30000;
        while (Time::Now < deadline && !IO::FileExists(targetPath)) yield();
        if (!IO::FileExists(targetPath)) throw("save timeout: " + target);
        trace("[TMIS-FX-COPY] SAVED " + target);
    } catch { trace("[TMIS-FX-COPY] FAIL " + getExceptionInfo()); }
}
#endif
