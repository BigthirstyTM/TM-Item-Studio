// Temporary, bounded engine diagnostic. Adapted from the installed Editor.op
// MeshDuplication::Solid2AliasCustomMatsIfEmpty (Unlicense/public domain).
// Only one disposable copy is eligible. Hold the native material owner and the
// target alive, then restore borrowed array fields before releasing either.
#if DEV
CPlugSolid2Model@[] borrowed;
CPlugSolid2Model@ materialOwner;
const string Gate = "Items/TMIS_FlagResearch/alias-material-probe.enable";

void Restore() {
    for (uint i=0; i<borrowed.Length; i++) {
        if (materialOwner !is null && Dev::GetOffsetUint64(borrowed[i],0xC8)==Dev::GetOffsetUint64(materialOwner,0xC8)) {
            Dev::SetOffset(borrowed[i], 0xC8, uint64(0));
            Dev::SetOffset(borrowed[i], 0xD0, uint64(0));
        } else trace("[TMIS-FLAG-MAT] native replaced table; borrowed pointer no longer installed");
    }
    if (borrowed.Length > 0) trace("[TMIS-FLAG-MAT] RESTORED " + borrowed.Length);
    borrowed.RemoveRange(0, borrowed.Length);
    @materialOwner = null;
}
void OnDestroyed() { Restore(); }
void OnDisabled() { Restore(); }

// Read-only follow-up. Never aliases materials or changes visual flags.
void InspectReferences() {
    string[] names={"NativeShaderUseFile","NativeShaderAbsolute"};
    try {
        for(uint i=0;i<names.Length;i++) {
            auto item=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_Persistence/TMIS_Refs_"+names[i]+".Item.Gbx")));
            if(item is null) throw("reference item missing");
            auto variants=cast<NPlugItem_SVariantList>(item.EntityModel);
            if(variants is null || variants.Variants.Length!=1) throw("reference variant mismatch");
            auto prefab=cast<CPlugPrefab>(variants.Variants[0].EntityModel);
            if(prefab is null || prefab.Ents.Length!=2) throw("reference prefab mismatch");
            auto dyna=cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
            if(dyna is null || dyna.Mesh is null || Dev::GetOffsetUint32(dyna.Mesh,0xB0)!=5) throw("reference mesh mismatch");
            auto buf=Dev::GetOffsetNod(dyna.Mesh,0xA8);
            uint enabled=0;
            for(uint v=0;v<5;v++) {
                auto vis=cast<CPlugVisual>(Dev::GetOffsetNod(buf,v*8));
                if(vis is null || Dev::GetOffsetUint32(vis,0x118)!=86) throw("reference visual mismatch");
                if((Dev::GetOffsetUint32(vis,0x24)&0x08000000)!=0) enabled++;
            }
            trace("[TMIS-FLAG-REF] READ ONLY "+names[i]+" structured="+enabled+"/5 native-mats="
                +Dev::GetOffsetUint32(dyna.Mesh,0xC0)+" material-insts="+Dev::GetOffsetUint32(dyna.Mesh,0xD0));
        }
    } catch { trace("[TMIS-FLAG-REF] FAIL "+getExceptionInfo()); }
}

void LogMaterialMesh(const string &in label, CPlugSolid2Model@ mesh) {
    if(mesh is null) throw("comparison mesh null");
    trace("[TMIS-FLAG-COMPARE] MESH "+label+" candidates="+Dev::GetOffsetUint32(mesh,0xC0)
        +" materials="+Dev::GetOffsetUint32(mesh,0xD0)+" custom="+Dev::GetOffsetUint32(mesh,0x200));
    for(uint a=0;a<2;a++) {
        uint offset=a==0 ? 0xB8 : 0xC8;
        uint len=Dev::GetOffsetUint32(mesh,offset+8);
        if(len>8) throw("comparison material count exceeds bound");
        if(len==0) continue;
        auto buf=Dev::GetOffsetNod(mesh,offset);
        if(buf is null) throw("comparison material buffer null");
        for(uint m=0;m<len;m++) {
            trace("[TMIS-FLAG-COMPARE] MATERIAL "+label+" array="+offset+" index="+m
                +" ptr="+Text::FormatPointer(Dev::GetOffsetUint64(buf,m*8)));
            auto mat=cast<CPlugMaterial>(Dev::GetOffsetNod(buf,m*8));
            if(mat is null) throw("comparison material type mismatch");
            uint devices=Dev::GetOffsetUint32(mat,0x40);
            if(devices>4) throw("comparison device count exceeds bound");
            if(devices==0) continue;
            auto devs=Dev::GetOffsetNod(mat,0x38);
            for(uint d=0;d<devices;d++) {
                auto fid=cast<CSystemFidFile>(Dev::GetOffsetNod(devs,d*0x38+8));
                if(fid is null) throw("comparison shader is not a file fid");
                trace("[TMIS-FLAG-COMPARE] SHADER FID "+label+" "+fid.FullFileName);
                trace("[TMIS-FLAG-COMPARE] SHADER FOLDER "+label+" "+Text::FormatPointer(Dev::GetOffsetUint64(fid,0x18)));
            }
        }
    }
    uint visuals=Dev::GetOffsetUint32(mesh,0xB0), enabled=0;
    if(visuals>8) throw("comparison visual count exceeds bound");
    auto buf=Dev::GetOffsetNod(mesh,0xA8);
    for(uint v=0;v<visuals;v++) {
        auto vis=cast<CPlugVisual>(Dev::GetOffsetNod(buf,v*8));
        if(vis is null) throw("comparison visual null");
        if((Dev::GetOffsetUint32(vis,0x24)&0x08000000)!=0) enabled++;
    }
    trace("[TMIS-FLAG-COMPARE] STRUCTURED "+label+" "+enabled+"/"+visuals);
}
void CompareStandalone() {
    try {
        // Known complete native shader candidate first; vanilla second. No flags are written.
        auto item=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_Standalone/TMIS_Refs_NativeShaderUseFile.Item.Gbx")));
        if(item is null) throw("comparison candidate missing");
        auto variants=cast<NPlugItem_SVariantList>(item.EntityModel);
        if(variants is null || variants.Variants.Length!=1) throw("comparison variants mismatch");
        auto prefab=cast<CPlugPrefab>(variants.Variants[0].EntityModel);
        if(prefab is null || prefab.Ents.Length!=2) throw("comparison prefab mismatch");
        auto dyna=cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
        if(dyna is null) throw("comparison dyna null");
        LogMaterialMesh("custom",dyna.Mesh);
        LogMaterialMesh("vanilla",cast<CPlugSolid2Model>(Fids::Preload(Fids::GetGame("GameData/Stadium/Media/Dyna/Flag/Flag.Mesh.Gbx"))));
        string[] paths={"Relative0","Absolute0","Absolute1","GameAlias0","GameDataAlias0"};
        for(uint i=0;i<paths.Length;i++) {
            auto test=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_Standalone/TMIS_Path_"+paths[i]+".Item.Gbx")));
            if(test is null) { trace("[TMIS-FLAG-COMPARE] REJECTED "+paths[i]); continue; }
            auto v=cast<NPlugItem_SVariantList>(test.EntityModel);
            if(v is null || v.Variants.Length!=1) throw("path variant mismatch");
            auto p=cast<CPlugPrefab>(v.Variants[0].EntityModel);
            if(p is null || p.Ents.Length!=2) throw("path prefab mismatch");
            auto d=cast<CPlugDynaObjectModel>(p.Ents[1].Model);
            if(d is null) throw("path dyna mismatch");
            LogMaterialMesh(paths[i],d.Mesh);
        }
    } catch { trace("[TMIS-FLAG-COMPARE] FAIL "+getExceptionInfo()); }
}

void CompareFileControls() {
    string[] names={"SessionFolderPointer","LocalShader"};
    for(uint i=0;i<names.Length;i++) {
        try {
            auto item=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_FileControls/TMIS_Control_"+names[i]+".Item.Gbx")));
            if(item is null) { trace("[TMIS-FLAG-COMPARE] REJECTED "+names[i]); continue; }
            auto variants=cast<NPlugItem_SVariantList>(item.EntityModel);
            if(variants is null || variants.Variants.Length!=1) throw("control variants mismatch");
            auto prefab=cast<CPlugPrefab>(variants.Variants[0].EntityModel);
            if(prefab is null || prefab.Ents.Length!=2) throw("control prefab mismatch");
            auto dyna=cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
            if(dyna is null) throw("control dyna null");
            LogMaterialMesh(names[i],dyna.Mesh);
        } catch { trace("[TMIS-FLAG-COMPARE] FAIL "+names[i]+" "+getExceptionInfo()); }
    }
}

// Fixed disposable-map placement only. The normal inventory misses this
// session-only diagnostic. No material, visual, or GPU state is written here.
void PlaceFileControl() {
    try {
        auto editor=cast<CGameCtnEditorFree>(GetApp().Editor);
        if(editor is null || editor.Challenge is null || editor.Challenge.AnchoredObjects.Length!=0)
            throw("placement requires an empty disposable map");
        auto model=cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_FileControls/TMIS_Control_SessionFolderPointer.Item.Gbx")));
        if(model is null) throw("session control not loaded");
        Editor::ItemSpec@[] items;
        items.InsertLast(Editor::MakeItemSpec(model,vec3(1480,8,1360),vec3(0,Math::PI/2,0)));
        bool placed=Editor::PlaceItems(items,true);
        trace("[TMIS-FLAG-COMPARE] PLACE session-control result="+placed+" items="+editor.Challenge.AnchoredObjects.Length);
    } catch { trace("[TMIS-FLAG-COMPARE] PLACE FAIL "+getExceptionInfo()); }
}

void Main() {
    if(IO::FileExists(IO::FromUserGameFolder("Items/TMIS_FileControls/place.enable"))) {
        PlaceFileControl(); return;
    }
    if(IO::FileExists(IO::FromUserGameFolder("Items/TMIS_FileControls/compare.enable"))) {
        CompareFileControls(); return;
    }
    if(IO::FileExists(IO::FromUserGameFolder("Items/TMIS_Standalone/compare.enable"))) {
        CompareStandalone(); return;
    }
    if(IO::FileExists(IO::FromUserGameFolder("Items/TMIS_Persistence/inspect.enable"))) {
        InspectReferences(); return;
    }
    if (!IO::FileExists(IO::FromUserGameFolder(Gate))) return;
    auto ownerEditor=cast<CGameCtnEditorFree>(GetApp().Editor);
    if (ownerEditor is null) {
        trace("[TMIS-FLAG-MAT] REFUSED: map editor required"); return;
    }
    string[] names = {"TMIS_DirectMaterials_Grid"};
    try {
        @materialOwner = cast<CPlugSolid2Model>(Fids::Preload(Fids::GetGame("GameData/Stadium/Media/Dyna/Flag/Flag.Mesh.Gbx")));
        if (materialOwner is null) throw("native material owner not loaded");
        uint64 gamePtr=Dev::GetOffsetUint64(materialOwner,0xC8);
        uint64 gameCountCap=Dev::GetOffsetUint64(materialOwner,0xD0);
        trace("[TMIS-FLAG-MAT] native owner ptr="+Text::FormatPointer(gamePtr)+" count="+uint(gameCountCap));
        if (gamePtr==0 || uint(gameCountCap)<1 || uint(gameCountCap)>2) throw("native material array mismatch");
        for (uint i=0; i<names.Length; i++) {
            auto item = cast<CGameItemModel>(Fids::Preload(Fids::GetUser("Items/TMIS_FlagResearch/"+names[i]+".Item.Gbx")));
            if (item is null) throw("source not loaded");
            auto variants = cast<NPlugItem_SVariantList>(item.EntityModel);
            if (variants is null || variants.Variants.Length != 1) throw("variant mismatch");
            auto prefab = cast<CPlugPrefab>(variants.Variants[0].EntityModel);
            if (prefab is null || prefab.Ents.Length != 2) throw("prefab mismatch");
            auto dyna = cast<CPlugDynaObjectModel>(prefab.Ents[1].Model);
            if (dyna is null || dyna.Mesh is null) throw("dyna mismatch");
            auto mesh = dyna.Mesh;
            if (Dev::GetOffsetUint32(mesh,0xB0)!=1) throw("one visual required for this probe");
            if (Dev::GetOffsetUint64(mesh,0xC8)!=0 || Dev::GetOffsetUint64(mesh,0xD0)!=0) throw("destination is not empty");
            trace("[TMIS-FLAG-MAT] mesh="+Text::FormatPointer(Dev::GetOffsetUint64(dyna,0x20))
                +" native="+uint(gameCountCap)+" user="+Dev::GetOffsetUint32(mesh,0x100));
            borrowed.InsertLast(mesh);
            Dev::SetOffset(mesh,0xC8,gamePtr);
            Dev::SetOffset(mesh,0xD0,gameCountCap);
            trace("[TMIS-FLAG-MAT] DIRECT GAME MATERIALS " + names[i]);
        }
        uint64 deadline=Time::Now+90000;
        while(Time::Now<deadline && IO::FileExists(IO::FromUserGameFolder(Gate)) && GetApp().Editor is ownerEditor) yield();
    } catch { trace("[TMIS-FLAG-MAT] FAIL " + getExceptionInfo()); }
    Restore();
}
#endif
