// Engine-only, bounded, fixed-purpose research. No raw memory edits.
#if DEV
void Main() {
    const string gate = IO::FromUserGameFolder("Items/TMIS_VanillaSteps/native-copy.enable");
    if (!IO::FileExists(gate)) return;
    IO::File gateFile(gate, IO::FileMode::Read);
    bool zero = gateFile.ReadToEnd().Trim() == "zero";
    gateFile.Close();
    string target = zero ? "TMIS_VanillaSteps/TMIS_Step01_ZeroFids.Item.Gbx" : "TMIS_VanillaSteps/TMIS_Step00_Native.Item.Gbx";
    try {
        if (IO::FileExists(IO::FromUserGameFolder("Items/" + target))) throw("destination already exists");
        if (zero) {
            auto current = cast<CGameEditorItem>(GetApp().Editor);
            if (current is null || current.ItemModel is null || current.ItemModel.Name != "Flag16m") throw("expected current vanilla flag editor");
            Editor::ZeroCurrentItemModelFids();
            trace("[TMIS-VANILLA-COPY] ZERO FIDS via installed Editor export");
            Editor::SaveItemAsEditorAsync(target);
            uint64 saveDeadline = Time::Now + 20000;
            while (Time::Now < saveDeadline && !IO::FileExists(IO::FromUserGameFolder("Items/" + target))) yield();
            if (!IO::FileExists(IO::FromUserGameFolder("Items/" + target))) throw("zero-FID save timeout");
            trace("[TMIS-VANILLA-COPY] SAVED " + target);
            return;
        }
        auto editor = cast<CGameCtnEditorFree>(GetApp().Editor);
        if (editor is null) throw("map editor required");
        auto model = cast<CGameItemModel>(Fids::Preload(Fids::GetGame("GameData/Stadium/Items/Flag16m.Item.Gbx")));
        if (model is null || string(model.IdName) != "Flag16m") throw("wrong vanilla source");
        trace("[TMIS-VANILLA-COPY] SOURCE " + string(model.IdName));
        Editor::OpenItemEditor(editor, model);
        uint64 deadline = Time::Now + 15000;
        CGameEditorItem@ itemEditor;
        while (Time::Now < deadline) {
            @itemEditor = cast<CGameEditorItem>(GetApp().Editor);
            if (itemEditor !is null) break;
            yield();
        }
        if (itemEditor is null || itemEditor.ItemModel is null) throw("item editor timeout");
        trace("[TMIS-VANILLA-COPY] OPEN " + string(itemEditor.ItemModel.IdName) + " name=" + itemEditor.ItemModel.Name);
        Editor::SaveItemAsEditorAsync(target);
        deadline = Time::Now + 20000;
        while (Time::Now < deadline && !IO::FileExists(IO::FromUserGameFolder("Items/" + target))) yield();
        if (!IO::FileExists(IO::FromUserGameFolder("Items/" + target))) throw("save timeout");
        trace("[TMIS-VANILLA-COPY] SAVED " + target);
    } catch { trace("[TMIS-VANILLA-COPY] FAIL " + getExceptionInfo()); }
}
#endif
