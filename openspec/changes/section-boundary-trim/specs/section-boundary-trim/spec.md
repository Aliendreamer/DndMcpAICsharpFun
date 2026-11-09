## ADDED Requirements

### Requirement: Heading cut ends a section at the next same-or-larger heading
The extractor SHALL end a world or rule section at the first later line that is a heading at least 0.9 times the height of the section's own heading line and at least 1.15 times the median body line height, in the same column or on a later page. The section's own heading MUST be found on the section's first page; when it is not, no heading cut SHALL be made. A line the extractor already treats as a sub-heading of the section SHALL NOT be a cut point.

#### Scenario: Swallowed sibling section is cut
- **WHEN** the section "Sorcerer" runs on into the lines of the sibling headings "Warlock" and "Wizard" of the same size
- **THEN** the section text ends before "Warlock"

#### Scenario: Continuation-page heading is not taken as the anchor
- **WHEN** the section's own heading is not found on its first page but a same-named heading appears on a later page
- **THEN** no heading cut is made and the section text is unchanged

#### Scenario: Smaller sub-heading is kept
- **WHEN** a section contains a sub-heading smaller than 0.9 times its own heading
- **THEN** the sub-heading and its text stay in the section

### Requirement: Shaded block with its own heading is excluded
The extractor SHALL exclude from a section any shaded block (background measured from rendered page pixels, darker than the page paper) that opens with its own heading line. A shaded block that follows a read-aloud cue and has no heading SHALL be kept.

#### Scenario: Sidebar is dropped
- **WHEN** a shaded box headed "Dragonmarks" sits inside the span of the section "Warforged Names"
- **THEN** the box's lines are not part of the section text

#### Scenario: Read-aloud box is kept
- **WHEN** a shaded box without a heading follows a line ending in "read:"
- **THEN** the box's lines stay in the section

### Requirement: Map labels are excluded
The extractor SHALL exclude lines that are map labels (`MAP <n>`, `1 square = 5 feet`, `Side View`) from section text.

#### Scenario: Map scale line dropped
- **WHEN** a section's lines include "1 square = 5 feet"
- **THEN** that line is not in the section text

### Requirement: One-off trim pass over flagged entities
A dev tool SHALL apply the boundary rules to NeedsReview entities of already-extracted books using the cached OCR, without re-extracting the book. It MUST skip the System Reference Document and dragonborn-slice, approved entities, and entities that already carry an override on the trimmed field. For each changed entity it SHALL write an override (`entityId`, `field`, `value`, `extracted`) to `books/canonical/<book>.overrides.json`. It SHALL support a dry run that writes nothing and reports per-book counts.

#### Scenario: Flagged entity gets a trim override
- **WHEN** the pass runs on a NeedsReview Lore entity whose text swallowed a sibling section
- **THEN** an override with the trimmed text as `value` and the original as `extracted` is written for that entity

#### Scenario: Approved and skipped books untouched
- **WHEN** the pass runs over all books
- **THEN** no override is written for an approved entity or for any SRD or dragonborn-slice entity

#### Scenario: Dry run writes nothing
- **WHEN** the pass runs with `--dry-run`
- **THEN** no overrides file changes and a per-book report of trimmed, approved and still-flagged counts is produced

### Requirement: Auto-approve only safe trims
The trim pass SHALL approve an entity (approval reason `boundary trim`, waiving only `text-complete`, `neighbour-absorbed` and `entry-level`) only when no other check still fails, the kept text holds at least 50% of the original tokens, the cut starts at a line identified by a boundary rule, and the kept text is non-empty and begins with the entity's own heading or name. Otherwise the override SHALL be written and the entity SHALL remain NeedsReview.

#### Scenario: Small trim approved
- **WHEN** a trim keeps 80% of the tokens and starts at a sibling heading
- **THEN** the override is written and the entity is approved with reason `boundary trim`

#### Scenario: Large cut left for review
- **WHEN** a trim would keep 20% of the tokens
- **THEN** the override is written and the entity stays NeedsReview

### Requirement: Regression set gates the rules
The extractor tests SHALL include the 258 page-checked sections as a regression fixture. No MATCH section MAY fall below token-F1 0.95 against its original text, and no section the pass would auto-approve MAY score lower against the page-checked text than before the trim.

#### Scenario: Regression run passes
- **WHEN** the test suite runs the boundary rules over the regression fixture
- **THEN** every MATCH section keeps token-F1 at or above 0.95 and no auto-approvable FIXED section gets worse
