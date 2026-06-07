FROM python:3.11-slim

RUN apt-get update && apt-get install -y \
    libgl1 \
    libglib2.0-0 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Install torch first so marker-pdf's resolver doesn't replace it
ARG TORCH_INDEX=https://download.pytorch.org/whl/nightly/cu128
RUN pip install --no-cache-dir torch torchvision --index-url ${TORCH_INDEX}

RUN pip install --no-cache-dir \
    "marker-pdf>=1.6.0" \
    "fastapi>=0.111.0" \
    "uvicorn>=0.29.0" \
    python-multipart

COPY app.py .

EXPOSE 5002
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "5002"]
