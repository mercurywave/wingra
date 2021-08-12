import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { ExecuteCommandRequest, LanguageClient, LanguageClientOptions, ServerOptions } from "vscode-languageclient/node";

// Defines the search path of your language server DLL. (.NET Core)
const languageServerPaths = [
    "bin/ext/WingraLanguageServer.dll",
]

let client: LanguageClient | undefined;

async function activateLanguageServer(context: vscode.ExtensionContext) {
    // The server is implemented in an executable application.
    let serverModule: string | undefined;
    for (let p of languageServerPaths) {
        p = context.asAbsolutePath(p);
        console.log(p);
        try {
            await fs.promises.access(p);
            serverModule = p;
            break;
        } catch (err) {
            // Skip this path.
        }
    }
    if (!serverModule) throw new URIError("Cannot find the language server module.");
    let workPath = path.dirname(serverModule);
    console.log(`Use ${serverModule} as server module.`);
    console.log(`Work path: ${workPath}.`);


    // If the extension is launched in debug mode then the debug server options are used
    // Otherwise the run options are used
    let serverOptions: ServerOptions = {
        run: { command: "dotnet", args: [serverModule], options: { cwd: workPath } },
        debug: { command: "dotnet", args: [serverModule, "--debug"], options: { cwd: workPath } }
    }
    // Options to control the language client
    let clientOptions: LanguageClientOptions = {
        // Register the server for plain text documents
        documentSelector: ["wingra", "wingraProj"],
        synchronize: {
            // Synchronize the setting section 'languageServerExample' to the server
            configurationSection: "wingraLanguageServer",
            // Notify the server about file changes to '.clientrc files contain in the workspace
            fileEvents: [
                vscode.workspace.createFileSystemWatcher("**/.clientrc"),
                vscode.workspace.createFileSystemWatcher("**/.wng"),
                vscode.workspace.createFileSystemWatcher("**/.wingraProj"),
            ]
        },
    }

    // Create the language client and start the client.
    client = new LanguageClient("wingraLanguageServer", "Wingra Language Server", serverOptions, clientOptions);
    let disposable = client.start();

    // Push the disposable to the context's subscriptions so that the 
    // client can be deactivated on extension deactivation
    context.subscriptions.push(disposable);
}

// this method is called when your extension is activated
// your extension is activated the very first time the command is executed
export async function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(vscode.languages.registerDocumentSemanticTokensProvider(
        { language: 'wingra'},
        new DocumentSemanticTokensProvider(), legend));
        
    let WTerminal: vscode.Terminal | null = null;
    var extPath = context.asAbsolutePath("");
    context.subscriptions.push(vscode.commands.registerCommand("wingra.run", async () => {
        // The code you place here will be executed every time your command is executed
        const doc = vscode.window.activeTextEditor?.document;
        if(vscode.workspace.workspaceFolders){
            const folder = vscode.workspace.workspaceFolders[0]; // TODO: maybe this needs to be fancier?
            if(folder){
                if (doc && doc.isDirty){
                    await doc.save();
                }
                //const wingra = `%USERPROFILE%\.vscode\extensions`;
                const config = getWorkspaceConfig();
                const wingra = config?.get('pathToExecutableFile', '')
                    || "& '" + extPath + "\\bin\\lang\\WingraConsole.exe'";
                WTerminal = WTerminal || vscode.window.createTerminal("Wingra");
                WTerminal.show();
                WTerminal.sendText(wingra, true);
            }
        }
    }));
   
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
    
    await activateLanguageServer(context);
}

function getWorkspaceConfig(): vscode.WorkspaceConfiguration | null {
	const currentWorkspaceFolder = getWorkspaceFolder();
    if (!currentWorkspaceFolder) return null;
	return vscode.workspace.getConfiguration('wingra', currentWorkspaceFolder.uri);
}

export function getWorkspaceFolder(uri?: vscode.Uri): vscode.WorkspaceFolder | null {
	if (uri) return vscode.workspace.getWorkspaceFolder(uri) || null;
    if (!vscode.workspace.workspaceFolders) return null;
	const currentDoc = getCurrentDocument();
	return currentDoc
		? vscode.workspace.getWorkspaceFolder(currentDoc.uri) || null
		: vscode.workspace.workspaceFolders[0];
}

function getCurrentDocument(): vscode.TextDocument | null {
	return vscode.window.activeTextEditor ? vscode.window.activeTextEditor.document : null;
}

// this method is called when your extension is deactivated
export async function deactivate(): Promise<void> {
    client?.stop();
}




// specifically needed to support textdata correctly
// modified from the MS vscode semantic tokens sample
const tokenTypes = new Map<string, number>();
const tokenModifiers = new Map<string, number>();
const legend = (function () {
	const tokenTypesLegend = [
		'string'
	];
	tokenTypesLegend.forEach((tokenType, index) => tokenTypes.set(tokenType, index));

	const tokenModifiersLegend: string[] = [];
	tokenModifiersLegend.forEach((tokenModifier, index) => tokenModifiers.set(tokenModifier, index));

	return new vscode.SemanticTokensLegend(tokenTypesLegend, tokenModifiersLegend);
})();

interface IParsedToken {
	line: number;
	startCharacter: number;
	length: number;
	tokenType: string;
	tokenModifiers: string[];
}

class DocumentSemanticTokensProvider implements vscode.DocumentSemanticTokensProvider {
	async provideDocumentSemanticTokens(document: vscode.TextDocument, token: vscode.CancellationToken): Promise<vscode.SemanticTokens> {
        
		const allTokens = this._parseText(document.getText());
		const builder = new vscode.SemanticTokensBuilder();
		allTokens.forEach((token) => {
			builder.push(token.line, token.startCharacter, token.length, this._encodeTokenType(token.tokenType), this._encodeTokenModifiers(token.tokenModifiers));
		});
		return builder.build();
	}

	private _encodeTokenType(tokenType: string): number {
		if (tokenTypes.has(tokenType)) {
			return tokenTypes.get(tokenType)!;
		} else if (tokenType === 'notInLegend') {
			return tokenTypes.size + 2;
		}
		return 0;
	}

	private _encodeTokenModifiers(strTokenModifiers: string[]): number {
		let result = 0;
		for (let i = 0; i < strTokenModifiers.length; i++) {
			const tokenModifier = strTokenModifiers[i];
			if (tokenModifiers.has(tokenModifier)) {
				result = result | (1 << tokenModifiers.get(tokenModifier)!);
			} else if (tokenModifier === 'notInLegend') {
				result = result | (1 << tokenModifiers.size + 2);
			}
		}
		return result;
	}

	private _parseText(text: string): IParsedToken[] {
		const r: IParsedToken[] = [];
		const lines = text.split(/\r\n|\r|\n/);
        var textDataIndent = -1;
		for (let i = 0; i < lines.length; i++) {
			const line = lines[i];
            const spaces = line.search(/\S/);
            if (textDataIndent >= 0)
            {
                if (spaces <= textDataIndent && line.trim().length > 0) {
                    textDataIndent = -1;
                } else {
                    r.push({
                        line: i,
                        startCharacter: spaces,
                        length: line.length - spaces,
                        tokenType: "string",
                        tokenModifiers: []
                    });
                }
            }
            if (textDataIndent < 0){
                // TODO: this is a very naive search for textdata
                if(line.search("textdata") >= 0)
                    textDataIndent = spaces;
            }
		}
		return r;
	}
}