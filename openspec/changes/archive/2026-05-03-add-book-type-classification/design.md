## Context

The corpus today carries two book-identity axes on every Qdrant point: `version` (rules edition — Edition2014 / Edition2024) and `source_book` (exact-match display name). For multi-book collections this is sufficient *only* if every query is willing to enumerate source_book values. A consuming agent that wants "rules-only, no lore" content has no clean way to express that — it would need to know every adventure and setting book by name and exclude each one explicitly.

The `BookType` field collapses that long enumeration into a five-value categorical axis. The taxonomy maps directly to how WotC publishes:

- **Core** — the foundational three (PHB / MM / DMG) of any edition.
- **Supplement** — rules-and-content expansions that don't establish a new edition (Volo's, Xanathar's, Tasha's, MotM, Fizban's, etc.).
- **Adventure** — story modules with stat blocks and locations but no general rules (Curse of Strahd, Tomb of Annihilation, Storm King's Thunder, etc.).
- **Setting** — campaign settings: Eberron, Wildemount, SCAG, Theros, Strixhaven, Spelljammer, etc.
- **Unknown** — explicit default for unset / pre-existing rows; meaningful in queries that want to find "anything that hasn't been classified yet" or to safely include unclassified content.

## Goals / Non-Goals

**Goals:**
- One orthogonal field that lets retrieval distinguish core rules from supplement rules from adventure-specific from setting-specific content.
- Strictly additive: no existing record or Qdrant point is invalidated; default behaviour for unfiltered queries is unchanged.
- Operator-friendly registration: optional, case-insensitive, missing/invalid values do not break the upload.
- Composable with existing `version` and `source_book` filters.

**Non-Goals:**
- Auto-classifying books from PDF content. Tagging is a manual step at registration time.
- Multi-tag support (a book is exactly one type). If a book legitimately defies the taxonomy, the operator picks the closest fit or registers as `Unknown`.
- Backfilling pre-existing `dnd_blocks` points. Operators delete-and-re-ingest if they want pre-existing books retroactively tagged.
- Renaming or repurposing `version` or `source_book`. Those fields keep their meanings.

## Decisions

**1. Five values fixed in code, not configurable.**
Alternatives considered: free-form string tag list. Rejected because (a) the WotC publishing model doesn't generate new top-level book types frequently, (b) a strict enum gives the consuming MCP agent a finite set to reason over, (c) free-form would re-introduce the same case/typo problems we just sidestepped on `version`.

**2. Permissive registration.**
Invalid or missing values default to `BookType.Unknown` instead of returning HTTP 400. Rationale: the operator already has to think about `sourceName`, `displayName`, `version`, and now this. Failing on a typo of an *optional* enrichment field is a poor trade. The cost of a wrong tag is much smaller than the cost of a failed upload.

**3. EF migration adds a nullable column; C# default supplies `Unknown`.**
A nullable SQL column means EF can map old rows with no value to whatever default we choose. The C# property's `= BookType.Unknown` default takes effect on insert and on read-of-null. Cleanest minimum-friction migration.

**4. Pre-existing Qdrant points are not migrated.**
They simply lack the `book_type` payload field. `QdrantPayloadMapper.ToChunkMetadata` returns `BookType.Unknown` when the field is absent. A query for `?bookType=Core` excludes them, which is correct — they were tagged before classification existed and we don't know what they are. To classify them, the operator deletes and re-ingests the book.

**5. Orthogonal to category.**
`category` describes *what kind of content the chunk is* (Spell, Monster, Class…). `bookType` describes *what kind of book it came from* (Core, Supplement, Adventure…). Keeping them orthogonal means both filters compose cleanly: `?category=Monster&bookType=Adventure` finds adventure-specific monster blocks (e.g., Strahd's specific minions) without polluting with MM stat blocks.

## Risks / Trade-offs

- **[Risk]** Operators don't tag new books and everything ends up `Unknown` again. → **Mitigation:** the `.http` example demonstrates the field on every register block, and `GET /admin/books` will show `bookType` next to `displayName`, so unfiltered books are immediately visible to whoever runs the registration.
- **[Trade-off]** The 5-value taxonomy will fit ~95% of WotC's catalog cleanly but some books are weird (e.g., Mordenkainen Presents: Monsters of the Multiverse is a Supplement that wholly republishes earlier Supplements). The operator picks the dominant fit — there's no "Hybrid" value. → Acceptable: minor classification fuzziness is fine; the agent isn't relying on perfect categorization, and `source_book` still gives an exact-match escape hatch.
- **[Risk]** Adding a sixth value later (e.g., `Reference` for compendiums of rules clarifications) requires a code change rather than a config change. → Acceptable: bookType evolves on a years-long cadence, far slower than the rest of the system.
