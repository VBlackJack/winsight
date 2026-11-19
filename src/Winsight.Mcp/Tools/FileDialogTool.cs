// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

[McpServerToolType]
public static class FileDialogTool
{
    private const string StandardDialogClass = "#32770";
    private const string FileNameEditAutomationId = "1148";
    private const string ConfirmButtonAutomationId = "1";
    private const int DefaultCloseTimeoutMs = 5000;
    private const int MaxCloseTimeoutMs = 30000;

    private static readonly string[] FileNameFallbacks = ["File name:", "Nom du fichier :", "File name", "Nom du fichier"];
    private static readonly string[] ConfirmButtonFallbacks = ["Open", "Save", "Ouvrir", "Enregistrer", "Select Folder", "Sélectionner un dossier"];

    [McpServerTool(Name = "select_file_dialog", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Fast-path for Windows standard Open/Save file dialogs (#32770). Locates the active file dialog owned by the target process, " +
                 "sets the file path in the standard File name input (AutomationId 1148 or localized fallback), clicks the Open/Save confirm button, " +
                 "and verifies the dialog closes. Avoids fragile folder-by-folder UIA navigation.")]
    public static string SelectFileDialog(
        WindowDiscovery discovery,
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Full path (or filename) to enter into the Open/Save dialog.")]
        string filePath,
        [Description("Optional dialog window title substring (e.g. 'Open', 'Save As', 'Ouvrir'). Omit to auto-detect the open #32770 or modal dialog.")]
        string? dialogTitle = null,
        [Description("Milliseconds to wait for the dialog to close after confirming (1..30000, default 5000).")]
        int timeoutMs = DefaultCloseTimeoutMs,
        [Description("Target process id. Optional; falls back to set_target.")]
        int? processId = null)
        => ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(AgentFailure.Repairable(
                    "missing_target", targetError, "target_resolution", "set_target",
                    new { processId = "<target process id>" }));
            }
            if (string.IsNullOrWhiteSpace(filePath) || filePath.Length > 32767)
            {
                return Serialize(OperationResult.Failure("filePath must be 1..32767 characters."));
            }
            if (timeoutMs is < 1 or > MaxCloseTimeoutMs)
            {
                return Serialize(OperationResult.Failure($"timeoutMs must be 1..{MaxCloseTimeoutMs}."));
            }

            if (ToolAccessPolicy.GuardMutation("select_file_dialog", new { processId = pid, filePath, dialogTitle }) is { } gated)
            {
                return Serialize(gated);
            }

            IReadOnlyList<WindowDescriptor> windows = discovery.List(pid);
            WindowDescriptor? dialog = PickDialog(windows, dialogTitle);
            if (dialog is null)
            {
                return Serialize(AgentFailure.Repairable(
                    "dialog_not_found",
                    "No open file dialog (#32770 or modal dialog) was found for the target process.",
                    cause: "window_resolution",
                    suggestedTool: "list_windows",
                    suggestedArgs: new { processId = pid }));
            }

            OperationResult typed = TrySetFileName(invoker, pid, dialog.Title, filePath);
            if (!typed.Success)
            {
                return Serialize(OperationResult.FailureWith(
                    $"Failed to enter file path into dialog \"{dialog.Title}\": {typed.Error}",
                    new { dialogTitle = dialog.Title, step = "type_path" }));
            }

            OperationResult confirmed = TryConfirmDialog(invoker, pid, dialog.Title);
            if (!confirmed.Success)
            {
                return Serialize(OperationResult.FailureWith(
                    $"Entered path in \"{dialog.Title}\" but could not click Open/Save: {confirmed.Error}",
                    new { dialogTitle = dialog.Title, step = "confirm" }));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                bool stillOpen = discovery.List(pid).Any(w => w.WindowHandle == dialog.WindowHandle);
                if (!stillOpen)
                {
                    return Serialize(OperationResult.OkWith(new
                    {
                        processId = pid,
                        dialogTitle = dialog.Title,
                        filePath,
                        closed = true,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                    }));
                }
                Thread.Sleep(100);
            }

            return Serialize(OperationResult.FailureWith(
                $"File dialog \"{dialog.Title}\" remained open after {timeoutMs} ms (check for overwrite confirmation or invalid path).",
                new { processId = pid, dialogTitle = dialog.Title, filePath, closed = false }));
        });

    internal static WindowDescriptor? PickDialog(IReadOnlyList<WindowDescriptor> windows, string? dialogTitle)
    {
        if (!string.IsNullOrWhiteSpace(dialogTitle))
        {
            return windows.FirstOrDefault(w => w.Title.Contains(dialogTitle, StringComparison.OrdinalIgnoreCase));
        }

        return windows.FirstOrDefault(w => string.Equals(w.ClassName, StandardDialogClass, StringComparison.Ordinal))
            ?? windows.FirstOrDefault(w => w.Kind == WindowClassifier.KindDialog || w.IsModal);
    }

    private static OperationResult TrySetFileName(ActionInvoker invoker, int pid, string dialogTitle, string filePath)
    {
        OperationResult primary = invoker.Type(
            pid,
            new ElementLocator(AutomationId: FileNameEditAutomationId, ControlType: "Edit"),
            filePath,
            dialogTitle,
            focusWindow: true);
        if (primary.Success) return primary;

        foreach (string name in FileNameFallbacks)
        {
            OperationResult fallback = invoker.Type(
                pid,
                new ElementLocator(Name: name, ControlType: "Edit"),
                filePath,
                dialogTitle,
                focusWindow: true);
            if (fallback.Success) return fallback;
        }

        return primary;
    }

    private static OperationResult TryConfirmDialog(ActionInvoker invoker, int pid, string dialogTitle)
    {
        OperationResult primary = invoker.Click(
            pid,
            new ElementLocator(AutomationId: ConfirmButtonAutomationId, ControlType: "Button"),
            ClickStyle.Primary,
            dialogTitle);
        if (primary.Success) return primary;

        foreach (string name in ConfirmButtonFallbacks)
        {
            OperationResult fallback = invoker.Click(
                pid,
                new ElementLocator(Name: name, ControlType: "Button"),
                ClickStyle.Primary,
                dialogTitle);
            if (fallback.Success) return fallback;
        }

        return primary;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions.Default);
}
