> **Interim shipped (2026-07-05, commit `803da7b`):** qwen3 `/no_think` is appended to the extraction
> user turn in `OllamaEntityExtractionClient` as a fast, hardcoded think-suppression. Validated live on
> the DMG re-extracts — ~8× faster (~114 s → ~14–42 s/candidate), the thinking-runaway "empty response"
> losses eliminated (7 → 0), and no classification regression (siege weapons still extract correctly).
> This effectively pre-answers the A/B (tasks 4.1–4.3): `think:false` wins for structured extraction,
> because type selection is already deterministic (`DeterministicTypeResolver`) + union-constrained.
> **This spec's REMAINING scope** is to REPLACE the hardcoded `/no_think` with the configurable `think`
> option below (tasks 1–3), so it is toggleable + the `<think>` strip is defensive, rather than a
> permanent prompt directive. Task 4 is now a confirmation/cleanup, not a discovery.

## 1. Configurable think option

- [ ] 1.1 Add `bool? Think` to `EntityExtractionOptions` (default null = model default)
- [ ] 1.2 Add `bool? Think` to `ExtractionRequest`; populate it in `CandidateExtractor` from `EntityExtractionOptions`
- [ ] 1.3 In `OllamaEntityExtractionClient`, set the Ollama chat request `think` field only when the option is non-null; confirm the request model serializes `think`
- [ ] 1.4 (test) With `Think = null` the request omits `think`; with `Think = false` the request sends `think:false`

## 2. Defensive think stripping

- [ ] 2.1 (test) A response containing a `<think>…</think>` block before the content parses to the content only (block removed)
- [ ] 2.2 Implement a `<think>…</think>` strip pass in the client before JSON/tool parsing (match well-formed spans only)

## 3. Per-candidate A/B observability

- [ ] 3.1 Emit a structured per-candidate metric line `{thinkMode, name, type, wallMs, outcome}` (outcome ∈ ok|empty|declined) in the runner/orchestrator
- [ ] 3.2 (test) The metric line is emitted with the correct outcome for an ok, an empty, and a declined candidate

## 4. A/B run + decision

- [ ] 4.1 Choose the sample (full DMG book 3, or a curated subset that includes siege-weapon stat blocks) and run the extraction twice: `Think=null` then `Think=false`
- [ ] 4.2 Compute the comparison from the metric lines: total wall time, empty-response count, and `(name → type)` classification diff (esp. siege weapons → `Object`; known entities → correct type)
- [ ] 4.3 Record the result against the acceptance criteria (design.md) and recommend: adopt `think:false`, keep thinking on, or pursue the no_think-on-retry fallback

## 5. Validation

- [ ] 5.1 `dotnet build` 0/0 (warnings-as-errors); full non-persistence test suite green
- [ ] 5.2 No endpoint/schema change — `.http`/`.insomnia` unchanged; note the new config option where extraction config is documented
