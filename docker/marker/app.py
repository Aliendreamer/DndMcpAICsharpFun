import os
import uuid
import time
import json
import logging
import threading
from concurrent.futures import ThreadPoolExecutor
from contextlib import asynccontextmanager
from pydantic import BaseModel
from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse
from marker.converters.pdf import PdfConverter
from marker.models import create_model_dict
from marker.config.parser import ConfigParser

logger = logging.getLogger("marker-service")

artifact_dict = None

# Single-worker pool — one conversion at a time
_executor = ThreadPoolExecutor(max_workers=1)
_jobs: dict[str, dict] = {}
_jobs_lock = threading.Lock()


@asynccontextmanager
async def lifespan(app: FastAPI):
    global artifact_dict
    logger.info("Loading Marker models...")
    artifact_dict = create_model_dict()
    logger.info("Marker models loaded.")
    yield
    _executor.shutdown(wait=False)


app = FastAPI(title="Marker JSON Conversion Service (spike)", lifespan=lifespan)


LLM_ENDPOINT = os.getenv("MARKER_LLM_ENDPOINT", "http://ollama:11434/v1")


def _convert(filepath: str, use_llm: bool = False, llm_model: str | None = None,
             page_range: str | None = None) -> dict:
    cfg = {"output_format": "json"}
    if page_range:
        cfg["page_range"] = page_range
    if use_llm:
        cfg["use_llm"] = True
        cfg["llm_service"] = "marker.services.openai.OpenAIService"
        cfg["openai_base_url"] = LLM_ENDPOINT
        cfg["openai_model"] = llm_model or "qwen2.5vl:3b"
        cfg["openai_api_key"] = "ollama"
        cfg["timeout"] = 300        # CPU-offloaded local vision models exceed the 30s default
        cfg["openai_image_format"] = "png"  # safest for Ollama's vision endpoint
    config_parser = ConfigParser(cfg)
    converter = PdfConverter(
        config=config_parser.generate_config_dict(),
        artifact_dict=artifact_dict,
        processor_list=config_parser.get_processors(),
        renderer=config_parser.get_renderer(),
        llm_service=config_parser.get_llm_service() if use_llm else None,
    )
    rendered = converter(filepath)
    # rendered is a JSONOutput pydantic model; serialise to plain dict
    return json.loads(rendered.model_dump_json())


def _run_job(job_id: str, file_path: str, use_llm: bool = False,
             llm_model: str | None = None, page_range: str | None = None):
    try:
        logger.info("Job %s: starting JSON conversion for %s (use_llm=%s model=%s pages=%s)",
                    job_id, file_path, use_llm, llm_model, page_range)
        result = _convert(file_path, use_llm, llm_model, page_range)
        with _jobs_lock:
            _jobs[job_id] = {"state": "done", "result": result}
        logger.info("Job %s: done", job_id)
    except Exception as exc:
        logger.exception("Job %s: conversion failed", job_id)
        with _jobs_lock:
            _jobs[job_id] = {"state": "failed", "error": str(exc)}


@app.get("/health")
def health():
    return {"status": "ok", "models_loaded": artifact_dict is not None}


class ConvertByPathRequest(BaseModel):
    file_path: str
    use_llm: bool = False
    llm_model: str | None = None
    page_range: str | None = None  # marker 0-based page indices, e.g. "23-29,147-149"


@app.post("/convert-by-path", status_code=202)
async def convert_by_path(req: ConvertByPathRequest):
    if artifact_dict is None:
        raise HTTPException(status_code=503, detail="Models not loaded yet")

    if not os.path.isfile(req.file_path):
        raise HTTPException(status_code=404, detail=f"File not found: {req.file_path}")

    job_id = str(uuid.uuid4())
    with _jobs_lock:
        _jobs[job_id] = {"state": "running", "started_at": time.time()}

    _executor.submit(_run_job, job_id, req.file_path, req.use_llm, req.llm_model, req.page_range)
    logger.info("Job %s: queued for %s", job_id, req.file_path)
    return JSONResponse(status_code=202, content={"job_id": job_id})


@app.get("/status/{job_id}")
async def job_status(job_id: str):
    with _jobs_lock:
        job = _jobs.get(job_id)

    if job is None:
        raise HTTPException(status_code=404, detail=f"Unknown job: {job_id}")

    result = {"state": job["state"]}
    if "started_at" in job:
        result["elapsed_seconds"] = int(time.time() - job["started_at"])
    if "error" in job:
        result["error"] = job["error"]
    return result


@app.get("/result/{job_id}")
async def job_result(job_id: str):
    with _jobs_lock:
        job = _jobs.get(job_id)

    if job is None:
        raise HTTPException(status_code=404, detail=f"Unknown job: {job_id}")
    if job["state"] != "done":
        raise HTTPException(status_code=409, detail=f"Job state is {job['state']}")
    return JSONResponse(content=job["result"])
