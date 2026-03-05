from __future__ import annotations

import json
import re
from difflib import SequenceMatcher
from dataclasses import dataclass
from decimal import Decimal
from functools import lru_cache
from pathlib import Path
from typing import Optional


@dataclass(frozen=True)
class NormalizedReading:
    biomarker_code: str
    source_name: str
    value: Decimal
    unit: str
    normalized_value: Optional[Decimal]
    normalized_unit: Optional[str]


def _normalize_text(value: str) -> str:
    return " ".join(value.strip().lower().split())


_OCR_NAME_CORRECTIONS = {
    "wec": "wbc",
    "w8c": "wbc",
    "her": "hct",
    "hcr": "hct",
    "mev": "mcv",
    "mew": "mcv",
    "row cv": "rdw cv",
    "row-cv": "rdw cv",
    "row sd": "rdw sd",
    "row-sd": "rdw sd",
    "it": "plt",
    "neu%": "neu",
    "lym%": "lymph",
    "mon%": "mono",
    "eos%": "eos",
    "bas%": "baso",
    "bas#": "baso",
}


def _apply_ocr_name_corrections(value: str) -> str:
    corrected = _OCR_NAME_CORRECTIONS.get(value, value)
    return corrected


def _humanize_catalog_key(value: str) -> str:
    spaced = re.sub(r"[-_]+", " ", value)
    spaced = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", spaced)
    return _normalize_text(spaced)


def biomarker_name_to_code(name: str) -> str:
    sanitized = re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_")
    return sanitized.upper()


@lru_cache(maxsize=1)
def _load_biomarker_catalog() -> dict:
    catalog_path = Path(__file__).resolve().parent / "data" / "biomarker.json"
    if not catalog_path.exists():
        raise FileNotFoundError(f"Missing biomarker catalog file: {catalog_path}")

    with catalog_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    if not isinstance(payload, dict):
        raise ValueError("Invalid biomarker catalog format: root must be an object")

    return payload


@lru_cache(maxsize=1)
def _build_alias_index() -> dict[str, str]:
    catalog = _load_biomarker_catalog()
    alias_index: dict[str, str] = {}

    for canonical_name, metadata in catalog.items():
        code = biomarker_name_to_code(canonical_name)
        alias_index[_normalize_text(canonical_name)] = code

        humanized = _humanize_catalog_key(canonical_name)
        if humanized:
            alias_index[humanized] = code

        aliases = []
        if isinstance(metadata, dict):
            aliases = metadata.get("aliases") or []

        if not isinstance(aliases, list):
            continue

        for alias in aliases:
            if isinstance(alias, str) and alias.strip():
                alias_index[_normalize_text(alias)] = code

    return alias_index


@lru_cache(maxsize=1)
def _sorted_aliases() -> tuple[tuple[str, str], ...]:
    aliases = _build_alias_index()
    return tuple(sorted(aliases.items(), key=lambda item: len(item[0]), reverse=True))


@lru_cache(maxsize=1)
def _known_biomarker_codes() -> set[str]:
    return set(_build_alias_index().values())


def canonicalize_biomarker(name: str) -> str:
    aliases = _build_alias_index()

    def _match(candidate: str) -> str | None:
        if candidate in aliases:
            return aliases[candidate]

        tokens = set(candidate.split())
        padded_candidate = f" {candidate} "

        for alias, code in _sorted_aliases():
            if not alias:
                continue

            if " " in alias:
                if f" {alias} " in padded_candidate:
                    return code
                continue

            if len(alias) <= 2:
                if alias in tokens:
                    return code
                continue

            if alias in tokens:
                return code

        return None

    key = _apply_ocr_name_corrections(_normalize_text(name))
    matched = _match(key)
    if matched:
        return matched

    category_prefixes = {
        "hematology",
        "metabolic",
        "lipid",
        "diabetes",
        "thyroid",
        "inflammation",
        "nutrition",
        "vitamins",
    }
    parts = key.split()
    if parts and parts[0] in category_prefixes and len(parts) > 1:
        without_category = " ".join(parts[1:])
        without_category = _apply_ocr_name_corrections(without_category)
        matched = _match(without_category)
        if matched:
            return matched

    fuzzy_candidate = key
    if parts and parts[0] in category_prefixes and len(parts) > 1:
        fuzzy_candidate = " ".join(parts[1:])

    best_alias = None
    best_score = 0.0
    for alias, code in _sorted_aliases():
        if not alias:
            continue

        if len(alias) <= 2:
            continue

        score = SequenceMatcher(None, fuzzy_candidate, alias).ratio()
        threshold = 0.74 if len(fuzzy_candidate) <= 4 else 0.82
        if score >= threshold and score > best_score:
            best_score = score
            best_alias = code

    if best_alias:
        return best_alias

    return biomarker_name_to_code(name)


def is_known_biomarker_code(code: str) -> bool:
    return code in _known_biomarker_codes()


def normalize_unit(raw_unit: str) -> str:
    unit = (
        raw_unit.strip()
        .replace("μ", "u")
        .replace("µ", "u")
        .replace("×", "x")
        .replace("²", "^2")
        .replace("³", "^3")
        .replace("⁶", "^6")
    )
    compact = unit.lower().replace(" ", "")

    mapping = {
        "mg/dl": "mg/dL",
        "mmol/l": "mmol/L",
        "g/dl": "g/dL",
        "%": "%",
        "x10^3/ul": "x10^3/uL",
        "10^3/ul": "x10^3/uL",
        "x10^6/ul": "x10^6/uL",
        "10^6/ul": "x10^6/uL",
    }
    return mapping.get(compact, unit)


def to_canonical_value(biomarker_code: str, value: Decimal, unit: str) -> tuple[Optional[Decimal], Optional[str]]:
    canonical_unit = normalize_unit(unit)

    if biomarker_code in {"GLUCOSE", "BLOOD_GLUCOSE"}:
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("18.0")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code in {"TOTAL_CHOLESTEROL", "HDL", "LDL", "HDL_CHOLESTEROL", "LDL_CHOLESTEROL"}:
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("38.67")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code == "TRIGLYCERIDES":
        if canonical_unit == "mg/dL":
            return value, "mg/dL"
        if canonical_unit == "mmol/L":
            return (value * Decimal("88.57")).quantize(Decimal("0.01")), "mg/dL"

    if biomarker_code in {"HBA1C", "HEMOGLOBIN_A1C"}:
        if canonical_unit == "%":
            return value, "%"

    if biomarker_code == "HEMOGLOBIN":
        if canonical_unit == "g/dL":
            return value, "g/dL"

    if biomarker_code in {"WBC", "WHITE_BLOOD_CELLS", "WHITEBLOODCELLS"}:
        if canonical_unit == "x10^3/uL":
            return value, "x10^3/uL"

    if biomarker_code in {"RBC", "RED_BLOOD_CELLS", "REDBLOODCELLS"}:
        if canonical_unit == "x10^6/uL":
            return value, "x10^6/uL"

    if biomarker_code in _known_biomarker_codes():
        return value, canonical_unit

    return None, None


def normalize_reading(source_name: str, value: Decimal, unit: str) -> NormalizedReading:
    biomarker_code = canonicalize_biomarker(source_name)
    display_unit = normalize_unit(unit)
    normalized_value, normalized_unit = to_canonical_value(biomarker_code, value, display_unit)

    return NormalizedReading(
        biomarker_code=biomarker_code,
        source_name=source_name,
        value=value,
        unit=display_unit,
        normalized_value=normalized_value,
        normalized_unit=normalized_unit,
    )
