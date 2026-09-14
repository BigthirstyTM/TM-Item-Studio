# Imported kinematic controls regression

```sh
npm ci --prefix Tests/Browser
npm --prefix Tests/Browser run test:kinematic-preservation
```

Requires the .NET 8 SDK/runtime (or compatible roll-forward runtime) and
Playwright Chromium. `DOTNET_HOST` can select a separate runtime for the archive
inspector. The runner publishes the checkout and serves it on an isolated port;
it does not rely on a developer server or a global Trackmania installation.

The approved original SnowCar archive is uploaded through the real UI. Its
first moving variant must retain its 0–32 metre range and 1920 ms translation
key rather than a generic two-second animation preset. The oracle reads every
constraint from every variant directly with GBX.NET and compares serialized
constraint bytes across the actual UI download. It does not use Studio's
motion reader to produce the expected archive values. It also checks all nine
SnowCar variants and explicit single-range/single-key edits. The expected edited
archives come from changing only those requested fields directly with GBX.NET;
every other serialized constraint byte must stay intact, including shared sources.

The fixture has `IsDuration=true`. The current typed evaluator deliberately
does not claim support for that mode. The browser must display that limitation
and retain the rest pose, not invent rotation, alter the flag, or rewrite the
source timeline to make preview work. The animation check controls only the
clock and observes world-space uploaded mesh vertices; it does not inject scene
data or substitute the renderer.

No native playback, collision or game-equivalence claim is made by this test.
The fixture is unchanged, and test-generated exports stay in temporary storage.
