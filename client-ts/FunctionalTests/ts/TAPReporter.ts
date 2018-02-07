export class TAPReporter implements jasmine.CustomReporter {
    private counter: number = 1;

    constructor(private log: (message?: any, ...optionalParams: any[]) => void) {
    }

    jasmineStarted(suiteInfo: jasmine.SuiteInfo): void {
        this.taplog("TAP version 13");
        this.taplog(`1..${suiteInfo.totalSpecsDefined}`);
    }

    suiteStarted(result: jasmine.CustomReporterResult): void {
    }

    specStarted(result: jasmine.CustomReporterResult): void {
    }

    specDone(result: jasmine.CustomReporterResult): void {
        if (result.status === "failed") {
            this.taplog(`not ok ${this.counter} ${result.fullName}`);
        }
        else {
            this.taplog(`ok ${this.counter} ${result.fullName}`);
        }

        this.counter += 1;
    }

    suiteDone(result: jasmine.CustomReporterResult): void {
    }

    jasmineDone(runDetails: jasmine.RunDetails): void {
        this.log("##tapend");
    }

    private taplog(msg: string) {
        // We wrap TAP messages in a "##tap[]" sequence because we have other random
        // stuff being written to the console.
        for (let line of msg.split(/\r|\n|\r\n/)) {
            this.log(`##tap:${line}`);
        }
    }
}