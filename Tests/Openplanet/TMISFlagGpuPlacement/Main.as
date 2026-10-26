// Fixed, gated engine-only fixture. Set only the proven transient SRV flag
// before first render. This is NOT a standalone-item fix.
#if DEV
CPlugVisual@[] Changed;
void Restore() {
    for(uint i=0;i<Changed.Length;i++) {
        uint flags=Dev::GetOffsetUint32(Changed[i],0x24);
        Dev::SetOffset(Changed[i],0x24,flags & ~uint(0x08000000));
    }
    if(Changed.Length>0) trace("[TMIS-GPU-FLAG] RESTORED transient bits " + Changed.Length);
    Changed.RemoveRange(0,Changed.Length);
}
void OnDestroyed() { Restore(); }
void OnDisabled() { Restore(); }
void ObserveSavedMap(CGameCtnEditorFree@ editor) {
    uint enabled=0, identical=0, count=0;
    for(uint i=0;i<editor.Challenge.AnchoredObjects.Length;i++) {
        auto item=editor.Challenge.AnchoredObjects[i].ItemModel;
        if(item is null || !string(item.IdName).StartsWith("TMIS_VanillaSteps\\TMIS_Step")) continue;
        auto variants=cast<NPlugItem_SVariantList>(item.EntityModel);
        if(variants is null || variants.Variants.Length!=1) throw("saved variant mismatch");
        auto prefab=cast<CPlugPrefab>(variants.Variants[0].EntityModel);
        if(prefab is null || prefab.Ents.Length!=2) throw("saved prefab mismatch");
        auto dyna=cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
        if(dyna is null || dyna.Mesh is null || Dev::GetOffsetUint32(dyna.Mesh,0xB0)!=5) throw("saved mesh mismatch");
        auto buf=Dev::GetOffsetNod(dyna.Mesh,0xA8);
        for(uint v=0;v<5;v++) {
            auto vis=cast<CPlugVisual>(Dev::GetOffsetNod(buf,v*8));
            if(vis is null) throw("saved visual null");
            count++;
            if((Dev::GetOffsetUint32(vis,0x24) & 0x08000000)!=0) enabled++;
            for(uint k=0;k<Changed.Length;k++) if(vis is Changed[k]) { identical++; break; }
        }
    }
    trace("[TMIS-GPU-FLAG] SAVED MAP visuals="+count+" enabled="+enabled+" identical-to-preload="+identical);
}
void Main() {
    const string placementGate = IO::FromUserGameFolder("Items/TMIS_VanillaSteps/gpu-placement.enable");
    const string startupGate = IO::FromUserGameFolder("Items/TMIS_VanillaSteps/startup-proof.enable");
    bool startup = IO::FileExists(startupGate);
    const string gate = startup ? startupGate : placementGate;
    if (!IO::FileExists(gate)) return;
    try {
        auto editor=cast<CGameCtnEditorFree>(GetApp().Editor);
        if(startup && editor !is null) throw("startup proof requires menu");
        if(!startup && (editor is null || editor.Challenge is null || editor.Challenge.AnchoredObjects.Length!=0))
            throw("expected empty disposable map");
        if(startup) trace("[TMIS-GPU-FLAG] STARTUP companion proof; standalone item NOT proven");
        string[] names={"TMIS_Step06_Long250.Item.Gbx","TMIS_Step07_Swallowtail.Item.Gbx","TMIS_Step08_Round.Item.Gbx"};
        for(uint n=0;n<names.Length;n++) {
            auto item=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_VanillaSteps/"+names[n])));
            if(item is null) throw("fixed fixture missing");
            auto variants=cast<NPlugItem_SVariantList>(item.EntityModel);
            if(variants is null || variants.Variants.Length!=1) throw("variant mismatch");
            auto prefab=cast<CPlugPrefab>(variants.Variants[0].EntityModel);
            if(prefab is null || prefab.Ents.Length!=2) throw("prefab mismatch");
            auto dyna=cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
            if(dyna is null || dyna.Mesh is null) throw("cloth missing");
            if(Dev::GetOffsetUint32(dyna.Mesh,0xB0)!=5) throw("five visuals required");
            auto buf=Dev::GetOffsetNod(dyna.Mesh,0xA8);
            for(uint v=0;v<5;v++) {
                auto vis=cast<CPlugVisual>(Dev::GetOffsetNod(buf,v*8));
                if(vis is null || Dev::GetOffsetUint32(vis,0x118)!=86) throw("86-frame visual required");
                uint flags=Dev::GetOffsetUint32(vis,0x24);
                if((flags & 0x08000000)!=0) throw("flag was already set");
                Changed.InsertLast(vis);
                Dev::SetOffset(vis,0x24,flags | uint(0x08000000));
            }
            trace("[TMIS-GPU-FLAG] PREPARED " + names[n]);
        }
        uint64 deadline=Time::Now+(startup ? 600000 : 240000);
        bool observed=false;
        while(Time::Now<deadline && IO::FileExists(gate) && (startup || GetApp().Editor is editor)) {
            if(startup && !observed) {
                auto current=cast<CGameCtnEditorFree>(GetApp().Editor);
                if(current !is null && current.Challenge !is null && current.Challenge.AnchoredObjects.Length==3) {
                    ObserveSavedMap(current);
                    observed=true;
                }
            }
            yield();
        }
    } catch { trace("[TMIS-GPU-FLAG] FAIL " + getExceptionInfo()); }
    Restore();
}
#endif
