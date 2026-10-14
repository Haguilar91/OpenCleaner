#!/usr/bin/env python3
"""Checks that every text passed to the translation function has a Spanish entry.

Usage: check_keys.py <table file> <source files...>
The translation calls are found as tr("..."), L.T("..."), T("...") or _("..."), with string literals joined by "+"
(Kotlin, Swift, C#, Python). Table entries are read as "key" (or { "key") followed by "to", ":" or "," and a string.
Prints the missing keys and the unused ones; exits 1 when any key is missing.
"""
import re
import sys

STR = r'"((?:[^"\\]|\\.)*)"'


def unescape(s):
    return re.sub(r'\\(.)', lambda m: {'n': '\n', 't': '\t'}.get(m.group(1), m.group(1)), s)


def call_keys(text):
    keys = set()
    for m in re.finditer(r'(?:\bL\.T|(?<![A-Za-z0-9_.])(?:tr|T|_))\(\s*' + STR, text):
        parts = [m.group(1)]
        rest = text[m.end():]
        while True:   # "a" + "b" + ... (also across lines)
            n = re.match(r'\s*\+\s*' + STR, rest)
            if not n:
                break
            parts.append(n.group(1))
            rest = rest[n.end():]
        keys.add(unescape(''.join(parts)))
    return keys


def table_keys(text):
    keys = set()
    for m in re.finditer(r'^\s*\{?\s*' + STR + r'\s*(?:to|:|,)\s*(?:\n\s*)?"', text, re.M):
        keys.add(unescape(m.group(1)))
    return keys


def main():
    table = table_keys(open(sys.argv[1], encoding='utf-8').read())
    used = set()
    for f in sys.argv[2:]:
        used |= call_keys(open(f, encoding='utf-8').read())
    missing = sorted(used - table)
    unused = sorted(table - used)
    for k in missing:
        print('MISSING:', repr(k))
    for k in unused:
        print('unused: ', repr(k))
    print(f'{len(used)} keys used, {len(table)} in the table, {len(missing)} missing')
    sys.exit(1 if missing else 0)


if __name__ == '__main__':
    main()
