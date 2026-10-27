// Native cloud-only fixture. Never linked into the product or installed bundle.
#import "UpdaterBridge.m"
#include <assert.h>
static int lastState;
static void event(int state, const char *version, double progress) { lastState = state; }
int main(void) {
    @autoreleasepool {
        instance = [OSDUpdater new]; instance.callback = event;
        __block int choices = 0;
        __block SPUUserUpdateChoice choice = SPUUserUpdateChoiceDismiss;
        [instance showReadyToInstallAndRelaunch:^(SPUUserUpdateChoice value) { choices++; choice = value; }];
        assert(lastState == 5 && choices == 0);
        osd_updater_cancel();
        assert(lastState == 8 && choices == 1 && choice == SPUUserUpdateChoiceSkip);
        assert(osd_updater_install() == 0 && choices == 1);
        [instance showReadyToInstallAndRelaunch:^(SPUUserUpdateChoice value) { choices++; choice = value; }];
        assert(osd_updater_install() == 1 && choices == 2 && choice == SPUUserUpdateChoiceInstall);
        assert(osd_updater_install() == 0 && choices == 2);
        __block int cancels = 0;
        [instance showDownloadInitiatedWithCancellation:^{ cancels++; }];
        assert(lastState == 3);
        osd_updater_cancel(); assert(cancels == 1 && lastState == 8);
        osd_updater_cancel(); assert(cancels == 1);
        [instance showUserInitiatedUpdateCheckWithCancellation:^{ cancels++; }];
        assert(lastState == 1);
        osd_updater_cancel(); assert(cancels == 2);
        [instance showDownloadDidStartExtractingUpdate]; assert(lastState == 4);
        assert(osd_updater_cancel() == 0 && instance.cancelRequested);
        [instance showExtractionReceivedProgress:0.5]; assert(lastState == 4);
        [instance showReadyToInstallAndRelaunch:^(SPUUserUpdateChoice value) { choices++; choice = value; }];
        assert(lastState == 8 && choice == SPUUserUpdateChoiceSkip && !instance.extracting);
        assert(osd_updater_install() == 0);
        [instance showUpdateNotFoundWithError:[NSError errorWithDomain:@"Fixture" code:0 userInfo:nil] acknowledgement:^{ choices++; }];
        assert(lastState == 7 && choices == 4);
        printf("Native updater reply/cancellation/extraction checks passed. No GUI or installation.\n");
    }
    return 0;
}
