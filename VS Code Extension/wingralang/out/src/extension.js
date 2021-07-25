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
exports.deactivate = exports.getWorkspaceFolder = exports.activate = void 0;
const fs = require("fs");
const path = require("path");
const vscode = require("vscode");
const node_1 = require("vscode-languageclient/node");
// Defines the search path of your language server DLL. (.NET Core)
const languageServerPaths = [
    "bin/ext/WingraLanguageServer.dll",
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
        context.subscriptions.push(vscode.languages.registerDocumentSemanticTokensProvider({ language: 'wingra' }, new DocumentSemanticTokensProvider(), legend));
        let WTerminal = null;
        var extPath = context.asAbsolutePath("");
        context.subscriptions.push(vscode.commands.registerCommand("wingra.run", () => __awaiter(this, void 0, void 0, function* () {
            var _a;
            // The code you place here will be executed every time your command is executed
            const doc = (_a = vscode.window.activeTextEditor) === null || _a === void 0 ? void 0 : _a.document;
            if (vscode.workspace.workspaceFolders) {
                const folder = vscode.workspace.workspaceFolders[0]; // TODO: maybe this needs to be fancier?
                if (folder) {
                    if (doc && doc.isDirty) {
                        yield doc.save();
                    }
                    //const wingra = `%USERPROFILE%\.vscode\extensions`;
                    const config = getWorkspaceConfig();
                    const wingra = (config === null || config === void 0 ? void 0 : config.get('pathToExecutableFile', ''))
                        || "& '" + extPath + "\\bin\\lang\\WingraConsole.exe'";
                    WTerminal = WTerminal || vscode.window.createTerminal("Wingra");
                    WTerminal.show();
                    WTerminal.sendText(wingra, true);
                }
            }
        })));
        // var type = "wingraRunProvider";
        // vscode.tasks.registerTaskProvider(type,{
        //     provideTasks(token?: vscode.CancellationToken) {
        //         var execution = new vscode.ShellExecution("echo \"Hello World\"");
        //         var problemMatchers = ["$runWingra"];
        //         return [
        //             new vscode.Task({type: type}, vscode.TaskScope.Workspace,
        //                 "Run", "wingralang", execution, problemMatchers)
        //         ];
        //     },
        //     resolveTask(task: vscode.Task, token?: vscode.CancellationToken) {
        //         return task;
        //     }
        // });
        yield activateLanguageServer(context);
    });
}
exports.activate = activate;
function getWorkspaceConfig() {
    const currentWorkspaceFolder = getWorkspaceFolder();
    if (!currentWorkspaceFolder)
        return null;
    return vscode.workspace.getConfiguration('v', currentWorkspaceFolder.uri);
}
function getWorkspaceFolder(uri) {
    if (uri)
        return vscode.workspace.getWorkspaceFolder(uri) || null;
    if (!vscode.workspace.workspaceFolders)
        return null;
    const currentDoc = getCurrentDocument();
    return currentDoc
        ? vscode.workspace.getWorkspaceFolder(currentDoc.uri) || null
        : vscode.workspace.workspaceFolders[0];
}
exports.getWorkspaceFolder = getWorkspaceFolder;
function getCurrentDocument() {
    return vscode.window.activeTextEditor ? vscode.window.activeTextEditor.document : null;
}
// this method is called when your extension is deactivated
function deactivate() {
    return __awaiter(this, void 0, void 0, function* () {
        client === null || client === void 0 ? void 0 : client.stop();
    });
}
exports.deactivate = deactivate;
// specifically needed to support textdata correctly
// modified from the MS vscode semantic tokens sample
const tokenTypes = new Map();
const tokenModifiers = new Map();
const legend = (function () {
    const tokenTypesLegend = [
        'string'
    ];
    tokenTypesLegend.forEach((tokenType, index) => tokenTypes.set(tokenType, index));
    const tokenModifiersLegend = [];
    tokenModifiersLegend.forEach((tokenModifier, index) => tokenModifiers.set(tokenModifier, index));
    return new vscode.SemanticTokensLegend(tokenTypesLegend, tokenModifiersLegend);
})();
class DocumentSemanticTokensProvider {
    provideDocumentSemanticTokens(document, token) {
        return __awaiter(this, void 0, void 0, function* () {
            const allTokens = this._parseText(document.getText());
            const builder = new vscode.SemanticTokensBuilder();
            allTokens.forEach((token) => {
                builder.push(token.line, token.startCharacter, token.length, this._encodeTokenType(token.tokenType), this._encodeTokenModifiers(token.tokenModifiers));
            });
            return builder.build();
        });
    }
    _encodeTokenType(tokenType) {
        if (tokenTypes.has(tokenType)) {
            return tokenTypes.get(tokenType);
        }
        else if (tokenType === 'notInLegend') {
            return tokenTypes.size + 2;
        }
        return 0;
    }
    _encodeTokenModifiers(strTokenModifiers) {
        let result = 0;
        for (let i = 0; i < strTokenModifiers.length; i++) {
            const tokenModifier = strTokenModifiers[i];
            if (tokenModifiers.has(tokenModifier)) {
                result = result | (1 << tokenModifiers.get(tokenModifier));
            }
            else if (tokenModifier === 'notInLegend') {
                result = result | (1 << tokenModifiers.size + 2);
            }
        }
        return result;
    }
    _parseText(text) {
        const r = [];
        const lines = text.split(/\r\n|\r|\n/);
        var textDataIndent = -1;
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const spaces = line.search(/\S/);
            if (textDataIndent >= 0) {
                if (spaces <= textDataIndent) {
                    textDataIndent = -1;
                }
                else {
                    r.push({
                        line: i,
                        startCharacter: spaces,
                        length: line.length - spaces,
                        tokenType: "string",
                        tokenModifiers: []
                    });
                }
            }
            if (textDataIndent < 0) {
                // TODO: this is a very naive search for textdata
                if (line.search("textdata") >= 0)
                    textDataIndent = spaces;
            }
        }
        return r;
    }
}
//# sourceMappingURL=extension.js.map