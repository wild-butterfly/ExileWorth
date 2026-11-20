"""Build the account-level Pages redirect site without claiming the old repo name.

Publish the output as wild-butterfly/wild-butterfly.github.io (branch main, root).
This leaves GitHub's Vaaluation -> ExileWorth repository redirect intact.
The root sitemap index points to the project sitemap, so future page additions
only need to update website/sitemap.xml. GitHub Pages has no server redirect
rules; known old URLs use immediate HTML redirects and canonical destinations.
"""
import html
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
BASE = "https://wild-butterfly.github.io/ExileWorth/"
HOST = "https://wild-butterfly.github.io/"


def redirect(destination):
    safe = html.escape(destination, quote=True)
    return f'''<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>ExileWorth has moved</title>
  <meta http-equiv="refresh" content="0; url={safe}" />
  <link rel="canonical" href="{safe}" />
  <script>location.replace({json.dumps(destination)} + location.search + location.hash);</script>
</head>
<body><h1>ExileWorth has moved</h1><p><a href="{safe}">Continue to ExileWorth</a>.</p></body>
</html>
'''


def build(output):
    output.mkdir(parents=True, exist_ok=True)
    (output / ".nojekyll").write_text("")
    (output / "index.html").write_text(redirect(BASE))
    paths = {}
    for element in ET.parse(ROOT / "website/sitemap.xml").findall("{*}url/{*}loc"):
        url = element.text
        assert url.startswith(BASE), "Only ExileWorth destinations are allowed"
        relative = url.removeprefix(BASE)
        assert ".." not in relative and not relative.startswith("/"), "Unsafe path"
        file = output / "Vaaluation" / relative / "index.html"
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(redirect(url))
        paths["/Vaaluation/" + relative] = url
    # Only legacy paths redirect from the 404 handler. Other projects retain a
    # normal missing-page response instead of being sent to ExileWorth.
    (output / "404.html").write_text('''<!doctype html>
<html lang="en"><head><meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /><meta name="robots" content="noindex" /><title>Page not found</title>
<script>
const legacyPages = ''' + json.dumps(paths, indent=2) + ''';
if (location.pathname === '/Vaaluation' || location.pathname.startsWith('/Vaaluation/')) {
  const path = location.pathname.replace(/index\\.html$/, '');
  const target = legacyPages[path] || 'https://wild-butterfly.github.io/ExileWorth/';
  location.replace(target + location.search + location.hash);
}
</script></head><body><h1>Page not found</h1><p><a href="https://wild-butterfly.github.io/ExileWorth/">Visit ExileWorth</a>.</p></body></html>
''')
    (output / "robots.txt").write_text("User-agent: *\nAllow: /\n\nSitemap: " + HOST + "sitemap.xml\n")
    (output / "sitemap.xml").write_text('''<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <sitemap><loc>https://wild-butterfly.github.io/ExileWorth/sitemap.xml</loc></sitemap>
</sitemapindex>
''')
    print(f"Built {len(paths)} legacy page redirects and root sitemap discovery in {output}")


if __name__ == "__main__":
    build(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "hosting/github-pages-root")
