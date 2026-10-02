"""Prints every failed test of the .trx files under a folder as a GitHub Actions error annotation,
so failures show (and can be read through the public API) without opening the raw log."""
import glob
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def clean(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


failed = 0
for path in glob.glob(sys.argv[1] + "/**/*.trx", recursive=True):
    root = ET.parse(path).getroot()
    for result in root.iterfind(".//t:UnitTestResult", NS):
        if result.get("outcome") != "Failed":
            continue
        failed += 1
        message = result.findtext(".//t:Message", default="", namespaces=NS).strip()
        stack = result.findtext(".//t:StackTrace", default="", namespaces=NS).strip()
        detail = (message + "\n" + "\n".join(stack.splitlines()[:6]))[:3000]
        print(f"::error title={clean(result.get('testName', 'test'))}::{clean(detail)}")
print(f"{failed} failed test(s)")
