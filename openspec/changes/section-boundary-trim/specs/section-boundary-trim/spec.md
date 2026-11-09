## ADDED Requirements

### Requirement: Heading cut ends a section at a sibling heading
The extractor SHALL end a world or rule section at the first later line that is a heading at least 0.9 times the height of the section's own heading line and at least 1.15 times the median body line height, in the same column or on a later page, but only when that line name-matches (normalized similarity ≥ 0.85) the title of another section or candidate produced by the same parse, excluding the section's own title. The section's own heading MUST be found exactly once on the section's first page; otherwise no heading cut SHALL be made. A line the extractor already treats as a sub-heading of the section SHALL NOT be a cut point. A cut that would keep fewer than 50% of the section's tokens SHALL NOT be applied. Extraction never approves an entity.

#### Scenario: Swallowed sibling section is cut
- **WHEN** the section "Sorcerer" runs on into the line "Warlock", a same-size heading that is another section's title in the same parse
- **THEN** the section text ends before "Warlock"

#### Scenario: In-section heading that is not a sibling is kept
- **WHEN** a section contains a same-size bold heading such as "They've Gone Berserk!" that is no other section's title
- **THEN** no heading cut is made at that line

#### Scenario: Continuation-page heading is not taken as the anchor
- **WHEN** the section's own heading is not found on its first page but a same-named heading appears on a later page
- **THEN** no heading cut is made and the section text is unchanged

#### Scenario: Smaller sub-heading is kept
- **WHEN** a section contains a sub-heading smaller than 0.9 times its own heading
- **THEN** the sub-heading and its text stay in the section

### Requirement: Shaded block with its own heading is excluded when page pixels are available
When page pixels are available (the trim-pass dev tool renders them; extraction does not), the boundary rules SHALL exclude from a section any shaded block (background darker than the page paper) that opens with its own heading line. A shaded block that follows a read-aloud cue (a line ending in `read:` or `reads:`) and has no heading SHALL be kept. During extraction shade is 0 and this rule does not fire.

#### Scenario: Sidebar is dropped
- **WHEN** a shaded box headed "Dragonmarks" sits inside the span of the section "Warforged Names" and shade is measured
- **THEN** the box's lines are not part of the section's lines

#### Scenario: Read-aloud box is kept
- **WHEN** a shaded box without a heading follows a line ending in "read:"
- **THEN** the box's lines stay in the section

### Requirement: Map labels are excluded
The extractor SHALL exclude map-label lines (`MAP <n>`, `1 square = <n> feet`, `Side View`) from section text, but only label-like lines: at most 6 words and fewer than 3 lowercase words of 3 or more letters. Prose such as "Map 4.1 depicts a region of Annam's Cradle with" SHALL be kept.

#### Scenario: Map scale line dropped
- **WHEN** a section's lines include "1 square = 5 feet"
- **THEN** that line is not in the section text

#### Scenario: Map prose kept
- **WHEN** a section's lines include "Map 4.1 depicts a region of Annam's Cradle with"
- **THEN** that line stays in the section text

### Requirement: One-off trim pass is dry-run only
A dev tool (`bookextractor.boundary_fix`) SHALL apply the boundary rules to NeedsReview world and rule entities of already-extracted books from the cached OCR and report per-book counts of candidates, trims, would-be approvals and skips (`no-change`, `unverified-cut`, `large-cut`, `ambiguous-removal`, `render-mismatch`, `missing-ocr-page`, `already-overridden`, `approved`). It MUST skip the System Reference Document and dragonborn-slice. Because its removal step can delete words from inside sentences, the tool SHALL refuse a run without `--dry-run` and write nothing.

#### Scenario: Dry run writes nothing
- **WHEN** the pass runs with `--dry-run`
- **THEN** no overrides file changes and a per-book report is produced

#### Scenario: Real run refused
- **WHEN** the pass runs without `--dry-run`
- **THEN** it exits with code 2 and no file changes

### Requirement: Approval gate for trims
The boundary rules SHALL treat a trim as approvable (reason `boundary trim`, waiving only `text-complete`, `neighbour-absorbed` and `entry-level`) only when no other check fails, the kept text holds at least 50% of the original tokens, the cut was identified by a boundary rule, and the first 8 tokens of the entries (or all, if fewer) are unchanged.

#### Scenario: Small trim approvable
- **WHEN** a trim keeps 80% of the tokens, starts at a sibling heading, and only `text-complete` fails
- **THEN** the trim is approvable

#### Scenario: Other failing check blocks approval
- **WHEN** a trim is otherwise approvable but `table-cut` fails
- **THEN** the trim is not approvable

### Requirement: Regression set gates the rules
The extractor tests SHALL include the page-checked sections (FIXED rows with corrected text, and MATCH rows) as a regression fixture. No MATCH section MAY fall below token-F1 0.95 against its original text, and no approvable section MAY score lower against the page-checked text than before the trim.

#### Scenario: Regression run passes
- **WHEN** the test suite runs the boundary rules over the regression fixture
- **THEN** every MATCH section keeps token-F1 at or above 0.95 and no approvable FIXED section gets worse
