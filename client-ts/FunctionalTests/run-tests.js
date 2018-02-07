#!/usr/bin/env node

const spawn = require("child_process").spawn;
const os = require("os");
const argv = require("yargs").argv;

const { Builder, logging } = require("selenium-webdriver");

const rootDir = __dirname;

let args = process.argv.slice(2);
for (let i = 0; i < args.length; i += 1) {
    switch(args[i]) {
        case "-v":
            verboseEnabled = true;
            break;
    }
}

function verbose(message) {
    if (argv.v || argv.verbose) {
        console.log(message);
    }
}

// This script launches the functional test app and then uses Selenium WebDriver to run the tests and verify the results.

console.log("# Launching Functional Test server");
let dotnet = spawn("dotnet", ["run", "--no-build"], {
    cwd: rootDir,
});

process.on("SIGINT", () => {
    if (!dotnet.killed) {
        console.log("# Killing dotnet.exe server process");
        dotnet.kill();
    }
})

const logExtractorRegex = /[^ ]+ [^ ]+ "(.*)"/

function getMessage(logMessage) {
    let r = logExtractorRegex.exec(logMessage);

    // Unescape \"
    if (r && r.length >= 2) {
        return r[1].replace(/\\"/g, "\"");
    } else {
        return logMessage;
    }
}

async function runTests(serverUrl) {
    console.log("# Launching browser at " + serverUrl);
    let logPrefs = new logging.Preferences();
    logPrefs.setLevel(logging.Type.BROWSER, logging.Level.INFO);

    let driver = await new Builder()
        .usingServer("http://localhost:4444/wd/hub")
        .setLoggingPrefs(logPrefs)
        .forBrowser(argv.browser || "chrome")
        .build();
    try {
        await driver.get(serverUrl);
        let complete = false;
        while (!complete) {
            let logs = await driver.manage().logs().get("browser");
            for (let log of logs) {
                let message = getMessage(log.message);
                if (message.startsWith("##tap:")) {
                    console.log(message.substring(6));
                }
                else if (message === "##tapend") {
                    // End of the tests, terminate!
                    complete = true;
                    break;
                }
            }
        }
        console.log("# End of tests");
    }
    catch (e) {
        console.error("Error from browser: " + e.toString());
    }
    finally {
        await driver.quit();
    }
}

let lastLine = "";
const regex = /Now listening on: (http:\/\/localhost:([\d])+)/;
async function onData(chunk) {
    chunk = chunk.toString();

    // Process lines
    let lineEnd = chunk.indexOf(os.EOL);
    while (lineEnd >= 0) {
        let chunkLine = lastLine + chunk.substring(0, lineEnd);
        lastLine = "";

        chunk = chunk.substring(lineEnd + os.EOL.length);

        verbose("dotnet: " + chunkLine);
        let results = regex.exec(chunkLine);
        if (results && results.length > 0) {
            try {
                await runTests(results[1])
                dotnet.stdout.removeAllListeners("data");
                dotnet.kill();
                console.log("# Server process shut down. Testing completed");
                process.exit(0);
            } catch (e) {
                console.error("Error running tests: " + e.toString());
            }
        }
        lineEnd = chunk.indexOf(os.EOL);
    }

    lastLine = chunk.toString();
}

dotnet.on("close", (code, signal) => {
    if(code != 0) {
        console.error(`Server process exited with code: ${code}`);
        process.exit(code);
    }
    else {
        console.log(`Server process exited early`);
        process.exit(1);
    }
})

dotnet.stdout.on("data", onData);