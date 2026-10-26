// Fixed-purpose, engine-only placement. No shader, material or visual writes.
// Editor ItemSpec APIs follow the installed Editor++ exports (Unlicense).
#if DEV
void Main() {
    trace("[TMIS-NATIVE-PACKAGE] READY placement-only-v1");
    if (!IO::FileExists(IO::FromUserGameFolder("Items/TMIS_NativePackage/place.enable"))) return;
    try {
        auto editor=cast<CGameCtnEditorFree>(GetApp().Editor);
        if(editor is null || editor.Challenge is null || editor.Challenge.MapName!="Unnamed"
            || editor.Challenge.AnchoredObjects.Length!=0) throw("empty disposable map required");
        string[] shapes={"Long","Swallowtail","Round"};
        Editor::ItemSpec@[] items;
        for(uint i=0;i<shapes.Length;i++) {
            string name="TMIS_NativePackage_"+shapes[i];
            auto model=cast<CGameItemModel>(Fids::Preload(Fids::GetGame("GameData/Stadium/Items/"+name+".Item.Gbx")));
            if(model is null || string(model.IdName)!=name) throw("wrong or missing model "+name);
            items.InsertLast(Editor::MakeItemSpec(model,vec3(1460+20*i,8,1360),vec3(0,Math::PI/2,0)));
        }
        bool placed=Editor::PlaceItems(items,true);
        trace("[TMIS-NATIVE-PACKAGE] PLACE result="+placed+" items="+editor.Challenge.AnchoredObjects.Length);
    } catch { trace("[TMIS-NATIVE-PACKAGE] FAIL "+getExceptionInfo()); }
}
#endif
