## Why

The Marker parser misses the structures the companion needs most: on the PHB it surfaced only 4/12
class names and 1/9 race names as headings, and the structured layer was recall-limited. The MinerU
spike (2026-06-29, four-way validated) proved a clear winner — **MinerU + `-m ocr` + a spell-chapter
splitter** beats Marker on every axis:

| | Marker | MinerU+ocr+splitter |
|---|---|---|
| classes /12 | 4 | **12** |
| races /9 | 1 | **7** |
| PHB spells /361 | 265 | **323** |
| total entities | 382 | **443** (zero noise) |

The spike runs the `mineru` CLI by hand and the converter reads a pre-produced file. This change makes
MinerU the **production main parser** by running it automatically.

## What Changes

- **Add a MinerU conversion service** to the stack (MinerU's FastAPI `mineru-api`, GPU, `-b pipeline
  -m ocr`), reachable on the shared network like the Marker service.
- **Rewire `MinerUPdfConverter`** to submit the PDF to that service and consume its `content_list`
  output, instead of reading a pre-produced file from a mounted directory.
- **Make MinerU the default** `IPdfStructureConverter` (`MinerU:Enabled` defaults on); **Marker is
  retained as a config-selectable fallback**, not removed.
- **Keep the committed spell-chapter splitter** (`Casting Time:`-anchored spell-name recovery) — it is
  parser-output post-processing and stays in `MinerUPdfConverter`.
- **Cache conversions on disk** (a MinerU equivalent of `PdfConversionDiskCache`) — `-m ocr` is a
  ~60–90 min/book GPU step, so re-runs must not re-convert.
- **`-m ocr` is mandatory** (not `txt`/`auto`): the core PDFs have a corrupt embedded text layer that
  `txt` extracts as garbage; OCR re-reads the rendered pixels and yields clean text while keeping the
  layout headings.

## Capabilities

### New Capabilities
- `mineru-pdf-conversion`: the MinerU-based PDF structure converter — the MinerU service (pipeline +
  ocr backend), the HTTP converter, the spell-chapter splitter, conversion caching, and MinerU as the
  default `IPdfStructureConverter`.

### Modified Capabilities
- (none — `marker-pdf-conversion` is unchanged and retained as the fallback converter, selected when
  `MinerU:Enabled` is false.)

## Impact

- Code: new MinerU service client / HTTP path in `MinerUPdfConverter` (replaces the file-read spike
  path), a MinerU conversion disk cache, DI default flip in `ServiceCollectionExtensions`, new
  `MinerUOptions` fields (service URL, backend, method, timeout).
- Infra: a MinerU service in `docker-compose.yml` (GPU). The spike's gitignored
  `docker-compose.override.yml` + `.mineru-spike/` are retired.
- Cost: ~60–90 min/book one-time OCR conversion on the 8 GB GPU; conversion and qwen3 extraction are
  sequential so contention is bounded. Re-converting the corpus (MM, DMG, …) is a one-time operation.
- No DB/API-contract change. Validation: re-convert + re-extract PHB through the service and confirm
  the spike numbers (12/12 classes, 7/9 races, ~323 spells, no noise).
