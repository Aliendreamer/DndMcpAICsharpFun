# BookExtractor

Deterministic Python extractor that turns D&D book PDFs (OCR or text layer) into canonical JSON.
No LLM and no GPU: CPU onnxruntime only.

## Setup

```bash
cd Tools/BookExtractor
python3 -m venv .venv
.venv/bin/pip install -e ".[dev]"
.venv/bin/pytest -q
```
