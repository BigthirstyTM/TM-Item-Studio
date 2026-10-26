# Lang doek en zwaluwstaart: ingame vervolgproef

> **Later vervolg:** [Native vanilla kopie en bewezen GPU-oorzaak](custom-flag-vanilla-steps-2026-10-21.md).
> Beide eigen vormen zijn inmiddels correct geanimeerd weergegeven met een
> tijdelijke engineflag. Dezelfde kaart faalt weer na een schone herstart zonder
> hulpmiddelen; een zelfstandige export is nog niet gelukt. Hieronder staan de
> eerdere ongecorrigeerde shapeproeven.

## Resultaat

**De twee afwijkende doekmodellen zijn gemaakt, maar de bedoelde vormen zijn
nog niet correct ingame weergegeven.** Beide modellen laden en zijn zichtbaar
geanimeerd. In de frontale close-ups blijven ze echter op een normaal vanilla
doek lijken: de 2,5× lengte en diepe V-inkeping verschijnen niet herkenbaar.
Dit is een mislukte renderacceptatietest, geen geslaagde custom vlag.

Dit vervolg bouwt voort op het [eerdere runtimeonderzoek](custom-flag-runtime-2026-10-21.md).
De oorzaak tussen opgeslagen/geëmbedde geometrie en gerenderd doek is nog niet
vastgesteld. De proef bewijst geen algemene onmogelijkheid.

## Gemaakte modellen

Alle bestanden staan buiten de live gamecontent in
`artifacts/flag-shapes-20261021/models/`.

| Model | Bedoelde wijziging | Ingame resultaat |
| --- | --- | --- |
| [Long250](../artifacts/flag-shapes-20261021/models/TMIS_Shape11_Long250.Item.Gbx) | Lokale doek-X × 2,5 in alle 86 frames en alle vijf LODs; attachment blijft op X≈0 | Geen aantoonbaar 2,5× langer doek |
| [Swallowtail](../artifacts/flag-shapes-20261021/models/TMIS_Shape11_Swallowtail.Item.Gbx) | Vrije rand langer aan de twee uiteinden en sterk ingekort in het midden; diepe V-inkeping | V-inkeping ontbreekt in frontale close-ups |
| [Control](../artifacts/flag-shapes-20261021/models/TMIS_Shape11_Control.Item.Gbx) | Zelfstandig custom item met oorspronkelijke doekposities | Normaal ogend geanimeerd doek; vergelijkingsmodel |

De zwaluwstaart gebruikt UV-V als vormparameter: de X-schaalfactor loopt van
1,75 aan boven-/onderrand naar 0,60 in het midden. De bestaande geanimeerde
topologie en frametabellen blijven behouden. De statische verre LOD krijgt negen
vertices en 24 indices uit het eerste lage-detailframe, zodat ook die geometrie
een inkeping kan bevatten. De mast/collider, dynamische doekhost, vanilla
materialen, animatieperiode/-fase, classificatie en plaatsingsstructuur zijn
behouden. Normals worden mee aangepast; dit blijft experimenteel materiaal.

De maximale lokale X-envelop over alle LOD0-frames is circa 4,031 m voor de
controle, 10,077 m voor Long250 en 6,992 m voor Swallowtail. Dit zijn
**bestandsmetingen**, geen gemeten gerenderde breedtes.

## Uitgevoerde gameproeven

- Game opnieuw gestart via `uplay://launch/5595/0`, na het installeren van de
  verse bestanden in een eigen Items-submap. Inventory en geplaatste bestandsnamen
  gecontroleerd via de bestaande localhostbrug.
- Eén eigen testkaart met vanilla `Flag16m`, custom controle, Long250 en
  Swallowtail, telkens 20 m uit elkaar. Vier daadwerkelijke plaatsingen
  teruggelezen in [placed-items.json](../artifacts/flag-shapes-20261021/placed-items.json).
- Editor: overzicht en individuele close-ups. Daarna de echte
  `MapEditor_Playground`, met camera-readback en native viewportcaptures.
- Per afwijkend model aanvullende captures op 10, 25, 80, 180 en 550 m,
  plus achteraanzicht en schuine kijkrichting: veertien checks in
  [view-matrix.json](../artifacts/flag-shapes-20261021/view-matrix.json).
  De verre beelden zijn te klein voor betrouwbare beoordeling van een inkeping;
  de ingestelde afstand alleen bewijst niet welke GPU-LOD actief was.
- Drie bewegingsopnamen: vergelijking met vanilla en individuele close-ups van
  beide afwijkende modellen. Elke opname beslaat ruim 21 s, langer dan de
  vanilla animatieperiode van maximaal 16 s. Veranderende doekplooien zijn in
  de ruwe frames zichtbaar, maar de gewenste nieuwe silhouetten niet.
- Testkaart native opgeslagen. De drie volledige custom modellen zijn embedded.
  Alle vijf LODs, vertexposities, indices en frametabellen vergeleken met de
  geëxporteerde bestanden: gelijk. De kaart heeft de geometrie dus niet verloren.

Er is geen volledige solo-race-, skin- of collisionpariteit geclaimd. Zonder
correcte doekvorm zijn de modellen niet geschikt voor acceptatie als vlaggen
die exact als vanilla functioneren.

## Opname en bewijs

[Gecombineerde ingame opname, 66 s](../artifacts/flag-shapes-20261021/custom-flag-shapes-ingame.mp4)

De video bevat achtereenvolgens de vergelijking, Long250 en Swallowtail.
De labels noemen de gewenste wijzigingen en de ontbrekende zichtbare vorm.
De vergelijking toont van links naar rechts: zwaluwstaart, lang doek,
custom controle en vanilla.

Het is een opname van **echte native gameframes**, ongeveer drie bronbeelden
per seconde, met de werkelijk gemeten tijdsintervallen. Er is geen
bewegingsinterpolatie toegepast. De MP4 is op 12 fps gecodeerd met herhaalde
frames; het is geen vloeiende 60-fps schermopname. In totaal zijn 188 ruwe
WebP-frames bewaard. De labels bedekken alleen een smalle strook bovenaan.

| Bewijs | Inhoud |
| --- | --- |
| [Editorvergelijking](../artifacts/flag-shapes-20261021/evidence/editor-comparison.webp) | Vier geplaatste vlaggen |
| [Long250 dichtbij](../artifacts/flag-shapes-20261021/evidence/playground-close-long250.webp) | Geen herkenbaar lang doek |
| [Swallowtail dichtbij](../artifacts/flag-shapes-20261021/evidence/playground-close-swallowtail.webp) | Geen herkenbare V-inkeping |
| [Testkaart](../artifacts/flag-shapes-20261021/TMIS_FlagShapes_Probe.Map.Gbx) | Vier vlaggen; volledige afwijkende geometrie embedded |
| [Modelmanifest](../artifacts/flag-shapes-20261021/models/shapes-manifest.json) | SHA256, LOD-afmetingen en eerste-frameposities/UVs |
| [Capturelog](../artifacts/flag-shapes-20261021/captures.jsonl) | Native bronpaden, UTC-tijden en bestandscaptures |
| [Gamecalls](../artifacts/flag-shapes-20261021/game-calls.jsonl) | Requests en antwoorden van setup tot opruimen |
| [Video-/modelhashes](../artifacts/flag-shapes-20261021/artifact-manifest.json) | Exacte bewaarde bytes |

Ruwe opnamen, elapsed-tijden en camera-/itemstate staan onder
`motion/{comparison,long250,swallowtail}-ingame/`. Capture-mtime is voor elke
native screenshot gecontroleerd om oude bestanden af te wijzen. De native
bronbestanden zijn na afloop naar `native-screenshots/` verplaatst.

## Reproduceren en validatie

Generator: [FlagShapes.cs](../Tests/FlagProbe/FlagShapes.cs).
Gamefixture/opname: [shapes_fixture.py](../Tests/FlagProbe/shapes_fixture.py).

```powershell
dotnet run --project Tests/FlagProbe -- --research shapes artifacts/flag-shapes-20261021/models
dotnet run --project Tests/FlagProbe -- --research shapes-map artifacts/flag-shapes-20261021/TMIS_FlagShapes_Probe.Map.Gbx artifacts/flag-shapes-20261021/models
```

De generator heeft per model 18792 roundtrip-/structuurchecks doorlopen,
56376 totaal. Die controleren onder andere frame- en indexintegriteit en
het bewaren van alle gewijzigde posities. **Dit is geen renderbewijs.**
De kaartvergelijking is voor de drie embedded modellen geslaagd.
De onderzoekstool bouwt zonder waarschuwingen/fouten; Python syntaxcontrole
en `git diff --check` slagen.

Getest in Trackmania `2026.2.2.1751`, Openplanet `1.29.14` Next/Public.
De bestaande Editor.op is niet gebouwd of vervangen en houdt SHA256
`A07D1450FF06CA281BBFD5B1F44B7C08E647A2C13D472E1EFD7AC4E4036FA644`.
De oorspronkelijke XertroV-vlag is ongewijzigd:
`3B14E7E9D8DDF237E8CA2D885031CC26D9D08BE1205E2FFBDF69F6C0DF1223FF`.

## Afhandeling

`RequestLeavePlayground` veranderde de actieve modus niet. De bestaande
`BackToMainMenu`-route heeft de eigen testsessie beëindigd; `Menu` is daarna
teruggelezen. Er is geen bestaande gebruikerskaart bewerkt. Het default
agenttag is gewist en de negen uitsluitend eigen, inmiddels stale tagrecords
zijn opgeruimd met `ClearTagIndex`; count is nul.

De eigen Items-submap en native opgeslagen testkaart zijn uit de live
contentmappen naar dit onderzoeksarchief verplaatst. Geen tijdelijke plugin
was voor deze vervolgproef nodig. Productcode is niet gewijzigd.
