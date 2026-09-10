"""Complete the macOS bundle metadata omitted by Dotnet.Bundle."""
import argparse
import plistlib
import shutil
from pathlib import Path
from xml.etree import ElementTree

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('bundle', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
plist_path = args.bundle / 'Contents/Info.plist'
with plist_path.open('rb') as stream:
    metadata = plistlib.load(stream)
project = ElementTree.parse(root / 'OpenUtau/OpenUtau.csproj')
document_types = project.find('.//CFBundleDocumentTypes/array')
if document_types is None:
    raise SystemExit('Project document types are missing')
metadata['CFBundleDocumentTypes'] = plistlib.loads(
    b'<plist version="1.0">' + ElementTree.tostring(document_types) + b'</plist>')
with plist_path.open('wb') as stream:
    plistlib.dump(metadata, stream)
resources = args.bundle / 'Contents/Resources'
resources.mkdir(exist_ok=True)
shutil.copy2(root / 'OpenUtau/Assets/OpenUtau.icns', resources / 'OpenUtau.icns')
print(f'Completed metadata and standard icon: {args.bundle}')
