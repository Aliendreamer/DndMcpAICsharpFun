## Why

The audit's SEC-08 finding (an Important IDOR) cannot be closed by a small edit, so it is split out of
`security-vps-hardening` into its own change. `resolve_character_feature` — and any character-scoped
MCP tool — accepts an arbitrary `heroSnapshotId` with no ownership check. All MCP tools authenticate
only with the shared `Mcp:ApiKey` and carry no per-user identity; the chat client reaches the MCP
server over a **loopback HTTP call**, so the signed-in user's cookie identity is lost at the MCP
boundary. A user (via a crafted chat prompt) or any holder of the MCP key can therefore read rule
facts derived from **other users'** hero snapshots by iterating snapshot ids — a cross-tenant
data-access flaw.

Closes audit finding: **SEC-08**.

## What Changes

Two candidate approaches (decision pending — see `design.md`):

- **(a) Thread identity through the loopback.** Pass the authenticated user id from the chat session
  into the MCP call, and have `CharacterResolutionService` verify the snapshot →
  hero → campaign → user ownership chain before resolving. The tool refuses snapshots the caller
  does not own.
- **(b) Move character-scoped resolution off the shared-key MCP surface.** Register an in-process AI
  tool inside the authenticated Blazor chat pipeline that closes over the signed-in user id and calls
  `CharacterResolutionService` directly (with the ownership check), and drop
  `resolve_character_feature` from the shared-key MCP tool set.

Either way, the resolution path SHALL enforce ownership; the difference is where identity enters.

## Capabilities

### Modified Capabilities

- `security-hardening`: adds the requirement that character-scoped resolution authorizes by the
  calling user's identity.

## Impact

- Modified: `Features/Mcp/DndMcpTools.cs`, `Features/Resolution/CharacterResolutionService.cs`
  (ownership verification), `Features/Campaigns/HeroRepository.cs` (an ownership-scoped snapshot
  lookup), and — depending on the chosen approach — `Features/Chat/*` (in-process tool wiring) or the
  MCP loopback client to carry identity.
- No data-model change beyond possibly a scoped query.

## Non-goals

- A general per-user identity system for all MCP tools (only character-scoped tools need it now).
- Reworking the MCP transport beyond what the chosen approach requires.
