# Custom vlag: native kopie en bewezen GPU-oorzaak

## Resultaat

**De vanilla vlag kan als eigen `.Item.Gbx` worden opgeslagen en geplaatst. Een
zelfstandig custom bestand dat exact als vanilla werkt is nog niet gelukt.**

De oorzaak van de verkeerde weergave is nu wel rechtstreeks gemeten. De eigen
doekvertices bereiken de GPU, maar worden via een gewone vertexbuffer aangeboden.
De gebruikte VertexTween-shader leest zijn posities uit een structured buffer op
vertex-shader slot `t0`. Daar ontbreekt de eigen buffer, of er staat nog een buffer
van een eerder getekende vanilla vlag. Daardoor kan een custom vlag op vanilla
lijken, verkeerd vervormen of helemaal geen zichtbaar doek hebben.

Twee causale proeven herstellen dit:

1. De eigen vertexbuffer tijdelijk als structured SRV binden rond de draw call.
   De langere doek en zwaluwstaart verschijnen en bewegen correct.
2. Vóór de eerste render uitsluitend de ontbrekende native visualflag zetten.
   De engine maakt en bindt vervolgens zelf de juiste buffers. Dit werkt ook in
   de echte editor-playground, zonder D3D11-correctiehook.

**Dit zijn tijdelijke runtimecorrecties.** Na opslaan, verwijderen van de losse
testitems en diagnoseplugins, herstarten en opnieuw openen van dezelfde kaart,
zijn de twee items aanwezig maar hun doeken weer onzichtbaar. Het is dus geen
afgeronde zelfstandige vlag-export en geen bewijs van volledige vanilla-pariteit.

## Direct bekijken

Alle links hieronder verwijzen naar bewaarde lokale onderzoeksbestanden.

[Gecombineerde bewijsopname, 52 s](../artifacts/flag-vanilla-steps-20261021/custom-flag-proof-with-limit.mp4):
eerst 22,25 s editor, dan 22,25 s editor-playground, daarna dezelfde kaart na een
schone herstart zonder hulpmiddelen. De drie onderstaande bronclips zijn zonder
bewegingsbewerking samengevoegd.

| Bewijs | Wat het aantoont |
| --- | --- |
| [Normale renderer met tijdelijke engineflag, 22 s](../artifacts/flag-vanilla-steps-20261021/engine-structured-flag.mp4) | Links zwaluwstaart, rechts 2,5× langere doek; eigen vormen en animatie zichtbaar |
| [Editor-playground, 22 s](../artifacts/flag-vanilla-steps-20261021/engine-playground.mp4) | Beide vormen bewegen ook in `MapEditor_Playground`, met tijdelijke engineflag |
| [Dezelfde opgeslagen kaart na schone herstart, 8 s](../artifacts/flag-vanilla-steps-20261021/cold-embedded-no-helper.mp4) | Zonder hulpmiddelen ontbreekt het doek weer; geen losse testitems aanwezig |
| [Native kopie naast vanilla](../artifacts/flag-vanilla-steps-20261021/native-vanilla-copy.mp4) | Eigen native bestand is geplaatst; normaal ogend doek bewijst door de gevonden bindingsfout geen zelfstandige correcte rendering |
| [Eerste directe SRV-correctie](../artifacts/flag-vanilla-steps-20261021/diagnostic-srv-binding.mp4) | Eerste zichtbare langere doek en zwaluwstaart door tijdelijk binden van de eigen GPU-data |

De opnamen bestaan uit echte native viewportcaptures, ongeveer drie bronbeelden
per seconde, met de daadwerkelijk gemeten tijdsintervallen. MP4 gebruikt herhaalde
frames op 12 fps; er is geen beweging gegenereerd of geïnterpoleerd. De twee
langere opnamen overspannen de vanilla animatieperiode van maximaal 16 seconden.
Ruwe WebP-frames, tijdstippen, camerastate en itemnamen zijn behouden in
`artifacts/flag-vanilla-steps-20261021/motion/` en `captures.jsonl`.

## Vanilla opslaan en wijzigingen isoleren

De referentie is de echte `Flag16m` uit de game. De geïnstalleerde Editor++ heeft
de FIDs van een native kopie geleegd en de game heeft die kopie opgeslagen als
`TMIS_Step01_ZeroFids.Item.Gbx` (528965 bytes). Het bestand laadt en kan als eigen
item geplaatst worden. Bij het verlaten van deze Item Editor-sessie trad een
crash op; het opgeslagen bestand bleef intact en laadbaar. Deze route is daarom
geen aanbevolen productworkflow.

Vervolgens zijn onafhankelijke kleine wijzigingen op diezelfde native kopie
gemaakt. Ze stapelen niet op elkaar.

| Stap | Enige bedoelde wijziging | Uitkomst |
| --- | --- | --- |
| 01 | Native kopie, FIDs geleegd, native opgeslagen | Eigen bestand laadbaar en geplaatst; bindingsfout blijft mogelijk |
| 02 | Alleen GBX.NET save/reparse | Geometrie en frametabellen behouden; geen renderfix |
| 03 | Alleen zichtbare itemnaam | Geen renderfix |
| 04 | Doekattachment Y +0,25 m | GPU-instancetransformatie verschuift eveneens +0,25 m |
| 05 | Doek-X ×1,025 | Eigen grotere posities in de IA-buffer; shader leest die buffer niet |
| 06 | Doek-X ×2,5 | Eigen max-X 10,077 m gemeten; lange doek pas correct na tijdelijke correctie |
| 07 | X-schaal afhankelijk van UV-V | Eigen max-X 6,992 m gemeten; zwaluwstaart pas correct na tijdelijke correctie |

De zwaluwstaart gebruikt schaal `0.6 + 1.15 * abs(2*v - 1)` met genormaliseerde
UV-V. Alle bestaande frames en LOD-vertices krijgen de vormwijziging. De
attachment, mast en collider blijven uit de native kopie afkomstig. De generator
controleert na reparse alle gewijzigde posities, frametabellen en bounds. Dit
bewijst bestandstrouw; het is op zichzelf geen renderacceptatietest.

De native Editor++-kopie heeft bovendien de verre LOD veranderd: ook die heeft nu
86 frames en 774 vertices. Vanilla heeft daar een statische visual. De kopie is
daarom ook structureel geen bewezen exacte vanilla-pariteit.

Bron: [FlagVanillaSteps.cs](../Tests/FlagProbe/FlagVanillaSteps.cs).
Modellen en manifest: `artifacts/flag-vanilla-steps-20261021/models/`.
De achtvlaggenkaart is bewaard als `TMIS_VanillaSteps_20261021.Map.Gbx` in dat
onderzoeksarchief. Het oorspronkelijke XertroV-item is niet als seed overschreven.

## Wat de GPU daadwerkelijk las

De geïsoleerde D3D11-probe neemt alleen beperkte relevante drawcalls op en ruimt
na maximaal 15 seconden zijn hooks en resources op. De optionele correctie is
apart gegated en beperkt tot de vaste fixtureposities, framecount, indexcount,
stride en buffergrootte. De normale engineflag-opname is vóór de readbackprobe
gemaakt. In het latere playground-proces is deze DLL helemaal niet geladen.

Disassembly van de echte game-DXBC toont:

- De vertexshader gebruikt `SV_VertexID`; hij leest geen `POSITION`-input uit IA.
- `g_VertexPackeds` is een structured SRV op **VS `t0`**, stride **28 bytes**.
- Een packed vertex bevat positie op offset 0, packed normals op 12/16 en UV op 20.
- CB0 bevat onder meer instance-index, vertices-per-frame, framecount,
  wereldtransformatie, frame-offsets en interpolatiegewicht.
- De instancegegevens staan op VS `t119`, 32 bytes per instantie.

De instancebuffers koppelen de gemeten draws aan de echte geplaatste testvlaggen.
De wereldposities sluiten aan op de itemposities en hun prefabattachment. Een
willekeurige stadionvlag is dus niet als eigen testitem aangemerkt.

| Drawpad | IA vertexbuffer | VS t0 | Gemeten eigen doekdata |
| --- | --- | --- | --- |
| Vanilla | Geen | Juiste structured buffer | Vanilla |
| Custom zonder correctie | Eigen buffer, stride 28 | Null of overgebleven buffer | Eigen wijzigingen wel in IA, niet als eigen t0 |
| Custom met engineflag | Geen | Eigen structured buffer, stride 28 | Lang max-X 10,077027; zwaluwstaart 6,992158 |

LOD0 heeft 144 vertices per frame, 86 frames en 726 indices. De goede eigen
structured buffers zijn elk **346752 bytes**. De engineflag-proef levert precies
die buffers, met de juiste verschillende vormen, zonder vervangende GPU-buffer.

Bewijs: `gpu-tools/native-probe/run2/`, `run3/`, `run4-binding-repair/` en
[`run6-native-structured-flag/structured-summary.json`](../artifacts/flag-vanilla-steps-20261021/gpu-tools/native-probe/run6-native-structured-flag/structured-summary.json).
Ruwe buffers en de log met `restored` blijven bij iedere run bewaard.
De eerste run selecteerde stadionvlaggen en wordt niet gebruikt als eigenitembewijs.

## Ontbrekende initialisatie in de engine

Dit is vastgesteld voor Trackmania **2026.2.2.1751**. De onderstaande adressen zijn
PE-adressen bij imagebase `0x140000000`, geen herbruikbare live procespointers.

- `CPlugVisual + 0x24`, bit 27 (`0x08000000`) vraagt de shader-readable bufferroute.
- Native setter `0x1404059B0` zet dit bit en roept de dirtycallback aan.
- De vertex-uploadroutine `0x140A7D120` controleert dit bit bij een visual met
  meerdere frames en markeert de buffer als structured vóór het aanmaken ervan.
- Solid2 post-load rond `0x140438700` doorloopt de shaded geoms en zet het bit
  wanneer het reeds beschikbare materiaal een passende shader oplevert.
- Die controle staat vóór latere opbouw van de custom materialen. Dit is een
  concrete brononderbouwde verklaring waarom de onderzochte custom-materialroute
  het bit mist; niet iedere mogelijke bestandsroute is daarmee uitgesloten.
- Native visual-archive `0x0900600F` schrijft/leest slechts een selectie flags.
  **Bit 27 wordt daar niet opgeslagen.** Een runtimebit in een gewone GBX-flagswaarde
  zetten is dus geen aangetoonde bestandsoplossing.

De beslissende proef zet alleen dit bit op de tien reeds gevalideerde visuals van
de twee vaste bestanden vóór hun eerste render. Na maximaal 90 seconden, bij het
verwijderen van de gate, een editorwissel of unload worden de toegevoegde bits
teruggezet. Beide normale renderer-opnamen werken zo. De GPU-readback bevestigt
de eigen t0-buffers. Het terugzetten van het bit verwijdert een eenmaal gemaakte
GPU-buffer niet noodzakelijk meteen; een schone procesherstart is gebruikt voor
de controle zonder hulpmiddelen.

Bronnen: [TMISFlagGpuPlacement/Main.as](../Tests/Openplanet/TMISFlagGpuPlacement/Main.as),
[GpuFlagReadback.cpp](../Tests/FlagProbe/GpuFlagReadback.cpp) en de bewaarde
`vertex-upload.asm.txt`, `enable-structured-buffer-callers.asm.txt` en
`visual-archive.asm.txt` onder `gpu-tools/`.

## Bestandsroutes die de correctie nog niet vervangen

| Proef | Native resultaat |
| --- | --- |
| Echt inline materiaal, inline parentmateriaal, inline shader | GBX.NET accepteert de bestanden; native FID-preload geeft null, ook na herstart |
| `MaterialIds` met game-materiaalmap | Native geladen en geplaatst; alleen mast zichtbaar |
| Zelfde UserInst alvast in mastmateriaalarray | Na schone herstart geladen/geplaatst; alleen mast zichtbaar |
| Afzonderlijke UserInst alvast in mastmateriaalarray | Zelfde mislukte renderresultaat |
| Eerdere lege mesh als materiaalowner, gedeelde UserInst | Zelfde mislukte renderresultaat |

De oorzaak van de inline preloadweigering is niet geïsoleerd. Dit is geen bewijs
dat de engine elk inline materiaal verbiedt. De drie materiaalvolgordeproeven
hebben wel eigen laad- en plaatsingsbewijs en een capture zonder zichtbaar doek.

De nieuwe opgeslagen tweevlaggenkaart meldt native twee embedded modellen,
895,7 kB totaal. Na herstart opent de kaart zonder de losse itemfiles en bevat
`GetItems` exact beide oorspronkelijke itemnamen en posities. Hun doek ontbreekt
weer op dezelfde camera. Zie `cold-map-state.json`, de cold-opname en gamecalls.

De GBX.NET-vergelijking van de eerdere achtvlaggenkaart stopt met
`Unknown unskippable chunk 0x00000160`, na `CPlugVisual 0x09006005`. Voor die
nieuwe native kaart is daarom **geen volledige embedded-geometriepariteit**
geclaimd. De geslaagde embedded-controle uit het oudere shapesrapport betrof
andere bestanden en blijft daarvan gescheiden.

## Testdekking en resterende grens

- Gecontroleerde kleine bestandsexperimenten; native laden, plaatsen en readback.
- Echte GPU-bindings en eigen bufferbytes, gekoppeld aan geplaatste instances.
- Twee verschillende tijdelijke correcties met zichtbaar causale uitkomst.
- Normale renderer in editor én `MapEditor_Playground`, telkens ruim 21 s beweging.
- Editorcaptures op 40, 80, 180 en 550 m en vanaf de andere kant, met camerareadback
  in `engine-view-matrix.json`. Verre beelden zijn te klein voor betrouwbare
  vormacceptatie en ingestelde afstand bewijst niet zelfstandig de actieve LOD.
- Schone herstart met dezelfde opgeslagen kaart, zonder losse testitems of
  diagnoseplugins: rendering opnieuw mislukt.
- Geen volledige solo-race-, skin-, collision-, lighting-/normal- of LOD-pariteit.
  De bestaande normals zijn in de native-stepvormproeven niet opnieuw berekend.

De volgende inhoudelijke stap heeft nu een concreet criterium: een bestandsroute
moet de native post-loadcheck zelf het structured-bufferbit laten zetten, of een
renderer/exporterfix moet dit na materiaalopbouw doen. Een structurele oplossing
moet daarna zonder runtimehulp een schone herstart en embedded-maptest doorstaan.
De huidige bevinding bewijst geen algemene onmogelijkheid, maar ook geen
zelfstandige werkende export.

## Reproduceren, identiteit en opruimen

Onderzoekstools staan onder `Tests/FlagProbe/`. De vaste enginefixture en cleanup
zijn [engine_flag_fixture.py](../Tests/FlagProbe/engine_flag_fixture.py),
[cold_flag_fixture.py](../Tests/FlagProbe/cold_flag_fixture.py) en
[archive_flag_helpers.py](../Tests/FlagProbe/archive_flag_helpers.py).
Deze vereisen de expliciet beschreven eigen fixtures en een wegwerpkaart.

De engine-only proef heeft geen zinvolle pure RED/GREEN-unit-test: de acceptatie
is native GPU-upload en zichtbare animatie. De vaste, begrensde fixture, echte
bufferreadback, herstartcontrole en unloadbewijs zijn de vervangende liveproef.
De plugin is windowloos, DEV-only, heeft geen exports of sockets en is geen
productfunctie. De vaste scope en teardown zijn in bron en logs vastgelegd.

De exacte gestagede pluginbytes zijn bytegelijk aan de onderzochte bron:
`gpu-flag-identity.json`. `gpu-flag-lsp.json` bevat nul diagnostics met typedb
Openplanet **1.29.14**, Next/Public **6fd4200f** en gamebuild
**2026-02-02 17:51:19**. Verse load-, PREPARED- en RESTORED-regels staan in
`gpu-flag-fresh.log` en `engine-matrix-fresh.log`; verse unloads in
`cleanup-fresh.log`. Oude of door de socket gemelde logregels zijn niet als
compilebewijs gebruikt.

De tijdelijke plugins zijn na geverifieerde unload uit de live Plugins-map
gearchiveerd. De eigen losse testitems zijn met hashcontrole verplaatst naar
`held-live/`. Geen diagnosegate of GPU-probe is actief. De testkaarten en native
captures zijn in het onderzoeksarchief behouden; de game is na afloop terug in
het hoofdmenu. De open kaart was door de gebruiker expliciet als wegwerpkaart
aangewezen. Er is geen bestaande gebruikerskaart overschreven.

Editor++ is niet gebouwd of vervangen. Gecontroleerde SHA256:
`A07D1450FF06CA281BBFD5B1F44B7C08E647A2C13D472E1EFD7AC4E4036FA644`.
Het oorspronkelijke `XertrovItems/ztest-flag3.Item.Gbx` is ongewijzigd:
`3B14E7E9D8DDF237E8CA2D885031CC26D9D08BE1205E2FFBDF69F6C0DF1223FF`.

`dotnet build Tests/FlagProbe --no-restore` slaagt zonder warnings/fouten,
Python syntaxcontrole slaagt en `git diff --check` meldt geen whitespacefouten.
Er is geen productcode aangepast voor de tijdelijke rendererproeven.
