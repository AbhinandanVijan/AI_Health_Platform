FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       tesseract-ocr \
       poppler-utils \
    && rm -rf /var/lib/apt/lists/*

COPY src/OcrWorker/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY src/OcrWorker /app

CMD ["python", "main.py"]
