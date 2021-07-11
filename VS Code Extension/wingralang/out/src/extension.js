"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.deactivate = exports.activate = void 0;
const fs = require("fs");
const path = require("path");
const vscode = require("vscode");
const node_1 = require("vscode-languageclient/node");
// Defines the search path of your language server DLL. (.NET Core)
const languageServerPaths = [
    "server/WingraLanguageServer.dll",
    "../../WingraLanguageServer/bin/Debug/netcoreapp3.1/WingraLanguageServer.dll",
];
let client;
function activateLanguageServer(context) {
    return __awaiter(this, void 0, void 0, function* () {
        // The server is implemented in an executable application.
        let serverModule;
        for (let p of languageServerPaths) {
            p = context.asAbsolutePath(p);
            console.log(p);
            try {
                yield fs.promises.access(p);
                serverModule = p;
                break;
            }
            catch (err) {
                // Skip this path.
            }
        }
        if (!serverModule)
            throw new URIError("Cannot find the language server module.");
        let workPath = path.dirname(serverModule);
        console.log(`Use ${serverModule} as server module.`);
        console.log(`Work path: ${workPath}.`);
        // If the extension is launched in debug mode then the debug server options are used
        // Otherwise the run options are used
        let serverOptions = {
            run: { command: "dotnet", args: [serverModule], options: { cwd: workPath } },
            debug: { command: "dotnet", args: [serverModule, "--debug"], options: { cwd: workPath } }
        };
        // Options to control the language client
        let clientOptions = {
            // Register the server for plain text documents
            documentSelector: ["wingra"],
            synchronize: {
                // Synchronize the setting section 'languageServerExample' to the server
                configurationSection: "wingraLanguageServer",
                // Notify the server about file changes to '.clientrc files contain in the workspace
                fileEvents: [
                    vscode.workspace.createFileSystemWatcher("**/.clientrc"),
                    vscode.workspace.createFileSystemWatcher("**/.wng"),
                ]
            },
        };
        // Create the language client and start the client.
        client = new node_1.LanguageClient("wingraLanguageServer", "Wingra Language Server", serverOptions, clientOptions);
        let disposable = client.start();
        // Push the disposable to the context's subscriptions so that the 
        // client can be deactivated on extension deactivation
        context.subscriptions.push(disposable);
    });
}
// this method is called when your extension is activated
// your extension is activated the very first time the command is executed
function activate(context) {
    return __awaiter(this, void 0, void 0, function* () {
        console.log("wingraLang extension is now activated.");
        yield activateLanguageServer(context);
        // The command has been defined in the package.json file
        let disposable = vscode.commands.registerCommand("extension.sayHello", () => {
            // The code you place here will be executed every time your command is executed
            // Display a message box to the user
            vscode.window.showInformationMessage("Hello World!");
        });
        context.subscriptions.push(disposable);
    });
}
exports.activate = activate;
// this method is called when your extension is deactivated
function deactivate() {
    return __awaiter(this, void 0, void 0, function* () {
        client === null || client === void 0 ? void 0 : client.stop();
    });
}
exports.deactivate = deactivate;
//# sourceMappingURL=extension.js.map