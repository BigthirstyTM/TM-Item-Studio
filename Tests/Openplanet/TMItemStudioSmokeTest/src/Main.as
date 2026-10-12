// Native in-game verification of the studio's exports.
//
// Trackmania's own FID loader reads the files the studio wrote, so these checks use the game's
// parser and the game's class layout rather than GBX.NET. Items live in the
// "02. Freebuff RE Items" Items subfolder; Tests/GameTestItems writes them with the studio's
// own export path.
//
// Results go to Openplanet.log with the [TMIS-SMOKE] prefix.

const string TestItemsRoot = "Items/02. Freebuff RE Items/";

int g_pass = 0;
int g_fail = 0;

void Pass(const string &in message) { g_pass++; trace("[TMIS-SMOKE] PASS " + message); }
void Fail(const string &in message) { g_fail++; error("[TMIS-SMOKE] FAIL " + message); }
void Info(const string &in message) { trace("[TMIS-SMOKE] INFO " + message); }

CGameItemModel@ LoadItem(const string &in fileName)
{
    return LoadItemFrom(TestItemsRoot + fileName, fileName);
}

CGameItemModel@ LoadItemFrom(const string &in fullPath, const string &in label)
{
    auto fid = Fids::GetUser(fullPath);
    if (fid is null) {
        Fail(label + " fid-not-found");
        return null;
    }

    trace("[TMIS-SMOKE] INFO " + label + " preloading");
    auto item = cast<CGameItemModel>(Fids::Preload(fid));
    if (item is null) {
        Fail(label + " native-preload-failed");
        return null;
    }

    if (item.EntityModel is null) {
        Fail(label + " missing-entity-model");
        return null;
    }

    Pass(label + " native-preload name=" + item.Name);
    return item;
}

// Depth-first walk of the entity tree; kinematic constraints sit in prefab entries.
void CollectConstraints(CMwNod@ node, array<NPlugDyna_SKinematicConstraint@> &inout found, int depth)
{
    if (node is null || depth > 32)
        return;

    auto constraint = cast<NPlugDyna_SKinematicConstraint>(node);
    if (constraint !is null) {
        found.InsertLast(constraint);
        return;
    }

    auto prefab = cast<CPlugPrefab>(node);
    if (prefab !is null) {
        for (uint i = 0; i < prefab.Ents.Length; i++) {
            auto entry = prefab.Ents[i];
            CollectConstraints(entry.Model, found, depth + 1);
        }
    }

    auto variants = cast<NPlugItem_SVariantList>(node);
    if (variants !is null) {
        for (uint i = 0; i < variants.Variants.Length; i++)
            CollectConstraints(variants.Variants[i].EntityModel, found, depth + 1);
    }
}

// Reports every placement parameter the engine sees; the game stores head/content/debug slots.
CGameItemPlacementParam@ FindPlacement(CGameItemModel@ item, const string &in fileName)
{
    CGameItemPlacementParam@[] params;
    params.InsertLast(item.DefaultPlacementParam_Content);
    params.InsertLast(item.DefaultPlacementParam_Head);
    params.InsertLast(item.DefaultPlacementParam_Dbg);

    CGameItemPlacementParam@ best = null;
    for (uint i = 0; i < params.Length; i++) {
        auto param = params[i];
        if (param is null)
            continue;
        int pivots = int(param.m_PivotPositions.Length);
        int magnets = int(param.m_MagnetLocs_Degrees.Length);
        Info(fileName + " placement slot " + i + " pivots=" + pivots + " magnets=" + magnets
            + " switchPivotManually=" + param.SwitchPivotManually + " yawOnly=" + param.YawOnly
            + " notOnObject=" + param.NotOnObject + " autoRotation=" + param.AutoRotation);
        if (best is null || pivots > int(best.m_PivotPositions.Length))
            @best = param;
    }

    return best;
}

void CheckControl()
{
    LoadItem("FBRE_Control.Item.Gbx");
}

void CheckPlacement()
{
    const string fileName = "FBRE_Placement10.Item.Gbx";
    auto item = LoadItem(fileName);
    if (item is null)
        return;

    auto placement = FindPlacement(item, fileName);
    if (placement is null) {
        Fail(fileName + " no-placement-parameter");
        return;
    }

    if (placement.m_PivotPositions.Length != 10) {
        Fail(fileName + " pivot-count=" + placement.m_PivotPositions.Length + " expected=10");
        return;
    }

    if (placement.m_MagnetLocs_Degrees.Length != 2) {
        Fail(fileName + " magnet-locs=" + placement.m_MagnetLocs_Degrees.Length + " expected=2");
        return;
    }

    if (!placement.SwitchPivotManually || !placement.NotOnObject || placement.YawOnly || placement.AutoRotation) {
        Fail(fileName + " flags switchPivotManually=" + placement.SwitchPivotManually
            + " notOnObject=" + placement.NotOnObject + " yawOnly=" + placement.YawOnly
            + " autoRotation=" + placement.AutoRotation);
        return;
    }

    Pass(fileName + " pivots=10 magnets=2 flags-as-authored");
}

void CheckKinematic(const string &in fileName, int expectedSegments)
{
    auto item = LoadItem(fileName);
    if (item is null)
        return;

    array<NPlugDyna_SKinematicConstraint@> constraints;
    CollectConstraints(item.EntityModel, constraints, 0);
    if (constraints.Length == 0) {
        Fail(fileName + " no-kinematic-constraint");
        return;
    }

    int timelines = 0;
    bool mismatch = false;
    for (uint i = 0; i < constraints.Length; i++) {
        auto constraint = constraints[i];
        if (constraint.TransAnimFunc !is null) {
            timelines++;
            if (int(constraint.TransAnimFunc.SubFuncs.Length) != expectedSegments)
                mismatch = true;
            Info(fileName + " constraint " + i + " translation segments="
                + constraint.TransAnimFunc.SubFuncs.Length);
        }
        if (constraint.RotAnimFunc !is null) {
            timelines++;
            if (int(constraint.RotAnimFunc.SubFuncs.Length) != expectedSegments)
                mismatch = true;
            Info(fileName + " constraint " + i + " rotation segments="
                + constraint.RotAnimFunc.SubFuncs.Length);
        }
    }

    if (timelines == 0) {
        Fail(fileName + " constraints-without-timelines=" + constraints.Length);
        return;
    }

    if (mismatch) {
        Fail(fileName + " segment-count-mismatch expected=" + expectedSegments);
        return;
    }

    Pass(fileName + " constraints=" + constraints.Length + " timelines=" + timelines
        + " segments=" + expectedSegments);
}

void CheckSourceControl()
{
    // The pristine source, untouched. If this one fails, the source item is the problem and not our edit.
    LoadItemFrom("Items/BF2_ASSETS/Test_Items/Chained_Square_Loop.Item.Gbx",
        "source-control/Chained_Square_Loop.Item.Gbx");
}

void Main()
{
    trace("[TMIS-SMOKE] ---- start native export verification ----");
    CheckControl();
    CheckSourceControl();
    CheckPlacement();
    // Ascending segment counts. A hard stop in this list is the engine's limit for this source;
    // everything before it already passed in the same run.
    CheckKinematic("FBRE_Kinematic04.Item.Gbx", 4);
    CheckKinematic("FBRE_Kinematic05.Item.Gbx", 5);
    CheckKinematic("FBRE_Kinematic08.Item.Gbx", 8);
    CheckKinematic("FBRE_Kinematic10.Item.Gbx", 10);
    trace("[TMIS-SMOKE] ---- done pass=" + g_pass + " fail=" + g_fail + " ----");
}
