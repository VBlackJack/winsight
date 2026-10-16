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
using System.Text.Json;
using ModelContextProtocol.Server;
using Winsight.Core.Models;
using Winsight.Core.Modules.Clipboard;
using Winsight.Core.Modules.UiAutomation;
using Winsight.Mcp.Serialization;

namespace Winsight.Mcp.Tools;

/// <summary>
/// Text input without a UI Automation element, and the clipboard. Clipboard
/// tools stay off unless WINSIGHT_ALLOW_CLIPBOARD is set; their contents are
/// returned to the caller only, never written to traces or logs.
/// </summary>
[McpServerToolType]
public static class TextInputTool
{
    private const string ProcessIdDescription =
        "Target process id. Optional: falls back to the session default set by set_target.";

    [McpServerTool(Name = "type_keys")]
    [Description("Type text into the focused control of the target's window when there is no UI Automation " +
                 "element to type into (iced, winit, egui and other GPU-drawn UIs); use type_text when there " +
                 "is one. Raises the target window and verifies focus first, like send_key_chord. mode=keys " +
                 "(default) sends each character as a Unicode keystroke, line breaks as Enter and tabs as Tab, " +
                 "and stops with foreground_changed if the window loses the foreground mid-way. mode=paste " +
                 "needs WINSIGHT_ALLOW_CLIPBOARD: it saves the clipboard, puts the text on it (kept out of " +
                 "clipboard history and the cloud clipboard), sends Ctrl+V, waits restoreDelayMs, then puts " +
                 "the saved clipboard back and reports restored=full|partial|failed. Refuses WebView2 focus " +
                 "unless allowWebView2Input=true. Returns { mode, length, windowHandle, windowTitle, " +
                 "focusedElement, restored, skippedFormats }.")]
    public static string TypeKeys(
        ActionInvoker invoker,
        SessionTarget target,
        [Description("Text to type, up to 4096 characters.")]
        string text,
        [Description("keys (default) or paste.")]
        string? mode = null,
        [Description("Restrict the target window to those whose title contains this text.")]
        string? windowTitle = null,
        [Description("Paste mode: milliseconds to leave the text on the clipboard before restoring it " +
                     "(0 to 5000, default 300).")]
        int? restoreDelayMs = null,
        [Description("Send the text even when keyboard focus is inside a WebView2 surface. Default false.")]
        bool allowWebView2Input = false,
        [Description(ProcessIdDescription)]
        int? processId = null)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (!target.TryResolve(processId, out int pid, out string targetError))
            {
                return Serialize(OperationResult.Failure(targetError));
            }
            if (!TypeKeysMode.TryParse(mode, out bool paste, out string? modeError))
            {
                return Serialize(OperationResult.Failure(modeError!));
            }
            if (paste && ClipboardPolicy.Refusal("type_keys mode=paste") is { } refusal)
            {
                return Serialize(OperationResult.Failure(refusal));
            }
            if (ToolAccessPolicy.GuardMutation(
                    "type_keys",
                    new { processId = pid, length = text?.Length ?? 0, mode, windowTitle, restoreDelayMs })
                is { } gated)
            {
                return Serialize(gated);
            }
            return Serialize(invoker.TypeKeys(pid, text!, windowTitle, paste, allowWebView2Input, restoreDelayMs));
        });
    }

    [McpServerTool(Name = "set_clipboard_text")]
    [Description("Replace the clipboard with text, for example to prepare a paste. Needs " +
                 "WINSIGHT_ALLOW_CLIPBOARD (the clipboard may hold secrets). The text is kept out of clipboard " +
                 "history, the cloud clipboard and clipboard monitors. Returns { length }; the text is never " +
                 "logged.")]
    public static string SetClipboardText(
        ClipboardService clipboard,
        [Description("Text to place on the clipboard.")]
        string text)
    {
        return ToolSafety.SafeInvoke(() =>
        {
            if (ClipboardPolicy.Refusal("set_clipboard_text") is { } refusal)
            {
                return Serialize(OperationResult.Failure(refusal));
            }
            if (ToolAccessPolicy.GuardMutation("set_clipboard_text", new { length = text?.Length ?? 0 }) is { } gated)
            {
                return Serialize(gated);
            }
            return Serialize(clipboard.WriteText(text ?? string.Empty));
        });
    }

    [McpServerTool(Name = "read_clipboard_text")]
    [Description("Read the clipboard text, for example to check what a Copy button copied. Needs " +
                 "WINSIGHT_ALLOW_CLIPBOARD (the clipboard may hold secrets). Returns { text, length }; text is " +
                 "null when the clipboard holds no text. The text goes to the caller only: traces record its " +
                 "size, never its content.")]
    public static string ReadClipboardText(ClipboardService clipboard)
    {
        return ToolSafety.SafeInvoke(() =>
            Serialize(ClipboardPolicy.Refusal("read_clipboard_text") is { } refusal
                ? OperationResult.Failure(refusal)
                : clipboard.ReadText()));
    }

    private static string Serialize(OperationResult result) => JsonSerializer.Serialize(result, JsonOptions.Default);
}
