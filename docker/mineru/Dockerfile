# MinerU PDF structure-parser service (pipeline + OCR backend) for the DndMcpAICsharpFun app.
# Based on the official MinerU global Dockerfile
# (https://github.com/opendatalab/MinerU/blob/master/docker/global/Dockerfile).
# Base is vLLM/CUDA-13.0 — matches the host driver (CUDA 13.2). We use only the `pipeline`
# backend with `-m ocr` (not the VLM backend), so we bake just the pipeline/OCR models.
FROM vllm/vllm-openai:v0.21.0

# libgl for opencv; Noto fonts for glyph coverage during OCR rendering.
RUN apt-get update && \
    apt-get install -y \
        fonts-noto-core \
        fonts-noto-cjk \
        fontconfig \
        libgl1 && \
    fc-cache -fv && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# MinerU itself.
RUN python3 -m pip install -U 'mineru[core]>=3.4.0' --break-system-packages && \
    python3 -m pip cache purge

# Bake the pipeline + OCR models (not the VLM models — unused here).
RUN /bin/bash -c "mineru-models-download -s huggingface -m pipeline"

ENV MINERU_MODEL_SOURCE=local
EXPOSE 8000

# Serve the built-in FastAPI. The wrapper entrypoint sets MINERU_MODEL_SOURCE and execs the command.
ENTRYPOINT ["/bin/bash", "-c", "export MINERU_MODEL_SOURCE=local && exec \"$@\"", "--"]
CMD ["mineru-api", "--host", "0.0.0.0", "--port", "8000"]
