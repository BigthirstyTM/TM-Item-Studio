const string TestItemsRoot = "Items/BF2_ASSETS/Test_Items/";

void CheckItem(const string &in fileName)
{
    auto fid = Fids::GetUser(TestItemsRoot + fileName);
    if (fid is null) {
        error("[TMIS-SMOKE] FAIL " + fileName + " fid-not-found");
        return;
    }

    auto item = cast<CGameItemModel>(Fids::Preload(fid));
    if (item is null) {
        error("[TMIS-SMOKE] FAIL " + fileName + " native-preload-failed");
        return;
    }

    if (item.EntityModel is null) {
        error("[TMIS-SMOKE] FAIL " + fileName + " missing-entity-model");
        return;
    }

    trace("[TMIS-SMOKE] PASS " + fileName + " native-preload-and-entity-model");
}

void Main()
{
    CheckItem("CustomItem_Static.Item.Gbx");
    CheckItem("CustomItem_Kinematic.Item.Gbx");
}
