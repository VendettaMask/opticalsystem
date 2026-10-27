#import <AppKit/AppKit.h>
#import <Sparkle/Sparkle.h>

// C ABI shared with MacOtaState. All entry points and Sparkle callbacks use the
// AppKit main thread. No automatic checks, downloads or install-on-quit consent.
typedef void (*OSDUpdateEvent)(int, const char *, double);
@interface OSDUpdater : NSObject <SPUUserDriver, SPUUpdaterDelegate>
@property SPUUpdater *updater;
@property OSDUpdateEvent callback;
@property BOOL includeTests;
@property(copy) void (^choice)(SPUUserUpdateChoice);
@property(copy) void (^ready)(SPUUserUpdateChoice);
@property(copy) void (^cancel)(void);
@property uint64_t expected;
@property uint64_t received;
@property BOOL extracting;
@property BOOL cancelRequested;
@property(copy) NSString *version;
- (BOOL)isTrustedDownloadURL:(NSURL *)url;
@end

@implementation OSDUpdater
- (void)emit:(int)state progress:(double)progress {
    if (self.callback) self.callback(state, (self.version ?: @"").UTF8String, progress);
}
- (NSSet<NSString *> *)allowedChannelsForUpdater:(SPUUpdater *)updater {
    return self.includeTests ? [NSSet setWithObject:@"test"] : [NSSet set];
}
- (BOOL)updater:(SPUUpdater *)updater mayPerformUpdateCheck:(SPUUpdateCheck)check error:(NSError **)error {
    return check == SPUUpdateCheckUpdates; // user initiated only
}
- (void)showUpdatePermissionRequest:(SPUUpdatePermissionRequest *)request reply:(void (^)(SUUpdatePermissionResponse *))reply {
    reply([[SUUpdatePermissionResponse alloc] initWithAutomaticUpdateChecks:NO sendSystemProfile:NO]);
}
- (void)showUserInitiatedUpdateCheckWithCancellation:(void (^)(void))cancellation {
    self.cancel = cancellation; self.version = @""; [self emit:1 progress:0];
}
- (void)showUpdateFoundWithAppcastItem:(SUAppcastItem *)item state:(SPUUserUpdateState *)state reply:(void (^)(SPUUserUpdateChoice))reply {
    // The feed cannot turn the updater into an arbitrary download/installer.
    if (![self isTrustedDownloadURL:item.fileURL] || item.informationOnlyUpdate) { reply(SPUUserUpdateChoiceSkip); [self emit:9 progress:0]; return; }
    self.version = [NSString stringWithFormat:@"%@ (%@)", item.displayVersionString, item.versionString];
    self.cancel = nil;
    if (state.stage == SPUUserUpdateStageInstalling) { self.ready = reply; [self emit:5 progress:100]; }
    else { self.choice = reply; [self emit:2 progress:0]; }
}
- (BOOL)isTrustedDownloadURL:(NSURL *)url {
    BOOL trusted = [url.scheme isEqualToString:@"https"] && [url.host isEqualToString:@"github.com"] &&
        url.user == nil && url.password == nil && url.port == nil &&
        [url.path hasPrefix:@"/VendettaMask/opticalsystem/releases/download/"] &&
        [url.lastPathComponent hasPrefix:@"OpticalSystemDesign-"] && [url.lastPathComponent hasSuffix:@"-osx-arm64-AdHocTest.dmg"];
    trusted = trusted || ([url.scheme isEqualToString:@"https"] && [url.host isEqualToString:@"github.com"] &&
        url.user == nil && url.password == nil && url.port == nil &&
        [url.path hasPrefix:@"/VendettaMask/opticalsystem/releases/download/"] &&
        [url.lastPathComponent hasPrefix:@"OpticalSystemDesign-"] && [url.lastPathComponent hasSuffix:@"-osx-arm64-AdHoc.dmg"]);
    return trusted;
}
- (void)showUpdateReleaseNotesWithDownloadData:(SPUDownloadData *)data { }
- (void)showUpdateReleaseNotesFailedToDownloadWithError:(NSError *)error { }
- (void)showUpdateNotFoundWithError:(NSError *)error acknowledgement:(void (^)(void))acknowledgement {
    [self emit:7 progress:0]; acknowledgement();
}
- (void)showUpdaterError:(NSError *)error acknowledgement:(void (^)(void))acknowledgement {
    NSLog(@"Optical System Design updater: %@", error);
    [self emit:9 progress:0]; acknowledgement();
}
- (void)showDownloadInitiatedWithCancellation:(void (^)(void))cancellation {
    self.cancel = cancellation; self.received = self.expected = 0; [self emit:3 progress:0];
}
- (void)showDownloadDidReceiveExpectedContentLength:(uint64_t)length { self.expected = length; }
- (void)showDownloadDidReceiveDataOfLength:(uint64_t)length {
    self.received += length; [self emit:3 progress:self.expected ? 100.0 * self.received / self.expected : 0];
}
- (void)showDownloadDidStartExtractingUpdate { self.cancel = nil; self.extracting = YES; [self emit:4 progress:0]; }
- (void)showExtractionReceivedProgress:(double)progress { [self emit:4 progress:100 * progress]; }
- (void)showReadyToInstallAndRelaunch:(void (^)(SPUUserUpdateChoice))reply {
    self.extracting = NO;
    if (self.cancelRequested) { self.cancelRequested = NO; reply(SPUUserUpdateChoiceSkip); [self emit:8 progress:0]; return; }
    self.ready = reply; [self emit:5 progress:100];
}
- (void)showInstallingUpdateWithApplicationTerminated:(BOOL)terminated retryTerminatingApplication:(void (^)(void))retry {
    [self emit:6 progress:100];
}
- (void)showUpdateInstalledAndRelaunched:(BOOL)relaunched acknowledgement:(void (^)(void))acknowledgement { acknowledgement(); }
- (void)dismissUpdateInstallation { self.cancel = nil; self.choice = nil; self.ready = nil; self.extracting = NO; self.cancelRequested = NO; }
- (void)showUpdateInFocus { }
@end

static OSDUpdater *instance;
__attribute__((visibility("default"))) int osd_updater_initialize(OSDUpdateEvent callback) {
    if (![NSThread isMainThread] || !callback) return 0;
    if (instance) { instance.callback = callback; return 1; }
    NSBundle *bundle = NSBundle.mainBundle;
    if (![bundle.bundleIdentifier isEqualToString:@"com.starlabs.optical-system-design"] ||
        ![[bundle objectForInfoDictionaryKey:@"SUPublicEDKey"] isKindOfClass:NSString.class]) return 0;
    OSDUpdater *candidate = [OSDUpdater new]; candidate.callback = callback;
    candidate.updater = [[SPUUpdater alloc] initWithHostBundle:bundle applicationBundle:bundle userDriver:candidate delegate:candidate];
    NSError *error;
    if (![candidate.updater startUpdater:&error]) { NSLog(@"Updater initialization: %@", error); return 0; }
    candidate.updater.automaticallyChecksForUpdates = NO;
    candidate.updater.automaticallyDownloadsUpdates = NO;
    instance = candidate;
    return 1;
}
__attribute__((visibility("default"))) void osd_updater_check(int includeTests) {
    if (![NSThread isMainThread] || !instance || !instance.updater.canCheckForUpdates) return;
    instance.includeTests = includeTests != 0; [instance.updater checkForUpdates];
}
__attribute__((visibility("default"))) void osd_updater_download(void) {
    if (![NSThread isMainThread] || !instance.choice) return;
    void (^reply)(SPUUserUpdateChoice) = instance.choice; instance.choice = nil; reply(SPUUserUpdateChoiceInstall);
}
__attribute__((visibility("default"))) int osd_updater_install(void) {
    if (![NSThread isMainThread] || !instance.ready) return 0;
    NSString *identifier = NSBundle.mainBundle.bundleIdentifier;
    if (identifier.length > 0) {
        for (NSRunningApplication *app in [NSRunningApplication runningApplicationsWithBundleIdentifier:identifier]) {
            if (app.processIdentifier != NSProcessInfo.processInfo.processIdentifier && !app.terminated) return 0;
        }
    }
    void (^reply)(SPUUserUpdateChoice) = instance.ready; instance.ready = nil; reply(SPUUserUpdateChoiceInstall);
    return 1;
}
__attribute__((visibility("default"))) int osd_updater_cancel(void) {
    if (![NSThread isMainThread] || !instance) return 0;
    if (instance.extracting) { instance.cancelRequested = YES; return 0; }
    // Dismiss would authorize installation on quit. Skip cancels that consent.
    void (^ready)(SPUUserUpdateChoice) = instance.ready; instance.ready = nil;
    void (^choice)(SPUUserUpdateChoice) = instance.choice; instance.choice = nil;
    void (^cancel)(void) = instance.cancel; instance.cancel = nil;
    if (ready) ready(SPUUserUpdateChoiceSkip);
    else if (choice) choice(SPUUserUpdateChoiceDismiss);
    else if (cancel) cancel();
    [instance emit:8 progress:0];
    return 1;
}
