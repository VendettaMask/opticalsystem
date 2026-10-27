// Cloud-only application. Updates a disposable copy of the complete product;
// its own relaunch records completion without opening the product's desktop UI.
#import "UpdaterBridge.m"

static NSString *root;
static NSString *mode;
static NSMutableArray<NSNumber *> *states;
static void writeResult(BOOL success, NSString *detail) {
    NSDictionary *report = @{ @"Success": @(success), @"Mode": mode ?: @"", @"States": states ?: @[], @"Detail": detail ?: @"" };
    NSData *data = [NSJSONSerialization dataWithJSONObject:report options:NSJSONWritingPrettyPrinted error:NULL];
    [data writeToFile:[root stringByAppendingPathComponent:@"result.json"] atomically:YES];
}

@interface OSDFixtureDriver : OSDUpdater <NSApplicationDelegate>
@property(copy) NSString *feed;
@end
@implementation OSDFixtureDriver
- (NSString *)feedURLStringForUpdater:(SPUUpdater *)updater { return self.feed; }
- (BOOL)isTrustedDownloadURL:(NSURL *)url {
    return [url.scheme isEqualToString:@"http"] && [url.host isEqualToString:@"127.0.0.1"] &&
        [url.lastPathComponent isEqualToString:@"update.dmg"];
}
- (void)emit:(int)state progress:(double)progress {
    [states addObject:@(state)];
    if (state == 2) dispatch_async(dispatch_get_main_queue(), ^{ osd_updater_download(); });
    if (state == 3 && [mode isEqualToString:@"cancel-download"])
        dispatch_async(dispatch_get_main_queue(), ^{ osd_updater_cancel(); });
    if (state == 5) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if ([mode isEqualToString:@"cancel-ready"]) osd_updater_cancel();
            else {
                [@"relaunch" writeToFile:[root stringByAppendingPathComponent:@"relaunch-expected"] atomically:YES encoding:NSUTF8StringEncoding error:NULL];
                if (!osd_updater_install()) { writeResult(NO,@"Native install reply rejected"); exit(21); }
            }
        });
    }
    if (state == 8 || state == 9) {
        BOOL expected = state == 8 ? [mode hasPrefix:@"cancel-"] : [mode isEqualToString:@"tampered-archive"];
        writeResult(expected, state == 8 ? @"Cancelled" : @"Verification rejected");
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 2 * NSEC_PER_SEC), dispatch_get_main_queue(), ^{ [NSApp terminate:nil]; });
    }
}
@end

int main(void) {
    @autoreleasepool {
        NSBundle *bundle = NSBundle.mainBundle;
        root = [bundle objectForInfoDictionaryKey:@"OSDFixtureRoot"];
        mode = [bundle objectForInfoDictionaryKey:@"OSDFixtureMode"];
        if (!root || !mode) return 20;
        states = [NSMutableArray new];
        if ([NSFileManager.defaultManager fileExistsAtPath:[root stringByAppendingPathComponent:@"relaunch-expected"]]) {
            writeResult(YES,@"Relaunched after installation");
            return 0;
        }
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        OSDFixtureDriver *driver = [OSDFixtureDriver new];
        driver.feed = [bundle objectForInfoDictionaryKey:@"OSDFixtureFeed"];
        instance = driver;
        NSApp.delegate = driver;
        NSBundle *host = [NSBundle bundleWithPath:[bundle objectForInfoDictionaryKey:@"OSDFixtureHost"]];
        driver.updater = [[SPUUpdater alloc] initWithHostBundle:host applicationBundle:bundle userDriver:driver delegate:driver];
        NSError *error;
        if (![driver.updater startUpdater:&error]) { writeResult(NO,error.description); return 22; }
        driver.updater.automaticallyChecksForUpdates = NO;
        driver.updater.automaticallyDownloadsUpdates = NO;
        dispatch_async(dispatch_get_main_queue(), ^{ [driver.updater checkForUpdates]; });
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 150 * NSEC_PER_SEC), dispatch_get_main_queue(), ^{ writeResult(NO,@"Timed out"); exit(23); });
        [NSApp run];
    }
    return 0;
}
