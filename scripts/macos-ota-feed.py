"""Public Mac appcast metadata. Signing is performed by pinned Sparkle tools."""
import argparse
import base64
import json
import os
import pathlib
import plistlib
import re
import xml.etree.ElementTree as ET

FEED = 'https://raw.githubusercontent.com/VendettaMask/opticalsystem/main/updates/macos/appcast.xml'
NS = 'http://www.andymatuschak.org/xml-namespaces/sparkle'
ET.register_namespace('sparkle', NS)

def bundle_version(run_number, attempt):
    # CFBundleVersion's numeric 4/2/2 digit limits; monotonic for release.yml.
    ordinal = int(run_number) * 100 + int(attempt)
    if int(run_number) <= 0 or not 0 < ordinal < 99_990_000 or not 0 < int(attempt) < 100:
        raise ValueError('Invalid monotonic Mac build number')
    return f'{1 + ordinal // 10000}.{ordinal // 100 % 100}.{ordinal % 100}'

def configure(plist_path, key_path):
    public = pathlib.Path(key_path).read_text().strip()
    if len(base64.b64decode(public, validate=True)) != 32:
        raise ValueError('Invalid Mac OTA public key')
    path = pathlib.Path(plist_path)
    info = plistlib.loads(path.read_bytes())
    info.update(SUFeedURL=FEED, SUPublicEDKey=public, SUEnableAutomaticChecks=False,
                SUAutomaticallyUpdate=False, SUSendProfileInfo=False, SUVerifyUpdateBeforeExtraction=True, SURequireSignedFeed=True)
    info['CFBundleVersion'] = bundle_version(os.environ.get('GITHUB_RUN_NUMBER', '1'), os.environ.get('GITHUB_RUN_ATTEMPT', '1'))
    path.write_bytes(plistlib.dumps(info, fmt=plistlib.FMT_XML, sort_keys=False))

def merge(feed_path, entry_path, output):
    entry = json.loads(pathlib.Path(entry_path).read_text())
    version, build, name, tag = (entry[k] for k in ('version','bundleVersion','name','tag'))
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version):
        raise ValueError('Invalid version')
    test = re.fullmatch(r'v'+re.escape(version)+r'-test\.[0-9]{8}', tag) is not None
    if not test and tag != 'v'+version:
        raise ValueError('Invalid release tag')
    if name != f'OpticalSystemDesign-{version}-osx-arm64-{"AdHocTest" if test else "AdHoc"}.dmg':
        raise ValueError('Invalid Mac package name')
    if not re.fullmatch(r'[1-9][0-9]{0,3}\.(0|[1-9][0-9]?)\.(0|[1-9][0-9]?)', build) or not 512 < entry['size'] <= 2147483648:
        raise ValueError('Invalid build or size')
    if len(base64.b64decode(entry['signature'], validate=True)) != 64:
        raise ValueError('Invalid Ed25519 signature')
    path = pathlib.Path(feed_path)
    root = ET.fromstring(path.read_bytes()) if path.exists() else ET.Element('rss', {'version':'2.0'})
    channel = root.find('channel')
    if channel is None:
        channel = ET.SubElement(root,'channel')
        ET.SubElement(channel,'title').text = 'Optical System Design for Mac'
        ET.SubElement(channel,'link').text = 'https://github.com/VendettaMask/opticalsystem/releases'
    channel_name = 'test' if test else ''
    for old in list(channel.findall('item')):
        old_version = old.findtext(f'{{{NS}}}shortVersionString')
        if old_version and tuple(map(int, old_version.split('.'))) > tuple(map(int, version.split('.'))):
            raise ValueError('Mac feed would offer an older product version')
        if (old.findtext(f'{{{NS}}}channel') or '') == channel_name:
            old_build = old.findtext(f'{{{NS}}}version')
            if old_build and tuple(map(int, old_build.split('.'))) >= tuple(map(int, build.split('.'))):
                raise ValueError('Mac feed would roll back or reuse a build')
            channel.remove(old)
    item = ET.SubElement(channel,'item')
    ET.SubElement(item,'title').text = 'Optical System Design '+version
    ET.SubElement(item,f'{{{NS}}}version').text = build
    ET.SubElement(item,f'{{{NS}}}shortVersionString').text = version
    ET.SubElement(item,f'{{{NS}}}minimumSystemVersion').text = '14.0'
    ET.SubElement(item,'link').text = f'https://github.com/VendettaMask/opticalsystem/releases/tag/{tag}'
    if test: ET.SubElement(item,f'{{{NS}}}channel').text = 'test'
    ET.SubElement(item,'enclosure', {'url':f'https://github.com/VendettaMask/opticalsystem/releases/download/{tag}/{name}',
        'length':str(entry['size']), 'type':'application/octet-stream', f'{{{NS}}}edSignature':entry['signature'], f'{{{NS}}}os':'macos'})
    ET.indent(root)
    pathlib.Path(output).write_bytes(ET.tostring(root, encoding='utf-8', xml_declaration=True)+b'\n')

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('mode', choices=['configure','merge'])
    parser.add_argument('paths', nargs='+')
    args = parser.parse_args()
    if args.mode == 'configure': configure(*args.paths)
    else: merge(*args.paths)
