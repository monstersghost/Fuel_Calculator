"""Minimal .xlsx reader using only the standard library (shared strings + inline values)."""
import re
import zipfile
import xml.etree.ElementTree as ET

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
      "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships"}


def _col_index(ref):
    letters = re.match(r"[A-Z]+", ref).group(0)
    n = 0
    for ch in letters:
        n = n * 26 + ord(ch) - 64
    return n - 1


def sheets(path):
    with zipfile.ZipFile(path) as z:
        wb = ET.fromstring(z.read("xl/workbook.xml"))
        rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
        targets = {r.get("Id"): r.get("Target") for r in rels}
        out = []
        for s in wb.find("m:sheets", NS):
            rid = s.get("{%s}id" % NS["r"])
            target = targets[rid].lstrip("/")
            if not target.startswith("xl/"):
                target = "xl/" + target
            out.append((s.get("name"), target))
        return out


def rows(path, sheet_target):
    with zipfile.ZipFile(path) as z:
        shared = []
        if "xl/sharedStrings.xml" in z.namelist():
            sst = ET.fromstring(z.read("xl/sharedStrings.xml"))
            for si in sst.findall("m:si", NS):
                shared.append("".join(t.text or "" for t in si.iter("{%s}t" % NS["m"])))
        root = ET.fromstring(z.read(sheet_target))
        for row in root.iter("{%s}row" % NS["m"]):
            values = {}
            for c in row.findall("m:c", NS):
                ref = c.get("r")
                t = c.get("t")
                v = c.find("m:v", NS)
                if t == "s" and v is not None:
                    val = shared[int(v.text)]
                elif t == "inlineStr":
                    val = "".join(x.text or "" for x in c.iter("{%s}t" % NS["m"]))
                elif v is not None:
                    val = v.text
                else:
                    val = None
                values[_col_index(ref)] = val
            if values:
                width = max(values) + 1
                yield [values.get(i) for i in range(width)]
