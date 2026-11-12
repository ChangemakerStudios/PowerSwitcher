# Release Procedure

PowerPlanSwitcher follows [Gitflow](https://nvie.com/posts/a-successful-git-branching-model/), uses [GitVersion](https://gitversion.net/) (ContinuousDelivery mode) for versions and [Velopack](https://velopack.io/) for the installer and updates. GitHub Actions builds, packages and uploads automatically based on the branch.

## Branch Overview

| Branch      | Purpose                                   | Update channel | GitHub release   |
|-------------|-------------------------------------------|----------------|------------------|
| `master`    | Production releases only                  | `-stable`      | Release (draft)  |
| `develop`   | Integration branch for the next release   | `-dev`         | Pre-release (draft) |
| `feature/*` | Feature branches off `develop`            | `-alpha`       | —                |
| `release/*` | Release stabilization branches off `develop` | `-alpha`    | —                |
| `hotfix/*`  | Critical fixes off `master`               | `-alpha`       | —                |

Channels are per architecture: `win-x64-stable`, `win-arm64-dev`, and so on. An installed copy updates only from the channel it was installed from, so a `-dev` install follows `develop` builds and a `-stable` install follows releases.

**Releases are created as drafts.** `vpk upload github` leaves the GitHub release as a draft; nothing is visible to users, and installed copies don't update to it, until you publish it on GitHub.

## Versioning

GitVersion derives the version automatically:

- On a `release/X.Y.Z` branch, **the version comes from the branch name** — this is the way to choose the next version.
- On `develop`, the version increments **Patch** per commit by default. A commit message containing `+semver: minor` or `+semver: major` forces a larger bump.
- Until the first release tag exists, `next-version` in [GitVersion.yml](GitVersion.yml) sets the starting point.
- To see the version a build would get, run `.\build.ps1 --target=Build` — Cake prints it first.

## Building locally

```powershell
.\build.ps1                    # restore, build, package x64 + Arm64 into .\releases (uploads only in CI)
.\build.ps1 --target=Build     # just compile
```

[build.ps1](build.ps1) restores Cake from the tool manifest; [build.cake](build.cake) pins GitVersion and `vpk`. Packages land in `releases/`: `PowerPlanSwitcher-win-x64-<channel>-Setup.exe`, a portable zip, and the update feed files.

The packages are framework-dependent: Setup installs the .NET 10 Desktop Runtime first if it's missing.

## Releasing a Stable Version

1. **Make sure `develop` is ready** — features merged, CI green.
2. **Create the release branch**
   ```bash
   git checkout develop && git pull
   git checkout -b release/X.Y.Z
   ```
3. **Finalize on the branch** — update [ReleaseNotesCurrent.md](ReleaseNotesCurrent.md) (it's shown in the installer and update) with the version header and changes.
4. **Merge into `master` and tag** — push the tag together with the branch so CI sees it:
   ```bash
   git checkout master && git pull
   git merge --no-ff release/X.Y.Z
   git tag X.Y.Z
   git push origin master --tags
   ```
   CI builds and packages x64 and Arm64 and uploads them to a **draft** GitHub release `X.Y.Z`.
5. **Publish the draft** on GitHub (Releases → the draft → Publish), or `gh release edit X.Y.Z --draft=false --latest`. Installed `-stable` copies pick it up within 12 hours.
6. **Merge back into `develop`**
   ```bash
   git checkout develop && git merge --no-ff release/X.Y.Z && git push origin develop
   git branch -d release/X.Y.Z
   ```
   If the release branch had no commits of its own, that merge is a no-op and the `X.Y.Z` tag (on `master`'s merge commit) isn't in `develop`'s history, so `develop` would keep building `X.Y.Z-alpha.N`. Bring the tag in with `git merge master` (a fast-forward) instead; the next `develop` build should then be `X.Y.(Z+1)-alpha.0`.

Pushes that only touch the README, RELEASING.md, `ReadmeAssets/` or the licence don't run the pipeline, so documentation can go straight to `master` without making a release.

## Pre-releases (from `develop`)

Every push to `develop` uploads `-dev` packages to a draft pre-release. Publish it to let `-dev` installs update.

## Testing the update flow locally

Set `POWERSWITCHER_UPDATE_SOURCE` to a local `releases` folder before starting an installed copy; it then updates from that folder instead of GitHub.

## Key Files

| File | Purpose |
|------|---------|
| [build.cake](build.cake) | Cake build script — build, package and upload tasks |
| [build.ps1](build.ps1) | Bootstrap: restores Cake, runs `build.cake` |
| [build/Velopack.cake](build/Velopack.cake) | `vpk pack` / `vpk upload github` helpers |
| [.github/workflows/build.yml](.github/workflows/build.yml) | CI pipeline |
| [GitVersion.yml](GitVersion.yml) | Versioning configuration |
| [ReleaseNotesCurrent.md](ReleaseNotesCurrent.md) | Notes for the current version, embedded in the packages |

## Troubleshooting

- **Release came out as `X.Y.Z-N`?** The pipeline ran before the tag was pushed. Delete the draft, make sure the tag is on the `master` head, and re-run the workflow (`gh run rerun <run-id>`).
- **Upload skipped?** `DeployReleases` only runs on `master`/`develop` in GitHub Actions with `GITHUB_TOKEN`; the workflow grants it `contents: write`.
- **"Windows protected your PC" when installing?** The installer isn't code-signed, so SmartScreen warns until the download gains reputation. Signing (e.g. Azure Trusted Signing) removes it.
