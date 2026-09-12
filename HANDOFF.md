# Handoff — NekoProxyCore

Updated: 2026-09-12

## Current canonical state
- Canonical root: `E:\Github\NekoProxyCore`
- Canonical branch: `main`
- Canonical/main SHA at cleanup: `77b849f660a6ce923715eb5254594e4d0ee99ec3`

## Accepted released Core authority
For the currently accepted v5.1.0 product, use:
- `E:\Github\worktrees\NekoProxyCore-release-5.1-stable`
- branch `fix/stable-5.1-core-start-cancel-race-tdd4`
- SHA `8705c7be2399c8624ae430f0c78543bddc6b953c`

This distinction matters: canonical `main` is the navigation/development branch, while the accepted release authority is the pinned stable worktree above.

## Role
Core is the low-level network/driver/runtime boundary. It must remain fail-closed and is controlled locally through the authorized Launcher start path.

## Sensitive boundary
Do not expose runtime secrets, trust material or proxy configuration in logs/docs. Do not treat the Admin web as a direct Core controller.

## Next cross-project work
The current Launcher v5.1.0 public release is installer-only, so automatic update payload/Core authority retrieval needs a separate immutable signed backend/storage design. Any Core distribution change must preserve exact hash/size/installed-identity verification and immutable provenance.

Detailed next-work handoff:
`E:\Github\Project manager\current\NEXT_WORK_AUTO_UPDATE_BACKEND_HANDOFF.md`

## First checks for a new maintainer
1. verify `git status --short --branch` in canonical root
2. verify the stable worktree SHA before investigating released v5.1.0 behavior
3. run managed + Windows/native tests appropriate to the change
4. do not change accepted release authority just to make branch layout look simpler
