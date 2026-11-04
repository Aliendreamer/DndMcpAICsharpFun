# syntax=docker/dockerfile:1
# Deterministic book extractor service (no LLM, no GPU: CPU onnxruntime only).
FROM python:3.12-slim

# curl: compose healthcheck; libgomp1: onnxruntime; libgl1/libglib2.0-0/libxcb1: opencv (rapidocr dependency).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgomp1 libgl1 libglib2.0-0 libxcb1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY pyproject.toml ./
COPY bookextractor ./bookextractor
RUN pip install --no-cache-dir .

# Bake the RapidOCR models into the image (needs www.modelscope.cn at build time) so a running
# container never downloads them. Same engine construction the reader uses.
RUN python -c "from bookextractor.reader import default_engine_factory; default_engine_factory()"

RUN useradd --create-home --uid 1000 extractor \
    && mkdir -p /cache && chown extractor /cache
USER extractor

ENV BOOKS_DIR=/books CACHE_DIR=/cache OCR_THREADS=4 OCR_PROCESSES=2
EXPOSE 8000
HEALTHCHECK --interval=15s --timeout=5s --retries=5 --start-period=20s \
    CMD curl -fsS http://localhost:8000/health || exit 1
CMD ["uvicorn", "--factory", "bookextractor.service:create_app", "--host", "0.0.0.0", "--port", "8000"]
