# Item Studio: GBX.NET 2.5 audit en ontwikkelroute

Datum: 30 oktober 2026. Status: audit; gevonden fouten zijn nog niet opgelost of gedeployed.

## Conclusie en scope

GBX.NET 2.5.0 is daadwerkelijk geladen, maar de migratie van de hele applicatie en testomgeving is niet af. Er zijn reproduceerbare fouten in placement/magnets, vlagconversie en Mesh Modeler-export, plus ontbrekende ondersteuning en falende aanvullende projecten. Eerst de bestaande functies betrouwbaar maken, daarna uitbreiden.

Het doel is iedere beschikbare `.Item.Gbx` te kunnen beoordelen en zoveel mogelijk veilig te bewerken en exporteren voor Trackmania 2020. Universele ondersteuning is nu niet bewezen: parserondersteuning, preview, bewerkbaarheid, dependencies en werking in het spel zijn afzonderlijke voorwaarden. Een leesbaar ouder Trackmania-item is niet automatisch een geschikt TM2020-item.

De huidige checkout (`e538273`, migratie `7239f5b`) is onderzocht. De actuele mislukte GitHub-run hoort bij mergecommit `8bcca8b`. Parser: 2.5.0, upstream `11dc57abbc005ececd95d88bcab640747eba2930`, DLL SHA-256 `3a086f8a2eafeb755dd9595eae4dfe061f5a6190890f07176aa8ead0d20a2bf9`. Deze hash is ook in beide corpusprogramma's gecontroleerd.

Uitgevoerd: 14 centrale suites; builds van alle 51 ingecheckte test-/hulpprojecten; corpusinventarisatie en onafhankelijke save/reparse-controle op 7.550 lokale items; headless Chromium tegen de huidige lokale app; browserimport/export van het aangeleverde Mesh Modeler-bestand; vergelijking met upstream releasenotes en layouts.

Dit is geen volledige native Trackmania-acceptatietest. De 7.550 items zijn niet allemaal in het spel geplaatst of gespeeld. Console-roundtrips bewijzen niet automatisch behoud van iedere eigenschap. De corpusmap bevat ook eerdere experimentele probes. De oudere telling van 7.454 items is geen identieke vergelijkingsset.

Bewijs: [artifacts/audit-gbx250](../artifacts/audit-gbx250/), met `summary.txt`, `tool-summary.txt`, `extra-build-summary.txt`, `corpus/`, `roundtrip.json`, browserlogs en `*-ui-probe.log`. Bronitems zijn niet overschreven.

## Testresultaten

| Controle | Resultaat | Betekenis |
|---|---|---|
| Corpus lezen | 7.510/7.550 | 40 leesfouten |
| Opslaan en opnieuw lezen | 7.510/7.510 leesbare items | Parser accepteert eigen output; nog geen semantische/native garantie |
| Byte-identieke output | 143/7.510 | 7.367 worden anders gecodeerd; verschil alleen bewijst geen corruptie |
| Centrale suites | 8 geslaagd, 6 gefaald | Vier compileerproblemen, twee runtime-fouten |
| Test-/hulpprojecten | 33 bouwen, 18 bouwen niet | Een build is geen gedragstest |
| Geldig aangeroepen browsergroepen | 6 geslaagd, 3 gefaald | Details hieronder |
| Capability-browsertest | Niet voltooid | Fixturegenerator faalt op verdwenen private field |

Geslaagde centrale suites: ItemEdits (91 checks), ItemIcons, ItemMaterials (4), ItemMotion (31), ItemScene (27), ParserSentinels (6), ParserSurfaces (23), VariantExport (38).

Gefaalde centrale suites: FlagConversion, WavingFlagExport, WavingFlagMapPackaging en KinematicReference compileren niet; MacroblockExport heeft 14 passes en 2 failures; PlacementEdits stopt bij een heropende placement-chunk.

Browser geslaagd: editor-shell, compact-layout, icon-pipeline, typed-viewer (19 groepen), material-preview en variant-navigation. Gefaald: viewer-lifecycle (verwachte fouttekst niet gevonden), workflow-regressions (vlagdownload blijft uit), helper-coverage (vijf controls zonder hulptekst). Twee initiële aanroepen zonder verplicht fixture-argument zijn uitgesloten: materiaalpreview is correct herhaald; capability-gating bleef geblokkeerd door de generator.

## P0: bestaande functionaliteit en releasebetrouwbaarheid

1. **Placement-alternatives/magnets falen na heropenen.** `Models/ItemPlacementAlternatives.cs` verwacht `ISkippableChunk.Data`. In 2.5 wordt `2E020004` getypeerd als `MagnetLocs` met `Position` en `YawPitchRoll`. Fout: `The 2E020004 chunk carries no payload to read`. Gebruik typed data voor bekende versies met gecontroleerde fallback voor onbekende payloads. Noem deze gegevens in de UI correct magnets/aansluitpunten; niet zonder meer extra plaatsingsvarianten.

2. **Vlagconversie verwijdert haar eigen placement-class.** `Models/ItemFlagConversion.cs:295` kent `PlacementClass` toe en roept daarna `References.PlacementClass.Clear()` aan. Dat wist de hele referentie, inclusief de nieuwe node. Browserreproductie: `PlacementClass: absent`. Embedded toekenning ruimt de externe descriptor al op; de oude semantiek van `*File = null` mag niet mechanisch naar `Clear()` worden vertaald.

3. **Mesh Modeler-preview gebruikt een onjuiste laagkoppeling.** `Models/ItemSceneGeometry.cs` gebruikt alleen `Transforms[0]` voor de eerstvolgende geometrylaag. Het aangeleverde bestand bevat `Layer0/block`, een deformation-mask gericht op negen groepen van `Layer0`, gevolgd door `Layer1/clip_1` en `Layer2/clip_2`. De code transformeert `clip_1` zonder mask of volledige deformation-context. `Transform`, `Box` en de enabled-state van de modifier worden niet correct meegenomen. De definitieve pivot/deformation-semantiek moet tegen een native referentie worden vastgesteld; niet een offset of andere matrix gokken. De bestaande ItemScene-test bevat geen deformation-laag en vangt dit niet.

4. **Mesh Modeler-export wordt geweigerd.** Het aangeleverde item importeert, maar export geeft `The item has no entity model and cannot be loaded by Trackmania`. `ItemExportValidator` accepteert alleen `EntityModel`, terwijl de editorrepresentatie in `EntityModelEdition.MeshCrystal` zit. Voeg een afzonderlijke geteste exportroute voor edition-items toe; versoepel niet blind de controle voor werkelijk lege items.

5. **29 corpusbestanden falen op gedeelde vertex-declarations.** Fout: `Shared declaration must refer to an earlier buffer with the same stride`. Ook een prefab-case van MacroblockExport raakt deze fout. Onderzoek bronlayouts en upstream-validatie met minimale fixtures; schakel validatie niet zomaar uit. De overige 11 fouten: vier class-id 0, vier class-id FF, twee end-of-stream en één onverwachte class-id. Diverse bestanden zijn experimentele probes, dus niet allemaal een bewezen regressie op geldige game-assets.

6. **Deployment negeert rode tests.** Pages start zelfstandig bij een push naar main en hangt alleen van de eigen build af. De mail is actueel: [Tests-run 41059086595](https://github.com/BigthirstyTM/TM-Item-Studio/actions/runs/41059086595), 30 oktober 2026 om 13:55:48 Nederlandse tijd. Tests moeten een vereiste worden voor publicatie van dezelfde commit.

## P1: testdekking, dependencies en UX

7. **18 projecten bouwen niet.** Naast de vier centrale compileerfailures en FlagProbe/PathFollowerDiag: FlagBakedRotation, FlagKinematicRemap, FxClone, FxEmitterTransform, FxMapPackaging, FxMeshPreview, FxParticleModelAudit, FxPlacementAudit, FxPointWriter, FxVisualMap, SkywardBannerResearch en SkywardMaterialResearch. Zowel oude API's als conflicterende transitive GBX.NET-references komen voor. Centraliseer parser/LZO-references en controleer de geladen DLL-hash. Onderzoekstools herkenbaar apart houden, failures niet stilzwijgend schrappen.

8. **Fixturegenerator gebruikt verdwenen private velden.** `Tests/Browser/FixtureGenerator/Program.cs:126` zoekt `count` via reflection. Gebruik publieke count/declaration-API's waar mogelijk, anders een versiegeteste fixturebuilder.

9. **Macroblock-export geeft een NullReferenceException bij ontbrekende Ident.** De test voor een nette weigering faalt. Dit is een afzonderlijke fout naast de shared-buffer-case.

10. **Materialen-dropdown is niet doorzoekbaar.** `MaterialOptionsFor` retourneert de volledige bibliotheek; de UI rendert die per slot als gewone select. De materiaalpreview zelf slaagt, inclusief full-link matching, PBR, BC4/BC5, kleurmaskers en behoud van exportbytes tijdens alleen previewen.

11. **Materiaalbewerking heeft een onduidelijke grens.** De handler accepteert alleen `CPlugSolid2Model.Material`; crystal- en andere slots worden geweigerd. Toon editmogelijkheden vooraf per slot en voeg een afzonderlijke geteste crystal-materialadapter toe.

12. **Help ontbreekt op vijf controls:** FX-carrierpreset, particlekeuze, particle-auditkeuze, Blender-addon-download en cloth-import. De lifecycle-test verwacht daarnaast een oude fouttekst: een timeout hier is niet automatisch een renderercrash.

13. **Viewer-cacheverversing matcht niet.** Pages vervangt `meshViewer.js?v=12`, terwijl index.html `v=28-audit-fixes` bevat. Die vervanging doet niets. Dit bewijst niet dat de screenshot door cache komt, maar moet worden hersteld.

14. **Automatische updatepoort is te smal.** De wekelijkse workflow maakt al een PR voor de nieuwste stabiele parser, maar draait slechts drie suites en een appbuild. Voeg placement-, export-, flag-, dependency-, browser- en corpusregressies toe. Nieuwe API's worden niet automatisch UI-functionaliteit.

15. **Dependencies en documentatie lopen uiteen.** Er zijn `System.IO.Hashing`-versiewaarschuwingen. Comments/documentatie noemen classes nog unsupported die 2.5 nu kan lezen. De hoofdapp bouwt, maar dat bewijst geen volledige migratie.

## Bestaande functies verbeteren met 2.5

| Gebied | Verbetering | Vereiste verificatie |
|---|---|---|
| Placement | Typed MagnetLocs, rotaties en placement-class; magnetpreview | Roundtrip en native snapping |
| Dependencies | NodeRef/NodeRefList/References en lazy resolution | Embedded/file/resource-descriptors behouden |
| Variants/prefabs | DeepClone voor geïsoleerde werkdocumenten en duplicatie | Gedeelde nodes, cycli en references behouden |
| Mesh Modeler | Layer-stack, masks/groups, modifier-volgorde en materials | Kleine game-authored fixtures en native beeldvergelijking |
| FX | Nieuwe CPlugFxSystem-reader benutten; header-only route per versie herzien | Typed roundtrip en native emittergedrag |
| Skins/characters/media | Nieuwe readers en gerichte scene/edit-adapters | Readeraanwezigheid is geen editgarantie |
| Nieuwe items | Optioneel GBX.NET.Builders met TM2020-defaults | Chunkversies, defaults en native load |
| Map-packaging | Upstream hergebruik van ongewijzigde embedded data onderzoeken | Exacte bestaande payload-behoudtests |
| Diagnostics | Betekenisvolle membernamen en game-version-metadata | Alleen bewezen functies editbaar tonen |

Upstream bevat nu readers voor CPlugFxSystem, CGameCharacterModel, CPlugMediaClipList en CPlugGameSkinAndFolder. Item Studio benut die niet overal: GbxDependencyLoader behandelt FX-systemen bijvoorbeeld standaard header-only. Herzie die routes per versie en behoud fallback voor onbekende data.

Builders, CLI en PAK zijn afzonderlijke onderdelen. De release noemt PAK 2.4.5 en CLI 0.1.0; een core-DLL-upgrade integreert die niet. GBX.NET levert geen texturepixels wanneer het item alleen naar game-assets verwijst. Die moeten beschikbaar zijn uit meegeleverde bestanden, een gekozen lokale game/extractiemap of een toekomstige lokale assetservice.

## Nieuwe opties

### Eerst: betrouwbare import en export

- **Compatibiliteitskaart per item:** gelezen, preview compleet/deels, bewerkbare onderdelen, ontbrekende dependencies, export heropend en native getest. Geen generiek groen 'werkt' voor alleen parse-succes.
- **Exportcontrole:** heropenen, references en belangrijke eigenschappen vergelijken, onbekende data controleren en een fout per onderdeel. Ongewijzigd origineel beschikbaar houden bij unsupported bewerking.
- **Dependency-manager:** ontbrekende mesh/material/texture/shape opsporen, alleen nodige assets laden, dubbele namen/conflicts tonen en waar geldig een exportpakket met manifest maken.
- **Doorzoekbare materiaalkeuze:** huidige link, miniatuur, categorie, bron en texture-aanwezigheid; favorieten en recent gebruikt.
- **Placement-editor:** magnets, pivots, snap/grid en rotaties visueel bewerken; presets en aansluitpuntpreview.
- **Undo/redo en wijzigingenoverzicht:** geïsoleerd werkdocument, per bewerking terugzetten en vóór export tonen wat daadwerkelijk verandert.

### Daarna: meer itemfamilies en bewerkingen

- **Mesh/lagen:** groepselectie, zichtbaarheid/collision apart, transforms, materiaaltoewijzing, normals en UV-inspectie. Begin met bewezen crystal-/triangle-layouts.
- **Varianten:** dupliceren, verwijderen, hernoemen, tags, volgorde en expliciet beheer van gedeelde onderdelen.
- **UV-tools:** verschuiven, draaien, schalen, spiegelen en checker per veilige scope; uitbreiden naar crystal-faces.
- **Collision/gameplay:** collision-overlay, fysiek materiaal, triggers/start/checkpoint/finish en primitieve shapes, met rij-/contacttest.
- **Animatie:** tijdlijn, easing-presets, parent/child-relaties en ondersteunde baking. De gemeten viersegmentgrens niet oprekken omdat een parser meer aankan.
- **FX/licht:** typed effectinspectie, gecontroleerde emitterparameters en presets met native vergelijking; geen universele shadereditor beloven.
- **Skins:** slotmapping, externe texturebinding, dependencydiagnose en preview per skin.
- **Batchbewerking:** ident, iconen, materiaalvervanging en placementpresets met dry-run en resultaat per bestand.
- **Assetbrowser:** thumbnails en filters op type, gameversie, materiaal, collision, animation en ondersteuningsstatus.
- **Native testknop via optionele lokale companion:** export, game-load, disposable map, plaatsing, screenshot en saved-map-heropening. GitHub Pages kan deze lokale toegang niet zelfstandig verzorgen.
- **Icon-generator:** bestaande raw/WebP-import behouden, thumbnails maken met gekozen camera/licht.
- **Legacy-conversieprofielen:** afzonderlijke regels voor oudere Trackmania/Maniaplanet-assets; ongeschikte gameplay-, shader- en vehicletypes begrenzen.

## Bediening: compact en doorzoekbaar

Behoud de centrale viewport. Groepeer in **Item**, **Model**, **Materialen**, **Plaatsing**, **Gedrag** en **Export**, met geavanceerde informatie ingeklapt. Toon alleen relevante controls. De huidige compacte layout en variantnavigatie slagen: een totale UI-herbouw is niet nodig.

Maak één herbruikbare searchable combobox voor materialen, textures, assets, FX, presets en grote variantlijsten:

1. Zoekveld bovenaan; direct hoofdletterongevoelig filteren op naam en volledig pad.
2. Grote lijsten virtualiseren; categorie-/typefilters, resultaataantal en lege-resultaatmelding.
3. Miniatuur en korte naam, met volledig pad en bron op de tweede regel. Volledige link blijft de identiteit, ook bij gelijke namen.
4. Pijltjestoetsen, Enter, Escape, focusherstel en correcte ARIA; bruikbaar op klein scherm.
5. Zoeken wijzigt het item niet; alleen selecteren verandert de waarde. Huidige onbekende/externe waarde behouden.
6. Recente keuzes en favorieten; onderscheid tussen texture aanwezig, alleen reference en ontbrekend.

Gewone dropdowns blijven passend voor twee of drie keuzes. Bij duizenden materialen zijn zoeken en efficiënte rendering standaard. Vrije material-links kunnen onder Geavanceerd blijven.

## Uitvoervolgorde en acceptatie

**Fase 1 — betrouwbaar 2.5:** references/Clear, typed magnets, edition-export, deformation-preview en shared-declarations; projecten migreren en dependencies uniform maken. Acceptatie: kernsuites groen, echte regressiefixtures, browserexport/heropenen en gerichte native bevestiging.

**Fase 2 — veilige import/export:** compatibilitykaart, dependency-manager, werkdocument/undo en semantische exportdiff. Iedere ondersteunde fixture krijgt een expliciet contract; unsupported data behouden of weigeren vóór mutatie.

**Fase 3 — vindbare bediening:** doorzoekbare dropdowns, miniaturen/favorieten, contextuele controls en help. Grote bibliotheek blijft vlot; toetsenbord en mobiel werken; selectie verandert precies het bedoelde slot.

**Fase 4 — uitbreiden per familie:** crystal/lagen/UV, variants, collision, animatie, FX/skins/characters. Iedere familie doorloopt lezen, wijzigen, schrijven, heropenen, native laden, plaatsen en zichtbaarheid/gedrag controleren.

**Releasepoort:** alleen de exact geteste commit publiceren. Automatische update-PR's behouden, testdekking uitbreiden en app/parser/gameversies plus fixturehash bij native bewijs registreren.

## Openstaande status

De mail is actueel en de Mesh Modeler-klacht is terecht. Dit is geen reparatierelease. Productcode is tijdens de audit niet aangepast; er is geen nieuwe versie gepubliceerd. De fouten blijven open, inclusief de definitieve native gevalideerde meshcorrectie. Dit verslag vormt het concrete werkpakket voor herstel en uitbreiding.

Upstream: https://github.com/BigBang1112/gbx-net/releases/tag/v2.5.0
