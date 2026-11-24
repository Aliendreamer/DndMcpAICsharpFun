## Context

`OverrideMerger.Apply` (fresh extraction) and `Reapply` (apply-overrides on the current canonical file) apply each
entity's overrides via `ApplyOne`. A `canonicalText` override replaces the whole text. The text it removed is not
tracked anywhere afterwards. `FieldOverride.Extracted` records the value the override replaced.

## Goals / Non-Goals

**Goals:** never lose a printed line through an override; keep the rescued text reviewable and stable across re-runs.

**Non-Goals:** extractor stop rules (stat blocks absorbing lore — a later change if the review shows a pattern);
rescuing text removed by `fields.*` overrides (`canonicalText` carries the printed text); UI changes.

## Decisions

- **Base text.** The text before the override: in `Apply`, the extracted entity's own `canonicalText`; in `Reapply`,
  the override's recorded `Extracted` (when it is a string). The kept text is the entity's final `canonicalText`.
- **Removed line.** A line of the base text whose reduced form (lower-case `[a-z0-9]`) has 30+ characters and is not
  contained in the reduced final text of ANY entity of the merged book (the host included, Declined excluded,
  other rescue entities excluded), and that is not merely corrected: when one line of the host's final text holds at least 70% of its words
  of 3+ characters, the override only fixed it (OCR typos) and it is not rescued. Short lines (headings, labels) are never rescued on their own; a heading line
  directly above a rescued line is carried along as its first line.
- **Rescue entity.** One per host with removed lines. Id: the host id with its type segment replaced by `lore` and
  `-trimmed` appended (`mm14.monster.adult-blue-dracolich` → `mm14.lore.adult-blue-dracolich-trimmed`). Type Lore,
  DataSource Programmatic, provenance copied from the host, `fields.entries` = the rescued paragraphs (consecutive
  lines joined, de-hyphenated like the extractor), `canonicalText` = name + "\n" + entries. Name: the carried heading
  line when there is one, else "<host name> (trimmed text)". One failed check `text-complete` with reason
  "text a review override removed from <host id>; no other entity holds it". So it is NeedsReview until approved.
- **Its own overrides.** The rescue entity then goes through the same per-entity override/approval/decline handling
  as any entity, so a review of it (fix, approve, decline) persists. Overrides/approvals/declines on a rescue id are
  not orphans while the rescue exists.
- **Rebuilt every time.** On `Reapply`, rescue entities present in the input are dropped and recomputed, so a rescue
  disappears when its text becomes held (e.g. the extractor improves) and never duplicates.
- **Id clash.** If an extracted entity already has the rescue id, append `-2` (the existing unique-id convention).

## Risks / Trade-offs

- ~900 new flagged entities → reviewed with the AI loop; junk (credits, labels) gets declined, which persists.
- A rescue may hold text the reviewer meant to drop as junk → the decline of the rescue entity records that choice.

## Known behaviours

- **Short continuation lines.** A short line (reduced length 1 to 29) directly next to a removed line, repeatedly, is rescued with it, so a paragraph tail ("becomes undead.") is kept and the paragraph stays one entry. Headings and short lines the host still carries are not pulled in; an isolated short line is never rescued.
- **Retired overrides.** On a fresh extraction the base text for a host is its new extracted text plus the lines of the override's recorded `extracted` text that it lacks, so an override the extractor now reproduces does not erase the text it once trimmed. After drift, `Reapply` uses the extractor text held in the drift check.
- **Duplicates.** A line already rescued for an earlier host (host order) is not rescued again for a later one.
- **Migration.** When a host id moves, its rescue id's overrides, approvals and declines move with it.
- **M3.** The `/review` page does not recompute rescues; they change only on the next apply or re-extraction.
- **M4.** An approval waives `text-complete` by name, like any other waivable check.
- **M5.** A rescue that is a one-sentence lead-in is auto-Declined by the `LeadInLore` rule; a Declined host gets no rescue.
