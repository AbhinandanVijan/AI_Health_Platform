from __future__ import annotations

import io
import re
from decimal import Decimal, InvalidOperation
from typing import Iterable

import pytesseract
from pdf2image import convert_from_bytes
from PIL import Image
from pypdf import PdfReader

from app.normalization import NormalizedReading, is_known_biomarker_code, normalize_reading


_MEASUREMENT_PATTERN = re.compile(
    r"(?P<name>[A-Za-z][A-Za-z0-9\s\-_/()%]+?)\s{1,}(?P<value>-?\d+(?:\.\d+)?)\s*(?P<unit>%|[A-Za-z0-9\^/\.]+)",
    re.IGNORECASE,
)

_TABLE_ROW_PATTERN = re.compile(
    r"^(?P<name>[A-Za-z#%()\-\s]+?)\s+(?P<value>-?\d+(?:\.\d+)?)\s+"
    r"(?:(?:\d+(?:\.\d+)?\s*[-–]\s*\d+(?:\.\d+)?)|(?:up\s*to\s*\d+(?:\.\d+)?)|(?:upto\s*\d+(?:\.\d+)?))"
    r"\s+(?P<unit>%|[A-Za-z0-9\^/\.]+)$",
    re.IGNORECASE,
)

_NUMERIC_VALUE_PATTERN = re.compile(r"^-?\d+(?:\.\d+)?$")
_UNIT_ONLY_PATTERN = re.compile(r"^[A-Za-zµμu×x0-9\^²³⁶/%.]+$")

_NON_MEASUREMENT_LABELS = {
    "test",
    "result",
    "reference range",
    "units",
    "patient name",
    "patient id",
    "date of birth",
    "age",
    "gender",
    "ordering physician",
    "category",
    "flag",
    "report date",
    "physician",
    "specimen type",
}

_CATEGORY_HINTS = {
    "hematology",
    "metabolic",
    "lipid",
    "diabetes",
    "thyroid",
    "inflammation",
    "vitamins",
}


def _normalize_ocr_artifacts(line: str) -> str:
    text = line
    text = text.replace("µ", "u").replace("μ", "u")

    replacements = {
        "¾": "^3",
        "³": "^3",
        "¼": "^4",
        "⁴": "^4",
        "⁶": "^6",
    }
    for source, target in replacements.items():
        text = text.replace(source, target)

    text = re.sub(r"x\s*10\s*\^\s*(3|4|6)\s*l\b", r"x10^\1/L", text, flags=re.IGNORECASE)
    text = re.sub(r"x\s*10\s*(3|4|6)\s*l\b", r"x10^\1/L", text, flags=re.IGNORECASE)
    text = re.sub(r"x\s*10\s*/\s*l\b", "x10/L", text, flags=re.IGNORECASE)

    return text


def _extract_pdf_text(pdf_bytes: bytes) -> str:
    reader = PdfReader(io.BytesIO(pdf_bytes))
    pieces: list[str] = []
    for page in reader.pages:
        pieces.append(page.extract_text() or "")
    return "\n".join(pieces).strip()


def _ocr_pdf(pdf_bytes: bytes) -> str:
    pages = convert_from_bytes(pdf_bytes, dpi=300)
    text_chunks: list[str] = []
    for page in pages:
        text_chunks.append(pytesseract.image_to_string(page))
    return "\n".join(text_chunks).strip()


def _ocr_image(image_bytes: bytes) -> str:
    image = Image.open(io.BytesIO(image_bytes))
    return pytesseract.image_to_string(image).strip()


def _looks_like_pdf(content: bytes) -> bool:
    return content.lstrip().startswith(b"%PDF-")


def extract_text(content: bytes, content_type: str | None, file_name: str | None) -> str:
    name = (file_name or "").lower()
    ctype = (content_type or "").lower()

    is_pdf_by_metadata = "pdf" in ctype or name.endswith(".pdf")
    is_pdf_by_bytes = _looks_like_pdf(content)

    if is_pdf_by_bytes:
        text = _extract_pdf_text(content)
        if len(text) >= 100:
            return text
        return _ocr_pdf(content)

    if is_pdf_by_metadata:
        return _ocr_image(content)

    return _ocr_image(content)


def _parse_measurements(lines: Iterable[str]) -> list[NormalizedReading]:
    readings: list[NormalizedReading] = []
    seen: set[tuple[str, Decimal, str]] = set()

    cleaned_lines = [
        " ".join(_normalize_ocr_artifacts(line).replace("|", " ").split())
        for line in lines
        if line.strip()
    ]

    def _to_decimal(raw: str) -> Decimal | None:
        try:
            return Decimal(raw.replace(",", ""))
        except InvalidOperation:
            return None

    def _is_numeric(raw: str) -> bool:
        return _NUMERIC_VALUE_PATTERN.fullmatch(raw.replace(",", "")) is not None

    def _first_numeric_match(raw: str):
        return re.search(r"-?\d+(?:\.\d+)?", raw)

    def _looks_like_reference_range(raw: str) -> bool:
        lower = raw.lower()
        return any(ch.isdigit() for ch in raw) and ("-" in raw or "–" in raw or " to " in lower)

    def _looks_like_unit(raw: str) -> bool:
        compact = raw.replace(" ", "")
        return _UNIT_ONLY_PATTERN.fullmatch(compact) is not None and any(ch.isalpha() or ch in {"%", "µ", "μ", "u"} for ch in compact)

    def _is_label(raw: str) -> bool:
        return raw.strip().lower().rstrip(":") in _NON_MEASUREMENT_LABELS

    def _looks_like_name(raw: str) -> bool:
        candidate = raw.strip().strip(":")
        if len(candidate) < 2:
            return False
        if any(ch.isdigit() for ch in candidate):
            return False
        return bool(re.fullmatch(r"[A-Za-z#%()\-\s]+", candidate))

    def _add_reading(name: str, raw_value: str, unit: str) -> None:
        if _is_label(name):
            return
        if not _looks_like_name(name):
            return

        value = _to_decimal(raw_value)
        if value is None:
            return
        normalized = normalize_reading(name, value, unit)
        if not is_known_biomarker_code(normalized.biomarker_code):
            return
        key = (normalized.biomarker_code, normalized.value, normalized.unit)
        if key in seen:
            return
        seen.add(key)
        readings.append(normalized)

    for clean in cleaned_lines:
        if len(clean) < 4:
            continue

        table_row = _TABLE_ROW_PATTERN.match(clean)
        if table_row:
            name = table_row.group("name").strip(" :-")
            raw_value = table_row.group("value")
            unit = table_row.group("unit")
            _add_reading(name, raw_value, unit)
            continue

        match = _MEASUREMENT_PATTERN.search(clean)
        if not match:
            continue

        name = match.group("name").strip(" :-")
        raw_value = match.group("value")
        unit = match.group("unit")

        _add_reading(name, raw_value, unit)

    for index in range(len(cleaned_lines) - 3):
        name = cleaned_lines[index].strip(" :-")
        value_line = cleaned_lines[index + 1]
        range_line = cleaned_lines[index + 2]
        unit_line = cleaned_lines[index + 3]

        if _is_label(name):
            continue
        if not _is_numeric(value_line):
            continue
        if not _looks_like_reference_range(range_line):
            continue
        if not _looks_like_unit(unit_line):
            continue

        _add_reading(name, value_line, unit_line)

    for index in range(1, len(cleaned_lines) - 2):
        value_line = cleaned_lines[index]
        unit_line = cleaned_lines[index + 1]
        range_line = cleaned_lines[index + 2]
        name = cleaned_lines[index - 1].strip(" :-")

        if not _is_numeric(value_line):
            continue
        if not _looks_like_unit(unit_line):
            continue
        if not _looks_like_reference_range(range_line):
            continue
        if _is_label(name):
            continue
        if name.lower() in _CATEGORY_HINTS:
            continue

        _add_reading(name, value_line, unit_line)

    for index in range(2, len(cleaned_lines) - 2):
        category = cleaned_lines[index - 2].strip(" :-").lower()
        name = cleaned_lines[index - 1].strip(" :-")
        value_line = cleaned_lines[index]
        unit_line = cleaned_lines[index + 1]
        range_line = cleaned_lines[index + 2]

        if category not in _CATEGORY_HINTS:
            continue
        if _is_label(name):
            continue
        if not _is_numeric(value_line):
            continue
        if not _looks_like_unit(unit_line):
            continue
        if not _looks_like_reference_range(range_line):
            continue

        _add_reading(name, value_line, unit_line)

    for clean in cleaned_lines:
        lowered = clean.lower()
        if len(clean) < 6:
            continue
        if not any(lowered.startswith(f"{hint} ") for hint in _CATEGORY_HINTS):
            continue

        value_match = _first_numeric_match(clean)
        if not value_match:
            continue

        name = clean[: value_match.start()].strip(" :-")
        if _is_label(name):
            continue

        raw_value = value_match.group(0)
        tail = clean[value_match.end():].strip()
        unit_candidate = tail.split()[0] if tail else ""

        if not _looks_like_unit(unit_candidate):
            unit_candidate = ""

        _add_reading(name, raw_value, unit_candidate)

    return readings


def parse_and_normalize(content: bytes, content_type: str | None, file_name: str | None) -> list[NormalizedReading]:
    text = extract_text(content, content_type=content_type, file_name=file_name)
    lines = text.splitlines()
    return _parse_measurements(lines)
