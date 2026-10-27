"""Independent public-key verification; requires Python cryptography for this tool only."""
import argparse
import base64
import hashlib
import json
import pathlib
import re
import xml.etree.ElementTree as ET
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

def verify(feed_path, key_path, archive_path=None):
    data = pathlib.Path(feed_path).read_bytes()
    if len(data)>1024*1024: raise ValueError('Feed exceeds limit')
    matches=list(re.finditer(rb'<!-- sparkle-signatures:\s*edSignature: ([A-Za-z0-9+/=]+)\s*length: ([0-9]+)\s*-->',data))
    if len(matches)!=1: raise ValueError('Missing or ambiguous feed signature')
    match=matches[0]; length=int(match[2])
    if length!=match.start() or data[match.end():].strip(): raise ValueError('Feed signature boundaries rejected')
    public=base64.b64decode(pathlib.Path(key_path).read_text().strip(),validate=True)
    key=Ed25519PublicKey.from_public_bytes(public)
    key.verify(base64.b64decode(match[1],validate=True),data[:length])
    root=ET.fromstring(data[:length])
    ns='{http://www.andymatuschak.org/xml-namespaces/sparkle}'
    result={'FeedSignatureVerified':True,'PublicKeySha256':hashlib.sha256(public).hexdigest(),
        'FeedSha256':hashlib.sha256(data).hexdigest(),'Items':len(root.findall('channel/item'))}
    if archive_path is not None:
        path=pathlib.Path(archive_path)
        items=[item for item in root.findall('channel/item') if item.find('enclosure').get('url','').endswith('/'+path.name)]
        if len(items)!=1: raise ValueError('Missing or ambiguous Mac archive')
        enclosure=items[0].find('enclosure')
        if not enclosure.get('url','').startswith('https://github.com/VendettaMask/opticalsystem/releases/download/'):
            raise ValueError('Untrusted archive source')
        if path.stat().st_size!=int(enclosure.get('length','0')) or path.stat().st_size>2147483648:
            raise ValueError('Archive size differs from signed feed')
        archive=path.read_bytes()
        key.verify(base64.b64decode(enclosure.get(ns+'edSignature',''),validate=True),archive)
        result.update(ArchiveSignatureVerified=True,Archive=path.name,ArchiveSize=len(archive),
            ArchiveSha256=hashlib.sha256(archive).hexdigest(),BundleVersion=items[0].findtext(ns+'version'))
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('feed'); parser.add_argument('public_key'); parser.add_argument('--archive')
    args=parser.parse_args()
    print(json.dumps(verify(args.feed,args.public_key,args.archive),indent=2))
