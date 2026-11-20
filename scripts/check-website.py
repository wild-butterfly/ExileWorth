"""Validate the assembled static site before publishing it to GitHub Pages."""
import json
from html.parser import HTMLParser
from pathlib import Path
import sys
from urllib.parse import unquote, urljoin, urlsplit
import xml.etree.ElementTree as ET

BASE = "https://wild-butterfly.github.io/ExileWorth/"


class Page(HTMLParser):
    def __init__(self, path):
        super().__init__()
        self.path = path
        self.ids = set()
        self.links = []
        self.images = []
        self.title = ""
        self.h1s = 0
        self.canonicals = []
        self.description = ""
        self.noindex = False
        self.in_title = False
        self.in_json = False
        self.json_text = ""
        self.feed(path.read_text())

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            assert attrs["id"] not in self.ids, f"{self.path}: duplicate ID {attrs['id']}"
            self.ids.add(attrs["id"])
        if tag == "title":
            self.in_title = True
        if tag == "h1":
            self.h1s += 1
        if tag == "meta" and attrs.get("name") == "description":
            self.description = attrs.get("content", "")
        if tag == "meta" and attrs.get("name") == "robots":
            self.noindex |= "noindex" in attrs.get("content", "")
        if tag == "link" and attrs.get("rel") == "canonical":
            self.canonicals.append(attrs.get("href"))
        if tag == "script" and attrs.get("type") == "application/ld+json":
            self.in_json = True
            self.json_text = ""
        if tag == "a" and attrs.get("href"):
            self.links.append(attrs["href"])
        if tag in ("img", "script", "link"):
            resource = attrs.get("src") if tag != "link" else attrs.get("href")
            if resource:
                self.links.append(resource)
        if tag == "img":
            assert "alt" in attrs, f"{self.path}: image missing alt"
            assert int(attrs.get("width", 0)) > 0 and int(attrs.get("height", 0)) > 0, f"{self.path}: image dimensions missing"
            for item in attrs.get("srcset", "").split(","):
                if item.strip():
                    self.links.append(item.strip().split()[0])

    def handle_data(self, data):
        if self.in_title:
            self.title += data
        if self.in_json:
            self.json_text += data

    def handle_endtag(self, tag):
        if tag == "title":
            self.in_title = False
        if tag == "script" and self.in_json:
            json.loads(self.json_text)
            self.in_json = False


def check(root):
    pages = {}
    for file in root.rglob("index.html"):
        relative = file.relative_to(root).as_posix().removesuffix("index.html")
        page = Page(file)
        url = BASE + relative
        assert page.canonicals == [url], f"{file}: canonical mismatch: {page.canonicals}"
        assert page.title.strip() and page.description.strip(), f"{file}: title/description missing"
        assert page.h1s == 1 and not page.noindex, f"{file}: H1/indexability issue"
        pages[url] = page
    assert len({p.title for p in pages.values()}) == len(pages), "Duplicate titles"
    listed = [node.text for node in ET.parse(root / "sitemap.xml").findall("{*}url/{*}loc")]
    assert len(listed) == len(set(listed)), "Duplicate sitemap URLs"
    assert set(listed) == set(pages), "Sitemap must contain every canonical HTML page"
    assert set((root / "sitemap.txt").read_text().splitlines()) == set(listed), "Text/XML sitemaps differ"
    for url, page in pages.items():
        for link in page.links:
            target = urlsplit(urljoin(url, link))
            if target.netloc != urlsplit(BASE).netloc or not target.path.startswith("/ExileWorth/"):
                continue
            relative = unquote(target.path.removeprefix("/ExileWorth/"))
            file = root / relative
            if target.path.endswith("/"):
                file /= "index.html"
            assert file.is_file(), f"{page.path}: broken local link {link}"
            if target.fragment and file.suffix == ".html":
                other = pages.get(BASE + relative)
                if other is None:
                    other = Page(file)
                assert unquote(target.fragment) in other.ids, f"{page.path}: broken anchor {link}"
    print(f"Website checks passed: {len(pages)} canonical pages, valid sitemaps/schema, internal links, anchors and image assets.")


if __name__ == "__main__":
    check(Path(sys.argv[1] if len(sys.argv) > 1 else "_site"))
