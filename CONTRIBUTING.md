# Contributing

Keep business behavior in Core/Query/Storage, drawing in Rendering, reusable UI in Controls and platform adapters in App. Do not introduce a dependency from a library back to the application.

## Before a pull request

Run the managed tests, compile the desktop target and publish the browser target using the commands in the README. A behavior change should include a failing-then-passing regression test. Changes to data editing must preserve record identities, atomic rollback and expected-revision checks. Never bypass tests or declare a failed operation successful to keep a demo running.

Keep documentation proportionate: public API contracts, architectural decisions and explicit compatibility boundaries belong in the repository; transient progress diaries do not. A PR should state what changed, what was actually tested and what remains unverified.

## Browser checks

After `dotnet publish` and `python3 scripts/stage-site.py`:

```bash
mkdir -p artifacts/server/DataSpace
cp -a artifacts/site/. artifacts/server/DataSpace/
printf '<!doctype html><title>DataSpace storage test</title>' > artifacts/server/DataSpace/storage-test.html
(cd scripts && npm install --ignore-scripts && npx playwright install chromium)
python3 -m http.server 4173 --bind 127.0.0.1 --directory artifacts/server
# In a second terminal:
node scripts/browser-smoke.mjs
```

Linux browser tests may require `npx playwright install --with-deps chromium`. `DATASPACE_URL` overrides the default `http://127.0.0.1:4173/DataSpace/`; `DATASPACE_SCREENSHOTS` sets the diagnostics directory. The `/DataSpace/` path intentionally checks repository-subpath deployment, not just root hosting. The test-only `storage-test.html` file must not be mistaken for the application.

Tests use isolated browser contexts and a separately named IndexedDB database. Do not point automated destructive tests at a production browser profile. Add interactive tests as features are qualified; startup and storage tests alone do not validate all UI behavior.

## Packages and releases

The release workflow accepts pushed version tags such as `v0.1.0` and `v0.2.0-preview.1`. It runs verification before publishing artifacts. Tags are maintainer actions; do not create release tags for an unreviewed or failing branch. Tag builds attach self-contained desktop executables and packages to the GitHub Release, then publish the packages to NuGet.org through Trusted Publishing from the protected `nuget` environment (only the `NUGET_USER` variable is needed; no API key is stored). A manual `workflow_dispatch` run with a `version` input is a dry run that publishes nothing.

When changing Uno or SkiaSharp, align managed and native packages and retest both targets. The documented native build compatibility target is version-specific; do not keep accumulating broad overrides after upgrading dependencies.
