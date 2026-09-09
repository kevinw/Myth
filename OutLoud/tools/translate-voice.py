#!/usr/bin/env python3
"""OutLoud voice-translation bake.

Collects English story lines (bud.json `text` fields + C# Say/GreetPlayer calls,
including references to string constants declared in the same source file),
translates the new ones to German via the free MyMemory API, and writes an incremental
English->German cache to Myth/Assets/Voice/voice-de.json. Only the German *vowel melody*
is used at runtime to colour the babble; the screen still shows the English.

Usage: translate-voice.py [--lang en|de] [--email you@example.com]
  (--email raises MyMemory's anonymous daily quota; optional.)
"""
import json, re, sys, time, urllib.parse, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE_DIR = ROOT / "Myth" / "Source"
BUD_JSON = ROOT / "Myth" / "Assets" / "Storylets" / "bud.json"
OUT = ROOT / "Myth" / "Assets" / "Voice" / "voice-de.json"

LANG = "en|de"
EMAIL = None

# C# string literal (handles \" and \\ escapes).
STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')


def say_strings(source: str):
	constants = {
		m.group(1): m.group(2).encode().decode("unicode_escape")
		for m in re.finditer(r'\bconst\s+string\s+(\w+)\s*=\s*' + STRING.pattern, source)
	}
	for m in re.finditer(r"\b(?:Say|GreetPlayer)\s*\(", source):
		depth, i, n = 1, m.end(), len(source)
		while i < n and depth:
			c = source[i]
			if c == '"':
				lit = STRING.match(source, i)
				if lit:
					yield lit.group(1).encode().decode("unicode_escape")
					i = lit.end()
					continue
			elif c == "(":
				depth += 1
			elif c == ")":
				depth -= 1
			elif c.isalpha() or c == "_":
				identifier = re.match(r"\w+", source[i:]).group()
				if depth == 1 and identifier in constants and re.match(r"\s*[,)]", source[i + len(identifier):]):
					yield constants[identifier]
				i += len(identifier)
				continue
			i += 1


def collect():
	lines = set()
	for cs in SOURCE_DIR.rglob("*.cs"):
		lines.update(say_strings(cs.read_text(encoding="utf-8")))
	if BUD_JSON.exists():
		deck = json.loads(BUD_JSON.read_text(encoding="utf-8"))
		for s in deck.get("storylets", []):
			text = (s.get("content") or {}).get("text") or s.get("text")
			if isinstance(text, str):
				lines.add(text)
	return {l.strip() for l in lines if l.strip()}


def translate(text: str) -> str:
	q = urllib.parse.urlencode({"q": text, "langpair": LANG, **({"de": EMAIL} if EMAIL else {})})
	with urllib.request.urlopen(f"https://api.mymemory.translated.net/get?{q}", timeout=20) as r:
		data = json.load(r)
	if str(data.get("responseStatus")) != "200":
		raise RuntimeError(data.get("responseDetails") or "Translation service failed")
	translated = data["responseData"]["translatedText"]
	if not isinstance(translated, str) or not translated.strip():
		raise ValueError("Translation service returned empty text")
	return translated


def main(argv):
	global LANG, EMAIL
	for i, a in enumerate(argv):
		if a == "--lang" and i + 1 < len(argv): LANG = argv[i + 1]
		if a == "--email" and i + 1 < len(argv): EMAIL = argv[i + 1]

	cache = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else {}
	english = collect()
	missing = sorted(english - {k for k, v in cache.items() if v})
	print(f"{len(english)} lines, {len(missing)} need translation")

	for n, text in enumerate(missing, 1):
		try:
			cache[text] = translate(text)
			print(f"  [{n}/{len(missing)}] {text!r} -> {cache[text]!r}")
		except Exception as e:
			print(f"  [{n}/{len(missing)}] FAILED {text!r}: {e}", file=sys.stderr)
		time.sleep(0.5)  # be gentle with the free endpoint

	OUT.parent.mkdir(parents=True, exist_ok=True)
	OUT.write_text(json.dumps(cache, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8")
	print(f"wrote {len(cache)} translations -> {OUT}")


if __name__ == "__main__":
	main(sys.argv[1:])
