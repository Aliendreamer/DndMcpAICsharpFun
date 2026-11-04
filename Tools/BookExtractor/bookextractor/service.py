"""HTTP service: one extraction job at a time, state in memory (the OCR cache makes a re-submit cheap)."""
from __future__ import annotations

import os
import threading
import uuid
from pathlib import Path

from fastapi import FastAPI, HTTPException, Query, Response
from pydantic import BaseModel

from .assemble import ALL_TYPES, extract_book
from .reader import BookReader


class JobRequest(BaseModel):
    pdfPath: str
    bookSlug: str
    types: list[str] | None = None


class _Job:
    def __init__(self) -> None:
        self.status = "queued"
        self.pages_done = 0
        self.pages_total = 0
        self.error: str | None = None
        self.result: dict | None = None


def create_app(books_dir: Path | None = None, cache_dir: Path | None = None,
               threads: int | None = None, processes: int | None = None) -> FastAPI:
    books = Path(books_dir or os.environ.get("BOOKS_DIR", "/books")).resolve()
    cache = Path(cache_dir or os.environ.get("CACHE_DIR", "/cache"))
    n_threads = threads or int(os.environ.get("OCR_THREADS", "4"))
    n_procs = processes or int(os.environ.get("OCR_PROCESSES", "2"))
    app = FastAPI(title="BookExtractor")
    jobs: dict[str, _Job] = {}
    lock = threading.Lock()

    def resolve_pdf(rel: str) -> Path:
        path = (books / rel).resolve()
        if not path.is_relative_to(books) or not path.is_file():
            raise HTTPException(400, "pdfPath must be an existing file under the books directory")
        return path

    def run(job: _Job, pdf: Path, slug: str, types: set[str]) -> None:
        def progress(done: int, total: int) -> None:
            job.pages_done, job.pages_total = done, total

        try:
            job.status = "running"
            pages = BookReader(pdf, cache, threads=n_threads, processes=n_procs).read(progress)
            res = extract_book(pages, slug, types)
            job.result = {"entities": res.entities, "report": res.report}
            job.status = "done"
        except Exception as e:  # noqa: BLE001 - surfaced to the caller as the job error
            job.error = f"{type(e).__name__}: {e}"
            job.status = "failed"

    @app.get("/health")
    def health() -> dict:
        return {"status": "ok"}

    @app.post("/jobs", status_code=202)
    def submit(req: JobRequest) -> dict:
        pdf = resolve_pdf(req.pdfPath)
        types = set(req.types) if req.types else set(ALL_TYPES)
        if types - set(ALL_TYPES):
            raise HTTPException(400, f"unknown types: {sorted(types - set(ALL_TYPES))}")
        with lock:
            if any(j.status in ("queued", "running") for j in jobs.values()):
                raise HTTPException(409, "a job is already running")
            job_id = uuid.uuid4().hex
            job = jobs[job_id] = _Job()
        threading.Thread(target=run, args=(job, pdf, req.bookSlug, types), daemon=True).start()
        return {"jobId": job_id}

    def get_job(job_id: str) -> _Job:
        job = jobs.get(job_id)
        if job is None:
            raise HTTPException(404, "unknown job")
        return job

    @app.get("/jobs/{job_id}")
    def status(job_id: str) -> dict:
        j = get_job(job_id)
        body = {"status": j.status, "pagesDone": j.pages_done, "pagesTotal": j.pages_total}
        if j.error:
            body["error"] = j.error
        return body

    @app.get("/jobs/{job_id}/result")
    def result(job_id: str) -> dict:
        j = get_job(job_id)
        if j.status != "done" or j.result is None:
            raise HTTPException(404, "result not ready")
        return j.result

    @app.get("/page-image")
    def page_image(pdfPath: str = Query(...), page: int = Query(..., ge=1)) -> Response:
        pdf = resolve_pdf(pdfPath)
        try:
            data = BookReader(pdf, cache).render_png(page)
        except (IndexError, ValueError):
            raise HTTPException(400, "page out of range") from None
        return Response(content=data, media_type="image/png")

    return app


# Run with: uvicorn --factory bookextractor.service:create_app --host 0.0.0.0 --port 8000
