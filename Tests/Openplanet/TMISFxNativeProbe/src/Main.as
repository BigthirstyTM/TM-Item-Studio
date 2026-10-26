const string Prefix = "[TMIS-FX] ";
int g_pass = 0;
int g_fail = 0;

void Pass(const string &in msg) { g_pass++; trace(Prefix + "PASS " + msg); }
void Fail(const string &in msg) { g_fail++; error(Prefix + "FAIL " + msg); }

void CheckItem(const string &in path)
{
    auto fid = Fids::GetUser(path);
    if (fid is null) { Fail(path + " fid-not-found"); return; }
    auto item = cast<CGameItemModel>(Fids::Preload(fid));
    if (item is null) { Fail(path + " native-preload-failed"); return; }
    if (item.EntityModel is null) { Fail(path + " missing-entity-model"); return; }
    Pass(path + " id=" + item.IdName + " name=" + item.Name
        + " entity=" + item.EntityModel.IdName);
}

void Main()
{
    trace(Prefix + "---- start ----");
    CheckItem("Items/XertrovItems/Fogger_Animated.Item.Gbx");
    CheckItem("Items/02. Freebuff RE Items/FBRE_Control.Item.Gbx");
    CheckItem("Items/XertrovItems/TMIS_Vanilla_Fogger8m.Item.Gbx");
    CheckItem("Items/XertrovItems/TMIS_Vanilla_Sparkler8m.Item.Gbx");
    CheckItem("Items/XertrovItems/TMIS_Fogger8m_Clone.Item.Gbx");
    CheckItem("Items/XertrovItems/TMIS_Sparkler8m_Clone.Item.Gbx");
    CheckItem("Items/ShadocAlex Items/FogVolumetric2/FogVolumetric1_FogEmissionRate_100.Item.Gbx");
    CheckItem("Items/TMIS_FX_EmbeddedFogger_20261025/TMIS_EmbeddedFoggerClone.Item.Gbx");
    trace(Prefix + "---- done pass=" + g_pass + " fail=" + g_fail + " ----");
}
