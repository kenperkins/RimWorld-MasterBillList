#!/usr/bin/env python3
"""Convert CHANGELOG.md (Markdown) to Steam Workshop BBCode.

CHANGELOG.md is the single source of truth; regenerate the Steam text from it
rather than maintaining a second copy.

Usage (run from the repo root):
    python3 tools/changelog_to_steam.py            # whole changelog as BBCode
    python3 tools/changelog_to_steam.py --latest   # only the newest version entry
    python3 tools/changelog_to_steam.py --latest | pbcopy   # straight to clipboard
"""
import re
import sys


def inline(t):
    t = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'[url=\2]\1[/url]', t)  # links
    t = re.sub(r'\*\*([^*]+)\*\*', r'[b]\1[/b]', t)                # **bold**
    t = re.sub(r'\*([^*]+)\*', r'[i]\1[/i]', t)                    # *italic*
    t = re.sub(r'_([^_]+)_', r'[i]\1[/i]', t)                      # _italic_
    t = re.sub(r'`([^`]+)`', r'\1', t)                             # `code` -> plain (Steam has no inline code)
    return t


def convert(md):
    out = []
    in_list = False

    def close_list():
        nonlocal in_list
        if in_list:
            out.append('[/list]')
            in_list = False

    for raw in md.splitlines():
        line = raw.rstrip()
        header = re.match(r'^(#{1,6})\s+(.*)$', line)
        bullet = re.match(r'^\s*[-*]\s+(.*)$', line)
        if header:
            close_list()
            level = min(len(header.group(1)), 3)  # Steam supports h1..h3
            out.append(f'[h{level}]{inline(header.group(2))}[/h{level}]')
        elif bullet:
            if not in_list:
                out.append('[list]')
                in_list = True
            out.append(f'[*]{inline(bullet.group(1))}')
        elif line.strip() == '':
            close_list()
            out.append('')
        else:
            close_list()
            out.append(inline(line))
    close_list()

    text = '\n'.join(out)
    text = re.sub(r'\n{3,}', '\n\n', text).strip() + '\n'
    return text


def latest_section(md):
    lines = md.splitlines()
    start = next((i for i, l in enumerate(lines) if re.match(r'^##\s+', l)), None)
    if start is None:
        return md
    end = next((j for j in range(start + 1, len(lines)) if re.match(r'^##\s+', lines[j])), len(lines))
    return '\n'.join(lines[start:end])


def main():
    args = sys.argv[1:]
    latest = '--latest' in args
    paths = [a for a in args if a != '--latest']
    path = paths[0] if paths else 'CHANGELOG.md'
    with open(path, encoding='utf-8') as f:
        md = f.read()
    if latest:
        md = latest_section(md)
    sys.stdout.write(convert(md))


if __name__ == '__main__':
    main()
