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

- [ ] Bidirectional 2-peer sync of a real working folder
- [ ] Conflict copies with peer attribution + notifications
- [ ] Avalonia dashboard: folder status, peer list, live activity feed, conflict browser
- [ ] Tray icon: at-a-glance status, pause, recent activity
- [ ] Installer + update channel (e.g. Velopack)
- [ ] **Exit criterion: the maintainer daily-drives Wander for their own working files**

## Phase 2 — The team release

- [ ] 3–4 peer mesh with opportunistic relay
- [ ] Soft presence hints in dashboard/tray
- [ ] Folder membership & invites built on tailnet identities
- [ ] **Wander back in time**: browsable per-file version history, A.N.S.W.E.R.S. retention engine
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
