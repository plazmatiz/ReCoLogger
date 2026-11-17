# Publishing to NuGet

## One-time setup

1. **Check that the package id `ReCoLogger` is free** on <https://www.nuget.org/packages/ReCoLogger>. If it is taken,
   change `PackageId` in `src/ReCoLogger/ReCoLogger.csproj` (keeping `AssemblyName`/namespace is fine).
2. Fill in the placeholders in `src/ReCoLogger/ReCoLogger.csproj`: `Authors`, `Copyright`, `PackageProjectUrl`,
   `RepositoryUrl`, `PackageReleaseNotes`. Also put your name in `LICENSE`.
3. Push the project to GitHub (repository `ReCoLogger`).
4. Set up **Trusted Publishing** (no API key needed; NuGet marks API keys as "not recommended"):
   - nuget.org → profile menu → **Trusted Publishing** → **Create**
   - Repository owner: your GitHub user/org, Repository: `ReCoLogger`, Workflow file: `publish.yml`,
     Environment: leave empty, package: `ReCoLogger` (or a glob such as `ReCoLogger*`)
   - In the GitHub repository add the secret `NUGET_USER` = your nuget.org profile name (not the email).
   - If NuGet refuses the very first publish of a brand-new package id, push version 1.0.0 once manually with a
     short-lived API key (see "Release manually"); later releases go through Trusted Publishing.

Optional: add a 128x128 `icon.png` next to the csproj and set `<PackageIcon>icon.png</PackageIcon>` plus
`<None Include="icon.png" Pack="true" PackagePath="\" />`.

## Release with GitHub Actions

Tag a commit; the workflow in `.github/workflows/publish.yml` builds, tests, packs and pushes:

```
git tag v1.0.0
git push origin v1.0.0
```

The version comes from the tag (`v1.0.0` becomes `1.0.0`; prerelease tags like `v1.1.0-beta.1` work too).

## Release manually

```
dotnet test -c Release
dotnet pack src/ReCoLogger/ReCoLogger.csproj -c Release -p:Version=1.0.0 -o artifacts
dotnet nuget push artifacts/ReCoLogger.1.0.0.nupkg --api-key <KEY> --source https://api.nuget.org/v3/index.json
```

The `.snupkg` symbols package next to the `.nupkg` is pushed automatically. Source Link is enabled, so users can step
into the library code while debugging (this needs the project to live in a Git repository with a GitHub `origin`).

## Checklist before every release

- [ ] `CHANGELOG.md` updated
- [ ] `dotnet test -p:TestAllTfms=true` is green
- [ ] README examples still compile
- [ ] Version follows semver (breaking API change = major)

NuGet packages cannot be changed after publishing; a bad release can only be *unlisted* and superseded.