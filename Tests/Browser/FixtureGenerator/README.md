# Upload animation regression fixtures

This generator constructs two uncompressed archives from scratch, using the
repository's bundled parser. No existing item, external asset, or private data
is read. Each contains two triangles, static/dynamic body flags, and a small
kinematic constraint. The second archive reverses the two bodies' array order.

The generator saves and reparses every output, then checks the actual decoded
geometry, body flags, and translation constraint. Vertex-stream declarations
require reflection because the bundled serializer does not expose setters;
that reflection is confined to fixture generation, not application code.

From the repository root, with the browser dependencies and Chromium installed:

```sh
npm --prefix Tests/Browser run test:animation-upload
```

This publishes the current checkout, generates both fixtures, and serves that
exact output on an automatically allocated localhost port. It prints the app
and viewer hashes and cleans up its temporary server/files afterward. An existing
developer server or `STUDIO_BASE_URL` cannot silently select an older build.
The child processes allow .NET runtime major-version roll-forward if not already
configured, so the net8 fixture generator can run with newer installed runtimes.

For manual debugging only:

```sh
dotnet run --project Tests/Browser/FixtureGenerator -- /tmp/studio-animation-fixtures
dotnet publish -o /tmp/studio-animation-app
python3 -m http.server 5199 --bind 127.0.0.1 --directory /tmp/studio-animation-app/wwwroot
```

In another terminal, with `Tests/Browser` dependencies and Chromium installed:

```sh
STUDIO_BASE_URL=http://127.0.0.1:5199 node Tests/Browser/animation-upload.cjs /tmp/studio-animation-fixtures/animation-static-first.Item.Gbx
STUDIO_BASE_URL=http://127.0.0.1:5199 node Tests/Browser/animation-upload.cjs /tmp/studio-animation-fixtures/animation-static-last.Item.Gbx
```

The browser test uploads via the real file input and drives the production
animation callback using a controlled clock. It reads the rendered triangles'
world-space vertices, requiring the static base to stay fixed while the moving
triangle changes. It does not inject scene payloads or substitute parser/viewer
implementations. It also fails on page errors.

This covers the legacy preview's static/moving classification, including array
order. It does **not** prove that the preview reproduces the authored constraint
timing or that these minimal items preload in TM2020. Those are separate gates;
the generated binaries remain local validation artifacts until validated in-game.
