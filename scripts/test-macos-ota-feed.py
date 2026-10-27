import base64
import importlib.util
import json
import pathlib
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('macfeed', pathlib.Path(__file__).with_name('macos-ota-feed.py'))
feed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feed)

class FeedTests(unittest.TestCase):
    def test_build_numbers_are_monotonic_and_fit_bundle_limits(self):
        values = [feed.bundle_version(run, attempt) for run,attempt in [(1,1),(1,2),(2,1),(99,99),(100,1)]]
        tuples = [tuple(map(int, value.split('.'))) for value in values]
        self.assertEqual(sorted(tuples), tuples)
        self.assertTrue(all(1<=a<=9999 and b<=99 and c<=99 for a,b,c in tuples))
        for args in [(0,0),(1,100),(1000000,1)]:
            with self.assertRaises(ValueError): feed.bundle_version(*args)

    def test_channels_are_preserved_and_reused_builds_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            target, entry_path = root/'appcast.xml', root/'entry.json'
            entry = dict(version='1.0.4', bundleVersion='1.2.3', name='OpticalSystemDesign-1.0.4-osx-arm64-AdHocTest.dmg',
                tag='v1.0.4-test.20261019', size=1024, signature=base64.b64encode(b'\0'*64).decode())
            entry_path.write_text(json.dumps(entry))
            feed.merge(target,entry_path,target)
            with self.assertRaises(ValueError): feed.merge(target,entry_path,target)
            entry.update(bundleVersion='1.2.4',name='OpticalSystemDesign-1.0.4-osx-arm64-AdHoc.dmg',tag='v1.0.4')
            entry_path.write_text(json.dumps(entry)); feed.merge(target,entry_path,target)
            items = ET.parse(target).findall('channel/item')
            self.assertEqual(2,len(items))
            self.assertEqual(['test',None],[item.findtext('{'+feed.NS+'}channel') for item in items])

    def test_unsafe_release_metadata_is_rejected(self):
        original = dict(version='1.0.4', bundleVersion='1.2.3', name='OpticalSystemDesign-1.0.4-osx-arm64-AdHocTest.dmg',
            tag='v1.0.4-test.20261019', size=1024, signature=base64.b64encode(b'\0'*64).decode())
        defects = [('version','../1.0.4'),('bundleVersion','99999.1.1'),('name','../other.dmg'),('tag','v2.0.0'),
            ('size',0),('size',2147483649),('signature',base64.b64encode(b'\0'*32).decode())]
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for name,value in defects:
                entry = original | {name:value}
                (root/'entry.json').write_text(json.dumps(entry))
                with self.subTest(name=name,value=value), self.assertRaises(ValueError):
                    feed.merge(root/'appcast.xml',root/'entry.json',root/'appcast.xml')

if __name__ == '__main__': unittest.main()
