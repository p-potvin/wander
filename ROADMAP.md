# Wander — Roadmap

> **Vision:** Tailnet-native shared folders for small teams.
> *Your team's drive, on your own network. No cloud, no accounts, no pairing codes.*

Wander syncs folders between the machines of a small team (1–4 people) over their
Tailscale tailnet. Tailscale provides discovery, identity, and transport security;
Wander provides the sync engine, the trust-building GUI, and guardrails that never
lose data — while staying lenient, because small teams communicate.

**Why not Syncthing / Dropbox?** Syncthing is hostile to non-technical teams
(device-ID exchange, no identity, spartan UX). Dropbox/Drive are the cloud that
tailnet users deliberately left. Nobody occupies "zero-config team sync on the
network you already own."

---

## Signed-off product decisions (interview, 2026-07-15)

| Decision | Choice |
| --- | --- |
| Audience | Built as the maintainer's daily driver first; packaged as a VaultWares product once proven |
| Tailscale | Fully native: peer discovery via Tailscale local API, identity via `WhoIs`, transport security from the tailnet. Non-Tailscale users are out of scope |
| Team model | Team-first: peers are *people* (attribution, presence, membership). Solo multi-device use is a team of one |
| Topology | Mesh of 3–4 peers. Opportunistic relay: any online peer holding the newest version serves it — no special roles |
| Conflicts | Lenient: newest edit wins everywhere; losing version preserved beside the file as `name (conflict — <peer>, <date>).ext` + notification. No locks, no blocking prompts. Soft presence hints ("Alice saved this 2 min ago") help avoid collisions |
| Deletes | Never silent: remote deletes land in a local `.wander/trash` with 30-day retention |
| v1 data scope | Repo-scale working files (docs, code, configs). Whole-file transfer + SHA-256 is acceptable; chunking/delta deferred |
| GUI | Mandatory in v1. Dashboard (peers, folders, live activity, conflicts) + tray icon. Built in **Avalonia** (Skia) — Windows polished first, macOS/Linux kept cheap |
| Versioning | v1 baseline: conflict copies + delete trash. Browsable history ("Wander back in time", powered by the **A.N.S.W.E.R.S.** retention policy — see `ANSWERS_backup_algo.md`) is the flagship Phase 2 feature |

---

## Phase 0 — Foundations (make what exists real)

The current code is a sound skeleton whose layers were never connected. This phase
turns mocks into a working core, with tests, before any feature work.

**Hygiene** *(done 2026-07-16)*
- [x] Remove `dist/` build output from git; ship via CI artifacts instead
- [x] Fold the orphaned root `themeManager.cs` into `Wander.UI/Theming` (wired to the WPF shell's theme toggle)
- [x] CI: build + test on push (`.github/workflows/ci.yml`)

**Engine (`Wander.Core`)** *(done 2026-07-16)*
- [x] Initial folder scanner: walks the sync root, mints file GUIDs, populates `StateDatabase` (`FolderScanner`)
- [x] File identity: GUID minted at first local index; watcher renames keep the GUID and propagate as moves
- [x] Outbound half is pull-based by design: peers publish state via `ListFiles` and serve `DownloadFile`; every node pulls what it's missing (this is also what makes opportunistic relay work)
- [x] `LocalIndexer` consumes watcher events with per-path debounce (500 ms quiet period)
- [x] Delete tombstones + `.wander/trash` with retention (`TrashService`)

**Network (`Wander.Network`)** *(done 2026-07-16)*
- [x] Real `ListFiles` / `GetFileState` / `DownloadFile` handlers backed by `StateDatabase` + chunked file streaming
- [x] Client-side caller: `SyncOrchestrator` runs a full pull round against a peer
- [x] Peer discovery via `tailscale status --json` (`TailscaleService`)
- [x] Inbound auth via `tailscale whois` interceptor (`TailscaleAuthInterceptor`) — *not yet exercised on a real tailnet*
- [x] `protocol_version` in Ping; orchestrator refuses mismatched peers

**Verification** *(done 2026-07-16 — 31 tests green)*
- [x] Unit tests: conflict-policy matrix, scanner GUID stability, trash retention, hash/path utils, state DB
- [x] Two-node loopback integration: create → update → conflict-with-preserved-copy → delete-to-trash, over real gRPC

**Remaining before Phase 1 starts**
- [ ] Run two real machines on the tailnet end-to-end (validates WhoIs auth + discovery in anger)
- [ ] Decide GUID collision handling when two peers create the same path independently (open question #1)

## Phase 1 — Trustworthy MVP (daily-drive milestone)

- [x] Bidirectional 2-peer sync engine (verified over loopback gRPC; live two-machine run staged on clopeux-desktop, awaiting launch)
- [x] Conflict copies with peer attribution; conflict browser card in the dashboard
- [x] Avalonia dashboard in **vaultwares-revisited**: warm document frame (node, peers, conflicts) wrapping the console activity core (per-file pull/move/conflict/trash feed, mono)
- [x] Tray icon (VaultWares mark): close-to-tray keeps the engine running; quit is explicit
- [x] Pause syncing (`SyncController`): paused node stops pulling *and* stops advertising its manifest, so it goes silent both directions while the local watcher keeps indexing for instant resume. Tray toggle + dashboard button, kept in sync. Integration-tested.
- [x] In-app notifications when a conflict / remote delete / sync error lands (Avalonia `WindowNotificationManager`, bottom-right toasts)
- [x] Installer + auto-update (Velopack): `scripts/pack.ps1` produces `VaultWaresWander-win-Setup.exe` + an update feed; `UpdateService` checks on startup, downloads in the background, and offers "restart to apply" via tray + toast (never restarts sync out from under you). Feed source is a plain URL/UNC path *or* a GitHub repo (uses the Releases feed). `.github/workflows/release.yml` cuts a release on a `v*` tag. Verified: `vpk pack` builds the setup bundle and confirms the `VelopackApp.Run()` hook.
- [ ] Code-sign the installer (currently unsigned → SmartScreen warns). Needs a cert; wire `--signParams` into `pack.ps1`.
- [ ] OS-level toasts when minimized to tray — now *unblocked* by the installer (the Velopack Start-Menu shortcut registers the AppUserModelID that Windows Action Center toasts require). In-app toasts cover the window-open case today.
- [ ] **Exit criterion: the maintainer daily-drives Wander for their own working files**

> Brand note (2026-07-16): all UI follows `vaultwares-themes/vaultwares-revisited/`
> (TOKENS.md / PHILOSOPHY.md). Console mode and warm mode coexist — console is the
> operational core, warm is the structural frame. Golden Slate et al. are legacy.

## Phase 2 — The team release

- [x] **Wander back in time** (flagship): per-file version history + restore, verified end-to-end.
  - Content-addressed `VersionStore` under `.wander/versions` (dedup); `FileVersions` timeline table.
  - `VersionRecorder` captures a version on every content change — local edit, initial scan, or pull from a peer (attributed to the source node).
  - **A.N.S.W.E.R.S.** retention engine (`AnswersRetention`, behind `IRetentionPolicy`) implements the owner's alternating non-sequential "middle-out" thinning. ⚠️ *Interpretation flagged for owner sign-off — see the doc comment in `AnswersRetention.cs`; the spec has genuine ambiguity and the exact eviction rule is the owner's call.*
  - Dashboard "Wander back in time" window: file list → version timeline (current marker, source, size) → Restore. Restore = a normal edit that propagates to the team.
  - Fixed a real startup crash surfaced by the live test: the scanner threw on two FileStates sharing one path (open question #1). Now tolerated (live-over-tombstone, then newest); regression-tested.
- [x] 3–4 peer mesh with opportunistic relay — the daemon already pulls from *every* discovered tailnet peer each round, so whoever holds the newest version serves it (relay is emergent, no special roles). Multi-peer works today; hardening/awareness features below.
- [ ] Soft presence hints in dashboard/tray ("Alice saved this 2 min ago")
- [ ] Folder membership & invites built on tailnet identities
- [ ] Reconcile duplicate GUIDs for the same path across peers (open question #1 — currently tolerated, not merged)
- [ ] macOS/Linux builds (Avalonia makes this a checkbox, not a rewrite)
- [ ] Transparent privacy panel: "here is exactly what Wander stores, and why" (VaultWares voice)

## Phase 3 — Showpieces

- [ ] Files-on-demand placeholders via Windows Cloud Filter API (OneDrive-style hydration)
- [ ] Encrypted vault folders: peers (e.g. a VPS anchor) that hold ciphertext only
- [ ] Chunked/delta transfer + resumable downloads (unlocks large-media scope)
- [ ] Bandwidth controls, ignore patterns, per-folder settings

---

## Open technical questions (owned by engineering, proposals to come)

1. **File identity:** GUID-per-file enables rename tracking, but who mints the GUID
   when two peers create the same path independently?
2. **Clock skew:** newest-wins needs a stance on unsynced clocks (tolerate ±skew,
   or hybrid logical clocks).
3. **Relay fairness:** how a peer advertises "I hold newest" without full state gossip.
