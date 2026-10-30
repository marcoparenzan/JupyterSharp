import * as vscode from "vscode";
import { JupyterSharpController } from "./kernelController";
import { JupyterSharpNotebookSerializer } from "./notebookSerializer";

export function activate(context: vscode.ExtensionContext): void {
    context.subscriptions.push(vscode.workspace.registerNotebookSerializer("jupytersharp-notebook", new JupyterSharpNotebookSerializer()));

    const controller = new JupyterSharpController(context);
    context.subscriptions.push(controller);

    context.subscriptions.push(
        vscode.commands.registerCommand("jupytersharp.newNotebook", async () => {
            const data = new vscode.NotebookData([
                new vscode.NotebookCellData(vscode.NotebookCellKind.Markup, "# New JupyterSharp notebook", "markdown"),
                new vscode.NotebookCellData(vscode.NotebookCellKind.Code, "1 + 1", "csharp")
            ]);
            const document = await vscode.workspace.openNotebookDocument("jupytersharp-notebook", data);
            await vscode.window.showNotebookDocument(document);
        })
    );

    context.subscriptions.push(
        vscode.commands.registerCommand("jupytersharp.restartHost", () => {
            const editor = vscode.window.activeNotebookEditor;
            if (!editor) {
                vscode.window.showWarningMessage("JupyterSharp: open a JupyterSharp notebook first.");
                return;
            }
            controller.disposeClient(editor.notebook);
            vscode.window.showInformationMessage("JupyterSharp: kernel host restarted for this notebook.");
        })
    );
}

export function deactivate(): void {
    // Controller disposal (via context.subscriptions) already terminates every JupyterSharp.Host process.
}
