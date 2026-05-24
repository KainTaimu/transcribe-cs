#!/usr/bin/env python3
"""Translate Japanese SRT subtitles to English using a local GGUF model."""

import argparse
import re
import sys
from pathlib import Path
from typing import Any, Optional

import srt
from llama_cpp import Llama

SYSTEM_PROMPT = (
    "Translate Japanese subtitle lines to natural English (visual novel style). "
    "Be faithful to meaning and preserve each speaker’s personality, tone, and attitude. "
    "Keep names/terms consistent. Do not add or omit meaning."
)

BATCH_SIZE = 25

MAX_TOKENS_BATCH = 4096
MAX_TOKENS_RETRY = 512

INFERENCE_KWARGS = {
    "temperature": 0.3,
    "top_k": 40,
    "top_p": 0.95,
    "min_p": 0.05,
    "repeat_penalty": 1.1,
}


_FULLWIDTH_TRANSLATE = str.maketrans(
    {
        # full-width digits
        "０": "0",
        "１": "1",
        "２": "2",
        "３": "3",
        "４": "4",
        "５": "5",
        "６": "6",
        "７": "7",
        "８": "8",
        "９": "9",
        # common punctuation variants
        "．": ".",
        "。": ".",
        "：": ":",
        "－": "-",
        "ー": "-",
        "–": "-",
        "—": "-",
        "（": "(",
        "）": ")",
    }
)


def _strip_code_fences(text: str) -> str:
    s = text.strip()
    if not s.startswith("```"):
        return s
    lines = s.splitlines()
    if len(lines) < 2:
        return s
    if not lines[0].startswith("```"):
        return s
    # Drop first ```(lang) line, and a trailing ``` if present.
    body = lines[1:]
    if body and body[-1].strip() == "```":
        body = body[:-1]
    return "\n".join(body).strip()


def _normalize_model_text(text: Optional[str]) -> str:
    if not text:
        return ""
    # Make numbering punctuation predictable.
    return _strip_code_fences(text).translate(_FULLWIDTH_TRANSLATE)


_ITEM_RE_STRICT = re.compile(r"^\s*(?:[-*]\s*)?(\d{1,3})\s*[\.)\:\-]\s*(.*)\s*$")
_ITEM_RE_SOFT = re.compile(r"^\s*(?:[-*]\s*)?(\d{1,3})\s+(.*)\s*$")


def _parse_numbered_blocks(raw: str, expected_max: int) -> dict[int, str]:
    """Parse model output into {N: text}, supporting multiline items.

    Accepts items like:
      1. text
      2) text
      3: text
      4 - text

    If item text wraps, following non-header lines are appended.
    """
    parsed: dict[int, list[str]] = {}
    current_n: int | None = None
    last_header_n: int | None = None

    for line in raw.splitlines():
        m = _ITEM_RE_STRICT.match(line)
        soft = False
        if not m:
            m = _ITEM_RE_SOFT.match(line)
            soft = bool(m)

        if m:
            n = int(m.group(1))
            if n < 1 or n > expected_max:
                continue
            # Soft form ("1 translation") is ambiguous; only accept if sequential.
            if soft and last_header_n is not None and n != last_header_n + 1:
                continue

            current_n = n
            last_header_n = n
            # If model repeats an item number, keep last block.
            parsed[current_n] = []
            first = m.group(2).strip()
            if first:
                parsed[current_n].append(first)
            continue

        if current_n is None:
            continue

        cont = line.strip()
        if cont:
            parsed[current_n].append(cont)

    out: dict[int, str] = {}
    for n, parts in parsed.items():
        text = " ".join(parts).strip()
        text = re.sub(r"\s+", " ", text)
        if text:
            out[n] = text
    return out


def load_progress(output_path: Path) -> dict:
    """Return a mapping of SRT index → Subtitle for any already-translated entries in output_path."""
    if not output_path.exists():
        return {}
    try:
        text = output_path.read_text(encoding="utf-8")
        subs = list(srt.parse(text))
        return {sub.index: sub for sub in subs}
    except Exception as exc:
        print(
            f"[warn] could not parse existing output file, starting fresh: {exc}",
            file=sys.stderr,
        )
        return {}


def iter_batches(items: list, batch_size: int) -> list[tuple[int, list]]:
    return [
        (i // batch_size + 1, items[i : i + batch_size])
        for i in range(0, len(items), batch_size)
    ]


def batch_done(
    batch: list[srt.Subtitle], translated_map: dict[int, srt.Subtitle]
) -> bool:
    return all(sub.index in translated_map for sub in batch)


def write_output(output_path: Path, translated_map: dict[int, srt.Subtitle]) -> None:
    done_subs = sorted(translated_map.values(), key=lambda s: s.index)
    output_path.write_text(srt.compose(done_subs), encoding="utf-8")


def _chat_content(
    llm: Llama, messages: list[dict[str, str]], *, max_tokens: int
) -> str:
    # llama_cpp typing expects ChatCompletionRequestMessage; runtime accepts dicts.
    resp: Any = llm.create_chat_completion(
        messages=messages,  # type: ignore[arg-type]
        stream=False,
        max_tokens=max_tokens,
        **INFERENCE_KWARGS,
    )
    # When stream=False, llama_cpp returns dict-like response.
    content = None
    try:
        content = resp["choices"][0]["message"].get("content")
    except Exception:
        pass
    return _normalize_model_text(content)


def _build_numbered_prompt(
    entries: list[srt.Subtitle], positions: Optional[list[int]] = None
) -> str:
    if positions is None:
        positions = list(range(1, len(entries) + 1))
    return "\n".join(
        f"{pos}. {entries[pos - 1].content.replace(chr(10), ' ')}" for pos in positions
    )


def _translate_numbered(
    llm: Llama,
    entries: list[srt.Subtitle],
    *,
    user_preamble: str,
    positions: Optional[list[int]] = None,
    max_tokens: int,
) -> tuple[dict[int, str], str]:
    numbered = _build_numbered_prompt(entries, positions)
    user_msg = f"{user_preamble}\n\n{numbered}"
    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": user_msg},
    ]
    raw = _chat_content(llm, messages, max_tokens=max_tokens)
    return _parse_numbered_blocks(raw, expected_max=len(entries)), raw


def translate_batch(llm: Llama, entries: list[srt.Subtitle]) -> list[str]:
    """Translate a batch of srt.Subtitle entries; return list of translated content strings."""
    parsed, raw = _translate_numbered(
        llm,
        entries,
        user_preamble=(
            f"Translate the following {len(entries)} Japanese subtitle lines to English.\n"
            "Return ONLY numbered translations in the same order, one per line. "
            "Do not add any explanation or commentary."
        ),
        max_tokens=MAX_TOKENS_BATCH,
    )

    # Retry once for missing positions only (common when model wraps / skips numbering).
    missing_positions = [i + 1 for i in range(len(entries)) if (i + 1) not in parsed]
    retry_raw = None
    if missing_positions:
        parsed_retry, retry_raw = _translate_numbered(
            llm,
            entries,
            user_preamble=(
                "Some items were missing or malformed. Translate ONLY the lines below.\n"
                "Return ONLY numbered translations using the SAME numbers shown, one per item. "
                "No extra text."
            ),
            positions=missing_positions,
            max_tokens=MAX_TOKENS_RETRY,
        )
        parsed.update(parsed_retry)

    results = []
    raw_logged = False
    for i, entry in enumerate(entries):
        translated = parsed.get(i + 1)
        if translated:
            results.append(translated)
        else:
            if not raw_logged:
                print(
                    "[warn] batch parse failures detected; raw model output:\n"
                    + raw
                    + (
                        "\n\n[warn] retry raw model output:\n" + retry_raw
                        if retry_raw
                        else ""
                    ),
                    file=sys.stderr,
                )
                raw_logged = True
            print(
                f"[warn] no translation for entry {entry.index} (batch pos {i + 1}); "
                f"parsed {len(parsed)}/{len(entries)} lines; "
                f"keeping original: {entry.content!r}",
                file=sys.stderr,
            )
            results.append(entry.content)

    return results


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Translate a Japanese SRT file to English using a local GGUF model."
    )
    parser.add_argument(
        "--input", required=True, help="Path to the input Japanese SRT file"
    )
    parser.add_argument(
        "--output", required=True, help="Path to write the translated SRT file"
    )
    parser.add_argument("--model", required=True, help="Path to the GGUF model file")
    parser.add_argument(
        "--n-gpu-layers",
        type=int,
        default=30,
        help="Number of model layers to offload to GPU (-1 = all, 0 = CPU only)",
    )
    parser.add_argument("--verbose", default=False)
    args = parser.parse_args()

    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[error] input SRT not found: {input_path}", file=sys.stderr)
        sys.exit(1)

    srt_text = input_path.read_text(encoding="utf-8")
    subtitles = list(srt.parse(srt_text))
    total = len(subtitles)
    batches = iter_batches(subtitles, BATCH_SIZE)
    total_batches = len(batches)
    print(
        f"[info] translating {total} subtitles in {total_batches} batches",
        file=sys.stderr,
    )

    output_path = Path(args.output)
    translated_map: dict[int, srt.Subtitle] = load_progress(output_path)
    if translated_map:
        print(
            f"[info] resuming: {len(translated_map)}/{total} subtitles already translated",
            file=sys.stderr,
        )

    has_pending = any(not batch_done(batch, translated_map) for _, batch in batches)
    if not has_pending:
        print("[info] all batches already translated; nothing to do", file=sys.stderr)
        return

    print(f"[info] loading model: {args.model}", file=sys.stderr)
    llm = Llama(
        model_path=args.model,
        n_ctx=12_000,
        n_gpu_layers=args.n_gpu_layers,
        flash_attn=True,
        verbose=args.verbose,
    )

    for batch_num, batch in batches:
        if batch_done(batch, translated_map):
            print(
                f"[info] batch {batch_num}/{total_batches} skipped (already translated)",
                file=sys.stderr,
            )
            continue

        print(f"[info] batch {batch_num}/{total_batches}", file=sys.stderr)

        translated_texts = translate_batch(llm, batch)
        for sub, text in zip(batch, translated_texts):
            sub.content = text
            translated_map[sub.index] = sub
            print(f"{sub.index}: {text}")

        write_output(output_path, translated_map)

    print(f"[info] wrote translated SRT to {output_path}", file=sys.stderr)


if __name__ == "__main__":
    main()
