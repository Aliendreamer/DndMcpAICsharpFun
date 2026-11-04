# BookExtractor

Deterministic Python extractor that turns D&D book PDFs (OCR or text layer) into canonical JSON.
No LLM and no GPU: CPU onnxruntime only.

## Setup

```bash
cd Tools/BookExtractor
uv venv .venv
uv pip install --python .venv/bin/python -e ".[dev]"
.venv/bin/pytest -q
```

`python3 -m venv` may fail where `ensurepip` is missing, so `uv` is the supported setup.
