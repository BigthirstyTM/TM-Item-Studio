# Custom vlag: ingame onderzoek, 21 oktober 2026

> **Later vervolg:** [Native vanilla kopie en bewezen GPU-oorzaak](custom-flag-vanilla-steps-2026-10-21.md).
> De IA/SRV-bindingsfout is inmiddels rechtstreeks gemeten. Lang doek en
> zwaluwstaart werken zichtbaar met een tijdelijke enginecorrectie, ook in de
> editor-playground. Na een schone herstart zonder hulpmiddelen mislukt de
> rendering weer. De onderstaande resultaten beschrijven de eerdere proeven.

## Resultaat

**Nog geen werkende custom vlag die aantoonbaar exact als vanilla functioneert.**
De bestanden laden, kunnen geplaatst worden en kunnen volledig in een kaart worden
ingesloten. Sommige vergelijkingen tonen normaal golvende vlaggen. Een eigen,
duidelijk gewijzigde doekvorm is echter niet correct weergegeven. Latere proeven
tonen sterke vervorming of een ontbrekend doek. Daarom zijn de kandidaten geen
goedgekeurde modellen en zijn ze uit de live Items-map gehaald.

Dit onderzoek bewijst geen algemene onmogelijkheid. Het openstaande probleem zit
tussen het geladen geometrie-/materiaalmodel en het daadwerkelijk gerenderde doek.
Een specifieke fout in de shader, vertexbufferbinding, runtimeconversie of cache
is nog niet bewezen.

Een [vervolgproef met een 2,5× langer doek en een zwaluwstaart](custom-flag-shapes-2026-10-21.md)
bevat nieuwe echte ingame opnamen. Ook daar bleven de bedoelde eigen vormen
onzichtbaar in de frontale geanimeerde close-ups, ondanks intacte embedded
geometrie. De modellen zijn als afgekeurde kandidaten bewaard.

## Testomgeving en bronnen

- Trackmania-engine: **2026-02-02 17:51:19**; executable file version
  `2026.2.2.1751`.
- Openplanet: **1.29.14**, Next/Public, `6fd4200f`.
- Bestaande localhostbrug `tm-control-mcp` en `tm-mcp-pack-epp`.
- De geïnstalleerde Editor.op is niet gebouwd of vervangen. SHA256:
  `A07D1450FF06CA281BBFD5B1F44B7C08E647A2C13D472E1EFD7AC4E4036FA644`.
- Origineel gebruikersbestand, niet gewijzigd:
  `Documents/Trackmania/Items/XertrovItems/ztest-flag3.Item.Gbx`.
  Gecontroleerde SHA256:
  `3B14E7E9D8DDF237E8CA2D885031CC26D9D08BE1205E2FFBDF69F6C0DF1223FF`.
- Vanilla `Flag16m`, bijbehorende prefab, dyna, mesh en materialen uit de
  Openplanet GameData-extractie; runtimecontrole met de echte game-FIDs.
- Alle plaatsingen gebeurden op eigen wegwerpkaarten. Geen bestaande gebruikerskaart
  overschreven. De game staat na opruimen in het hoofdmenu.

Gebruikte skills: de lokale TM Item Studio MCP-skill, Openplanet control,
development, lifecycle, visual, crash debugging en reviewer. Computer Use is
gebruikt om een Ubisoft Connect-startfout te inspecteren. Herstart via de bestaande
`uplay://launch/5595/0`-launcherroute herstelde de online sessie en testbrug.

## Wat vanilla daadwerkelijk doet

De vlag is geen realtime doeksimulatie. De nabije doek-LODs bevatten **86 gebakken
vertexframes** met dezelfde topologie. De VertexTween-shader interpoleert twee
frames. De verre LOD is statisch.

| Onderdeel | Gemeten structuur |
| --- | --- |
| Item | Ornament, variantlijst, `Type=Flag`, `MatModifier=Ice` |
| Prefab | Statische mast met collider plus dynamisch doek |
| Doekhost | `CPlugDynaObjectModel`; geen doekcollider, geen skeleton/LocAnim |
| Doekmateriaal | `ItemFlag`; verre LOD `ItemFlagNoAnim` |
| Shaderfamilie | `Tech3_Warp_TDiffSpec_VertexTween` |
| Animatie | Per instantie periode 8–16 s, fase 0–1, `IsKinematic=false` |
| LOD-afstanden | 16, 64, 128, 512 m; vijf LOD-maskers 1, 2, 4, 8, 16 |
| Geometrie | 12384, 4214, 1376, 774 vertices in de vier geanimeerde LODs; vier vertices in de verre LOD |
| Frametabel | `SubVisuals`: `(vertexStart, indexStart, indexCount)` |

`BoxAligned` is center + half extent. De werkelijke LOD0-vertexenvelop over alle
frames is ongeveer **4.031 × 4.523 × 4.209 m**. Oude documentatie behandelde de
velden ten onrechte als min/max; dat is gecorrigeerd in de meettool en documentatie.
Correcte bounds alleen lossen het renderprobleem niet op.

## Bestands- en runtimeproeven

| Proef | Waarneming | Conclusie |
| --- | --- | --- |
| Oorspronkelijke XertroV-vlag en gecorrigeerde plaatsingsmetadata | Beide konden in vroege vergelijkingen zichtbaar golven | Metadata-afwijkingen zijn geen bewezen oorzaak van de renderfout |
| Zelfstandig inline item, lege `Ident.Id` en lege `ArchetypeRef` | Native loader geeft bestandsidentiteit; volledige geometrie wordt embedded | Zelfrefererende archetypes vermijden is noodzakelijk voor deze standalone exports |
| Zelfrefererend archetype in eerdere kandidaten | Map bevatte slechts circa 1.5 KB grote shells zonder EntityModel | Dit was een afzonderlijke, aangetoonde embeddingfout |
| Correct standalone embedding | Twee modellen van elk 925199 bytes in map; posities en frametabellen van alle vijf LODs vergeleken | Geometrie overleeft het opslaan van de kaart; geen bewijs van correcte rendering |
| Herstart zonder losse standalone itemfiles, kaart opnieuw openen | Negen vlaggen zichtbaar, editor en editor playground, afstandsmatrix tot 550 m | Embedded assets kunnen onafhankelijk laden; eigen doekvorm nog onbewezen |
| Breder/hoger/20 m verschoven doek, alle frames gewijzigd | Bestandsdata en native CPU-posities gewijzigd; geanimeerde vorm niet overtuigend gevolgd | Een geometriewijziging op disk is geen visueel bewijs |
| Doekattachment verplaatst | Zichtbare verplaatsing | De prefabtransformatie werkt |
| Statische verre LOD geforceerd | Gewijzigde geometrie zichtbaar | Statische en geanimeerde paden gedragen zich verschillend |
| Unieke visual-/mesh-ID, timestamp, bounds, UV en expliciete vertexstreams | Geen correct eigen geanimeerd doek | Deze varianten leveren geen fix |
| Probes 6–8: eigen raster, ingekorte indexbuffer, andere timing, één LOD | Meestal enorm/vervormd doek; de verwachte rastervorm ontbreekt | Fout daadwerkelijk in game waargenomen |
| Native Item Editor save en vervolgens plaatsen | Bestand 196579 → 129142 bytes; eigen data blijft in geparseerd model; zichtbaar doek lijkt vanilla | Native save alleen verhelpt het probleem niet |
| Probe 9: eigen raster, unieke namen, tags leeg, schone game, alleen dat item | Enorm verkeerd doek | Meerdere geplaatste testmodellen zijn niet vereist voor de fout |
| Probe 10: `MaterialInsts`, Solid2 v15 | Mast zichtbaar, doek ontbreekt | Geen fix |
| Probe 10: `CustomMaterials`, Solid2 v28 | Groot vervormd doek | Geen fix |
| Probe 10: UserInst v11, game-material link | Groot vervormd doek | Geen fix |
| Externe vanilla-material-, dyna- en meshreferenties; inline materiaal met externe shader/texture-refs | Verschillende kandidaten reparsen, maar native preload geeft null | Niet geplaatst; parsing is geen native laadbewijs |
| Material-arrayalias na initialisatie | Geen verbetering; beide tabellen hersteld | Geen fix; oorspronkelijke arrays teruggezet |
| Native game-material-array vóór plaatsing, begrensd tot één visual | Mast zichtbaar, doek ontbreekt; `RESTORED 1` en unload gelogd | Geen fix en geen zelfstandige modeloplossing |
| Eigen skin-ZIP via skinsetter | Geen overtuigende skinverandering bewezen, ook niet op vanilla | Skinpariteit open |
| Race-/collisionpariteit | Geen complete rij-/collisiontestmatrix afgerond | Niet geclaimd |

De normaal weergegeven vroege vlaggen zijn echte gamebeelden. De later gevonden
fouten maken ze echter ongeschikt als bewijs van een geslaagd custom doekmodel.
Er is geen gecontroleerde causale verklaring voor het verschil tussen die sessies.

### Native geometriecontrole

De geladen anchored object verwees in de gecontroleerde proef naar hetzelfde
itemmodel als de FID-loader. Gewijzigde vertices waren aanwezig in native CPU-data.
Bij de streamvariant stonden de gewijzigde posities ook in de native Position-stream.
Dit sluit een simpele ontbrekende diskedit in die proeven uit; het zegt niet welke
vertexdata de uiteindelijke draw call gebruikte.

Let bij herhaling op de native layout: `visual+0xC0` was in de onderzochte build
de eerste streampointer zelf. Het is geen pointer naar een losse pointerarray die
nogmaals gederefereerd moet worden. Geen oude procespointers hergebruiken.

### Afgekeurde crashproeven

Drie varianten zijn niet veilig als modelrecept:

1. Doek op een statische host: crash tijdens plaatsing.
2. Alleen materiaalnamen zonder de vereiste UserInst-structuur: crash.
3. Frametabel verwijderd terwijl het VertexTween-materiaal behouden bleef:
   rendercrash in de plaatsingsreeks. De laatste succesvolle plaatsing was
   GridStatic; de exacte fout is niet apart geïsoleerd.

Crashlogs en afgekeurde bytes zijn alleen als onderzoeksbewijs bewaard. De
generator genereert de crashvariant zonder frametabel niet meer. De ongewijzigde
Editor.op is niet als oorzaak van deze crashes bewezen.

## Beelden en bewijsbestanden

Alles hieronder staat in de lokale, git-ignored map
`artifacts/flag-research-20261021/`.

| Bewijs | Betekenis |
| --- | --- |
| [Vroege playgroundvergelijking](../artifacts/flag-research-20261021/evidence/playground-comparison.webp) | Normaal golvende zichtbare vlaggen; geen bewezen eigen doekvorm |
| [Raster alleen na schone herstart](../artifacts/flag-research-20261021/evidence/probe9-grid-only-clean-process.webp) | Duidelijk mislukte custom weergave |
| [Custom versus vanilla, latere proef](../artifacts/flag-research-20261021/evidence/probes6-plus-vanilla-default-timing.webp) | Groot vervormd custom doek naast normaal vanilla exemplaar |
| [Native save, close-up](../artifacts/flag-research-20261021/evidence/compare9-native-close.webp) | Normale vorm na native save, maar nog steeds geen herkenbaar eigen raster |
| [v15 materiaalopslag](../artifacts/flag-research-20261021/evidence/probe10-UserInst15.webp) | Ontbrekend doek |
| [v28 materiaalopslag](../artifacts/flag-research-20261021/evidence/probe10-Custom28.webp) | Vervormd doek |
| [Directe native materiaalbinding](../artifacts/flag-research-20261021/evidence/direct-game-materials-before-render.webp) | Ontbrekend doek ondanks begrensde runtimeproef |
| [Bewegingsopname](../artifacts/flag-research-20261021/custom-flags-ingame.mp4) | 81 oorspronkelijke native frames over circa 28.94 s; gecorrigeerde labels zeggen uitdrukkelijk dat eigen doekvorm niet bewezen is |
| [Alle geregistreerde gamecalls](../artifacts/flag-research-20261021/game-calls.jsonl) | Requests, antwoorden en UTC-tijdstempels |
| [Capturelog](../artifacts/flag-research-20261021/evidence/captures.jsonl) | Bronpaden en gamemodus per native screenshot; mtime tegen stale captures gecontroleerd |
| [CPU-posities](../artifacts/flag-research-20261021/native-vertex-probe.json) | Gewijzigde posities in geladen modellen |
| [Vertexstream](../artifacts/flag-research-20261021/native-stream-probe.json) | Gewijzigde Position-stream |
| [Materiaalgegevens](../artifacts/flag-research-20261021/native-material-probe.json) | Runtime-materiaalrepresentaties; geen bewezen root cause |
| [Directe proef: load, gedrag, herstel, unload](../artifacts/flag-research-20261021/direct-material-complete.log) | Verse logwindow voor de tijdelijke plugin |

Oude JPG-captures en de R1-video hadden problemen met hergebruikte screenshotnamen
en/of te stellige labels. Ze zijn geen bewijs voor een geslaagde custom vlag.
De actuele MP4 is opnieuw gelabeld vanuit de bewaarde WebP-bronframes;
de bronbeelden zelf zijn niet aangepast.

### Gearchiveerde modellen

`held-items/not-approved-TMIS_CustomFlags/` bevat de twee vroege kandidaten:

| Bestand | Bytes | SHA256 |
| --- | ---: | --- |
| `TMIS_CustomFlag_Standalone.Item.Gbx` | 555061 | `F7E839CA8DAC19961A8BECB10D16BE59836B5ABE798A17EEFA4F308CC2BE9EC0` |
| `TMIS_CustomFlag_WideStandalone.Item.Gbx` | 554162 | `79989132DF788B8C510DD9577F31E09EB9ACDF662A6A41599A5B17F6117C2331` |

`held-items/not-approved-TMIS_FlagResearch/` bevat de overige probes, inclusief
het native opgeslagen model. De zelfstandige testkaart is gearchiveerd als
`TMIS_CustomFlags_Standalone.Map.Gbx`. Alle bestanden zijn **proefmateriaal**.

## Reproduceren en controles

Onderzoekscode: [FlagResearch.cs](../Tests/FlagProbe/FlagResearch.cs),
[game.py](../Tests/FlagProbe/game.py),
[live_fixture.py](../Tests/FlagProbe/live_fixture.py),
[capture.py](../Tests/FlagProbe/capture.py).
Deze scripts gebruiken de lokale bestaande Openplanet-testbrug en de originele
game-/gebruikersfixtures. De gamecalls worden automatisch vastgelegd.

```powershell
dotnet run --project Tests/FlagConversion
dotnet run --project Tests/FlagProbe -- --research verify artifacts/flag-research-20261021/held-items/not-approved-TMIS_CustomFlags
dotnet run --project Tests/FlagProbe -- --lod C:/Users/PC/Documents/Trackmania/Items/XertrovItems/ztest-flag3.Item.Gbx
python Tests/FlagProbe/capture.py --relabel
```

- **74 conversiechecks geslaagd**, inclusief roundtrip, exportsnapshotisolatie
  en gelijkblijvende bytes bij geweigerde conversie.
- **112584 assetinvarianten geslaagd** voor de twee vroege kandidaten. Dit
  controleert opgeslagen geometrie, frames, materiaalnamen, plaatsing en
  standalone-identiteit; het is geen renderacceptatietest.
- Alle laatst gegenereerde probes zijn gebouwd en met GBX.NET heropend.
- De gewijzigde `--lod`-uitvoer gebruikt center/halfextent en correcte afmetingen.
- Productcode en Editor.op zijn niet gewijzigd. Geen commit gemaakt.

## Tijdelijke plugin en opruimen

[TMISFlagMaterialProbe](../Tests/Openplanet/TMISFlagMaterialProbe/Main.as) was een
DEV-only engineproef zonder exports, dependencies of UI. Exact één eigen fixture
kon een material-array lenen. De plugin hield bron en doel in leven en herstelde
de pointervelden bij gate removal, timeout, editorwissel, disable of unload.

De directe proef had een gelogde compile/load zonder toerekenbare compilefout,
een echte plaatsing/capture en daarna `RESTORED 1` plus `Unloading plugin`.
De eerdere aliasproef logde `RESTORED 2`. LSP-pariteit was niet beschikbaar;
native gamecompilatie is wel waargenomen. Het blijft een tijdelijke diagnose,
geen pluginfix en geen ondersteund modelrecept.

Afhandeling: taggebonden testitems verwijderd, tag gewist, wegwerpkaart verlaten,
gate verwijderd, plugin unloaded en uit de live Plugins-map gearchiveerd.
Eigen testitems, testskins en de testkaart zijn uit de live contentmappen gehaald.
De lokale skin-HTTP-server op poort 18473 is gestopt. De game staat in `Menu`.

## Openstaande beslissende proef

Een gerichte inspectie van de **daadwerkelijke GPU draw call** moet vaststellen
welke twee vertexbuffers, frameoffsets en shaderconstanten de custom vlag bindt,
vergeleken met vanilla. De huidige metingen bereiken de CPU-/streamdata,
maar nog niet die laatste renderbinding. Verdere willekeurige varianten van
dezelfde vertices of plaatsingstags leveren daarvoor weinig nieuwe informatie.

Acceptatie blijft: een herkenbaar eigen doekmodel dat zonder tijdelijke
runtimepatch correct golft, opnieuw geladen/embedded kan worden, en de vanilla
LOD-, skin- en collision-/playtests doorstaat. Dat punt is nog niet bereikt.
