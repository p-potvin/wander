# Wander — Local Agent Ledger

Stand-in for the shared `agent-ledger` (unreachable from this machine — lives under
`C:\Users\Administrator\...`). Entries follow the same shape as
`record-agent-change.ps1`. Migrate these into the real ledger when it's available.

---

## 2026-07-15 — claude — plan

**Summary:** Scanned the wander repo end-to-end (P2P file sync over Tailscale: Wander.Core
sync engine + SQLite state, Wander.Network gRPC service with mock handlers, Wander.UI WPF
placeholder shell, Wander.WindowsAPI watcher/registry helpers). Ran a socratic roadmap
interview with the user. Signed-off decisions: team-first (3–4 person tailnet teams),
fully Tailscale-native (discovery + WhoIs identity), opportunistic relay, lenient
newest-wins conflicts with attributed conflict copies + soft presence hints, 30-day delete
trash, Avalonia GUI mandatory in v1, repo-scale data for v1, A.N.S.W.E.R.S. retention
powers Phase 2 version history. Wrote ROADMAP.md (source of truth).

**Files:** ROADMAP.md

## 2026-07-15 — claude — code-change

**Summary:** Phase 0 kickoff. Removed committed build output (`dist/`) from git tracking
and extended .gitignore (dist/, SQLite artifacts). Created this local ledger.

**Commands:** `git rm -r --cached dist`

**Files:** .gitignore, CHANGES.md

## 2026-07-16 — claude — code-change

**Summary:** Phase 0 complete. Consolidated the gRPC protocol into Wander.Core (single
proto, client+server codegen — killed 155 duplicate-type warnings), added ListFiles
manifest RPC + protocol_version. Built the real engine: FolderScanner (GUID minting,
tombstones), LocalIndexer (debounced watcher reconciliation, rename-keeps-GUID),
TrashService (30-day retention), rewrote SyncEngine with the signed-off conflict policy
(newest-wins, attributed conflict copies, hash-verified atomic downloads, rename-as-move),
SyncOrchestrator (pull rounds). Network: real gRPC handlers, TailscaleService
(status/whois via CLI), TailscaleAuthInterceptor (tailnet identity = auth), SyncDaemon
hosted service, DI'd Program.cs with config (WanderOptions). Folded root themeManager.cs
into Wander.UI/Theming and wired the theme toggle to real VaultWares palettes. Fixed
vulnerable SQLitePCLRaw transitive dep (GHSA-2m69-gcr7-jv3q). Added Wander.Tests (31
tests incl. two-node loopback integration, all passing) and GitHub Actions CI.

**Commands:** `dotnet build Wander.slnx` (0 errors, 0 warnings), `dotnet test` (31/31)

**Files:** Wander.Core/Protos/sync.proto, Wander.Core/Services/{SyncEngine,FolderScanner,LocalIndexer,TrashService,SyncOrchestrator}.cs, Wander.Core/Utils/{PathUtils,ConflictNaming}.cs, Wander.Network/Services/{SyncGrpcService,TailscaleService,TailscaleAuthInterceptor,SyncDaemon}.cs, Wander.Network/{Program.cs,WanderOptions.cs,appsettings.json}, Wander.UI/Theming/{ThemeManager,WpfThemeApplier}.cs, Wander.Tests/*, .github/workflows/ci.yml, ROADMAP.md
